using System.Text.Json;

namespace CyEngineHub.Core.Ai;

public sealed record HubAiVariant(string Id, string[] EngineVersions, string[] Platforms);
public sealed record HubAiAsset(string Id, string Source, string Name, string Description,
    string[] Categories, HubAiVariant[] Variants, string[] LocalPaths,
    string? ProductUrl = null, string? ThumbnailUrl = null, string? DownloadMode = null);
public sealed record HubAiTarget(string Id, string Name, string EngineVersion, string Kind);
public sealed record HubAiDownloadEstimate(string AssetId, string Source, string VariantId,
    long? DownloadBytes, long? UncompressedBytes, int? FileCount, string SizeSource);
public sealed record HubAiProposal(string Id, string Action, string AssetId, string Source,
    string? VariantId, string? TargetId, string[] Categories, string Reason, DateTimeOffset CreatedAt);

/// <summary>No credentials, arbitrary paths, shell commands or automatic approvals cross this boundary.</summary>
public interface IHubAiBackend
{
    Task<HubMcpOptions> GetOptionsAsync(CancellationToken cancellationToken) => Task.FromResult(new HubMcpOptions());
    Task<HubAiDownloadEstimate> EstimateDownloadAsync(string assetId, string source, string variantId, CancellationToken cancellationToken) =>
        Task.FromResult(new HubAiDownloadEstimate(assetId, source, variantId, null, null, null, "unknown"));
    Task<IReadOnlyList<HubAiAsset>> GetAssetsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<HubAiTarget>> GetTargetsAsync(CancellationToken cancellationToken);
    Task<object> GetTasksAsync(CancellationToken cancellationToken);
    Task<object> ProposeAsync(HubAiProposal proposal, CancellationToken cancellationToken);
    Task<object> GetProposalsAsync(CancellationToken cancellationToken);
}

public static class HubAiArguments
{
    public static string Text(JsonElement args, string name, bool required = false, int max = 512)
    {
        if (!args.TryGetProperty(name, out var value))
        {
            if (required) throw new ArgumentException($"Missing {name}.");
            return "";
        }
        if (value.ValueKind != JsonValueKind.String) throw new ArgumentException($"{name} must be a string.");
        var text = value.GetString()!.Trim();
        if (text.Length > max || (required && text.Length == 0)) throw new ArgumentException($"Invalid {name} length.");
        return text;
    }

    public static int Number(JsonElement args, string name, int fallback, int maximum)
    {
        if (!args.TryGetProperty(name, out var value)) return fallback;
        if (!value.TryGetInt32(out var number) || number < 0 || number > maximum)
            throw new ArgumentException($"{name} must be between 0 and {maximum}.");
        return number;
    }
}
