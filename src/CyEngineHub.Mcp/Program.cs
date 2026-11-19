using System.IO.Pipes;
using System.Text;
using CyEngineHub.Core.Ai;

var pipeName = HubMcpTransport.PipeName;
var command = args;
if (args is ["--pipe", var name, ..])
{
    pipeName = name;
    command = args[2..];
}
if (command is ["--help" or "-h" or "help"]) return McpCommands.Help("CyEngineHub.Mcp", "[--pipe NAME]");
if (command.Length > 0 && !McpCommands.IsCommand(command))
    return McpCommands.Failure("usage", McpCommands.Syntax, 2);

// In stdio mode stdout contains only MCP JSON-RPC.
try
{
    using var stop = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };
    await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(5000, stop.Token);
    using var reader = new StreamReader(pipe, new UTF8Encoding(false), leaveOpen: true);
    await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
    if (command.Length > 0)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        return await McpCommands.RunAsync(command, async (line, token) =>
        {
            await writer.WriteLineAsync(line.AsMemory(), token);
            return await reader.ReadLineAsync(token);
        }, stop.Token);
    }
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
    var message = "CyEngineHub MCP unavailable. Start the hub and enable Settings > IA / MCP. " + exception.Message;
    if (command.Length > 0) return McpCommands.Failure(exception is OperationCanceledException ? "cancelled" : "transport",
        message, exception is OperationCanceledException ? 130 : 1);
    await Console.Error.WriteLineAsync(message);
    return 1;
}
