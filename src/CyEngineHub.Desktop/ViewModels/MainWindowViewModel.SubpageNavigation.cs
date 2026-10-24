namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string NavigationBackLabel => _backNavigation.TryPeek(out var previous) ? previous switch
    {
        PageSection.ProjectDetails => "← Retour au projet",
        PageSection.EngineDetails => "← Retour au moteur",
        PageSection.Plugins => "← Retour aux plugins",
        PageSection.Builds => "← Retour aux builds",
        _ => "← Retour",
    } : "← Retour";
}
