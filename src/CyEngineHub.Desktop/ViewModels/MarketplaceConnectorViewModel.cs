using System.Windows.Input;
using CyEngineHub.Extensions;
using CyEngineHub.Core.Settings;
using System.Collections.ObjectModel;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class MarketplaceConnectorViewModel : ObservableObject
{
    private bool _isEnabled;
    private string _downloadDirectory;
    private string _saveStatus = "Paramètres chargés";
    private readonly AsyncRelayCommand _openInventoryCommand;
    private readonly Func<MarketplaceConnectorSettings, Task> _save;
    private Task _pendingWrite = Task.CompletedTask;
    private int _revision;
    private string _compression = "ZipFastest";
    public IReadOnlyList<MarketplaceCompressionChoice> CompressionOptions { get; } = MarketplaceCompressionChoice.AllOptions;
    public string Compression
    {
        get => _compression;
        set
        {
            if (!SetProperty(ref _compression, value)) return;
            OnPropertyChanged(nameof(SelectedCompressionOption));
            QueueSave();
        }
    }
    public MarketplaceCompressionChoice SelectedCompressionOption
    {
        get => CompressionOptions.FirstOrDefault(option => option.StorageMode == Compression)
            ?? CompressionOptions.Single(option => option.StorageMode == "ZipFastest");
        set { if (value is not null) Compression = value.StorageMode; }
    }
    public ObservableCollection<ConnectorLibraryLocationViewModel> LibraryLocations { get; } = [];
    public ICommand AddLibraryCommand { get; }

    public MarketplaceConnectorViewModel(HubExtensionSnapshot extension, bool enabled, string downloadDirectory,
        Func<Uri, Task> openInventory, Func<Task> openLibrary, Action<Exception> onError,
        Func<MarketplaceConnectorSettings, Task> save, MarketplaceConnectorSettings? storage = null)
    {
        Id = extension.Manifest.Id;
        Name = extension.Manifest.DisplayName;
        Description = extension.Manifest.Description;
        InventoryUrl = extension.Manifest.Marketplace!.InventoryUrl;
        _isEnabled = enabled;
        _downloadDirectory = downloadDirectory;
        _save = save;
        _compression = storage?.Compression ?? "ZipFastest";
        foreach (var location in storage?.Libraries ?? [new ConnectorLibraryDirectory(System.IO.Path.Combine(downloadDirectory, "Library"))])
            AddLocation(location.Path, location.MaximumBytes, location.Enabled);
        AddLibraryCommand = new RelayCommand(() => { AddLocation("", 0, true); });
        _openInventoryCommand = new AsyncRelayCommand(async () =>
        {
            await SaveAsync();
            await openInventory(new Uri(InventoryUrl));
        }, () => IsEnabled, onError);
        OpenLibraryCommand = new AsyncRelayCommand(openLibrary, onError: onError);
        SaveCommand = new AsyncRelayCommand(SaveAsync, onError: onError);
    }

    public string Id { get; }
    public bool HasPackageStorage => Id == "cyenginehub.marketplace-cosmos";
    public string Name { get; }
    public string SettingsHeading => HasPackageStorage ? "COSMOS" : Name.ToUpperInvariant();
    public string SettingsSubtitle => HasPackageStorage ? "Mes assets · inventaire web et bibliothèque locale" : "Connecteur · inventaire web";
    public string SettingsSaveLabel => HasPackageStorage ? "Enregistrer Cosmos" : "Enregistrer " + Name;
    public string LibrarySubtitle => Id == "cyenginehub.marketplace-megascans-fab" ? "Fiches natives · Fab / MCP" : "Mes assets téléchargés";
    public string Description { get; }
    public string InventoryUrl { get; }
    public ICommand OpenInventoryCommand => _openInventoryCommand;
    public ICommand OpenLibraryCommand { get; }
    public ICommand SaveCommand { get; }
    public string SaveStatus { get => _saveStatus; private set => SetProperty(ref _saveStatus, value); }
    public string DownloadDirectory
    {
        get => _downloadDirectory;
        set { if (SetProperty(ref _downloadDirectory, value)) QueueSave(); }
    }
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                _openInventoryCommand.NotifyCanExecuteChanged();
                QueueSave();
            }
        }
    }

    public Task PendingWrite => _pendingWrite;

    private void AddLocation(string path, long bytes, bool enabled)
    {
        var row = new ConnectorLibraryLocationViewModel(path, bytes == 0 ? "" : (bytes / 1073741824d).ToString(System.Globalization.CultureInfo.InvariantCulture), enabled,
            item => Move(item, -1), item => Move(item, 1), item => { LibraryLocations.Remove(item); Reindex(); QueueSave(); });
        LibraryLocations.Add(row); Reindex();
        row.PropertyChanged += (_, _) => QueueSave();
    }
    private void Reindex() { for (var index = 0; index < LibraryLocations.Count; index++) LibraryLocations[index].SetPriority(index + 1); }
    private void Move(ConnectorLibraryLocationViewModel item, int delta)
    { var index = LibraryLocations.IndexOf(item); if (index + delta >= 0 && index + delta < LibraryLocations.Count) { LibraryLocations.Move(index, index + delta); Reindex(); QueueSave(); } }
    public MarketplaceConnectorSettings Snapshot() => new(IsEnabled, DownloadDirectory, Compression,
        LibraryLocations.Select(item => new ConnectorLibraryDirectory(item.Path,
            string.IsNullOrWhiteSpace(item.MaximumGigabytes) ? 0 : double.TryParse(item.MaximumGigabytes.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var amount) && double.IsFinite(amount) && amount >= 0 && amount < long.MaxValue / 1073741824d
                ? checked((long)(amount * 1073741824)) : throw new ArgumentException("Quota de bibliothèque invalide."), item.IsEnabled)).ToArray());

    private void QueueSave() => _ = ObserveSaveAsync(SaveAsync());
    private static async Task ObserveSaveAsync(Task write)
    {
        // SaveAsync exposes failures in SaveStatus. Automatic edits must not throw async-void exceptions.
        try { await write; } catch (Exception) { }
    }

    public async Task SaveAsync()
    {
        var revision = ++_revision;
        SaveStatus = "Enregistrement…";
        try
        {
            var snapshot = Snapshot();
            // Preserve edit order even if the store is slow or a previous draft was invalid.
            var previous = _pendingWrite;
            _pendingWrite = Task.Run(() => PersistAfterAsync(previous, snapshot));
            await _pendingWrite;
            if (revision == _revision) SaveStatus = "Enregistré automatiquement";
        }
        catch (Exception exception)
        {
            if (revision == _revision) SaveStatus = "Non enregistré : " + exception.Message;
            throw;
        }
    }

    private async Task PersistAfterAsync(Task previous, MarketplaceConnectorSettings snapshot)
    {
        try { await previous.ConfigureAwait(false); } catch (Exception) { }
        await _save(snapshot).ConfigureAwait(false);
    }
}

public sealed class ConnectorLibraryLocationViewModel : ObservableObject
{
    private string _path, _quota;
    private bool _enabled;
    public ConnectorLibraryLocationViewModel(string path, string quota, bool enabled,
        Action<ConnectorLibraryLocationViewModel> up, Action<ConnectorLibraryLocationViewModel> down, Action<ConnectorLibraryLocationViewModel> remove)
    { _path = path; _quota = quota; _enabled = enabled; MoveUpCommand = new RelayCommand(() => up(this)); MoveDownCommand = new RelayCommand(() => down(this)); RemoveCommand = new RelayCommand(() => remove(this)); }
    public string Path { get => _path; set => SetProperty(ref _path, value); }
    public string MaximumGigabytes { get => _quota; set => SetProperty(ref _quota, value); }
    public bool IsEnabled { get => _enabled; set => SetProperty(ref _enabled, value); }
    private int _priority;
    public int Priority => _priority;
    public string PriorityLabel => $"#{Priority}";
    public void SetPriority(int priority)
    {
        if (!SetProperty(ref _priority, priority, nameof(Priority))) return;
        OnPropertyChanged(nameof(PriorityLabel));
    }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveCommand { get; }
}
