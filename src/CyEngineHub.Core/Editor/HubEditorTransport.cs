using System.IO.Pipes;
using System.Text;
using CyEngineHub.Core.Ai;

namespace CyEngineHub.Core.Editor;

public static class HubEditorTransport
{
    public static string PipeName => HubMcpTransport.PipeName.Replace(".Mcp.", ".Editor.", StringComparison.Ordinal);
    public static Task ServeAsync(IHubEditorBackend backend, CancellationToken token, string? pipeName = null) =>
        Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ServeSlotAsync(backend, pipeName ?? PipeName, token)));

    private static async Task ServeSlotAsync(IHubEditorBackend backend, string pipeName, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var line = await HubMcpTransport.ReadLineAsync(reader, timeout.Token).ConfigureAwait(false);
                if (line is not null)
                    await writer.WriteLineAsync((await new HubEditorSession(backend).HandleAsync(line, timeout.Token).ConfigureAwait(false)).AsMemory(), timeout.Token).ConfigureAwait(false);
            }
            catch (IOException) { await Task.Delay(100, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }
}
