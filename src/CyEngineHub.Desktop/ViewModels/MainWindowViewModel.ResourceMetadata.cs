using System.Text.Json;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Storage.Analysis;
using CyEngineHub.Storage.Models;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string ResourceFavoritesSetting = "workspace.favorites.v1";
    private HashSet<string> _resourceFavorites = new(StringComparer.OrdinalIgnoreCase);
    private Task _favoriteWrite = Task.CompletedTask;
    private bool _favoritesOnly, _favoritesFirst = true;
    public bool FavoritesOnly { get => _favoritesOnly; set { if (SetProperty(ref _favoritesOnly, value)) { SaveInterfacePreference("interface.favorites.only.v1", value); ApplyRemoteFabFilter(); } } }
    public bool FavoritesFirst { get => _favoritesFirst; set { if (SetProperty(ref _favoritesFirst, value)) { SaveInterfacePreference("interface.favorites.first.v1", value); ApplyRemoteFabFilter(); } } }
    public bool IsResourceSizeBusy { get; private set; }
    private Dictionary<string, StorageEntry> _resourceMeasurements = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, long?> _assetStoredSizes = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _partialAssetSizes = new(StringComparer.OrdinalIgnoreCase);
    private int _packMetadataGeneration;
    private CyEngineHub.Core.Marketplaces.MarketplaceLocalAsset[] _connectorFootprints = [];
    private sealed record FootprintTarget(StorageCategory Category, string Name, string Path);

    public static string ResourceIdentity(object item) => item switch
    {
        RemoteFabAssetCardViewModel card => AssetIdentity(card.Asset),
        RemoteFabAsset asset => AssetIdentity(asset),
        EngineCardViewModel engine => "engine|" + NormalizeResourcePath(engine.Engine.RootPath),
        LocalEngineInstallationCardViewModel engine => "engine|" + NormalizeResourcePath(engine.Installation.RootPath),
        ProjectCardViewModel project => "project|" + NormalizeResourcePath(project.Project.RootPath),
        LocalEngineProjectCardViewModel project => "project|" + NormalizeResourcePath(project.Project.RootPath),
        FabLocalContentPackCardViewModel pack => "pack|" + NormalizeResourcePath(pack.Path),
        QuixelLocalAssetCardViewModel pack => "asset|Quixel|" + pack.AssetId,
        ConnectorAssetViewModel pack => "pack|" + NormalizeResourcePath(pack.Asset.FilePath),
        QuixelCollectionCardViewModel collection => "collection|Quixel|" + collection.Collection.Id,
        _ => item.GetType().Name + "|" + ReadResourcePath(item),
    };
    private static string AssetIdentity(RemoteFabAsset asset) => "asset|" + NormalizeAssetSource(asset) + "|" + asset.AssetId;
    private static string NormalizeResourcePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }
    private static string ReadResourcePath(object item) => item switch
    {
        EngineCardViewModel engine => engine.Engine.RootPath,
        LocalEngineInstallationCardViewModel engine => engine.Installation.RootPath,
        ProjectCardViewModel project => project.Project.RootPath,
        LocalEngineProjectCardViewModel project => project.Project.RootPath,
        ConnectorAssetViewModel pack => pack.Asset.FilePath,
        _ => item.GetType().GetProperty("Path")?.GetValue(item)?.ToString() ?? "",
    };
    public bool IsResourceFavorite(object item) => _resourceFavorites.Contains(ResourceIdentity(item));
    public void ToggleResourceFavorite(object item)
        => SetResourceFavorites([item], !IsResourceFavorite(item));
    public void SetResourceFavorites(IEnumerable<object> items, bool favorite)
    {
        var changed = false;
        foreach (var item in items)
            changed |= favorite ? _resourceFavorites.Add(ResourceIdentity(item)) : _resourceFavorites.Remove(ResourceIdentity(item));
        if (!changed) return;
        var json = JsonSerializer.Serialize(_resourceFavorites.Order(StringComparer.OrdinalIgnoreCase));
        _favoriteWrite = SaveFavoritesAsync(_favoriteWrite, json);
        ApplyRemoteFabFilter();
    }
    private async Task SaveFavoritesAsync(Task previous, string json)
    {
        try { await previous.ConfigureAwait(true); } catch (Exception) { }
        try { await _settings.SetAsync(ResourceFavoritesSetting, json).ConfigureAwait(true); }
        catch (Exception error) { ShowError(error); }
    }
    private async Task LoadResourceFavoritesAsync()
    {
        var json = await _settings.GetAsync(ResourceFavoritesSetting).ConfigureAwait(true);
        try { _resourceFavorites = new((json is null ? [] : JsonSerializer.Deserialize<string[]>(json) ?? []).Take(50_000), StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { _resourceFavorites.Clear(); AppendRawLog("WARN", "Favorites", "Liste de favoris illisible, aucun fichier modifié."); }
        _favoritesOnly = bool.TryParse(await _settings.GetAsync("interface.favorites.only.v1").ConfigureAwait(true), out var only) && only;
        _favoritesFirst = !bool.TryParse(await _settings.GetAsync("interface.favorites.first.v1").ConfigureAwait(true), out var first) || first;
        ApplyRemoteFabFilter();
    }
    private RemoteFabAsset[] ApplyFavoriteAssets(RemoteFabAsset[] assets)
    {
        var filtered = assets.Where(asset => !FavoritesOnly || IsResourceFavorite(asset));
        long? Size(RemoteFabAsset asset) => _assetStoredSizes.GetValueOrDefault(AssetIdentity(asset));
        filtered = SelectedLibrarySort switch
        {
            "Taille locale · croissant" => filtered.OrderBy(asset => !Size(asset).HasValue).ThenBy(Size),
            "Taille locale · décroissant" => filtered.OrderBy(asset => !Size(asset).HasValue).ThenByDescending(Size),
            _ => filtered,
        };
        return (FavoritesFirst ? filtered.OrderByDescending(IsResourceFavorite) : filtered).ToArray();
    }
    private void IndexResourceMeasurements(StorageSnapshot snapshot)
    {
        _resourceMeasurements = snapshot.Entries.Where(entry => entry.Role == StorageEntryRole.Footprint)
            .GroupBy(entry => NormalizeResourcePath(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
    }
    public long? ResourceSizeBytes(object item)
    {
        if (item is RemoteFabAssetCardViewModel asset) return _assetStoredSizes.GetValueOrDefault(AssetIdentity(asset.Asset));
        var path = NormalizeResourcePath(ReadResourcePath(item));
        if (_resourceMeasurements.TryGetValue(path, out var measured)) return measured.SizeBytes;
        return item is ConnectorAssetViewModel connector ? connector.Asset.SizeBytes : null;
    }
    public string ResourceSizeLabel(object item)
    {
        var bytes = ResourceSizeBytes(item);
        if (!bytes.HasValue) return "Taille : à analyser";
        var path = NormalizeResourcePath(ReadResourcePath(item));
        var partial = item is RemoteFabAssetCardViewModel card ? _partialAssetSizes.Contains(AssetIdentity(card.Asset))
            : _resourceMeasurements.TryGetValue(path, out var measured) && measured.Health == StorageHealth.Partial;
        var value = (double)bytes.Value; var unit = 0;
        string[] units = ["o", "Kio", "Mio", "Gio", "Tio"];
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return (partial ? "≥ " : "") + $"{value:0.##} {units[unit]}" + (partial ? " (partielle)" : "");
    }
    public string ResourceSizeEvidence(object item)
    {
        if (item is RemoteFabAssetCardViewModel card)
        {
            var rows = _fabStoredDownloads.Where(row => IsStoredDownloadForAsset(row, card.Asset.AssetId, card.Name, NormalizeAssetSource(card.Asset))).ToArray();
            return rows.Any(row => _resourceMeasurements.GetValueOrDefault(NormalizeResourcePath(row.Path))?.Id.StartsWith("pack-metadata:", StringComparison.Ordinal) == true)
                ? "Taille stockée déclarée (manifeste / historique), hors fichiers annexes. Tout revérifier mesure l’espace disque réel." : "Somme des versions locales, dossiers dédupliqués.";
        }
        return _resourceMeasurements.GetValueOrDefault(NormalizeResourcePath(ReadResourcePath(item)))?.Diagnostic ?? "Taille à analyser.";
    }
    private async Task RefreshPackFootprintsAsync()
    {
        var generation = ++_packMetadataGeneration;
        var downloads = _fabStoredDownloads.ToArray();
        var cachedPacks = (_currentStorageSnapshot?.Entries ?? []).Where(entry => entry.Category == StorageCategory.MarketplaceLibrary && entry.Role == StorageEntryRole.Footprint).ToArray();
        var missingPaths = await Task.Run(() => cachedPacks.Where(entry => !Directory.Exists(entry.Path) && !File.Exists(entry.Path))
            .Select(entry => NormalizeResourcePath(entry.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase)).ConfigureAwait(true);
        var metadata = await Task.Run(() => downloads.Where(stored => Directory.Exists(stored.Path))
            .Select(stored => (Stored: stored, Sizes: ResolveStoredDownloadSizes(stored))).ToArray()).ConfigureAwait(true);
        var connectors = _connectorFootprints;
        try { connectors = await Task.Run(async () => (await ConnectorDownloadCatalog.LoadAsync().ConfigureAwait(false)).Where(pack => File.Exists(pack.FilePath)).ToArray()).ConfigureAwait(true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { AppendRawLog("WARN", "Storage/Metadata", "Catalogue des téléchargements externes illisible ; inventaire précédent conservé."); }
        if (generation != _packMetadataGeneration) return;
        _connectorFootprints = connectors;
        var entries = (_currentStorageSnapshot?.Entries ?? []).Where(entry => !entry.Id.StartsWith("pack-metadata:", StringComparison.Ordinal) && !entry.Id.StartsWith("pack-inventory:", StringComparison.Ordinal) &&
            !(entry.Category == StorageCategory.MarketplaceLibrary && entry.Role == StorageEntryRole.Footprint && missingPaths.Contains(NormalizeResourcePath(entry.Path)))).ToList();
        var indexedPaths = entries.Where(entry => entry.Role == StorageEntryRole.Footprint)
            .Select(entry => NormalizeResourcePath(entry.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in metadata.DistinctBy(row => NormalizeResourcePath(row.Stored.Path), StringComparer.OrdinalIgnoreCase))
        {
            if (row.Sizes.StoredBytes is not { } bytes || bytes < 0) continue;
            if (!indexedPaths.Add(NormalizeResourcePath(row.Stored.Path))) continue;
            entries.Add(new("pack-metadata:" + row.Stored.Path, StorageCategory.MarketplaceLibrary, StorageEntryRole.Footprint,
                "Pack téléchargé (métadonnées)", row.Stored.AssetTitle ?? row.Stored.AssetId, row.Stored.Path, row.Stored.Path,
                bytes, 0, 0, row.Stored.DownloadedAt, StorageHealth.Warning, false,
                "Taille stockée déclarée dans le manifeste / historique, hors fichiers annexes ; revérifier pour mesurer le disque."));
        }
        foreach (var pack in FabLocalContentPacks.Where(pack => pack.Pack.SupportedBytes > 0))
        {
            if (!indexedPaths.Add(NormalizeResourcePath(pack.Path))) continue;
            entries.Add(new("pack-inventory:" + pack.Path, StorageCategory.MarketplaceLibrary, StorageEntryRole.Footprint, "Inventaire partiel du pack", pack.Name, pack.Path, pack.Path,
                pack.Pack.SupportedBytes, pack.Pack.SupportedFileCount, 0, pack.Pack.LastModifiedAt, StorageHealth.Partial, false,
                "Inventaire des fichiers pris en charge uniquement ; compléter l’analyse pour mesurer tous les fichiers."));
        }
        foreach (var pack in connectors.Where(pack => pack.SizeBytes >= 0))
        {
            if (!indexedPaths.Add(NormalizeResourcePath(pack.FilePath))) continue;
            entries.Add(new("pack-metadata:" + pack.FilePath, StorageCategory.MarketplaceLibrary, StorageEntryRole.Footprint,
                "Fichier téléchargé (catalogue)", pack.Name, pack.FilePath, pack.FilePath, pack.SizeBytes, 1, 0, pack.DownloadedAt,
                StorageHealth.Warning, false, "Taille enregistrée dans le catalogue local ; revérifier pour mesurer le fichier actuel."));
        }
        var old = _currentStorageSnapshot;
        var snapshot = new StorageSnapshot(entries, old?.Drives ?? [], old?.Diagnostics ?? [], old?.RefreshedAt ?? DateTimeOffset.UtcNow);
        ReplaceStorageSnapshot(snapshot);
        RebuildAssetStoredSizes();
    }
    private void RebuildAssetStoredSizes()
    {
        _partialAssetSizes.Clear();
        _assetStoredSizes = _fabStoredDownloads.GroupBy(row => "asset|" + row.Source + "|" + row.AssetId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group =>
            {
                var entries = group.DistinctBy(row => NormalizeResourcePath(row.Path), StringComparer.OrdinalIgnoreCase)
                    .Select(row => _resourceMeasurements.GetValueOrDefault(NormalizeResourcePath(row.Path))).ToArray();
                if (entries.Any(entry => entry is null || entry.Health == StorageHealth.Partial)) _partialAssetSizes.Add(group.Key);
                var sizes = entries.Where(entry => entry is not null).Select(entry => entry!.SizeBytes).ToArray();
                return sizes.Length == 0 ? (long?)null : sizes.Aggregate(0L, (sum, size) => sum > long.MaxValue - size ? long.MaxValue : sum + size);
            }, StringComparer.OrdinalIgnoreCase);
    }
    private FootprintTarget[] FootprintTargets(string scope, object? selected)
    {
        if (selected is object[] selection) return selection.SelectMany(item => FootprintTargets(scope, item))
            .DistinctBy(item => NormalizeResourcePath(item.Path), StringComparer.OrdinalIgnoreCase).ToArray();
        IEnumerable<FootprintTarget> roots = scope switch
        {
            "engines" => Engines.Select(item => new FootprintTarget(StorageCategory.EngineInstallation, item.DisplayName, item.Path))
                .Concat(LocalEngineInstallations.Select(item => new FootprintTarget(StorageCategory.EngineInstallation, item.Name, item.Installation.RootPath))),
            "projects" => Projects.Select(item => new FootprintTarget(StorageCategory.Project, item.Name, item.Project.RootPath))
                .Concat(LocalEngineProjects.Select(item => new FootprintTarget(StorageCategory.Project, item.Name, item.Path))),
            _ => _fabStoredDownloads.Select(item => new FootprintTarget(StorageCategory.MarketplaceLibrary, item.AssetTitle ?? item.AssetId, item.Path))
                .Concat(FabLocalContentPacks.Select(item => new FootprintTarget(StorageCategory.MarketplaceLibrary, item.Name, item.Path)))
                .Concat(QuixelLocalAssets.Select(item => new FootprintTarget(StorageCategory.MarketplaceLibrary, item.Name, item.Path)))
                .Concat(_connectorFootprints.Select(item => new FootprintTarget(StorageCategory.MarketplaceLibrary, item.Name, item.FilePath))),
        };
        var asset = selected switch { RemoteFabAssetCardViewModel card => card.Asset, RemoteFabAsset remote => remote, _ => null };
        if (asset is not null)
            roots = _fabStoredDownloads.Where(item => IsStoredDownloadForAsset(item, asset.AssetId, asset.Title, NormalizeAssetSource(asset)))
                .Select(item => new FootprintTarget(StorageCategory.MarketplaceLibrary, asset.Title, item.Path));
        else if (selected is not null) roots = roots.Where(item => NormalizeResourcePath(item.Path).Equals(NormalizeResourcePath(ReadResourcePath(selected)), StringComparison.OrdinalIgnoreCase));
        return roots.DistinctBy(item => NormalizeResourcePath(item.Path), StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public async Task AnalyzeResourceSizesAsync(string scope, bool full, object? selected = null)
    {
        if (IsResourceSizeBusy) return;
        IsResourceSizeBusy = true;
        try
        {
            if (scope == "packs") await RefreshPackFootprintsAsync().ConfigureAwait(true);
            var roots = FootprintTargets(scope, selected).Where(item => full || !_resourceMeasurements.TryGetValue(NormalizeResourcePath(item.Path), out var entry) || entry.Health == StorageHealth.Partial).ToArray();
            if (roots.Length == 0) { StatusMessage = "Aucune taille manquante dans ce périmètre. Utilisez Tout revérifier pour une mesure disque."; return; }
            var measured = new List<StorageEntry>();
            var handle = _taskScheduler.Enqueue(new HubTaskDefinition("Stockage", (full ? "Revérifier" : "Compléter") + $" les tailles · {roots.Length} élément(s)", async (context, token) =>
            {
                await Task.Run(() =>
                {
                    var sizer = new BoundedDirectorySizer(1_000_000, TimeSpan.FromSeconds(30));
                    for (var index = 0; index < roots.Length; index++)
                    {
                        token.ThrowIfCancellationRequested(); var root = roots[index];
                        context.Report((double)index / roots.Length, root.Name);
                        if (NormalizeResourcePath(root.Path) == Path.GetPathRoot(root.Path)) continue;
                        if (File.Exists(root.Path))
                        {
                            var file = new FileInfo(root.Path);
                            measured.Add(new("resource-size:" + root.Path, root.Category, StorageEntryRole.Footprint, "Taille disque", root.Name, root.Path, root.Path, file.Length, 1, 0, file.LastWriteTimeUtc, StorageHealth.Ready, false, "Mesure du fichier local"));
                            continue;
                        }
                        var result = sizer.Analyze(root.Path, token);
                        if (!result.Exists) continue;
                        measured.Add(new("resource-size:" + root.Path, root.Category, StorageEntryRole.Footprint, "Taille disque", root.Name, root.Path, root.Path, result.SizeBytes, result.FileCount, result.DirectoryCount,
                            result.LastModified, result.IsPartial ? StorageHealth.Partial : StorageHealth.Ready, false, result.Diagnostic));
                    }
                }, token).ConfigureAwait(false);
            }));
            var done = await handle.Completion.ConfigureAwait(true);
            if (done.Status != HubTaskStatus.Completed) { StatusMessage = "Analyse interrompue ; anciennes mesures conservées. " + done.Error; return; }
            var paths = roots.Select(item => NormalizeResourcePath(item.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var old = _currentStorageSnapshot;
            var snapshot = new StorageSnapshot((old?.Entries ?? []).Where(entry => entry.Role != StorageEntryRole.Footprint || !paths.Contains(NormalizeResourcePath(entry.Path))).Concat(measured).ToArray(), old?.Drives ?? [], old?.Diagnostics ?? [], DateTimeOffset.UtcNow);
            await _storageSnapshotCache.SaveAsync(snapshot).ConfigureAwait(true);
            ReplaceStorageSnapshot(snapshot); RebuildAssetStoredSizes();
            StatusMessage = $"{measured.Count} taille(s) mesurée(s), {measured.Count(entry => entry.Health == StorageHealth.Partial)} partielle(s).";
        }
        catch (Exception error) { ShowError(error); }
        finally { IsResourceSizeBusy = false; }
    }
}
