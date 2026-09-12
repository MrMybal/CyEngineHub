using System.Windows.Input;
using Avalonia.Threading;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _fabWebSynchronizing;
    public bool IsLibraryFabWebSelected => LibrarySource == FabWebLibraryClient.Source;
    public ICommand SyncFabWebCommand => OpenMegascansFabCommand;
    private static bool IsFabWebAsset(RemoteFabAsset asset) => asset.Source == FabWebLibraryClient.Source;
    private static bool IsLauncherFabAsset(RemoteFabAsset asset) => !IsQuixelAsset(asset) && !IsFabWebAsset(asset);

    private async Task LoadFabWebCacheAsync()
    {
        var snapshot = await new MarketplaceLibraryCacheStore(FabCacheDirectory).LoadAsync("fabweb").ConfigureAwait(true);
        if (snapshot is not null) ReplaceRemoteAssets(IsFabWebAsset, snapshot.Assets);
    }

    private async Task SynchronizeFabWebAsync(Func<string, CancellationToken, Task<string>> read, CancellationToken cancellation)
    {
        if (_fabWebSynchronizing) throw new InvalidOperationException("Une synchronisation Fab est déjà en cours.");
        _fabWebSynchronizing = true;
        try { await SynchronizeFabWebCoreAsync(read, cancellation).ConfigureAwait(true); }
        finally { _fabWebSynchronizing = false; }
    }

    private async Task SynchronizeFabWebCoreAsync(Func<string, CancellationToken, Task<string>> read, CancellationToken cancellation)
    {
        await RequireMegascansFabConnectorAsync().ConfigureAwait(true);
        var cache = new MarketplaceLibraryCacheStore(FabCacheDirectory);
        var client = new FabWebLibraryClient((path, token) =>
            Dispatcher.UIThread.InvokeAsync(() => read(path, token)));
        var progress = new Progress<int>(count => StatusMessage = $"Megascans via Fab · {count:N0} fiche(s) acquise(s) reçue(s)…");
        var assets = await client.SynchronizeAsync(progress, cancellation).ConfigureAwait(true);
        cancellation.ThrowIfCancellationRequested();
        await Task.Run(() => cache.SaveAsync(new MarketplaceLibraryCacheSnapshot(DateTimeOffset.UtcNow, "fabweb", assets, assets.Count), cancellation), cancellation).ConfigureAwait(true);
        ReplaceRemoteAssets(IsFabWebAsset, assets);
        SelectLibrarySource(FabWebLibraryClient.Source);
        await RefreshRemoteFabViewNowAsync().ConfigureAwait(true);
        StatusMessage = $"Megascans via Fab · {assets.Count:N0} fiches acquises synchronisées · accessibles au MCP.";
    }

    private async Task OpenFabWebAssetAsync(RemoteFabAsset asset)
    {
        var connector = await RequireMegascansFabConnectorAsync().ConfigureAwait(true);
        await OpenMarketplaceBrowserAsync(connector.Id, connector.Name,
            asset.ListingUri ?? new Uri(connector.InventoryUrl)).ConfigureAwait(true);
    }
}
