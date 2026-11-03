using System.IO.Compression;
using System.Text.Json;
using CyEngineHub.Core.Marketplaces;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Storage;

namespace CyEngineHub.Epic.Tests;

public sealed class KitBashConnectorPackageTests : IDisposable
{
    private const string Id = "cyenginehub.marketplace-kitbash3d";
    private readonly string _root = Directory.CreateTempSubdirectory("cyhub-kitbash-test-").FullName;
    private string Scratch => Path.Combine(_root, "staging");
    private MarketplaceStorageProfile Storage => new(Id, _root, _root, [new(Path.Combine(_root, "library"))]);

    [Theory]
    [InlineData(LibraryPackageStorageMode.Uncompressed)]
    [InlineData(LibraryPackageStorageMode.ZipFastest)]
    public async Task LocalKitKeepsDependenciesSourceIdentityAndOriginals(LibraryPackageStorageMode mode)
    {
        var source = Path.Combine(_root, "My.Kit");
        Directory.CreateDirectory(Path.Combine(source, "Models"));
        Directory.CreateDirectory(Path.Combine(source, "Textures", "2K"));
        await File.WriteAllTextAsync(Path.Combine(source, "Models", "Tower.fbx"), "model fixture");
        await File.WriteAllTextAsync(Path.Combine(source, "Textures", "2K", "Wall.png"), "texture fixture");
        var package = await ConnectorLibraryPackageService.ImportAsync(source, Id, mode, Storage, Scratch);
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(package.ManifestPath!));
        Assert.Equal(Id, manifest.RootElement.GetProperty("source").GetString());
        Assert.Equal("My.Kit", manifest.RootElement.GetProperty("title").GetString());
        Assert.Equal("rawAsset", manifest.RootElement.GetProperty("contentKind").GetString());
        var extracted = Path.Combine(_root, "materialized");
        await DerivedLibraryPackageService.MaterializeAsync(package.DestinationDirectory, extracted, LibraryPackageContentKind.RawAsset);
        Assert.Equal("model fixture", await File.ReadAllTextAsync(Path.Combine(extracted, "Models", "Tower.fbx")));
        Assert.Equal("texture fixture", await File.ReadAllTextAsync(Path.Combine(extracted, "Textures", "2K", "Wall.png")));
        Assert.Equal("model fixture", await File.ReadAllTextAsync(Path.Combine(source, "Models", "Tower.fbx")));
        Assert.Equal(2, Directory.GetFiles(source, "*", SearchOption.AllDirectories).Length);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Scratch));
        Assert.Single(ConnectorLibraryPackageService.DiscoverPackages(Id, Storage.Libraries.Select(item => item.Path)));
        Assert.Empty(ConnectorLibraryPackageService.DiscoverPackages("cyenginehub.marketplace-cosmos", Storage.Libraries.Select(item => item.Path)));
    }

    [Theory]
    [InlineData("Content/Kit/Tower.uasset", "Pack Unreal")]
    [InlineData("Models/Tower.usd", "Assets RAW")]
    public async Task DownloadedZipIsCataloguedWithoutPretendingUsdIsAnUnrealPack(string entry, string expected)
    {
        var archive = Path.Combine(_root, "owned.zip");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        { using var writer = new StreamWriter(zip.CreateEntry(entry).Open()); writer.Write("fixture"); }
        var before = await File.ReadAllBytesAsync(archive);
        var catalog = new MarketplaceDownloadCatalog(Path.Combine(_root, "catalog.json"));
        Assert.Equal(expected, (await catalog.RegisterAsync(Id, archive)).AssetKind);
        var result = await ConnectorLibraryPackageService.ImportAsync(archive, Id, LibraryPackageStorageMode.ZipFastest, Storage, Scratch);
        var asset = await catalog.RegisterAsync(Id, result.DestinationDirectory);
        Assert.Equal(expected, asset.AssetKind);
        await catalog.RenameAsync(asset.Id, "My personal name", "notes");
        var refreshed = await catalog.RegisterAsync(Id, result.DestinationDirectory);
        Assert.Equal(asset.Id, refreshed.Id); Assert.Equal("My personal name", refreshed.Name); Assert.Equal("notes", refreshed.Notes);
        Assert.Equal(before, await File.ReadAllBytesAsync(archive));
    }

    [Fact]
    public async Task FailedPlacementRemovesOnlyOwnedStaging()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "Original")).FullName;
        var file = Path.Combine(source, "Tower.fbx"); await File.WriteAllTextAsync(file, "unchanged");
        var full = Storage with { Libraries = [new(Path.Combine(_root, "full"), 1)] };
        await Assert.ThrowsAsync<IOException>(() => ConnectorLibraryPackageService.ImportAsync(source, Id, LibraryPackageStorageMode.Uncompressed, full, Scratch));
        Assert.Equal("unchanged", await File.ReadAllTextAsync(file));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Scratch));
    }

    [Fact]
    public async Task EmptyKitNestedDestinationAndCancellationAreRejectedWithoutChangingOriginals()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "Kit")).FullName;
        await Assert.ThrowsAsync<InvalidDataException>(() => ConnectorLibraryPackageService.MaterializeDirectoryAsync(source, Path.Combine(_root, "empty")));
        await File.WriteAllTextAsync(Path.Combine(source, "Tower.fbx"), "fixture");
        await Assert.ThrowsAsync<InvalidDataException>(() => ConnectorLibraryPackageService.MaterializeDirectoryAsync(source, Path.Combine(source, "nested")));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConnectorLibraryPackageService.ImportAsync(source, Id, LibraryPackageStorageMode.ZipFastest, Storage, Scratch, token: stop.Token));
        Assert.Single(Directory.GetFiles(source));
        Assert.False(Directory.Exists(Path.Combine(source, "nested")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
