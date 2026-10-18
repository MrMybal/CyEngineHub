using System.Collections.Concurrent;
using System.Text.Json;
using System.Windows.Input;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Storage;
using CyEngineHub.Infrastructure.AppData;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private sealed record SavedLibraryDownload(RemoteFabAsset Asset, RemoteFabProjectVersion Version,
        MarketplaceStorageProfile Storage, LibraryPackageStorageMode Mode, DateTimeOffset CreatedAt,
        string State = "Pending", DirectFabDownloadResult? Result = null);
    private DurableDownloadJournal<SavedLibraryDownload>? _downloadJournal;
    private DurableDownloadJournal<SavedLibraryDownload> DownloadJournal => _downloadJournal ??= new(
        Path.Combine(AppDataPaths.Resolve().RootDirectory, "Downloads", "queue-v1.json"));
    private readonly Dictionary<Guid, string> _durableDownloadTasks = [];
    private readonly SemaphoreSlim _storedDownloadsPersistenceGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> AssetDownloadLocks = new(StringComparer.OrdinalIgnoreCase);
    private int _downloadConcurrency = 3;
    private int _engineChunkConcurrency = 12;
    private int _assetDownloadConnections = 12;
    private int _quixelDownloadConnections = 12;
    private bool _resumeDownloadsOnStartup = true;
    private string _downloadQueueStatus = "File persistante Fab / Megascans · téléchargements seuls. Installations et moteurs exclus.";
    private string _downloadRecoveryPath = "";
    internal bool ImportDownloadRecoveryOnStartup { get; set; }
    public int DownloadConcurrency { get => _downloadConcurrency; set => SetProperty(ref _downloadConcurrency, value); }
    public int EngineChunkConcurrency { get => _engineChunkConcurrency; set => SetProperty(ref _engineChunkConcurrency, value); }
    // Legacy shared setting kept for migration and API compatibility.
    public int AssetDownloadConnections { get => _assetDownloadConnections; set { FabDownloadConnections = value; QuixelDownloadConnections = value; OnPropertyChanged(); } }
    public int FabDownloadConnections { get => _assetDownloadConnections; set => SetProperty(ref _assetDownloadConnections, value); }
    public int QuixelDownloadConnections { get => _quixelDownloadConnections; set => SetProperty(ref _quixelDownloadConnections, value); }
    public bool ResumeDownloadsOnStartup { get => _resumeDownloadsOnStartup; set => SetProperty(ref _resumeDownloadsOnStartup, value); }
    public string DownloadQueueStatus { get => _downloadQueueStatus; private set => SetProperty(ref _downloadQueueStatus, value); }
    public string DownloadRecoveryPath { get => _downloadRecoveryPath; set => SetProperty(ref _downloadRecoveryPath, value); }
    private ICommand? _saveDownloadQueueSettingsCommand, _resumeDownloadQueueCommand, _importDownloadRecoveryCommand, _showDownloadSettingsCommand;
    public ICommand ShowDownloadSettingsCommand => _showDownloadSettingsCommand ??= new RelayCommand(() =>
    {
        OpenSettings();
        SelectedSettingsCategory = SettingsCategories.Single(category => category.Id == "downloads");
    });
    public ICommand SaveDownloadQueueSettingsCommand => _saveDownloadQueueSettingsCommand ??= new AsyncRelayCommand(async () =>
    {
        if (DownloadConcurrency is < 1 or > 16) throw new InvalidOperationException("Choisissez de 1 à 16 téléchargements simultanés.");
        if (EngineChunkConcurrency is < 1 or > 32) throw new InvalidOperationException("Choisissez de 1 à 32 connexions de chunks Unreal.");
        if (AssetDownloadConnections is < 1 or > 32) throw new InvalidOperationException("Choisissez de 1 à 32 connexions par téléchargement Fab / Megascans.");
        if (QuixelDownloadConnections is < 1 or > 32) throw new InvalidOperationException("Choisissez de 1 à 32 connexions par téléchargement Megascans.");
        await _settings.SetAsync("downloads.queue.options.v1", JsonSerializer.Serialize(new DownloadQueueOptions(DownloadConcurrency, ResumeDownloadsOnStartup, EngineChunkConcurrency, FabDownloadConnections, QuixelDownloadConnections))).ConfigureAwait(true);
        _taskScheduler.SetDownloadConcurrency(DownloadConcurrency);
        _epicBinaryEngineBackend.SetChunkConcurrency(EngineChunkConcurrency);
        _directFabClient.SetDownloadConnections(AssetDownloadConnections);
        _quixelClient.SetDownloadConnections(QuixelDownloadConnections);
        DownloadQueueStatus = $"Enregistré · {DownloadConcurrency} packs simultanés maximum · {EngineChunkConcurrency} connexions Unreal · Fab : {FabDownloadConnections} · Megascans : {QuixelDownloadConnections}. S’applique aux prochains transferts ; les tâches actives ne sont pas interrompues.";
    }, onError: ShowError);
    public ICommand ResumeDownloadQueueCommand => _resumeDownloadQueueCommand ??= new AsyncRelayCommand(async () =>
    {
        var engineRequests = 0;
        foreach (var download in EpicEngineDownloads.Where(item => item.CanRetry && item.StatusLabel == "Échec").ToArray())
            if (download.RetryCommand.CanExecute(null)) { download.RetryCommand.Execute(null); engineRequests++; }
        await ResumeLibraryDownloadsAsync(true).ConfigureAwait(true);
        if (engineRequests > 0) DownloadQueueStatus = $"{engineRequests} demande(s) de reprise moteur · " + DownloadQueueStatus;
    }, onError: ShowError);
    public ICommand ImportDownloadRecoveryCommand => _importDownloadRecoveryCommand ??= new AsyncRelayCommand(ImportDownloadRecoveryAsync, onError: ShowError);
    private sealed record DownloadQueueOptions(int Concurrency = 3, bool Resume = true, int EngineChunkConcurrency = 12, int AssetDownloadConnections = 12, int? QuixelDownloadConnections = null);
    private static string DurableDownloadId(RemoteFabAsset asset, RemoteFabProjectVersion version) =>
        NormalizeAssetSource(asset) + ":" + asset.AssetId + ":" + version.ArtifactId;

    private async Task LoadDownloadQueueAsync()
    {
        try
        {
            var json = await _settings.GetAsync("downloads.queue.options.v1").ConfigureAwait(true);
            var options = json is null ? new DownloadQueueOptions() : JsonSerializer.Deserialize<DownloadQueueOptions>(json) ?? new();
            DownloadConcurrency = Math.Clamp(options.Concurrency, 1, 16);
            ResumeDownloadsOnStartup = options.Resume;
            EngineChunkConcurrency = Math.Clamp(options.EngineChunkConcurrency, 1, 32);
            _epicBinaryEngineBackend.SetChunkConcurrency(EngineChunkConcurrency);
            AssetDownloadConnections = Math.Clamp(options.AssetDownloadConnections, 1, 32);
            QuixelDownloadConnections = Math.Clamp(options.QuixelDownloadConnections ?? options.AssetDownloadConnections, 1, 32);
            _directFabClient.SetDownloadConnections(AssetDownloadConnections);
            _quixelClient.SetDownloadConnections(QuixelDownloadConnections);
            _taskScheduler.SetDownloadConcurrency(DownloadConcurrency);
            if (ImportDownloadRecoveryOnStartup) await ImportDownloadRecoveryAsync().ConfigureAwait(true);
            if (ResumeDownloadsOnStartup || ImportDownloadRecoveryOnStartup) await ResumeLibraryDownloadsAsync(false).ConfigureAwait(true);
            if (ImportDownloadRecoveryOnStartup) AppendRawLog("INFO", "Downloads/Recovery", DownloadQueueStatus);
        }
        catch (Exception exception)
        {
            DownloadQueueStatus = "Reprise non effectuée : " + exception.Message;
            AppendRawLog("WARN", "Downloads/Recovery", DownloadQueueStatus);
        }
    }

    private async Task ObserveDurableDownloadAsync(Task completion)
    {
        try { await completion.ConfigureAwait(true); }
        catch (Exception exception) { DownloadQueueStatus = "Téléchargement non terminé : " + exception.Message; }
    }

    private bool HasStoredVariant(RemoteFabAsset asset, RemoteFabProjectVersion version) => _fabStoredDownloads.Any(item =>
        item.AssetId == asset.AssetId && item.Source.Equals(NormalizeAssetSource(asset), StringComparison.OrdinalIgnoreCase)
        && item.ArtifactId == version.ArtifactId && Directory.Exists(item.Path)
        && !string.IsNullOrEmpty(item.ManifestPath) && File.Exists(item.ManifestPath));

    private async Task ResumeLibraryDownloadsAsync(bool includeFailed)
    {
        var resumed = 0;
        var skipped = 0;
        foreach (var pair in DownloadJournal.Read().OrderBy(item => item.Value.CreatedAt))
        {
            var entry = pair.Value;
            if (entry.State == "Completed" && entry.Result is { } completedResult && !HasStoredVariant(entry.Asset, entry.Version)
                && Directory.Exists(completedResult.DestinationDirectory) && File.Exists(completedResult.ManifestPath))
                await SaveStoredFabDownloadAsync(entry.Asset, new(entry.Version, false), completedResult).ConfigureAwait(true);
            if (entry.State != "Pending" && !(includeFailed && entry.State == "Failed")) continue;
            if (!IsMarketplaceAvailable(NormalizeAssetSource(entry.Asset))) { skipped++; continue; }
            if (entry.Result is { } result && Directory.Exists(result.DestinationDirectory) && File.Exists(result.ManifestPath))
            {
                await SaveStoredFabDownloadAsync(entry.Asset, new(entry.Version, false), result).ConfigureAwait(true);
                DownloadJournal.Upsert(pair.Key, entry with { State = "Completed" });
                continue;
            }
            if (HasStoredVariant(entry.Asset, entry.Version))
            { DownloadJournal.Upsert(pair.Key, entry with { State = "Completed" }); continue; }
            var completion = DownloadSelectedFabVersionsAsync(FabInstallDestination.Library, entry.Asset,
                [new(entry.Version, true)], resumedEntry: entry);
            if (completion.IsFaulted) await completion.ConfigureAwait(true);
            _ = ObserveDurableDownloadAsync(completion);
            resumed++;
        }
        DownloadQueueStatus = $"{resumed} demande(s) de reprise · {skipped} en attente d’une marketplace activée. Les échecs se relancent avec Reprendre.";
    }

    private async Task ImportDownloadRecoveryAsync()
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(DownloadRecoveryPath).ConfigureAwait(true));
        var prepared = new List<SavedLibraryDownload>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var assetId = item.GetProperty("assetId").GetString();
            var source = item.GetProperty("source").GetString();
            var variantId = item.GetProperty("variantId").GetString();
            var asset = _remoteFabCatalog.FirstOrDefault(asset => asset.AssetId == assetId && NormalizeAssetSource(asset) == source)
                ?? throw new InvalidOperationException($"Asset absent du cache : {assetId}. Rechargez le catalogue avant l’import.");
            if (IsFabWebAsset(asset)) throw new InvalidOperationException("Import de file : le téléchargement web reste manuel.");
            var version = asset.ProjectVersions.FirstOrDefault(version => version.ArtifactId == variantId)
                ?? throw new InvalidOperationException($"Variante absente : {variantId}.");
            prepared.Add(new(asset, version, ResolveMarketplaceStorageProfile(asset), ResolveMarketplaceStorageMode(asset),
                item.GetProperty("createdAt").GetDateTimeOffset()));
        }
        var existing = DownloadJournal.Read();
        foreach (var entry in prepared)
        {
            var id = DurableDownloadId(entry.Asset, entry.Version);
            if (existing.ContainsKey(id) || HasStoredVariant(entry.Asset, entry.Version)) continue;
            DownloadJournal.Upsert(id, entry);
        }
        DownloadQueueStatus = "Sauvegarde importée. Cliquez sur Reprendre pour lancer les téléchargements manquants.";
    }
}
