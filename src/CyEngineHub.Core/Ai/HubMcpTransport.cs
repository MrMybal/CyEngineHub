using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace CyEngineHub.Core.Ai;

public static class HubMcpTransport
{
    public static string PipeName => "CyEngineHub.Mcp." + Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..24];

    public static Task<string?> ReadLineAsync(TextReader reader, CancellationToken token) =>
        ReadLineAsync(reader, HubMcpSession.MaximumMessageCharacters, token);

    public static async Task<string?> ReadLineAsync(TextReader reader, int maximumCharacters, CancellationToken token)
    {
        if (maximumCharacters == int.MaxValue)
            return await reader.ReadLineAsync(token).ConfigureAwait(false);
        var text = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), token).ConfigureAwait(false) != 0)
        {
            if (character[0] == '\n') return text.ToString().TrimEnd('\r');
            text.Append(character[0]);
            if (text.Length > maximumCharacters) throw new IOException("MCP message too large.");
        }
        return text.Length == 0 ? null : text.ToString();
    }

    public static async Task ServeAsync(IHubAiBackend backend, CancellationToken token, string? pipeName = null)
    {
        // Four independent sessions; same-account IPC only, no TCP listener or browser endpoint.
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ServeSlotAsync(backend, pipeName ?? PipeName, token))).ConfigureAwait(false);
    }

    private static async Task ServeSlotAsync(IHubAiBackend backend, string pipeName, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                var session = new HubMcpSession(backend);
                while (await ReadLineAsync(reader, token).ConfigureAwait(false) is { } line)
                {
                    var response = await session.HandleAsync(line, token).ConfigureAwait(false);
                    if (response is not null) await writer.WriteLineAsync(response.AsMemory(), token).ConfigureAwait(false);
                }
            }
            catch (IOException) { await Task.Delay(250, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        }
    }
}
