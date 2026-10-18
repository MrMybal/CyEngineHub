namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _useCompactInterface = true;
    private bool _engineCardsView = true;
    private bool _projectCardsView = true;
    private bool _engineImageOnlyCards, _projectImageOnlyCards, _engineRowImages, _projectRowImages;
    public bool EngineImageOnlyCards { get => _engineImageOnlyCards; set { if (SetProperty(ref _engineImageOnlyCards, value)) SaveInterfacePreference("interface.engines.image-only.v2", value); } }
    public bool ProjectImageOnlyCards { get => _projectImageOnlyCards; set { if (SetProperty(ref _projectImageOnlyCards, value)) SaveInterfacePreference("interface.projects.image-only.v2", value); } }
    public bool EngineRowImages { get => _engineRowImages; set { if (SetProperty(ref _engineRowImages, value)) SaveInterfacePreference("interface.engines.row-images.v2", value); } }
    public bool ProjectRowImages { get => _projectRowImages; set { if (SetProperty(ref _projectRowImages, value)) SaveInterfacePreference("interface.projects.row-images.v2", value); } }
    public void SetResourcePresentation(bool engines, int mode)
    {
        if (mode is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(mode));
        if (engines) { EngineCardsView = mode < 2; EngineImageOnlyCards = mode == 1; EngineRowImages = mode == 3; }
        else { ProjectCardsView = mode < 2; ProjectImageOnlyCards = mode == 1; ProjectRowImages = mode == 3; }
    }
    private bool _libraryImageOnlyCards;
    private bool _libraryRows;
    private bool _libraryRowImages;
    public bool IsLibraryRowsMode => _libraryRows;
    public bool IsLibraryRowImages => _libraryRowImages;
    private void SetLibraryRows(bool rows, bool images)
    {
        if (SetProperty(ref _libraryRows, rows, nameof(IsLibraryRowsMode))) SaveInterfacePreference("interface.library.rows.v2", rows);
        if (SetProperty(ref _libraryRowImages, images, nameof(IsLibraryRowImages))) SaveInterfacePreference("interface.library.row-images.v2", images);
        OnPropertyChanged(nameof(IsLibraryLargeCards));
    }
    public bool IsLibraryImageOnlyCards
    {
        get => _libraryImageOnlyCards;
        private set
        {
            if (!SetProperty(ref _libraryImageOnlyCards, value)) return;
            OnPropertyChanged(nameof(IsLibraryLargeCards));
            SaveInterfacePreference("interface.library.image-only.v2", value);
        }
    }
    private Task _interfaceWrite = Task.CompletedTask;
    public bool UseCompactInterface
    {
        get => _useCompactInterface;
        set
        {
            if (!SetProperty(ref _useCompactInterface, value)) return;
            SaveInterfacePreference("interface.compact.v2", value);
        }
    }
    public bool EngineCardsView
    {
        get => _engineCardsView;
        set { if (SetProperty(ref _engineCardsView, value)) SaveInterfacePreference("interface.engines.cards.v2", value); }
    }
    public bool ProjectCardsView
    {
        get => _projectCardsView;
        set { if (SetProperty(ref _projectCardsView, value)) SaveInterfacePreference("interface.projects.cards.v2", value); }
    }
    private void SaveInterfacePreference(string key, bool value) => _interfaceWrite = SaveInterfaceAsync(_interfaceWrite, key, value);
    private async Task SaveInterfaceAsync(Task previous, string key, bool value)
    {
        try { await previous.ConfigureAwait(true); } catch (Exception) { }
        try { await _settings.SetAsync(key, value ? "true" : "false").ConfigureAwait(true); }
        catch (Exception) { StatusMessage = "La préférence d’affichage n’a pas pu être enregistrée."; }
    }
    private async Task LoadInterfaceAsync()
    {
        InterfaceTheme = await _settings.GetAsync("interface.theme.v1").ConfigureAwait(true) ?? "Gris";
        var value = await _settings.GetAsync("interface.compact.v2").ConfigureAwait(true);
        _useCompactInterface = !bool.TryParse(value, out var compact) || compact;
        OnPropertyChanged(nameof(UseCompactInterface));
        var engines = await _settings.GetAsync("interface.engines.cards.v2").ConfigureAwait(true);
        _engineCardsView = !bool.TryParse(engines, out var engineCards) || engineCards;
        var projects = await _settings.GetAsync("interface.projects.cards.v2").ConfigureAwait(true);
        _projectCardsView = !bool.TryParse(projects, out var projectCards) || projectCards;
        OnPropertyChanged(nameof(EngineCardsView));
        OnPropertyChanged(nameof(ProjectCardsView));
        _engineImageOnlyCards = bool.TryParse(await _settings.GetAsync("interface.engines.image-only.v2").ConfigureAwait(true), out var engineMinimal) && engineMinimal;
        _projectImageOnlyCards = bool.TryParse(await _settings.GetAsync("interface.projects.image-only.v2").ConfigureAwait(true), out var projectMinimal) && projectMinimal;
        _engineRowImages = bool.TryParse(await _settings.GetAsync("interface.engines.row-images.v2").ConfigureAwait(true), out var engineImages) && engineImages;
        _projectRowImages = bool.TryParse(await _settings.GetAsync("interface.projects.row-images.v2").ConfigureAwait(true), out var projectImages) && projectImages;
        OnPropertyChanged(nameof(EngineImageOnlyCards));
        OnPropertyChanged(nameof(ProjectImageOnlyCards));
        OnPropertyChanged(nameof(EngineRowImages));
        OnPropertyChanged(nameof(ProjectRowImages));
        var imageOnly = await _settings.GetAsync("interface.library.image-only.v2").ConfigureAwait(true);
        _libraryImageOnlyCards = bool.TryParse(imageOnly, out var minimal) && minimal;
        _libraryRows = bool.TryParse(await _settings.GetAsync("interface.library.rows.v2").ConfigureAwait(true), out var rows) && rows;
        _libraryRowImages = bool.TryParse(await _settings.GetAsync("interface.library.row-images.v2").ConfigureAwait(true), out var rowImages) && rowImages;
        OnPropertyChanged(nameof(IsLibraryRowsMode));
        OnPropertyChanged(nameof(IsLibraryRowImages));
        OnPropertyChanged(nameof(IsLibraryImageOnlyCards));
        OnPropertyChanged(nameof(IsLibraryLargeCards));
    }
}
