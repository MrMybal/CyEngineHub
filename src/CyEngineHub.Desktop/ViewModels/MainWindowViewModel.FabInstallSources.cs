using CyEngineHub.Core.Tasks;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static string FabPackResourceKey(RemoteFabAsset asset) =>
        "library-pack:" + NormalizeAssetSource(asset) + ":" + asset.AssetId;

    // Call on the UI thread: the stored-download index is also updated there.
    private FabStoredDownloadRecord? FindLocalFabInstallSource(RemoteFabAsset asset, RemoteFabProjectVersion version) =>
        _fabStoredDownloads.LastOrDefault(item =>
            item.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase) &&
            item.Source.Equals(NormalizeAssetSource(asset), StringComparison.OrdinalIgnoreCase) &&
            item.ArtifactId.Equals(version.ArtifactId, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(item.Path));

    private HubTaskResources FabInstallationResources(RemoteFabAsset asset,
        IEnumerable<string> targetPaths, IEnumerable<RemoteFabProjectVersion> versions,
        MarketplaceStorageProfile storage)
    {
        var sources = versions.Select(version => FindLocalFabInstallSource(asset, version)).ToArray();
        var allLocal = sources.All(source => source is not null);
        var targets = targetPaths.ToArray();
        var disks = targets.Concat(sources.Where(source => source is not null).Select(source => source!.Path))
            .Append(DataDirectory);
        if (!allLocal) disks = disks.Append(storage.DownloadDirectory).Append(storage.CacheDirectory)
            .Concat(storage.Libraries.Where(library => library.Enabled).Select(library => library.Path));
        return new HubTaskResources
        {
            ExclusivePaths = targets,
            ExclusiveKeys = allLocal ? [] : [FabPackResourceKey(asset)],
            SharedKeys = allLocal ? [FabPackResourceKey(asset)] : [],
            DiskPaths = disks.Where(path => !string.IsNullOrWhiteSpace(path)).ToArray(),
        };
    }
}
