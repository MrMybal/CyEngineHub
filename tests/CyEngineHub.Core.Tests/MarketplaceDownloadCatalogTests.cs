using System.IO.Compression;
using System.Text.Json;
using CyEngineHub.Core.Marketplaces;
using CyEngineHub.Desktop.ViewModels;

namespace CyEngineHub.Core.Tests;

public sealed class MarketplaceDownloadCatalogTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cyhub-local-catalog-tests-").FullName;
    private string CatalogPath => Path.Combine(_root, "index", "catalog.json");
    private MarketplaceDownloadCatalog Catalog => new(CatalogPath);

    private string CreateFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, "existing local file");
        return path;
    }

    [Fact]
    public async Task CompletedFilePersistsAcrossInstancesWithoutMovingIt()
    {
        var file = CreateFile("My_Pack.rar");
        var before = File.ReadAllBytes(file);
        var asset = await Catalog.RegisterAsync("cosmos", file);
        Assert.Equal(asset, Assert.Single(await Catalog.LoadAsync()));
        Assert.Equal("My Pack", asset.Name);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.Equal(file, asset.FilePath);
    }

    [Fact]
    public async Task RetryingRegistrationPreservesUserMetadataAndIdentity()
    {
        var file = CreateFile("pack.rar");
        var first = await Catalog.RegisterAsync("cosmos", file);
        await Catalog.RenameAsync(first.Id, "My custom asset", "My notes");
        var retry = await Catalog.RegisterAsync("COSMOS", file);
        Assert.Equal(first.Id, retry.Id);
        Assert.Equal("My custom asset", retry.Name);
        Assert.Equal("My notes", retry.Notes);
        Assert.Single(await Catalog.LoadAsync());
    }

    [Fact]
    public async Task ConcurrentCompletionsDoNotLoseEntries()
    {
        var files = Enumerable.Range(0, 12).Select(i => CreateFile($"pack-{i}.rar")).ToArray();
        await Task.WhenAll(files.Select(file => Catalog.RegisterAsync("cosmos", file)));
        Assert.Equal(12, (await Catalog.LoadAsync()).Count);
    }

    [Fact]
    public async Task SimultaneousRetriesCreateOnlyOneEntry()
    {
        var file = CreateFile("same.rar");
        var entries = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Catalog.RegisterAsync("cosmos", file)));
        Assert.Single(entries.Select(item => item.Id).Distinct());
        Assert.Single(await Catalog.LoadAsync());
    }

    [Fact]
    public async Task DifferentFilesAndMarketplacesAreNotMergedByName()
    {
        var first = CreateFile("pack.rar");
        Directory.CreateDirectory(Path.Combine(_root, "other"));
        var second = CreateFile("other/pack.rar");
        await Catalog.RegisterAsync("cosmos", first);
        await Catalog.RegisterAsync("cosmos", second);
        await Catalog.RegisterAsync("other-marketplace", first);
        Assert.Equal(3, (await Catalog.LoadAsync()).Count);
    }

    [Fact]
    public async Task EmptyAndMissingFilesAreNotPublished()
    {
        var empty = CreateFile("empty.rar");
        File.WriteAllText(empty, "");
        await Assert.ThrowsAsync<InvalidDataException>(() => Catalog.RegisterAsync("cosmos", empty));
        await Assert.ThrowsAsync<InvalidDataException>(() => Catalog.RegisterAsync("cosmos", Path.Combine(_root, "missing.zip")));
        Assert.Empty(await Catalog.LoadAsync());
    }

    [Fact]
    public async Task CorruptCatalogIsNeverOverwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath)!);
        File.WriteAllText(CatalogPath, "{ corrupt data");
        await Assert.ThrowsAsync<JsonException>(() => Catalog.RegisterAsync("cosmos", CreateFile("pack.rar")));
        Assert.Equal("{ corrupt data", File.ReadAllText(CatalogPath));
    }

    [Theory]
    [InlineData("Demo/Demo.uproject", "Projet Unreal")]
    [InlineData("Plugin/Plugin.uplugin", "Plugin Unreal")]
    [InlineData("Content/Mesh.uasset", "Pack Unreal")]
    [InlineData("Assets/Mesh.fbx", "Assets RAW")]
    [InlineData("Readme.txt", "Archive ZIP")]
    public async Task ClassifiesArchiveWithoutExtractingIt(string entry, string expected)
    {
        var file = Path.Combine(_root, "pack.zip");
        using (var archive = ZipFile.Open(file, ZipArchiveMode.Create)) archive.CreateEntry(entry);
        var asset = await Catalog.RegisterAsync("cosmos", file);
        Assert.Equal(expected, asset.AssetKind);
        Assert.False(File.Exists(Path.Combine(_root, entry)));
    }

    [Fact]
    public async Task MalformedZipIsVisibleButNotMisidentifiedAsAnInstallablePack()
    {
        var asset = await Catalog.RegisterAsync("cosmos", CreateFile("broken.zip"));
        Assert.Equal("ZIP illisible · à vérifier", asset.AssetKind);
    }

    [Fact]
    public async Task NativeLibraryUsesContainsAndDoesNotMixMarketplaces()
    {
        await Catalog.RegisterAsync("cosmos", CreateFile("SciFi_Space_Station.rar"));
        await Catalog.RegisterAsync("cosmos", CreateFile("Nordic_Castle.rar"));
        await Catalog.RegisterAsync("other", CreateFile("Other_Space.rar"));
        var library = MakeLibrary();
        await library.LoadAsync();
        Assert.True(library.IsListVisible);
        Assert.Equal(2, library.Assets.Count);
        library.Search = "SPACE";
        Assert.Equal("SciFi Space Station", Assert.Single(library.Assets).Name);
        library.Search = "does not exist";
        Assert.True(library.IsEmpty);
    }

    [Fact]
    public async Task BrowserLinkOpensMatchingNativeProductEvenWithSearchFilter()
    {
        var file = CreateFile("Space.rar");
        await Catalog.RegisterAsync("cosmos", file);
        var library = MakeLibrary();
        library.Search = "hidden";
        await library.LoadAsync(file);
        Assert.True(library.IsProductVisible);
        Assert.Equal(file, library.SelectedAsset!.Asset.FilePath);
        library.EditName = "Unsaved edit";
        await library.LoadAsync();
        Assert.Equal("Unsaved edit", library.EditName);
        library.BackCommand.Execute(null);
        Assert.True(library.IsListVisible);
    }

    [Fact]
    public async Task RemovedLocalFileRemainsInCatalogWithMissingIndicator()
    {
        var file = CreateFile("Moved.rar");
        await Catalog.RegisterAsync("cosmos", file);
        File.Delete(file);
        var library = MakeLibrary();
        await library.LoadAsync();
        Assert.False(Assert.Single(library.Assets).IsAvailable);
        Assert.Single(await Catalog.LoadAsync());
    }

    private ConnectorLibraryViewModel MakeLibrary() => new("cosmos", "Cosmos", Catalog,
        () => Task.CompletedTask, () => Task.CompletedTask, _ => Task.CompletedTask);

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
