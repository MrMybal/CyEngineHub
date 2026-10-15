using System.Collections.ObjectModel;
using CyEngineHub.Storage.Analysis;
using CyEngineHub.Storage.Models;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private int _storageOverviewTab;
    public int StorageOverviewTab
    {
        get => _storageOverviewTab;
        set { if (SetProperty(ref _storageOverviewTab, value)) RefreshStorageOverview(); }
    }
    public ObservableCollection<StorageEntryCardViewModel> FilteredStorageEntries { get; } = [];
    public ObservableCollection<LibraryCapacityViewModel> StorageLibraryCapacities { get; } = [];
    public string StoragePacksSize => _currentStorageSnapshot is null ? "À mesurer" : FormatBytes(StorageOverview.SumDistinctRoots(
        _currentStorageSnapshot?.Entries.Where(entry => entry.Category is StorageCategory.MarketplaceLibrary or StorageCategory.VaultCache) ?? []));
    public string StorageAnalysisWarnings => _currentStorageSnapshot is null ? ""
        : string.Join(" · ", _currentStorageSnapshot.Diagnostics.Concat(
            _currentStorageSnapshot.Entries.Where(entry => entry.Health != StorageHealth.Ready)
                .Select(entry => entry.OwnerName + " : " + entry.Diagnostic)).Take(8));
    private string StorageCategorySize(StorageCategory category) => _currentStorageSnapshot is null ? "À mesurer"
        : FormatBytes(SumStorage(category)) + (_currentStorageSnapshot.Diagnostics.Count > 0 ||
            _currentStorageSnapshot.Entries.Any(entry => entry.Category == category && entry.Health == StorageHealth.Partial)
            ? " (partiel)" : "");
    public string StorageMeasurementDate => _currentStorageSnapshot is null
        ? "Tailles non mesurées : lancez l’analyse."
        : $"Mesures du {_currentStorageSnapshot.RefreshedAt.ToLocalTime():g}. Les volumes incluent les fichiers extérieurs au hub ; l’espace des téléchargements en attente n’est pas réservé.";
    public bool HasNoFilteredStorageEntries => FilteredStorageEntries.Count == 0;

    private AdditionalStorageRoot[] GetAdditionalStorageRoots()
    {
        var roots = new List<AdditionalStorageRoot>();
        roots.AddRange(_currentLocalEngineInstallations.Select(engine =>
            new AdditionalStorageRoot(StorageCategory.EngineInstallation, engine.DisplayName, engine.RootPath)));
        roots.AddRange(_currentLocalSnapshots.SelectMany(snapshot => snapshot.Projects).Select(project =>
            new AdditionalStorageRoot(StorageCategory.Project, project.Name, project.RootPath)));
        AddMarket("Fab", FabCacheDirectory, FabDownloadDirectory, FabLibraryLocations);
        AddMarket("Megascans", QuixelCacheDirectory, QuixelDownloadDirectory, QuixelLibraryLocations);
        AddMarket("Unity", UnityCacheDirectory, UnityDownloadDirectory, UnityLibraryLocations);
        roots.AddRange(EpicNativeStoreSources.Concat(MegascansNativeStoreSources).Select(source =>
            new AdditionalStorageRoot(StorageCategory.MarketplaceLibrary, source.StoreLabel, source.Path)));
        roots.AddRange(MarketplaceConnectors.Select(connector =>
            new AdditionalStorageRoot(StorageCategory.MarketplaceLibrary, connector.Name, connector.DownloadDirectory)));
        return roots.ToArray();

        void AddMarket(string name, string cache, string download, IEnumerable<MarketplaceLibraryLocationViewModel> libraries)
        {
            roots.Add(new(StorageCategory.MarketplaceCache, name + " · cache", cache));
            roots.Add(new(StorageCategory.MarketplaceDownloads, name + " · téléchargements", download));
            roots.AddRange(libraries.Where(location => location.IsEnabled).Select(location =>
                new AdditionalStorageRoot(StorageCategory.MarketplaceLibrary, name + " · bibliothèque", location.Path)));
        }
    }

    private void RefreshStorageOverview()
    {
        FilteredStorageEntries.Clear();
        foreach (var entry in StorageEntries.Where(card => StorageOverviewTab switch
        {
            1 => card.Entry.Category == StorageCategory.EngineInstallation,
            2 => card.Entry.Category == StorageCategory.Project,
            3 => card.Entry.Category is StorageCategory.MarketplaceLibrary or StorageCategory.VaultCache or StorageCategory.MarketplaceDownloads,
            4 => card.Entry.Category is not (StorageCategory.EngineInstallation or StorageCategory.Project or StorageCategory.MarketplaceLibrary or StorageCategory.VaultCache or StorageCategory.MarketplaceDownloads),
            _ => true,
        })) FilteredStorageEntries.Add(entry);
        StorageLibraryCapacities.Clear();
        AddCapacities("Fab", FabLibraryLocations);
        AddCapacities("Megascans", QuixelLibraryLocations);
        AddCapacities("Unity", UnityLibraryLocations);
        OnPropertyChanged(nameof(StoragePacksSize));
        OnPropertyChanged(nameof(StorageMeasurementDate));
        OnPropertyChanged(nameof(StorageAnalysisWarnings));
        OnPropertyChanged(nameof(HasNoFilteredStorageEntries));

        void AddCapacities(string name, IEnumerable<MarketplaceLibraryLocationViewModel> locations)
        {
            foreach (var location in locations.Where(location => location.IsEnabled))
            {
                var measured = StorageEntries.FirstOrDefault(card =>
                    card.Entry.Category == StorageCategory.MarketplaceLibrary &&
                    string.Equals(card.Path.TrimEnd('\\', '/'), location.Path.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
                long limit;
                try { limit = ParseMaximumLibraryBytes(location.MaximumGigabytes, name); }
                catch (Exception exception) when (exception is InvalidOperationException or OverflowException)
                {
                    StorageLibraryCapacities.Add(new(name + " · priorité " + location.Priority, location.Path,
                        "Quota invalide", "Corrigez cette valeur dans Paramètres → Marketplaces."));
                    continue;
                }
                var complete = measured?.Entry.Health == StorageHealth.Ready;
                var quota = limit <= 0 ? "Sans quota" : "Quota : " + FormatBytes(limit);
                var status = !complete ? "À mesurer (ou analyse partielle)" : limit > 0
                    ? "Reste sous quota : " + FormatBytes((long)Math.Max(0, limit - measured!.Entry.SizeBytes))
                    : "Occupé : " + measured!.Size;
                StorageLibraryCapacities.Add(new(name + " · priorité " + location.Priority, location.Path, quota,
                    status + (complete && limit > 0 && measured!.Entry.SizeBytes >= limit ? " · PLEIN : bascule au dossier suivant" : "")));
            }
        }
    }
}

public sealed record LibraryCapacityViewModel(string Name, string Path, string Quota, string Status);
