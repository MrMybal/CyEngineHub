using CyEngineHub.Core.Marketplaces;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Extensions;

namespace CyEngineHub.Core.Tests;

public sealed class KitBashConnectorTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cyhub-kitbash-vm-").FullName;

    [Fact]
    public async Task KitBashUsesSharedStorageWithoutClaimingAuthentication()
    {
        MarketplaceConnectorViewModel Create(string id) => new(new(new()
        {
            Id = id, DisplayName = "KitBash3D · Cargo", Marketplace = new() { InventoryUrl = "https://cargo-app.kitbash3d.com/account/my-assets" }
        }, _root, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null), true, _root,
            _ => Task.CompletedTask, () => Task.CompletedTask, _ => { }, _ => Task.CompletedTask);
        var kitbash = Create("cyenginehub.marketplace-kitbash3d");
        Assert.True(kitbash.HasPackageStorage);
        Assert.Contains("ALPHA", kitbash.SettingsHeading, StringComparison.Ordinal);
        Assert.Contains("session gérée par le site", kitbash.SettingsSubtitle, StringComparison.Ordinal);
        kitbash.IsEnabled = false; await kitbash.PendingWrite;
        Assert.False(kitbash.OpenInventoryCommand.CanExecute(null));
        Assert.True(Create("cyenginehub.marketplace-cosmos").HasPackageStorage);
        Assert.False(Create("cyenginehub.marketplace-megascans-fab").HasPackageStorage);
    }

    [Fact]
    public async Task DirectoryImportAndInstallationAreExplicitAndOptional()
    {
        const string id = "cyenginehub.marketplace-kitbash3d";
        var catalog = new MarketplaceDownloadCatalog(Path.Combine(_root, "catalog.json"));
        var file = Path.Combine(_root, "fixture.fbx"); await File.WriteAllTextAsync(file, "fixture");
        var asset = await catalog.RegisterAsync(id, file);
        var imported = new TaskCompletionSource<string>();
        var prepared = new TaskCompletionSource<MarketplaceLocalAsset>();
        var refreshes = 0;
        var library = new ConnectorLibraryViewModel(id, "KitBash3D", catalog, () => Task.CompletedTask,
            () => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask,
            path => { imported.TrySetResult(path); return Task.CompletedTask; },
            item => { prepared.TrySetResult(item); return Task.CompletedTask; },
            () => { refreshes++; return Task.CompletedTask; });
        Assert.True(library.SupportsDirectoryImport); Assert.True(library.SupportsInstallation);
        Assert.False(library.PrepareInstallationCommand.CanExecute(null));
        Assert.False(library.ImportDirectoryPathCommand.CanExecute(null));
        library.ImportDirectoryPath = _root;
        library.ImportDirectoryPathCommand.Execute(null);
        Assert.Equal(_root, await imported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await library.LoadAsync(file);
        Assert.True(refreshes > 0); Assert.True(library.PrepareInstallationCommand.CanExecute(null));
        library.PrepareInstallationCommand.Execute(null);
        Assert.Equal(asset.Id, (await prepared.Task.WaitAsync(TimeSpan.FromSeconds(5))).Id);
        library.BackCommand.Execute(null);
        Assert.False(library.PrepareInstallationCommand.CanExecute(null));
        var legacy = new ConnectorLibraryViewModel("other", "Other", catalog, () => Task.CompletedTask, () => Task.CompletedTask, _ => Task.CompletedTask);
        Assert.False(legacy.SupportsDirectoryImport); Assert.False(legacy.SupportsInstallation);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
