using System.Collections.ObjectModel;
using System.Windows.Input;
using CyEngineHub.Extensions;
using CyEngineHub.Desktop.Views;
using CyEngineHub.Core.Marketplaces;
using CyEngineHub.Core.Settings;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string MegascansFabConnectorId = "cyenginehub.marketplace-megascans-fab";
    private ICommand? _openMegascansFabCommand;
    private ICommand? _showMegascansFabDownloadsCommand;
    public ICommand OpenMegascansFabCommand => _openMegascansFabCommand ??= new AsyncRelayCommand(async () =>
    {
        var connector = await RequireMegascansFabConnectorAsync().ConfigureAwait(true);
        await OpenMarketplaceBrowserAsync(connector.Id, connector.Name, new Uri(connector.InventoryUrl)).ConfigureAwait(true);
    }, onError: ShowError);
    public ICommand ShowMegascansFabDownloadsCommand => _showMegascansFabDownloadsCommand ??= new AsyncRelayCommand(async () =>
    {
        var connector = await RequireMegascansFabConnectorAsync().ConfigureAwait(true);
        await ShowConnectorLibraryAsync(connector.Id, localOnly: true).ConfigureAwait(true);
    }, onError: ShowError);

    private async Task<MarketplaceConnectorViewModel> RequireMegascansFabConnectorAsync()
    {
        if (!MarketplaceConnectors.Any(item => item.Id == MegascansFabConnectorId))
            await RefreshHubExtensionsAsync().ConfigureAwait(true);
        var connector = MarketplaceConnectors.FirstOrDefault(item => item.Id == MegascansFabConnectorId)
            ?? throw new InvalidOperationException("Le connecteur Megascans via Fab manque dans cette distribution. Utilisez la version complète du hub.");
        if (!connector.IsEnabled) throw new InvalidOperationException("Activez Megascans via Fab dans Paramètres → Marketplaces.");
        return connector;
    }

    public ObservableCollection<MarketplaceConnectorViewModel> MarketplaceConnectors { get; } = [];
    public event EventHandler<MarketplaceBrowserRequestEventArgs>? MarketplaceBrowserRequested;
    public event EventHandler<MarketplaceImportRequestEventArgs>? MarketplaceImportRequested;
    public event EventHandler? MarketplaceLibraryActivated;
    private MarketplaceDownloadCatalog? _connectorDownloadCatalog;
    private readonly Dictionary<string, ConnectorLibraryViewModel> _connectorLibraries = new(StringComparer.OrdinalIgnoreCase);
    private ConnectorLibraryViewModel? _selectedConnectorLibrary;
    public ConnectorLibraryViewModel? SelectedConnectorLibrary
    {
        get => _selectedConnectorLibrary;
        private set => SetProperty(ref _selectedConnectorLibrary, value);
    }
    public bool IsConnectorLibrarySelected => LibrarySource.StartsWith("Connector:", StringComparison.Ordinal);
    private MarketplaceDownloadCatalog ConnectorDownloadCatalog => _connectorDownloadCatalog ??=
        new MarketplaceDownloadCatalog(Path.Combine(DataDirectory, "Library", "MarketplaceDownloads", "catalog.json"));

    private ConnectorLibraryViewModel GetConnectorLibrary(string id)
    {
        if (_connectorLibraries.TryGetValue(id, out var library)) return library;
        var connector = MarketplaceConnectors.First(item => item.Id == id);
        library = new ConnectorLibraryViewModel(id, connector.Name, ConnectorDownloadCatalog,
            () => OpenMarketplaceBrowserAsync(id, connector.Name, new Uri(connector.InventoryUrl)),
            () => ImportConnectorFilesAsync(id), OpenFolderAsync);
        _connectorLibraries.Add(id, library);
        return library;
    }

    private async Task ShowConnectorLibraryAsync(string id, string? filePath = null, bool localOnly = false)
    {
        if (id == MegascansFabConnectorId && filePath is null && !localOnly)
        {
            SelectLibrarySource(FabWebLibraryClient.Source);
            ShowSection(PageSection.Epic);
            await RefreshRemoteFabViewNowAsync().ConfigureAwait(true);
            return;
        }
        SelectedConnectorLibrary = GetConnectorLibrary(id);
        SelectLibrarySource("Connector:" + id);
        ShowSection(PageSection.Epic);
        MarketplaceLibraryActivated?.Invoke(this, EventArgs.Empty);
        await SelectedConnectorLibrary.LoadAsync(filePath, showList: filePath is null).ConfigureAwait(true);
    }

    private async Task RegisterConnectorDownloadAsync(string id, string path)
    {
        await ConnectorDownloadCatalog.RegisterAsync(id, path).ConfigureAwait(true);
        if (_connectorLibraries.TryGetValue(id, out var library)) await library.LoadAsync().ConfigureAwait(true);
    }

    private async Task ImportConnectorFilesAsync(string id)
    {
        if (MarketplaceImportRequested is null) throw new InvalidOperationException("Sélecteur de fichiers indisponible.");
        var request = new MarketplaceImportRequestEventArgs();
        MarketplaceImportRequested.Invoke(this, request);
        var paths = await request.Completion.Task.ConfigureAwait(true);
        foreach (var path in paths) await RegisterConnectorDownloadAsync(id, path).ConfigureAwait(true);
        if (paths.Count > 0) await ShowConnectorLibraryAsync(id, paths.Count == 1 ? paths[0] : null).ConfigureAwait(true);
    }

    private async Task RebuildMarketplaceConnectorsAsync(IReadOnlyList<HubExtensionSnapshot> extensions)
    {
        var previous = MarketplaceConnectors.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
        var connectors = new List<MarketplaceConnectorViewModel>();
        foreach (var extension in extensions.Where(item => item.Health == HubExtensionHealth.Ready &&
                     item.Manifest.Kind == HubExtensionKind.Marketplace && item.Manifest.Marketplace is not null))
        {
            if (previous.TryGetValue(extension.Manifest.Id, out var current))
            {
                connectors.Add(current);
                continue;
            }
            var store = new MarketplaceConnectorSettingsStore(_settings);
            var saved = await store.LoadAsync(extension.Manifest.Id, Path.Combine(_libraryDownloadRoot, extension.Manifest.Id, "Downloads")).ConfigureAwait(true);
            connectors.Add(new MarketplaceConnectorViewModel(extension, saved.Enabled, saved.DownloadDirectory,
                uri => OpenMarketplaceBrowserAsync(extension.Manifest.Id, extension.Manifest.DisplayName, uri),
                () => ShowConnectorLibraryAsync(extension.Manifest.Id), ShowError,
                snapshot => store.SaveAsync(extension.Manifest.Id, snapshot)));
        }
        MarketplaceConnectors.Clear();
        foreach (var connector in connectors) MarketplaceConnectors.Add(connector);
    }

    private string GetConnectorDownloadDirectory(string id)
    {
        var connector = MarketplaceConnectors.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (connector is null || !connector.IsEnabled)
            throw new InvalidOperationException("Cette marketplace est désactivée. Aucun nouveau téléchargement n’est lancé.");
        return MarketplaceDownloadStorage.ValidateDirectory(connector.DownloadDirectory);
    }

    private async Task OpenMarketplaceBrowserAsync(string id, string name, Uri uri)
    {
        if (!MarketplaceConnectors.Any(item => item.Id == id && item.IsEnabled))
            throw new InvalidOperationException("Cette marketplace est désactivée dans les paramètres.");
        if (MarketplaceBrowserRequested is null)
            throw new InvalidOperationException("La fenêtre principale n’est pas prête à ouvrir le navigateur interne.");
        await MarketplaceConnectors.First(item => item.Id == id).SaveAsync().ConfigureAwait(true);
        MarketplaceBrowserRequested.Invoke(this, new MarketplaceBrowserRequestEventArgs(id, name, uri,
            Path.Combine(DataDirectory, "WebView2", "Marketplaces", id),
            () => GetConnectorDownloadDirectory(id),
            async () =>
            {
                var directory = GetConnectorDownloadDirectory(id);
                await Task.Run(() => Directory.CreateDirectory(directory)).ConfigureAwait(true);
                await OpenFolderAsync(directory).ConfigureAwait(true);
            },
            () => _platformLauncher.OpenUriAsync(uri),
            path => RegisterConnectorDownloadAsync(id, path),
            path => ShowConnectorLibraryAsync(id, path))
        {
            SynchronizeFabLibrary = id == MegascansFabConnectorId ? SynchronizeFabWebAsync : null
        });
    }
}
