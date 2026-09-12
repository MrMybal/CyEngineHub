using System.Collections.ObjectModel;
using System.Windows.Input;
using CyEngineHub.Core.Marketplaces;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class ConnectorAssetViewModel
{
    public ConnectorAssetViewModel(MarketplaceLocalAsset asset, bool available, Action<ConnectorAssetViewModel> open)
    {
        Asset = asset;
        IsAvailable = available;
        OpenCommand = new RelayCommand(() => open(this));
    }
    public MarketplaceLocalAsset Asset { get; }
    public bool IsAvailable { get; }
    public string Name => Asset.Name;
    public string Details => $"{Asset.AssetKind} · {Asset.Format} · {Asset.SizeBytes / 1048576d:N1} Mo";
    public string Availability => IsAvailable ? "Téléchargé · local" : "Fichier introuvable · emplacement à vérifier";
    public string Date => Asset.DownloadedAt.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
    public ICommand OpenCommand { get; }
}

public sealed class ConnectorLibraryViewModel : ObservableObject
{
    private readonly MarketplaceDownloadCatalog _catalog;
    private readonly string _id;
    private readonly Func<string, Task> _openFolder;
    private ConnectorAssetViewModel[] _all = [];
    private ConnectorAssetViewModel? _selected;
    private string _search = "", _status = "", _editName = "", _editNotes = "";
    private int _generation;
    private bool _isBusy;

    public ConnectorLibraryViewModel(string id, string name, MarketplaceDownloadCatalog catalog,
        Func<Task> openInventory, Func<Task> importExisting, Func<string, Task> openFolder)
    {
        _id = id; Name = name; _catalog = catalog; _openFolder = openFolder;
        OpenInventoryCommand = new AsyncRelayCommand(openInventory, onError: ReportError);
        ImportExistingCommand = new AsyncRelayCommand(importExisting, onError: ReportError);
        RefreshCommand = new AsyncRelayCommand(() => LoadAsync(), onError: ReportError);
        BackCommand = new RelayCommand(() => Select(null));
        OpenFolderCommand = new AsyncRelayCommand(OpenSelectedFolderAsync, onError: ReportError);
        SaveCommand = new AsyncRelayCommand(SaveAsync, onError: ReportError);
    }

    public string Name { get; }
    public ObservableCollection<ConnectorAssetViewModel> Assets { get; } = [];
    public ConnectorAssetViewModel? SelectedAsset => _selected;
    public bool IsProductVisible => _selected is not null;
    public bool IsListVisible => _selected is null;
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsEmpty => Assets.Count == 0;
    public string Summary => $"{Assets.Count:N0} / {_all.Length:N0} fichier(s) téléchargé(s)";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) Filter(); } }
    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }
    public string EditNotes { get => _editNotes; set => SetProperty(ref _editNotes, value); }
    public ICommand OpenInventoryCommand { get; }
    public ICommand ImportExistingCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand SaveCommand { get; }

    public async Task LoadAsync(string? showPath = null, bool showList = false)
    {
        var generation = ++_generation;
        IsBusy = true;
        try
        {
            var entries = await Task.Run(async () => (await _catalog.LoadAsync().ConfigureAwait(false))
                .Where(item => item.MarketplaceId.Equals(_id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.DownloadedAt)
                .Select(item => (Asset: item, Available: File.Exists(item.FilePath))).ToArray());
            if (generation != _generation) return;
            _all = entries.Select(item => new ConnectorAssetViewModel(item.Asset, item.Available, Select)).ToArray();
            Filter();
            var path = showPath ?? (showList ? null : _selected?.Asset.FilePath);
            var preserveEdits = showPath is null && !showList && _selected is not null;
            var draftName = EditName;
            var draftNotes = EditNotes;
            Select(path is null ? null : _all.FirstOrDefault(item => item.Asset.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)));
            if (preserveEdits && _selected is not null) { EditName = draftName; EditNotes = draftNotes; }
            Status = "Catalogue local. Les nouveaux téléchargements passent par l’inventaire web intégré.";
        }
        catch (Exception exception) { if (generation == _generation) ReportError(exception); }
        finally { if (generation == _generation) IsBusy = false; }
    }

    private void Filter()
    {
        var query = Search.Trim();
        Assets.Clear();
        foreach (var asset in _all.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || item.Asset.AssetKind.Contains(query, StringComparison.OrdinalIgnoreCase))) Assets.Add(asset);
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(IsEmpty));
    }

    private void Select(ConnectorAssetViewModel? asset)
    {
        _selected = asset;
        EditName = asset?.Name ?? "";
        EditNotes = asset?.Asset.Notes ?? "";
        OnPropertyChanged(nameof(SelectedAsset)); OnPropertyChanged(nameof(IsProductVisible)); OnPropertyChanged(nameof(IsListVisible));
    }

    private async Task OpenSelectedFolderAsync()
    {
        var path = _selected?.Asset.FilePath;
        if (path is not null) await _openFolder(Path.GetDirectoryName(path)!);
    }

    private async Task SaveAsync()
    {
        if (_selected is null) return;
        var id = _selected.Asset.Id;
        var name = EditName;
        var notes = EditNotes;
        var asset = await Task.Run(() => _catalog.RenameAsync(id, name, notes));
        await LoadAsync(asset.FilePath);
        Status = "Fiche locale enregistrée. Le fichier d’origine n’a pas été modifié.";
    }

    private void ReportError(Exception exception) => Status = "Erreur du catalogue local : " + exception.Message;
}
