namespace CyEngineHub.Fab.Remote;

/// <summary>A snapshot of complete local downloads; build off the UI thread, query without disk I/O.</summary>
public sealed class MarketplaceLocalAssetIndex
{
    private readonly HashSet<(string Source, string Id)> _ids = [];
    private readonly HashSet<(string Source, string Title)> _titles = [];

    public static MarketplaceLocalAssetIndex Empty { get; } = new();

    public static MarketplaceLocalAssetIndex Build(
        IEnumerable<MarketplaceLocalAsset> downloads,
        CancellationToken cancellationToken = default)
    {
        var index = new MarketplaceLocalAssetIndex();
        var paths = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var download in downloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(download.Path)) continue;
            if (!paths.TryGetValue(download.Path, out var exists))
                paths[download.Path] = exists = Directory.Exists(download.Path);
            if (!exists) continue;
            var source = NormalizeSource(download.Source);
            if (!string.IsNullOrWhiteSpace(download.AssetId))
                index._ids.Add((source, download.AssetId.ToUpperInvariant()));
            var title = NormalizeTitle(download.Title);
            // Quixel reuses titles (e.g. "Acacia") for many distinct assets.
            // Only legacy Fab/Vault records need a title fallback.
            if (source == "FAB" && title.Length > 0) index._titles.Add((source, title));
        }
        return index;
    }

    public bool Contains(string source, string assetId, string? title)
    {
        var key = NormalizeSource(source);
        return _ids.Contains((key, assetId.ToUpperInvariant())) ||
            _titles.Contains((key, NormalizeTitle(title)));
    }

    private static string NormalizeSource(string source) =>
        source.Equals("quixel", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("megascan", StringComparison.OrdinalIgnoreCase) ||
        source.Equals("megascans", StringComparison.OrdinalIgnoreCase)
            ? "QUIXEL" : source.ToUpperInvariant();

    private static string NormalizeTitle(string? value) => new(
        (value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
}

public sealed record MarketplaceLocalAsset(string Source, string AssetId, string? Title, string Path);
