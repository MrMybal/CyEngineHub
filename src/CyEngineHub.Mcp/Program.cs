using System.IO.Pipes;
using System.Text;
using CyEngineHub.Core.Ai;

// Dedicated console process: stdout contains only MCP JSON-RPC, never GUI logs.
try
{
    using var stop = new CancellationTokenSource();
    // A custom name is useful for isolated integration tests or separate hub instances.
    var pipeName = args is ["--pipe", var name] ? name : HubMcpTransport.PipeName;
    if (args.Length != 0 && args is not ["--pipe", _]) throw new IOException("Usage: CyEngineHub.Mcp [--pipe NAME]");
    await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(5000);
    using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
    await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
    async Task ForwardAsync(TextReader from, TextWriter to, int maximumCharacters)
    {
        while (await HubMcpTransport.ReadLineAsync(from, maximumCharacters, stop.Token) is { } line)
        {
            await to.WriteLineAsync(line.AsMemory(), stop.Token);
            await to.FlushAsync(stop.Token);
        }
    }
    var input = ForwardAsync(Console.In, writer, HubMcpSession.MaximumMessageCharacters);
    var output = ForwardAsync(reader, Console.Out, int.MaxValue);
    var finished = await Task.WhenAny(input, output);
    await finished;
    await stop.CancelAsync();
    return 0;
}
catch (Exception exception) when (exception is IOException or TimeoutException or UnauthorizedAccessException or OperationCanceledException)
{
    await Console.Error.WriteLineAsync("CyEngineHub MCP unavailable. Start the hub and enable Settings > IA / MCP. " + exception.Message);
    return 1;
}
