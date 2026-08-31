using System.Runtime.CompilerServices;

namespace CyEngineHub.Fab.Remote;

public sealed record RemoteFabFacetMetadata(string[] Categories, string[] EngineVersions);

/// <summary>
/// Reuses normalized metadata while a catalog snapshot is alive. A refreshed asset
/// gets a new entry; old snapshots can be collected without an unbounded cache.
/// </summary>
public sealed class RemoteFabFacetMetadataCache
{
    private readonly ConditionalWeakTable<RemoteFabAsset, RemoteFabFacetMetadata> _entries = new();

    public RemoteFabFacetMetadata Get(RemoteFabAsset asset) => _entries.GetValue(asset, item =>
    {
        var categories = item.Categories
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new RemoteFabFacetMetadata(
            categories.Length == 0 ? ["Sans catégorie"] : categories,
            item.ProjectVersions.SelectMany(version => version.EngineVersions)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    });
}
