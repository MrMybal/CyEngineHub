using CyEngineHub.Storage.Analysis;
using CyEngineHub.Storage.Models;

namespace CyEngineHub.Storage.Tests;

public sealed class StorageOverviewTests
{
    [Fact]
    public void NestedAndSharedRootsAreNotCountedTwice()
    {
        using var directory = new StorageTestDirectory();
        var root = directory.CreateDirectory("library");
        var child = directory.CreateDirectory("library", "pack");
        var adjacent = directory.CreateDirectory("library-2");
        Assert.Equal(120, StorageOverview.SumDistinctRoots([
            Entry(child, 40), Entry(root, 100), Entry(root, 100), Entry(adjacent, 20)]));
    }

    [Fact]
    public async Task ProviderLibrariesAreMeasuredAndNeverCleanable()
    {
        using var directory = new StorageTestDirectory();
        var library = directory.CreateDirectory("packs");
        directory.CreateFile(Path.Combine("packs", "content.zip"), 150);
        var engine = directory.CreateDirectory("godot");
        directory.CreateFile(Path.Combine("godot", "editor.exe"), 50);
        var request = new StorageAnalysisRequest([], [], [])
        {
            AdditionalRoots = [new(StorageCategory.MarketplaceLibrary, "Fixture", library),
                new(StorageCategory.MarketplaceLibrary, "Same root", library),
                new(StorageCategory.EngineInstallation, "Godot", engine)],
        };
        var snapshot = await new StorageAnalysisService().AnalyzeAsync(request);
        var entry = Assert.Single(snapshot.Entries, entry => entry.Category == StorageCategory.MarketplaceLibrary);
        Assert.Equal(150, entry.SizeBytes);
        Assert.False(entry.IsSafeToClean);
        Assert.Equal(50, Assert.Single(snapshot.Entries, entry => entry.Category == StorageCategory.EngineInstallation).SizeBytes);
        Assert.Contains(snapshot.Drives, drive => drive.RootPath == Path.GetPathRoot(library));
    }

    [Fact]
    public async Task EmptyConfiguredFolderStillChecksDiskCapacity()
    {
        using var directory = new StorageTestDirectory();
        var absent = Path.Combine(directory.Root, "future-downloads");
        var snapshot = await new StorageAnalysisService().AnalyzeAsync(new([], [], [])
        {
            AdditionalRoots = [new(StorageCategory.MarketplaceDownloads, "Fixture", absent),
                new(StorageCategory.MarketplaceLibrary, "Invalid", "relative-path")],
        });
        Assert.Contains(snapshot.Drives, drive => drive.RootPath == Path.GetPathRoot(absent));
        Assert.DoesNotContain(snapshot.Entries, entry => entry.Path == absent);
        Assert.False(Directory.Exists(absent));
    }

    private static StorageEntry Entry(string path, long size) => new("fixture", StorageCategory.MarketplaceLibrary,
        StorageEntryRole.Footprint, "Fixture", "Fixture", path, path, size, 1, 1, null, StorageHealth.Ready, false, "");
}
