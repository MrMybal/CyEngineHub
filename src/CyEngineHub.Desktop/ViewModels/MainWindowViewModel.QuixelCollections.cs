using System.Windows.Input;
using Avalonia.Threading;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private QuixelCollection[] _quixelCollections = [];
    private QuixelCollectionCardViewModel[] _quixelCollectionCards = [];
    private QuixelCollection? _selectedQuixelCollection;
    private HashSet<string>? _quixelCollectionAssetIds;
    private bool _isQuixelCollectionsTabSelected;
    private bool _isQuixelCollectionsBusy;
    private string _quixelCollectionsSearch = string.Empty;
    private string _quixelCollectionsStatus = "Collections officielles de la bibliothèque Megascans historique.";
    private string? _quixelCollectionsLoadedPath;
    private DateTimeOffset? _quixelCollectionsUpdatedAt;
    private int _quixelCollectionsNavigation;
    private double _quixelCollectionsSpinnerAngle;
    private DispatcherTimer? _quixelCollectionsSpinner;

    public ICommand ShowQuixelAssetsTabCommand { get; private set; } = null!;
    public ICommand ShowQuixelCollectionsTabCommand { get; private set; } = null!;
    public ICommand RefreshQuixelCollectionsCommand { get; private set; } = null!;
    public BulkObservableCollection<QuixelCollectionCardViewModel> QuixelCollectionCards { get; } = [];
    public bool IsQuixelCollectionsTabSelected => _isQuixelCollectionsTabSelected;
    public bool IsQuixelAssetsTabSelected => !_isQuixelCollectionsTabSelected;
    public bool IsQuixelCollectionsListVisible => IsLibraryQuixelSelected && _isQuixelCollectionsTabSelected && _selectedQuixelCollection is null;
    public bool IsQuixelCollectionScopeVisible => IsLibraryQuixelSelected && _selectedQuixelCollection is not null;
    public bool IsRemoteMarketplaceContentVisible => IsLibraryRemoteSelected && !IsQuixelCollectionsListVisible;
    public string QuixelCollectionScopeTitle => _selectedQuixelCollection?.Name ?? string.Empty;
    public string QuixelCollectionScopeDescription => _selectedQuixelCollection?.Description ?? string.Empty;
    public string QuixelCollectionScopeSummary => $"{_librarySourceAssetCount:N0} / {_selectedQuixelCollection?.AssetIds?.Count ?? 0:N0} assets présents dans votre catalogue · seuls ces assets sont affichés ci-dessous.";
    public string QuixelCollectionsResults => $"{QuixelCollectionCards.Count:N0} / {_quixelCollections.Length:N0} collection(s)";
    public bool HasNoQuixelCollections => QuixelCollectionCards.Count == 0 && !IsQuixelCollectionsBusy;
    public string QuixelCollectionsRefreshLabel => IsQuixelCollectionsBusy ? "Chargement…" : "Actualiser les collections";
    public double QuixelCollectionsSpinnerAngle { get => _quixelCollectionsSpinnerAngle; private set => SetProperty(ref _quixelCollectionsSpinnerAngle, value); }
    public string QuixelCollectionsStatus { get => _quixelCollectionsStatus; private set => SetProperty(ref _quixelCollectionsStatus, value); }
    public string QuixelCollectionsSearch
    {
        get => _quixelCollectionsSearch;
        set { if (SetProperty(ref _quixelCollectionsSearch, value)) FilterQuixelCollections(); }
    }
    public bool IsQuixelCollectionsBusy
    {
        get => _isQuixelCollectionsBusy;
        private set
        {
            if (!SetProperty(ref _isQuixelCollectionsBusy, value)) return;
            OnPropertyChanged(nameof(QuixelCollectionsRefreshLabel));
            OnPropertyChanged(nameof(HasNoQuixelCollections));
            (RefreshQuixelCollectionsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            if (value)
            {
                if (_quixelCollectionsSpinner is null)
                {
                    _quixelCollectionsSpinner = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                    _quixelCollectionsSpinner.Tick += (_, _) => QuixelCollectionsSpinnerAngle = (QuixelCollectionsSpinnerAngle + 24) % 360;
                }
                _quixelCollectionsSpinner.Start();
            }
            else _quixelCollectionsSpinner?.Stop();
        }
    }

    private void InitializeQuixelCollections()
    {
        ShowQuixelAssetsTabCommand = new RelayCommand(() =>
        {
            _quixelCollectionsNavigation++;
            _isQuixelCollectionsTabSelected = false;
            _selectedQuixelCollection = null;
            _quixelCollectionAssetIds = null;
            CloseRemoteFabProduct();
            NotifyQuixelCollectionNavigation();
            ShowCachedRemoteFabPreview();
            QueueRemoteFabViewRefresh();
        });
        ShowQuixelCollectionsTabCommand = new AsyncRelayCommand(async () =>
        {
            _quixelCollectionsNavigation++;
            _isQuixelCollectionsTabSelected = true;
            _selectedQuixelCollection = null;
            _quixelCollectionAssetIds = null;
            CloseRemoteFabProduct();
            NotifyQuixelCollectionNavigation();
            await LoadQuixelCollectionsAsync(force: false).ConfigureAwait(true);
        });
        RefreshQuixelCollectionsCommand = new AsyncRelayCommand(() => LoadQuixelCollectionsAsync(force: true), () => !IsQuixelCollectionsBusy);
    }

    private void NotifyQuixelCollectionNavigation()
    {
        OnPropertyChanged(nameof(IsQuixelCollectionsTabSelected));
        OnPropertyChanged(nameof(IsQuixelAssetsTabSelected));
        OnPropertyChanged(nameof(IsQuixelCollectionsListVisible));
        OnPropertyChanged(nameof(IsQuixelCollectionScopeVisible));
        OnPropertyChanged(nameof(IsRemoteMarketplaceContentVisible));
        OnPropertyChanged(nameof(QuixelCollectionScopeTitle));
        OnPropertyChanged(nameof(QuixelCollectionScopeDescription));
        OnPropertyChanged(nameof(QuixelCollectionScopeSummary));
    }

    private string QuixelCollectionsCachePath => Path.Combine(QuixelCacheDirectory, "Catalog", "collections-v1.json.gz");

    private async Task LoadQuixelCollectionsAsync(bool force)
    {
        if (IsQuixelCollectionsBusy) return;
        IsQuixelCollectionsBusy = true;
        var path = QuixelCollectionsCachePath;
        var cache = new QuixelCollectionsCache(path);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            if (!string.Equals(_quixelCollectionsLoadedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                var cached = await Task.Run(() => cache.LoadAsync(timeout.Token), timeout.Token).ConfigureAwait(true);
                _quixelCollectionsLoadedPath = path;
                _quixelCollectionsUpdatedAt = cached?.UpdatedAt;
                ApplyQuixelCollections(cached?.Collections ?? []);
                if (cached is not null) QuixelCollectionsStatus = $"Collections en cache · {cached.UpdatedAt.ToLocalTime():dd/MM/yyyy HH:mm}.";
            }
            if (!force && _quixelCollectionsUpdatedAt is { } updated &&
                (!AutoRefreshQuixelOnStartup || DateTimeOffset.UtcNow - updated < TimeSpan.FromDays(1))) return;
            if (!IsHistoricalMegascansSearchAvailable)
            {
                QuixelCollectionsStatus = "Actualisation désactivée : aucune clé de recherche autorisée configurée. Les collections déjà en cache sont conservées.";
                return;
            }
            if (_quixelClient is not IQuixelCollectionsClient client) throw new NotSupportedException();
            QuixelCollectionsStatus = _quixelCollections.Length == 0 ? "Lecture des collections officielles Megascans…"
                : "Actualisation en arrière-plan · le dernier cache reste affiché.";
            var collections = await Task.Run(() => client.GetCollectionsAsync(timeout.Token), timeout.Token).ConfigureAwait(true);
            // Preserve already visited contents on automatic refresh; an explicit refresh reloads them on next open.
            var previous = _quixelCollections.ToDictionary(item => item.Id, StringComparer.OrdinalIgnoreCase);
            var merged = collections.Select(item => !force && previous.TryGetValue(item.Id, out var old)
                ? item with { AssetIds = old.AssetIds, Description = string.IsNullOrWhiteSpace(item.Description) ? old.Description : item.Description }
                : item).ToArray();
            _quixelCollectionsUpdatedAt = DateTimeOffset.UtcNow;
            ApplyQuixelCollections(merged);
            QuixelCollectionsStatus = $"{merged.Length:N0} collections officielles Megascans · cache {_quixelCollectionsUpdatedAt.Value.ToLocalTime():dd/MM/yyyy HH:mm}.";
            await SaveQuixelCollectionsAsync(cache, timeout.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            QuixelCollectionsStatus = _quixelCollections.Length == 0
                ? "Les collections Megascans sont momentanément indisponibles. Réessayez avec Actualiser."
                : "Actualisation des collections indisponible · le dernier cache est conservé.";
        }
        finally { IsQuixelCollectionsBusy = false; }
    }

    private void ApplyQuixelCollections(IReadOnlyList<QuixelCollection> collections)
    {
        foreach (var card in _quixelCollectionCards) card.Dispose();
        _quixelCollections = collections.ToArray();
        _quixelCollectionCards = _quixelCollections.Select(item =>
            new QuixelCollectionCardViewModel(item, _quixelRemoteImageCache, OpenQuixelCollectionAsync)).ToArray();
        FilterQuixelCollections();
    }

    private void FilterQuixelCollections()
    {
        var query = QuixelCollectionsSearch.Trim();
        QuixelCollectionCards.ReplaceWith(_quixelCollectionCards.Where(card => query.Length == 0 ||
            card.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || card.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            card.Description.Contains(query, StringComparison.OrdinalIgnoreCase)));
        OnPropertyChanged(nameof(QuixelCollectionsResults));
        OnPropertyChanged(nameof(HasNoQuixelCollections));
    }

    private async Task OpenQuixelCollectionAsync(string id)
    {
        if (IsQuixelCollectionsBusy) return;
        var collection = _quixelCollections.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (collection is null) return;
        var navigation = _quixelCollectionsNavigation;
        IsQuixelCollectionsBusy = true;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try
        {
            if (collection.AssetIds is null)
            {
                QuixelCollectionsStatus = $"Chargement de « {collection.Name} »…";
                if (_quixelClient is not IQuixelCollectionsClient client) throw new NotSupportedException();
                collection = await Task.Run(() => client.GetCollectionAsync(id, timeout.Token), timeout.Token).ConfigureAwait(true);
                _quixelCollections = _quixelCollections.Select(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase) ? collection : item).ToArray();
                await SaveQuixelCollectionsAsync(new QuixelCollectionsCache(QuixelCollectionsCachePath), timeout.Token).ConfigureAwait(true);
            }
            if (navigation != _quixelCollectionsNavigation || !IsLibraryQuixelSelected || !_isQuixelCollectionsTabSelected) return;
            var assetIds = collection.AssetIds ?? throw new InvalidOperationException("Contenu de collection absent.");
            _selectedQuixelCollection = collection;
            _quixelCollectionAssetIds = assetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            LibrarySearchQuery = string.Empty;
            ClearLibraryFacetSelections();
            CloseRemoteFabProduct();
            ShowCachedRemoteFabPreview();
            NotifyQuixelCollectionNavigation();
            QueueRemoteFabViewRefresh();
            QuixelCollectionsStatus = $"Collection « {collection.Name} » prête · {assetIds.Count:N0} références officielles.";
        }
        catch (Exception) { QuixelCollectionsStatus = "Le contenu de cette collection est momentanément indisponible. Réessayez plus tard."; }
        finally { IsQuixelCollectionsBusy = false; }
    }

    private async Task SaveQuixelCollectionsAsync(QuixelCollectionsCache cache, CancellationToken cancellationToken)
    {
        var snapshot = new QuixelCollectionsSnapshot(_quixelCollectionsUpdatedAt ?? DateTimeOffset.UtcNow, _quixelCollections);
        try { await Task.Run(() => cache.SaveAsync(snapshot, cancellationToken), cancellationToken).ConfigureAwait(true); }
        catch (Exception) { QuixelCollectionsStatus += " · Cache non enregistré : vérifiez le dossier Megascans dans Paramètres."; }
    }
}
