using System.Windows.Input;
using CyEngineHub.Extensions;
using CyEngineHub.Core.Settings;

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

    public MarketplaceConnectorViewModel(HubExtensionSnapshot extension, bool enabled, string downloadDirectory,
        Func<Uri, Task> openInventory, Func<Task> openLibrary, Action<Exception> onError,
        Func<MarketplaceConnectorSettings, Task> save)
    {
        Id = extension.Manifest.Id;
        Name = extension.Manifest.DisplayName;
        Description = extension.Manifest.Description;
        InventoryUrl = extension.Manifest.Marketplace!.InventoryUrl;
        _isEnabled = enabled;
        _downloadDirectory = downloadDirectory;
        _save = save;
        _openInventoryCommand = new AsyncRelayCommand(async () =>
        {
            await SaveAsync();
            await openInventory(new Uri(InventoryUrl));
        }, () => IsEnabled, onError);
        OpenLibraryCommand = new AsyncRelayCommand(openLibrary, onError: onError);
        SaveCommand = new AsyncRelayCommand(SaveAsync, onError: onError);
    }

    public string Id { get; }
    public string Name { get; }
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

    private void QueueSave() => _ = ObserveSaveAsync(SaveAsync());
    private static async Task ObserveSaveAsync(Task write)
    {
        // SaveAsync exposes failures in SaveStatus. Automatic edits must not throw async-void exceptions.
        try { await write; } catch (Exception) { }
    }

    public async Task SaveAsync()
    {
        var revision = ++_revision;
        var snapshot = new MarketplaceConnectorSettings(IsEnabled, DownloadDirectory);
        SaveStatus = "Enregistrement…";
        // Preserve edit order even if the store is slow or a previous draft was invalid.
        var previous = _pendingWrite;
        _pendingWrite = Task.Run(() => PersistAfterAsync(previous, snapshot));
        try
        {
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
