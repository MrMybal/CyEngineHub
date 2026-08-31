using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class MarketplaceLocalAssetIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyHubLocalIndexTests-" + Guid.NewGuid().ToString("N"));

    public MarketplaceLocalAssetIndexTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void MatchesIdentifiersAndLegacyTitlesWithoutCrossingMarketplaces()
    {
        var index = MarketplaceLocalAssetIndex.Build([new("Fab", "ABC", "My Pack!", _root)]);
        Assert.True(index.Contains("fab", "abc", "Renamed"));
        Assert.True(index.Contains("Fab", "legacy-id", "my-pack"));
        Assert.False(index.Contains("Quixel", "abc", "My Pack!"));
        Assert.False(index.Contains("Unity", "abc", "My Pack!"));
    }

    [Fact]
    public void QuixelAliasesAndRawVariantsShareTheSameAsset()
    {
        var index = MarketplaceLocalAssetIndex.Build([new("Quixel", "Plant", "Acacia", _root)]);
        Assert.True(index.Contains("megascans", "plant", ""));
        Assert.True(index.Contains("quixel", "PLANT", "Acacia"));
    }

    [Fact]
    public void SameNamedMegascansAreDistinctAssets()
    {
        var index = MarketplaceLocalAssetIndex.Build([new("Quixel", "plant-a", "Acacia", _root)]);
        Assert.True(index.Contains("quixel", "plant-a", "Acacia"));
        Assert.False(index.Contains("quixel", "plant-b", "Acacia"));
    }

    [Fact]
    public void MissingFoldersAndEmptyNamesAreNotDownloads()
    {
        var index = MarketplaceLocalAssetIndex.Build([
            new("Fab", "missing", "Missing", Path.Combine(_root, "absent")),
            new("Fab", "", "", _root),
            new("Fab", "empty-path", "Empty", "")]);
        Assert.False(index.Contains("Fab", "missing", "Missing"));
        Assert.False(index.Contains("Fab", "anything", ""));
        Assert.False(index.Contains("Fab", "empty-path", "Empty"));
    }

    [Fact]
    public void AnyRemainingVersionCountsAsDownloaded()
    {
        var index = MarketplaceLocalAssetIndex.Build([
            new("Fab", "pack", "Pack", Path.Combine(_root, "missing-version")),
            new("Fab", "pack", "Pack", _root),
            new("Fab", "pack", "Pack", _root)]);
        Assert.True(index.Contains("Fab", "pack", "Pack"));
    }

    [Fact]
    public void SnapshotQueriesDoNotProbeDiskAndRebuildDetectsExternalDeletion()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "download")).FullName;
        MarketplaceLocalAsset[] downloads = [new("Fab", "asset", "Asset", folder)];
        var index = MarketplaceLocalAssetIndex.Build(downloads);
        Directory.Delete(folder);
        Assert.True(index.Contains("Fab", "asset", "Asset"));
        Assert.False(MarketplaceLocalAssetIndex.Build(downloads).Contains("Fab", "asset", "Asset"));
    }

    [Fact]
    public void BuildHonorsCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => MarketplaceLocalAssetIndex.Build(
            [new("Fab", "asset", "Asset", _root)], cancellation.Token));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
