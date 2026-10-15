namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _useCompactInterface = true;
    private bool _engineCardsView = true;
    private bool _projectCardsView = true;
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
        var value = await _settings.GetAsync("interface.compact.v2").ConfigureAwait(true);
        _useCompactInterface = !bool.TryParse(value, out var compact) || compact;
        OnPropertyChanged(nameof(UseCompactInterface));
        var engines = await _settings.GetAsync("interface.engines.cards.v2").ConfigureAwait(true);
        _engineCardsView = !bool.TryParse(engines, out var engineCards) || engineCards;
        var projects = await _settings.GetAsync("interface.projects.cards.v2").ConfigureAwait(true);
        _projectCardsView = !bool.TryParse(projects, out var projectCards) || projectCards;
        OnPropertyChanged(nameof(EngineCardsView));
        OnPropertyChanged(nameof(ProjectCardsView));
    }
}
