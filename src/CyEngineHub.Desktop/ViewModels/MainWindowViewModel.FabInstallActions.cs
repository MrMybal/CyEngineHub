using CyEngineHub.Core.Tasks;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isFabTargetInstallationActive;
    private int _fabTargetInstallationCount;
    private bool _bypassFabInstallChecks;
    private string _fabInstallActionError = string.Empty;

    public bool BypassFabInstallChecks
    {
        get => _bypassFabInstallChecks;
        set
        {
            if (!SetProperty(ref _bypassFabInstallChecks, value)) return;
            (InstallSelectedFabVersionsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(FabInstallActionLabel));
            OnPropertyChanged(nameof(FabInstallActionStatus));
        }
    }

    public string FabInstallActionLabel => BypassFabInstallChecks ? "Installer quand même" : "Installer dans les cibles";

    public bool IsFabTargetInstallationActive
    {
        get => _isFabTargetInstallationActive;
        private set
        {
            if (!SetProperty(ref _isFabTargetInstallationActive, value)) return;
            (InstallSelectedFabVersionsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(FabInstallActionStatus));
        }
    }

    public string FabInstallActionError
    {
        get => _fabInstallActionError;
        private set => SetProperty(ref _fabInstallActionError, value);
    }

    public string FabInstallActionStatus => BypassFabInstallChecks
        ? "Forçage activé : les refus préalables du Hub sont ignorés. Votre méthode et ses options sont exécutées telles quelles ; les tâches sont placées dans Actions."
        : IsFabTargetInstallationActive
        ? "Installations en cours : les nouvelles cibles indépendantes peuvent démarrer en parallèle. Les projets, packs partagés et disques sont protégés."
        : IsEpicBusy
            ? "Une actualisation Epic/Fab est en cours. Cochez « Forcer l’installation avec la méthode choisie » pour lancer votre installation sans attendre."
            : "Pour passer outre les refus préalables, cochez « Forcer l’installation avec la méthode choisie ».";

    private void ShowFabInstallActionError(Exception exception)
    {
        FabInstallActionError = exception.Message;
        ShowError(exception);
    }

    private async Task InstallSelectedFabVersionsIntoTargetsAsync(Action<HubTaskHandle>? onQueued = null, bool editorSafe = false, bool force = false)
    {
        if (editorSafe && force) throw new InvalidOperationException("L’installation forcée n’est disponible que depuis le Hub, pas depuis le pont éditeur.");
        FabInstallActionError = string.Empty;
        _fabTargetInstallationCount++;
        IsFabTargetInstallationActive = true;
        try
        {
            await InstallSelectedFabVersionsIntoTargetsCoreAsync(onQueued, editorSafe, force).ConfigureAwait(true);
        }
        finally
        {
            IsFabTargetInstallationActive = --_fabTargetInstallationCount > 0;
            IsFabDownloadActive = IsFabTargetInstallationActive || _fabVersionDownloads.Any(item => !item.IsTerminal);
        }
    }

    private FabInstallTargetOptionViewModel SnapshotFabInstallTarget(FabInstallTargetOptionViewModel target)
    {
        var selected = target.SelectedInstallationScript;
        if (selected is null) return target;
        var script = _fabInstallationScripts.Find(selected.Id)
            ?? throw new InvalidOperationException($"Le script sélectionné {selected.DisplayName} est introuvable.");
        // Keep the exact method/options chosen at click time, including while another job runs.
        return new(target.Id, target.Name, target.Kind, target.Path, target.EngineVersion, target.IsCompatible,
            target.IgnoresEngineCompatibility, target.ContentKind, [script], selected.Id,
            target.GetInstallationConfiguration(), target.Project, target.Engine) { IsSelected = true };
    }
}
