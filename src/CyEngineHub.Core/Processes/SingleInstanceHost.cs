using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace CyEngineHub.Core.Processes;

/// <summary>Process-wide ownership plus same-user, activation-only IPC. No arguments are executed.</summary>
public sealed class SingleInstanceHost : IDisposable
{
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();
    private readonly string _pipeName;
    private readonly Task? _listener;
    private readonly object _gate = new();
    private Action? _activate;
    private bool _pending;
    private bool _disposed;
    public bool IsPrimary { get; }

    public SingleInstanceHost(string applicationId = "CyEngineHub.Desktop")
    {
        var identity = applicationId + ":" + Environment.UserDomainName + "\\" + Environment.UserName;
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
        _pipeName = "CyEngineHub.Activate." + suffix;
        _mutex = new Mutex(initiallyOwned: false, name: "CyEngineHub.Instance." + suffix, createdNew: out var created);
        // Keep the named object alive for the whole application lifetime. No thread-affine ownership.
        IsPrimary = created;
        if (IsPrimary) _listener = Task.Run(ListenAsync);
    }

    public void SetActivationHandler(Action activate)
    {
        ArgumentNullException.ThrowIfNull(activate);
        bool pending;
        lock (_gate) { _activate = activate; pending = _pending; _pending = false; }
        if (pending) activate();
    }

    public async Task<bool> ActivatePrimaryAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await pipe.WriteAsync(new byte[] { 1 }, timeout.Token).ConfigureAwait(false);
            var reply = new byte[1];
            return await pipe.ReadAsync(reply, timeout.Token).ConfigureAwait(false) == 1 && reply[0] == 1;
        }
        catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var request = new byte[1];
                if (await pipe.ReadAsync(request, timeout.Token).ConfigureAwait(false) == 1 && request[0] == 1)
                {
                    Action? activate;
                    lock (_gate) { activate = _activate; if (activate is null) _pending = true; }
                    activate?.Invoke();
                    await pipe.WriteAsync(request, timeout.Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (_stop.IsCancellationRequested) return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        _listener?.GetAwaiter().GetResult();
        _stop.Dispose();
        _mutex.Dispose();
    }
}
