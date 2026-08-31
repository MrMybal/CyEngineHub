using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class RemoteFabFacetMetadataCacheTests
{
    [Fact]
    public void KeepsFirstAndSingleCategoryAndReusesCachedMetadata()
    {
        var cache = new RemoteFabFacetMetadataCache();
        var asset = Asset(["Environments"]);
        Assert.Equal(["Environments"], cache.Get(asset).Categories);
        Assert.Same(cache.Get(asset), cache.Get(asset));
        Assert.Equal(["Environments", "Nature"], cache.Get(asset with { Categories = ["Environments", "Nature"] }).Categories);
    }

    [Fact]
    public void OnlyEmptyMetadataUsesUncategorizedAndTrimsDuplicates()
    {
        var cache = new RemoteFabFacetMetadataCache();
        Assert.Equal(["Sans catégorie"], cache.Get(Asset(["", " "])).Categories);
        Assert.Equal(["Plants"], cache.Get(Asset([" Plants ", "plants"])).Categories);
    }

    [Fact]
    public void NewCatalogSnapshotInvalidatesMetadataForSameAssetId()
    {
        var cache = new RemoteFabFacetMetadataCache();
        var original = Asset(["Plants"]);
        var refreshed = original with { Categories = ["3D Plants"] };
        Assert.NotSame(cache.Get(original), cache.Get(refreshed));
        Assert.Equal(["3D Plants"], cache.Get(refreshed).Categories);
    }

    private static RemoteFabAsset Asset(string[] categories) =>
        new("asset", "ue", "Pack", "", "fab", "ASSET_PACK", null, null, categories, []);
}
