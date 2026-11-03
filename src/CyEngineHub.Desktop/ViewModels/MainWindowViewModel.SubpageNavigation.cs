namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool HasLibraryProductBack => IsLibraryVisible && IsRemoteFabProductVisible;

    public string NavigationBackLabel => HasLibraryProductBack ? "← Retour aux assets" : _backNavigation.TryPeek(out var previous) ? previous switch
    {
        PageSection.ProjectDetails => "← Retour au projet",
        PageSection.EngineDetails => "← Retour au moteur",
        PageSection.Plugins => "← Retour aux plugins",
        PageSection.Builds => "← Retour aux builds",
        _ => "← Retour",
    } : "← Retour";
}
