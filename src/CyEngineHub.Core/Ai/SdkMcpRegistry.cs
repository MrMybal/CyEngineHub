using System.Runtime.InteropServices;
using System.Text.Json;

namespace CyEngineHub.Core.Ai;

/// <summary>The SDK validates the schemas and dispatches both MCP and chat calls to the managed handlers.</summary>
public sealed class SdkMcpRegistry : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly InvokeCallback _invoke;
    private readonly ReleaseCallback _release = Marshal.FreeCoTaskMem;
    private readonly Func<string, JsonElement, CancellationToken, Task<object>> _handler;

    public SdkMcpRegistry(object definitions, Func<string, JsonElement, CancellationToken, Task<object>> handler)
    {
        _handler = handler;
        _invoke = Invoke;
        Handle = Create(JsonSerializer.Serialize(definitions, Json), _invoke, _release);
        if (Handle == 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(LastError()));
    }

    public nint Handle { get; private set; }

    public void Dispose()
    {
        if (Handle == 0) return;
        Destroy(Handle);
        Handle = 0;
    }

    public SdkMcpSession CreateSession() => new(this);

    private nint Invoke(nint name, nint arguments, nint request)
    {
        string result;
        try
        {
            using var cancellation = new CancellationTokenSource();
            using (var probe = new CancellationProbe(request, cancellation))
            using (var document = JsonDocument.Parse(Marshal.PtrToStringUTF8(arguments)!))
            {
                var value = _handler(Marshal.PtrToStringUTF8(name)!, document.RootElement, cancellation.Token)
                    .GetAwaiter().GetResult();
                result = JsonSerializer.Serialize(new { result = value }, Json);
            }
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException)
        { result = JsonSerializer.Serialize(new { error = error.Message }, Json); }
        catch (OperationCanceledException)
        { result = JsonSerializer.Serialize(new { error = "Operation cancelled." }, Json); }
        catch (Exception)
        { result = JsonSerializer.Serialize(new { error = "CyEngineHub operation failed. See the application diagnostics." }, Json); }
        return Marshal.StringToCoTaskMemUTF8(result);
    }

    private sealed class CancellationProbe(nint request, CancellationTokenSource source) : IDisposable
    {
        private readonly Timer _timer = new(_ => { if (Cancelled(request) != 0) source.Cancel(); }, null, 0, 50);

        // The SDK owns the pointer until the enclosing callback returns.
        public void Dispose() => _timer.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint InvokeCallback(nint name, nint args, nint request);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseCallback(nint value);
    private const string Library = "cimgui";
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA2101", Justification = "The SDK ABI accepts UTF-8, explicitly marshalled here.")]
    [DllImport(Library, EntryPoint = "cyhub_mcp_registry", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Create([MarshalAs(UnmanagedType.LPUTF8Str)] string definitions, InvokeCallback invoke, ReleaseCallback release);
    [DllImport(Library, EntryPoint = "cyhub_mcp_last_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint LastError();
    [DllImport(Library, EntryPoint = "cyhub_mcp_registry_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Destroy(nint registry);
    [DllImport(Library, EntryPoint = "cyhub_mcp_cancelled", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Cancelled(nint request);
}

public sealed class SdkMcpSession : IDisposable
{
    private readonly SdkMcpRegistry _registry;
    private readonly nint _handle;

    internal SdkMcpSession(SdkMcpRegistry registry)
    {
        _registry = registry;
        _handle = Create(registry.Handle);
    }

    public async Task ServeAsync(TextReader input, TextWriter output, CancellationToken cancellationToken)
    {
        using var writeGate = new SemaphoreSlim(1);
        var active = new List<Task>();
        async Task ReplyAsync(string? response)
        {
            if (response is null) return;
            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await output.WriteLineAsync(response.AsMemory(), cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally { writeGate.Release(); }
        }

        using var stop = cancellationToken.Register(() => Cancel(_handle));
        try
        {
            while (await HubMcpTransport.ReadLineAsync(input, cancellationToken).ConfigureAwait(false) is { } line)
            {
                foreach (var task in active.Where(task => task.IsCompleted))
                    await task.ConfigureAwait(false);
                active.RemoveAll(task => task.IsCompleted);
                if (!IsToolCall(line, out var id))
                {
                    await ReplyAsync(Dispatch(line)).ConfigureAwait(false);
                    continue;
                }
                if (active.Count >= 4)
                {
                    await ReplyAsync($$$"""{"jsonrpc":"2.0","id":{{{id}}},"error":{"code":-32001,"message":"Server busy"}}""").ConfigureAwait(false);
                    continue;
                }
                active.Add(Task.Run(() => ReplyAsync(Dispatch(line)), cancellationToken));
            }
        }
        finally
        {
            Cancel(_handle);
            await Task.WhenAll(active).ConfigureAwait(false);
        }
    }

    private static bool IsToolCall(string line, out string id)
    {
        id = "null";
        try
        {
            using var request = JsonDocument.Parse(line);
            var root = request.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("method", out var method) ||
                method.ValueKind != JsonValueKind.String || method.GetString() != "tools/call")
                return false;
            if (root.TryGetProperty("id", out var value)) id = value.GetRawText();
            return true;
        }
        catch (JsonException) { return false; }
    }

    private string? Dispatch(string line) => Marshal.PtrToStringUTF8(Handle(_handle, line));

    public async Task<string?> ExchangeAsync(string line, CancellationToken token)
    {
        using var cancel = token.Register(() => Cancel(_handle));
        return await Task.Run(() => Dispatch(line), token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        Destroy(_handle);
        GC.KeepAlive(_registry);
    }

    private const string Library = "cimgui";
    [DllImport(Library, EntryPoint = "cyhub_mcp_session", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Create(nint registry);
    [DllImport(Library, EntryPoint = "cyhub_mcp_session_destroy", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Destroy(nint session);
    [DllImport(Library, EntryPoint = "cyhub_mcp_session_cancel", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Cancel(nint session);
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA2101", Justification = "The SDK ABI accepts UTF-8, explicitly marshalled here.")]
    [DllImport(Library, EntryPoint = "cyhub_mcp_handle", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Handle(nint session, [MarshalAs(UnmanagedType.LPUTF8Str)] string line);
}
