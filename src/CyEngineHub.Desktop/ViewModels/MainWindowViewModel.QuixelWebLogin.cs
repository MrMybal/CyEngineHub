using System.Windows.Input;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isQuixelManualSignInExpanded;
    public bool IsQuixelManualSignInExpanded
    {
        get => _isQuixelManualSignInExpanded;
        set => SetProperty(ref _isQuixelManualSignInExpanded, value);
    }
    private AsyncRelayCommand? _openManualQuixelAuthorizationCommand;
    public ICommand OpenManualQuixelAuthorizationCommand => _openManualQuixelAuthorizationCommand ??= new AsyncRelayCommand(
        OpenQuixelAuthorizationAsync, () => !IsFabWebLoginActive && !IsEpicBusy, ShowError);

    private async Task OpenEmbeddedQuixelAuthorizationAsync()
    {
        if (IsFabWebLoginActive || IsEpicBusy) return;
        if (FabWebAuthorizationRequested is null || !OperatingSystem.IsWindows())
        {
            IsQuixelManualSignInExpanded = true;
            QuixelStatusSummary = "Navigateur intégré indisponible : utilisez le mode manuel ou Megascans via Fab.";
            return;
        }
        IsFabWebLoginActive = true;
        try
        {
            QuixelStatusSummary = "Connectez-vous sur la page officielle Epic/Quixel. Le code est récupéré automatiquement.";
            var request = new FabWebAuthorizationRequestEventArgs(_quixelClient.AuthorizationUri,
                Path.Combine(DataDirectory, "WebView2", "Quixel")) { ProviderDisplayName = "Epic / Quixel historique" };
            FabWebAuthorizationRequested.Invoke(this, request);
            var result = await request.Completion.ConfigureAwait(true);
            if (result.UseManualMode)
            {
                IsQuixelManualSignInExpanded = true;
                QuixelStatusSummary = "Mode manuel : ouvrez l’autorisation Epic/Quixel puis collez la réponse.";
                return;
            }
            if (string.IsNullOrWhiteSpace(result.Code))
            {
                QuixelStatusSummary = "Connexion annulée. Le cache existant est conservé.";
                return;
            }
            IsEpicBusy = true;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
            ApplyQuixelStatus(await _quixelClient.ConnectAsync(result.Code, timeout.Token).ConfigureAwait(true));
            QuixelAuthorizationCode = string.Empty;
            StatusMessage = IsQuixelConnected
                ? "Session Quixel connectée. L’accès au catalogue historique dépend de sa disponibilité et d’une clé de recherche autorisée."
                : "Quixel n’a pas confirmé la connexion. Utilisez le mode manuel ou Megascans via Fab.";
        }
        catch (Exception)
        {
            IsQuixelManualSignInExpanded = true;
            QuixelStatusSummary = "La connexion Quixel n’a pas abouti. Réessayez, utilisez le mode manuel ou Megascans via Fab.";
            StatusMessage = QuixelStatusSummary;
            AppendRawLog("WARN", "Connections/Quixel", "Connexion intégrée non terminée ; alternatives disponibles.");
        }
        finally { IsEpicBusy = false; IsFabWebLoginActive = false; }
    }
}
