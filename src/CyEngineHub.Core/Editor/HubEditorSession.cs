using System.Text.Json;
using CyEngineHub.Core.Ai;

namespace CyEngineHub.Core.Editor;

public sealed record HubEditorProject(string Id, string Name, string EngineVersion);
public interface IHubEditorBackend
{
    Task<HubEditorProject> ResolveProjectAsync(string projectFile, CancellationToken token);
    Task<IReadOnlyList<HubAiAsset>> GetAssetsAsync(CancellationToken token);
    Task<object> InstallAsync(HubEditorProject project, string assetId, string source, string variantId, string requestId, CancellationToken token);
    Task<object> GetTasksAsync(HubEditorProject project, CancellationToken token);
    Task<object> ReviewAsync(HubEditorProject project, string assetId, string source, CancellationToken token) =>
        throw new InvalidOperationException("Open the desktop hub to review this product.");
}

/// <summary>Editor protocol: no shell, credentials, arbitrary destinations or AI permissions.</summary>
public sealed class HubEditorSession(IHubEditorBackend backend)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public async Task<string> HandleAsync(string line, CancellationToken token)
    {
        try
        {
            if (line.Length > HubMcpSession.MaximumMessageCharacters) throw new ArgumentException("Request too large.");
            using var document = JsonDocument.Parse(line);
            var args = document.RootElement;
            if (args.ValueKind != JsonValueKind.Object || HubAiArguments.Number(args, "protocol", 0, 1) != 1)
                throw new ArgumentException("Unsupported editor protocol.");
            var project = await backend.ResolveProjectAsync(HubAiArguments.Text(args, "projectFile", true, 4096), token).ConfigureAwait(false);
            var editorVersion = HubAiArguments.Text(args, "engineVersion", true, 32);
            if (!Compatible(new("editor", [editorVersion], ["Win64"]), project.EngineVersion))
                throw new InvalidOperationException("Le moteur ouvert ne correspond pas à l’association du projet dans CyEngineHub. Actualisez cette association avant l’installation.");
            var method = HubAiArguments.Text(args, "method", true);
            object result;
            if (method == "connect") result = project;
            else if (method == "tasks") result = await backend.GetTasksAsync(project, token).ConfigureAwait(false);
            else
            {
                var assets = await backend.GetAssetsAsync(token).ConfigureAwait(false);
                if (method == "search")
                {
                    var query = HubAiArguments.Text(args, "query");
                    var source = HubAiArguments.Text(args, "source");
                    var offset = HubAiArguments.Number(args, "offset", 0, 1_000_000);
                    var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var matches = assets.Where(a => (source.Length == 0 || a.Source.Equals(source, StringComparison.OrdinalIgnoreCase)) &&
                        terms.All(term => (a.Name + " " + string.Join(' ', a.Categories)).Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
                    result = new { project, total = matches.Length, offset, results = matches.Skip(offset).Take(40).Select(a => new {
                        a.Id, a.Source, a.Name, downloaded = a.LocalPaths.Length > 0,
                        variants = a.Variants.Where(v => Compatible(v, project.EngineVersion)).ToArray(), a.DownloadMode }) };
                }
                else if (method == "review")
                {
                    var id = HubAiArguments.Text(args, "assetId", true);
                    var source = HubAiArguments.Text(args, "source", true);
                    var asset = assets.FirstOrDefault(a => a.Id == id && a.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException("Asset absent from the enabled owned catalog.");
                    result = await backend.ReviewAsync(project, asset.Id, asset.Source, token).ConfigureAwait(false);
                }
                else if (method == "install")
                {
                    if (!args.TryGetProperty("confirmed", out var confirmed) || confirmed.ValueKind != JsonValueKind.True)
                        throw new ArgumentException("Explicit editor confirmation required.");
                    var id = HubAiArguments.Text(args, "assetId", true);
                    var source = HubAiArguments.Text(args, "source", true);
                    var variant = HubAiArguments.Text(args, "variantId", true);
                    var requestId = HubAiArguments.Text(args, "requestId", true, 64);
                    if (!Guid.TryParseExact(requestId, "N", out _)) throw new ArgumentException("Invalid requestId.");
                    var asset = assets.FirstOrDefault(a => a.Id == id && a.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException("Asset absent from the enabled owned catalog.");
                    if (!asset.Variants.Any(v => v.Id == variant && Compatible(v, project.EngineVersion)))
                        throw new ArgumentException("Choose an exact compatible variant; conversions must be reviewed in the hub.");
                    result = await backend.InstallAsync(project, asset.Id, asset.Source, variant, requestId, token).ConfigureAwait(false);
                }
                else throw new ArgumentException("Unknown editor method.");
            }
            return JsonSerializer.Serialize(new { ok = true, result }, JsonOptions);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        { return JsonSerializer.Serialize(new { ok = false, error = exception.Message }, JsonOptions); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return "{\"ok\":false,\"error\":\"Hub operation failed. Consult hub diagnostics; no automatic retry.\"}"; }
    }

    public static bool Compatible(HubAiVariant variant, string engineVersion)
    {
        static string Branch(string value) => string.Join('.', value.Split('.').Take(2));
        return variant.EngineVersions.Any(v => Branch(v) == Branch(engineVersion)) &&
            (variant.Platforms.Length == 0 || variant.Platforms.Any(p => p.Equals("Windows", StringComparison.OrdinalIgnoreCase) || p.Equals("Win64", StringComparison.OrdinalIgnoreCase)));
    }
}
