using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CyEngineHub.Fab.Storage;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _storageAutosaveInitialized;
    private bool _suspendStorageAutosave;
    private readonly Dictionary<string, Task> _storageWrites = [];
    private readonly Dictionary<string, int> _storageWriteVersions = [];
    private readonly Dictionary<string, string> _storageSaveStates = [];
    public string MarketplaceStorageSaveStatus => _storageSaveStates.Count == 0
        ? "Cache, téléchargements et bibliothèques : sauvegarde automatique après modification."
        : string.Join(" · ", _storageSaveStates.Select(pair => pair.Key + " : " + pair.Value));

    private void InitializeMarketplaceStorageAutosave()
    {
        if (_storageAutosaveInitialized) return;
        _storageAutosaveInitialized = true;
        PropertyChanged += (_, args) =>
        {
            switch (args.PropertyName)
            {
                case nameof(FabCacheDirectory): case nameof(FabDownloadDirectory): QueueStorageSave("Fab"); break;
                case nameof(QuixelCacheDirectory): case nameof(QuixelDownloadDirectory): QueueStorageSave("Megascans"); break;
                case nameof(UnityCacheDirectory): case nameof(UnityDownloadDirectory): QueueStorageSave("Unity"); break;
            }
        };
        ObserveLocations(FabLibraryLocations, "Fab");
        ObserveLocations(QuixelLibraryLocations, "Megascans");
        ObserveLocations(UnityLibraryLocations, "Unity");
    }

    private void ObserveLocations(ObservableCollection<MarketplaceLibraryLocationViewModel> locations, string source)
    {
        var subscribed = new HashSet<MarketplaceLibraryLocationViewModel>();
        void Changed(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(MarketplaceLibraryLocationViewModel.Path) or nameof(MarketplaceLibraryLocationViewModel.MaximumGigabytes) or nameof(MarketplaceLibraryLocationViewModel.IsEnabled)) QueueStorageSave(source);
        }
        void Rebind()
        {
            foreach (var item in subscribed) item.PropertyChanged -= Changed;
            subscribed.Clear();
            foreach (var item in locations) { item.PropertyChanged += Changed; subscribed.Add(item); }
        }
        Rebind();
        locations.CollectionChanged += (_, _) => { Rebind(); QueueStorageSave(source); };
    }

    private void QueueStorageSave(string source)
    {
        if (_suspendStorageAutosave) return;
        var revision = _storageWriteVersions.GetValueOrDefault(source) + 1;
        _storageWriteVersions[source] = revision;
        try
        {
            var (key, cache, download, locations) = source switch
            {
                "Fab" => (FabStorageProfileSetting, FabCacheDirectory, FabDownloadDirectory, FabLibraryLocations),
                "Megascans" => (QuixelStorageProfileSetting, QuixelCacheDirectory, QuixelDownloadDirectory, QuixelLibraryLocations),
                _ => (UnityStorageProfileSetting, UnityCacheDirectory, UnityDownloadDirectory, UnityLibraryLocations)
            };
            var snapshot = new MarketplaceStorageProfile(source, cache, download,
                locations.Select(item => new MarketplaceLibraryLocation(item.Path, ParseMaximumLibraryBytes(item.MaximumGigabytes, source), item.IsEnabled)).ToArray());
            var previous = _storageWrites.GetValueOrDefault(source) ?? Task.CompletedTask;
            var write = Task.Run(() => PersistStorageAfterAsync(previous, key, snapshot));
            _storageWrites[source] = write;
            _storageSaveStates[source] = "enregistrement…";
            OnPropertyChanged(nameof(MarketplaceStorageSaveStatus));
            _ = ObserveStorageWriteAsync(source, revision, write);
        }
        catch (Exception exception)
        {
            _storageSaveStates[source] = "non enregistré — " + exception.Message;
            OnPropertyChanged(nameof(MarketplaceStorageSaveStatus));
        }
    }

    private async Task PersistStorageAfterAsync(Task previous, string key, MarketplaceStorageProfile snapshot)
    {
        try { await previous.ConfigureAwait(false); } catch (Exception) { }
        await new MarketplaceStorageSettingsStore(_settings).SaveAsync(key, snapshot).ConfigureAwait(false);
    }

    private async Task ObserveStorageWriteAsync(string source, int revision, Task write)
    {
        var status = "enregistré";
        try { await write.ConfigureAwait(false); }
        catch (Exception exception) { status = "non enregistré — " + exception.Message; }
        Dispatcher.UIThread.Post(() =>
        {
            if (_storageWriteVersions.GetValueOrDefault(source) != revision) return;
            _storageSaveStates[source] = status;
            OnPropertyChanged(nameof(MarketplaceStorageSaveStatus));
        });
    }

    public Task FlushMarketplaceSettingsWritesAsync() => Task.WhenAll(_storageWrites.Values
        .Concat(MarketplaceConnectors.Select(item => item.PendingWrite)).ToArray());

    private async Task SaveMarketplaceSettingsAsync()
    {
        try { await FlushMarketplaceSettingsWritesAsync().ConfigureAwait(true); } catch (Exception) { /* Allow correcting an invalid draft. */ }
        _suspendStorageAutosave = true;
        try { await SaveMarketplaceSettingsCoreAsync().ConfigureAwait(true); }
        finally
        {
            _suspendStorageAutosave = false;
            QueueStorageSave("Fab");
            QueueStorageSave("Megascans");
            QueueStorageSave("Unity");
        }
    }
}
