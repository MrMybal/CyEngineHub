using System.Text;
using System.Text.Json;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Infrastructure.Processes;

// One-shot CyToolsCore ProcessWorker. Stdout is exactly one structured JSON value.
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = new UTF8Encoding(false);
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
try
{
    var workspace = Environment.GetEnvironmentVariable("CYHUB_BRIDGE_WORKSPACE")
        ?? throw new InvalidOperationException("Le host doit sélectionner CYHUB_BRIDGE_WORKSPACE.");
    var input = await Console.In.ReadToEndAsync().ConfigureAwait(false);
    if (input.Length > 2 * 1024 * 1024) throw new InvalidDataException("Message trop volumineux.");
    using var document = JsonDocument.Parse(input);
    var payload = document.RootElement;
    if (payload.GetProperty("protocolVersion").GetString() != "1.0") throw new InvalidDataException("Protocole worker invalide.");
    var parameters = payload.GetProperty("parameters");
    var service = new UnrealVersionBridgeAgentWorkspace(new SystemProcessRunner());
    var result = await service.ExecuteAsync(workspace, parameters.GetProperty("operation").GetString()!,
        parameters.GetProperty("arguments"), payload.GetProperty("workspace").GetString()!).ConfigureAwait(false);
    Console.Write(JsonSerializer.Serialize(new { ok = true, result }, jsonOptions));
    return 0;
}
catch (Exception exception) when (exception is not OutOfMemoryException)
{
    // A failed business operation is not a successful conversion.
    Console.Write(JsonSerializer.Serialize(new { ok = false, error = exception.Message }, jsonOptions));
    return 0;
}
