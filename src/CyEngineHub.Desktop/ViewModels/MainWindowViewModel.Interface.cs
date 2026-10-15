namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _useCompactInterface = true;
    private Task _interfaceWrite = Task.CompletedTask;
    public bool UseCompactInterface
    {
        get => _useCompactInterface;
        set
        {
            if (!SetProperty(ref _useCompactInterface, value)) return;
            _interfaceWrite = SaveInterfaceAsync(_interfaceWrite, value);
        }
    }
    private async Task SaveInterfaceAsync(Task previous, bool compact)
    {
        try { await previous.ConfigureAwait(true); } catch (Exception) { }
        try { await _settings.SetAsync("interface.compact.v2", compact ? "true" : "false").ConfigureAwait(true); }
        catch (Exception) { StatusMessage = "La densité de l’interface n’a pas pu être enregistrée."; }
    }
    private async Task LoadInterfaceAsync()
    {
        var value = await _settings.GetAsync("interface.compact.v2").ConfigureAwait(true);
        _useCompactInterface = !bool.TryParse(value, out var compact) || compact;
        OnPropertyChanged(nameof(UseCompactInterface));
    }
}
