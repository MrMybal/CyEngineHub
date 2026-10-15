using System.Windows.Input;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _packDownloadEstimateSummary = "Estimation non demandée. La taille finale du ZIP dépend du contenu et de la compression.";
    private readonly Dictionary<string, (DateTimeOffset Time, DirectFabDownloadEstimate Estimate)> _packEstimates = [];
    public string PackDownloadEstimateSummary
    {
        get => _packDownloadEstimateSummary;
        private set => SetProperty(ref _packDownloadEstimateSummary, value);
    }
    private AsyncRelayCommand? _estimatePackDownloadsCommand;
    public ICommand EstimatePackDownloadsCommand => _estimatePackDownloadsCommand ??= new AsyncRelayCommand(
        EstimatePackDownloadsAsync, onError: ShowError);

    private async Task EstimatePackDownloadsAsync()
    {
        var asset = SelectedRemoteFabAsset;
        var versions = FabInstallVersions.Where(option => option.IsSelected).Select(option => option.Version)
            .DistinctBy(version => version.ArtifactId, StringComparer.OrdinalIgnoreCase).ToArray();
        if (asset is null || versions.Length == 0) { PackDownloadEstimateSummary = "Sélectionnez au moins une variante."; return; }
        if (!IsMarketplaceAvailable(NormalizeAssetSource(asset))) { PackDownloadEstimateSummary = "Cette marketplace est désactivée."; return; }
        if (!IsLauncherFabAsset(asset)) { PackDownloadEstimateSummary = "Ce provider ne fournit pas de taille avant téléchargement. Taille inconnue (pas zéro)."; return; }
        PackDownloadEstimateSummary = "Lecture des manifests de la sélection…";
        long transfer = 0, raw = 0;
        var unknown = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        foreach (var version in versions)
        {
            try
            {
                var key = asset.Source + "|" + asset.Namespace + "|" + asset.AssetId + "|" + version.ArtifactId;
                DirectFabDownloadEstimate? estimate;
                if (_packEstimates.TryGetValue(key, out var cached) && cached.Time > DateTimeOffset.UtcNow.AddMinutes(-15)) estimate = cached.Estimate;
                else
                {
                    estimate = await _directFabClient.EstimateDownloadAsync(asset, version, timeout.Token).ConfigureAwait(true);
                    if (estimate is not null) _packEstimates[key] = (DateTimeOffset.UtcNow, estimate);
                }
                if (estimate is null) unknown++;
                else { transfer = checked(transfer + estimate.DownloadBytes); raw = checked(raw + estimate.UncompressedBytes); }
            }
            catch (Exception) { unknown++; }
        }
        if (SelectedRemoteFabAsset != asset || !FabInstallVersions.Where(option => option.IsSelected).Select(option => option.Version.ArtifactId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(versions.Select(version => version.ArtifactId))) return;
        PackDownloadEstimateSummary = $"{versions.Length} variante(s) · transfert connu : {FormatBytes(transfer)} · brut connu : {FormatBytes(raw)}" +
            (unknown > 0 ? $" · {unknown} taille(s) inconnue(s), total incomplet." : ".") +
            " ZIP final non prévisible ; prévoyez aussi l’espace temporaire pour l’extraction et le packaging.";
    }
}
