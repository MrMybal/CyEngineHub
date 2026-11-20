using System.Runtime.InteropServices;

namespace CyEngineHub.Core.Ai;

/// <summary>Owns the SDK loopback listener for the lifetime of an enabled MCP connection.</summary>
public sealed class SdkMcpHttpServer : IDisposable
{
    private readonly SdkMcpRegistry _registry;
    private nint _handle;

    public SdkMcpHttpServer(SdkMcpRegistry registry, HubMcpOptions options)
    {
        _registry = registry;
        _handle = Start(registry.Handle, options.AccessProfile, options.LocalPort, out var port);
        if (_handle == 0) throw new InvalidOperationException(Marshal.PtrToStringUTF8(LastError()));
        Port = port;
        Token = Marshal.PtrToStringUTF8(ReadToken(_handle)) ?? "";
    }

    public int Port { get; }
    public string Token { get; }

    public void Dispose()
    {
        if (_handle == 0) return;
        Stop(_handle);
        _handle = 0;
        GC.KeepAlive(_registry);
    }

    private const string Library = "cimgui";
    [DllImport(Library, EntryPoint = "cyhub_mcp_http_start", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint Start(nint registry, HubMcpAccessProfile profile, int port, out int boundPort);
    [DllImport(Library, EntryPoint = "cyhub_mcp_http_token", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint ReadToken(nint server);
    [DllImport(Library, EntryPoint = "cyhub_mcp_http_stop", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Stop(nint server);
    [DllImport(Library, EntryPoint = "cyhub_mcp_last_error", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint LastError();
}
