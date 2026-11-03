using System.IO.Compression;
using System.Text.Json;
using CyEngineHub.Core.Marketplaces;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Epic.Tests;

public sealed class MultiMarketplacePackageTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cyhub-markets-").FullName;

    [Theory]
    [InlineData("itchio")]
    [InlineData("blendkit")]
    [InlineData("artstation")]
    [InlineData("gumroad")]
    [InlineData("cgtrader")]
    public async Task EachMarketplaceRetainsExactIdentityOriginalAndLocalVariants(string slug)
    {
        var id = "cyenginehub.marketplace-" + slug;
        var zip = Path.Combine(_root, "original.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        { using var writer = new StreamWriter(archive.CreateEntry("Content/Props/Asset.uasset").Open()); writer.Write("fixture"); }
        var before = await File.ReadAllBytesAsync(zip);
        var storage = Path.Combine(_root, "library");
        var result = await ConnectorLibraryPackageService.ImportAsync(zip, id, LibraryPackageStorageMode.ZipFastest,
            new(id, _root, _root, [new(storage)]), Path.Combine(_root, "staging"));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath!));
        Assert.Equal(id, manifest.RootElement.GetProperty("source").GetString());
        Assert.Equal("contentPack", manifest.RootElement.GetProperty("contentKind").GetString());
        var catalog = new MarketplaceDownloadCatalog(Path.Combine(_root, "catalog.json"));
        var entry = await catalog.RegisterAsync(id, result.DestinationDirectory);
        Assert.Equal(id, entry.MarketplaceId); Assert.Equal("Pack Unreal", entry.AssetKind);
        Assert.Equal(before, await File.ReadAllBytesAsync(zip));
        Assert.Empty(ConnectorLibraryPackageService.DiscoverPackages("another-market", [storage]));
        Assert.Single(ConnectorLibraryPackageService.DiscoverPackages(id, [storage]));
    }

    [Theory]
    [InlineData("Scene.blend")]
    [InlineData("Model.max")]
    [InlineData("Manual.pdf")]
    [InlineData("Game.exe")]
    public async Task DccDocumentsAndExecutablesAreStoredWithoutClaimingUnrealCompatibility(string file)
    {
        var id = "cyenginehub.marketplace-gumroad";
        var source = Path.Combine(_root, file); await File.WriteAllTextAsync(source, "not executable fixture");
        var result = await ConnectorLibraryPackageService.ImportAsync(source, id, LibraryPackageStorageMode.Uncompressed,
            new(id, _root, _root, [new(Path.Combine(_root, "library"))]), Path.Combine(_root, "staging"));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath!));
        Assert.Equal("unknown", manifest.RootElement.GetProperty("contentKind").GetString());
        var entry = await new MarketplaceDownloadCatalog(Path.Combine(_root, "catalog.json")).RegisterAsync(id, result.DestinationDirectory);
        Assert.DoesNotContain("Unreal", entry.AssetKind, StringComparison.Ordinal);
        Assert.Equal("not executable fixture", await File.ReadAllTextAsync(source));
    }

    [Fact]
    public async Task BlendFileWithTexturesIsNotMistakenForAnImportableTexturePack()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "Blendkit asset")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "Model.blend"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(source, "Diffuse.png"), "fixture");
        const string id = "cyenginehub.marketplace-blendkit";
        var result = await ConnectorLibraryPackageService.ImportAsync(source, id, LibraryPackageStorageMode.ZipFastest,
            new(id, _root, _root, [new(Path.Combine(_root, "library"))]), Path.Combine(_root, "staging"));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(result.ManifestPath!));
        Assert.Equal("unknown", manifest.RootElement.GetProperty("contentKind").GetString());
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
