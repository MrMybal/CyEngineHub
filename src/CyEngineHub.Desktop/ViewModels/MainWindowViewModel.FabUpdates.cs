using System.Text.Json;
using System.Windows.Input;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly JsonSerializerOptions FabRevisionJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, FabUpdateReport> _fabUpdateReports = new(StringComparer.OrdinalIgnoreCase);
    private bool _isCheckingFabUpdates;
    private string _fabUpdateSummary = "Les versions locales Fab sont vérifiées après actualisation du catalogue.";
    public bool IsCheckingFabUpdates => _isCheckingFabUpdates;
    public string FabUpdateSummary => _fabUpdateSummary;
    public ICommand CheckFabUpdatesCommand => new AsyncRelayCommand(RefreshDirectFabLibraryAsync,
        () => IsDirectFabConnected && IsFabLibrarySourceAvailable && !IsFabLibraryRefreshing && !_isCheckingFabUpdates, ShowError);

    public void CheckFabAssetUpdates(RemoteFabAssetCardViewModel card) =>
        _ = CheckFabAssetUpdatesSafeAsync(card.Asset.AssetId);

    private async Task CheckFabAssetUpdatesSafeAsync(string id)
    {
        try { await CheckDownloadedFabUpdatesAsync(id).ConfigureAwait(true); }
        catch (Exception exception) { ShowError(exception); }
    }

    private void ApplyFabUpdateReport(RemoteFabAssetCardViewModel card)
    {
        if (!IsLauncherFabAsset(card.Asset)) return;
        if (_fabUpdateReports.TryGetValue(card.Asset.AssetId, out var report)) card.SetSupplierUpdate(report.Label, report.Available);
        else card.SetSupplierUpdate(string.Empty, false);
    }

    private async Task CheckDownloadedFabUpdatesAsync(string? onlyAssetId = null)
    {
        if (_isCheckingFabUpdates || !IsDirectFabConnected) return;
        // Never inspect/alter derivatives, installed project copies, Quixel or web-only metadata.
        var local = _fabStoredDownloads.Where(item => item.Source.Equals("Fab", StringComparison.OrdinalIgnoreCase) &&
            item.SourceArtifactId is null && !item.ArtifactId.StartsWith("derived:", StringComparison.OrdinalIgnoreCase) &&
            !item.StorageMode.Equals("DerivedVariant", StringComparison.OrdinalIgnoreCase) &&
            (onlyAssetId is null || item.AssetId.Equals(onlyAssetId, StringComparison.OrdinalIgnoreCase))).ToArray();
        var ids = local.Select(item => item.AssetId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assets = _remoteFabCatalog.Where(asset => IsLauncherFabAsset(asset) && ids.Contains(asset.AssetId)).ToArray();
        if (assets.Length == 0)
        {
            SetProperty(ref _fabUpdateSummary, "Aucun pack Fab fournisseur téléchargé à vérifier.", nameof(FabUpdateSummary));
            return;
        }
        SetProperty(ref _isCheckingFabUpdates, true, nameof(IsCheckingFabUpdates));
        SetProperty(ref _fabUpdateSummary, "Vérification des manifests des packs Fab téléchargés…", nameof(FabUpdateSummary));
        var results = new System.Collections.Concurrent.ConcurrentDictionary<string, FabUpdateReport>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var handle = _taskScheduler.Enqueue(new HubTaskDefinition("Bibliothèque · Fab", "Vérifier les mises à jour des packs téléchargés",
                [new HubTaskLink("library", "Fab", "Voir la bibliothèque Fab")], async (context, cancellationToken) =>
                {
                    var done = 0;
                    await Parallel.ForEachAsync(assets, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellationToken }, async (asset, token) =>
                    {
                        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        var available = false; var unknown = false;
                        var originals = local.Where(item => item.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase)).ToArray();
                        foreach (var group in originals.GroupBy(item => item.ArtifactId, StringComparer.OrdinalIgnoreCase))
                        {
                            var version = asset.ProjectVersions.FirstOrDefault(item => item.ArtifactId.Equals(group.Key, StringComparison.OrdinalIgnoreCase));
                            if (version is null)
                            {
                                unknown = true;
                                foreach (var item in group) versions[item.Path] = "Version fournisseur retirée ou non exposée · non vérifiable";
                                continue;
                            }
                            try
                            {
                                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                                var remote = await _directFabClient.GetSourceManifestAsync(asset, version, timeout.Token).ConfigureAwait(false);
                                var states = group.Select(item => (Download: item, State: FabPackageRevision.Compare(ReadLocalFabRevision(item), remote))).ToArray();
                                // An older retained copy is not an outstanding update if the latest copy is already downloaded.
                                var state = states.Any(item => item.State == FabPackageUpdateState.Current) ? FabPackageUpdateState.Current :
                                    states.Any(item => item.State == FabPackageUpdateState.UpdateAvailable) ? FabPackageUpdateState.UpdateAvailable : FabPackageUpdateState.Unknown;
                                available |= state == FabPackageUpdateState.UpdateAvailable;
                                unknown |= state == FabPackageUpdateState.Unknown;
                                foreach (var item in states)
                                    versions[item.Download.Path] = item.State switch
                                    {
                                        FabPackageUpdateState.Current => "À jour · contenu vérifié",
                                        FabPackageUpdateState.UpdateAvailable => "Mise à jour fournisseur disponible · télécharger les versions pour la conserver à côté",
                                        _ => "Révision non vérifiable · empreinte locale absente ou manifest indisponible",
                                    };
                            }
                            catch (Exception exception) when (exception is DirectFabException or HttpRequestException or IOException or JsonException or OperationCanceledException)
                            {
                                token.ThrowIfCancellationRequested();
                                unknown = true;
                                foreach (var item in group) versions[item.Path] = "Vérification indisponible · réessayer avec la connexion Fab";
                            }
                        }
                        var known = originals.Select(item => item.ArtifactId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        var newVersions = asset.ProjectVersions.Any(version => !known.Contains(version.ArtifactId));
                        available |= newVersions;
                        var label = available ? newVersions ? "Nouvelle version / variante Fab disponible" : "Mise à jour Fab disponible" :
                            unknown ? "Révision Fab non vérifiable" : "Fab à jour · contenu vérifié";
                        results[asset.AssetId] = new(available, unknown, label, versions);
                        var count = Interlocked.Increment(ref done);
                        context.Report((double)count / assets.Length, $"{count}/{assets.Length} pack(s) vérifié(s) · métadonnées uniquement");
                    }).ConfigureAwait(false);
                }));
            var completed = await handle.Completion.ConfigureAwait(true);
            foreach (var result in results) _fabUpdateReports[result.Key] = result.Value;
            foreach (var card in GetRealizedRemoteFabCards()) ApplyFabUpdateReport(card);
            if (SelectedRemoteFabAssetCard is { } selectedCard) ApplyFabUpdateReport(selectedCard);
            if (SelectedRemoteFabAsset is { } selected) RebuildSelectedFabStoredDownloads(selected.AssetId);
            var summary = completed.Status == HubTaskStatus.Completed
                ? $"Fab : {results.Values.Count(item => item.Available)} pack(s) avec mise à jour ou variante disponible · {results.Values.Count(item => item.Unknown)} non vérifiable(s). Aucun asset téléchargé ni projet modifié."
                : completed.Status == HubTaskStatus.Canceled ? "Vérification Fab annulée · résultats partiels conservés." : $"Vérification Fab interrompue : {completed.Error}";
            SetProperty(ref _fabUpdateSummary, summary, nameof(FabUpdateSummary));
        }
        finally { SetProperty(ref _isCheckingFabUpdates, false, nameof(IsCheckingFabUpdates)); }
    }

    private static DirectFabSourceManifest? ReadLocalFabRevision(FabStoredDownloadRecord stored)
    {
        if (!Directory.Exists(stored.Path) && !File.Exists(stored.Path)) return null;
        if (stored.SourceManifest is not null) return stored.SourceManifest;
        var path = stored.ManifestPath ?? Path.Combine(stored.Path, LibraryPackageService.ManifestFileName);
        if (!File.Exists(path) || new FileInfo(path).Length > 4 * 1024 * 1024) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("sourceManifest", out var manifest)
                ? manifest.Deserialize<DirectFabSourceManifest>(FabRevisionJsonOptions) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }

    private sealed record FabUpdateReport(bool Available, bool Unknown, string Label, Dictionary<string, string> Versions);
}
