using System.IO.Pipes;
using System.Text;
using CyEngineHub.Core.Ai;

var utf8 = new UTF8Encoding(false);
Console.InputEncoding = utf8;
Console.OutputEncoding = utf8;
var pipeName = HubMcpTransport.PipeName;
var command = args;
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; stop.Cancel(); };

if (args is ["--url", var address, ..])
{
    if (!McpHttpConnection.TryGetEndpoint(address, out var endpoint))
        return McpCommands.Failure("usage", "Use a local MCP URL of the form http://127.0.0.1:PORT/mcp.", 2);
    command = args[2..];
    string? token = null;
    if (command is ["--token-env", var variable, ..])
    {
        token = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(token))
            return McpCommands.Failure("usage", "Set the environment variable named by --token-env.", 2);
        command = command[2..];
    }
    if (!McpCommands.IsCommand(command)) return McpCommands.Failure("usage", McpCommands.Syntax, 2);
    await using var connection = new McpHttpConnection(endpoint, token);
    return await McpCommands.RunAsync(command, connection.ExchangeAsync, stop.Token);
}

if (args is ["--pipe", var name, ..])
{
    pipeName = name;
    command = args[2..];
}
if (command is ["--help" or "-h" or "help"])
    return McpCommands.Help("CyEngineHub.Mcp", "[--pipe NAME | --url URL [--token-env NAME]]");
if (command.Length > 0 && !McpCommands.IsCommand(command))
    return McpCommands.Failure("usage", McpCommands.Syntax, 2);

// In stdio mode stdout contains only MCP JSON-RPC.
try
{
    await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    await pipe.ConnectAsync(5000, stop.Token);
    using var reader = new StreamReader(pipe, utf8, leaveOpen: true);
    await using var writer = new StreamWriter(pipe, utf8, leaveOpen: true) { AutoFlush = true };
    if (command.Length > 0)
    {
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
