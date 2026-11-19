using System.Text.Json;

namespace CyEngineHub.Core.Ai;

/// <summary>Script commands over the same MCP session used by agents.</summary>
public static class McpCommands
{
    public const string Syntax = "list | describe NAME | call NAME [--json OBJECT | --json-file PATH]";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly JsonElement Empty = JsonSerializer.SerializeToElement(new { });

    public static int Help(string executable, string selector)
    {
        Write(Console.Out, new { usage = $"{executable} {selector} [{Syntax}]", stdio = "Omit the command for MCP stdio.",
            exitCodes = new { success = 0, transport = 1, usage = 2, operation = 3, cancelled = 130 } });
        return 0;
    }

    public static int Failure(string code, string message, int exitCode)
    {
        Write(Console.Out, new { error = new { code, message } });
        return exitCode;
    }

    public static bool IsCommand(string[] args) =>
        args is ["list"] or ["describe", _] or ["call", _] or ["call", _, "--json" or "--json-file", _];

    public static async Task<int> RunAsync(string[] args,
        Func<string, CancellationToken, Task<string?>> exchange, CancellationToken token, bool waitForRepair = false)
    {
        if (!IsCommand(args)) return Failure("usage", Syntax, 2);
        var arguments = Empty;
        if (args is ["call", _, var option, var input])
        {
            try
            {
                if (option == "--json-file")
                {
                    if (new FileInfo(input).Length > HubMcpSession.MaximumMessageCharacters)
                        return Failure("usage", "The argument file is too large.", 2);
                    input = await File.ReadAllTextAsync(input, token).ConfigureAwait(false);
                }
                if (input.Length > HubMcpSession.MaximumMessageCharacters)
                    return Failure("usage", "The argument object is too large.", 2);
                using var parsed = JsonDocument.Parse(input);
                if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                    return Failure("usage", "Arguments must be a JSON object.", 2);
                arguments = parsed.RootElement.Clone();
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
            { return Failure("usage", error.Message, 2); }
        }

        var id = 0;
        async Task<JsonElement> Request(string method, object parameters)
        {
            var requestId = ++id;
            var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = requestId, method, @params = parameters }, Json);
            var line = await exchange(request, token).ConfigureAwait(false)
                ?? throw new IOException("MCP closed the connection without replying.");
            using var response = JsonDocument.Parse(line);
            var value = response.RootElement;
            if (!value.TryGetProperty("id", out var responseId) || !responseId.TryGetInt32(out var actualId) || actualId != requestId)
                throw new IOException("Unexpected MCP response ID.");
            if (value.TryGetProperty("error", out var error))
                throw new McpCommandError(error.GetProperty("message").GetString() ?? "MCP error.");
            return value.GetProperty("result").Clone();
        }

        async Task<JsonElement> Call(string name, JsonElement toolArguments)
        {
            var result = await Request("tools/call", new { name, arguments = toolArguments }).ConfigureAwait(false);
            if (result.TryGetProperty("isError", out var failed) && failed.ValueKind == JsonValueKind.True)
                throw new McpCommandError(ReadToolText(result));
            if (result.TryGetProperty("structuredContent", out var structured)) return structured;
            using var parsed = JsonDocument.Parse(ReadToolText(result));
            return parsed.RootElement.Clone();
        }

        try
        {
            await Request("initialize", new { protocolVersion = "2025-06-18", capabilities = new { },
                clientInfo = new { name = "CyEngineHub CLI", version = "1.0" } }).ConfigureAwait(false);
            if (args[0] == "call")
            {
                var result = await Call(args[1], arguments).ConfigureAwait(false);
                if (waitForRepair && args[1].StartsWith("op_", StringComparison.Ordinal))
                {
                    var identity = JsonSerializer.SerializeToElement(new { jobId = result.GetProperty("jobId").GetString() });
                    while (result.GetProperty("state").GetString() is not ("Completed" or "Failed" or "Cancelled"))
                    {
                        await Task.Delay(100, token).ConfigureAwait(false);
                        result = await Call("cy_get_job", identity).ConfigureAwait(false);
                    }
                    if (result.GetProperty("state").GetString() != "Completed")
                    {
                        Write(Console.Out, result);
                        return 3;
                    }
                }
                Write(Console.Out, result);
                return 0;
            }

            var tools = new List<JsonElement>();
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            do
            {
                var page = await Request("tools/list", cursor is null ? new { } : new { cursor }).ConfigureAwait(false);
                tools.AddRange(page.GetProperty("tools").EnumerateArray());
                cursor = page.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
                if (cursor is not null && (!cursors.Add(cursor) || cursors.Count >= 1000))
                    throw new IOException("Invalid or excessive MCP catalogue pagination.");
            } while (cursor is not null);
            if (args[0] == "list") Write(Console.Out, new { tools });
            else
            {
                var tool = tools.FirstOrDefault(item => item.GetProperty("name").GetString() == args[1]);
                if (tool.ValueKind == JsonValueKind.Undefined) return Failure("unknown_tool", $"Unknown tool: {args[1]}.", 3);
                Write(Console.Out, tool);
            }
            return 0;
        }
        catch (McpCommandError error) { return Failure("operation", error.Message, 3); }
        catch (OperationCanceledException) { return Failure("cancelled", "Command cancelled.", 130); }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        { return Failure("transport", error.Message, 1); }
    }

    private static string ReadToolText(JsonElement result) =>
        result.GetProperty("content")[0].GetProperty("text").GetString() ?? "";

    private static void Write(TextWriter writer, object value) => writer.WriteLine(JsonSerializer.Serialize(value, Json));

    private sealed class McpCommandError(string message) : Exception(message);
}
