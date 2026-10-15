using System.Windows.Input;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _isFabWebLoginActive;
    private bool _isFabManualSignInExpanded;

    public ICommand OpenEmbeddedFabAuthorizationCommand { get; }
    public event EventHandler<FabWebAuthorizationRequestEventArgs>? FabWebAuthorizationRequested;

    public bool IsFabWebLoginActive
    {
        get => _isFabWebLoginActive;
        private set
        {
            if (SetProperty(ref _isFabWebLoginActive, value))
            {
                OnPropertyChanged(nameof(FabWebLoginButtonText));
                (OpenEmbeddedFabAuthorizationCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (OpenQuixelAuthorizationCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                _openManualQuixelAuthorizationCommand?.NotifyCanExecuteChanged();
                (ConnectQuixelCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (ConnectDirectFabCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (DisconnectDirectFabCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (ConnectQuixelFromEpicCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (DisconnectQuixelCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public string FabWebLoginButtonText => IsFabWebLoginActive ? "Connexion en cours…" : "Se connecter dans CyEngineHub";

    public bool IsFabManualSignInExpanded
    {
        get => _isFabManualSignInExpanded;
        set => SetProperty(ref _isFabManualSignInExpanded, value);
    }

    private async Task OpenEmbeddedFabAuthorizationAsync()
    {
        if (IsFabWebLoginActive || IsEpicBusy || IsDirectFabConnected) return;
        if (FabWebAuthorizationRequested is null || !OperatingSystem.IsWindows())
        {
            IsFabManualSignInExpanded = true;
            DirectFabStatusSummary = "Navigateur intégré indisponible : utilisez le mode manuel ci-dessous.";
            return;
        }
        IsFabWebLoginActive = true;
        var connected = false;
        try
        {
            DirectFabStatusSummary = "Connectez-vous dans la fenêtre Epic. La réponse JSON sera récupérée automatiquement.";
            var request = new FabWebAuthorizationRequestEventArgs(_directFabClient.AuthorizationUri,
                Path.Combine(DataDirectory, "WebView2", "Fab"));
            FabWebAuthorizationRequested.Invoke(this, request);
            var result = await request.Completion.ConfigureAwait(true);
            if (result.UseManualMode)
            {
                IsFabManualSignInExpanded = true;
                DirectFabStatusSummary = "Mode manuel : ouvrez Epic dans votre navigateur, puis copiez le JSON ou collez le code ci-dessous.";
                return;
            }
            if (string.IsNullOrEmpty(result.Code))
            {
                DirectFabStatusSummary = "Connexion annulée. Les modes automatique et manuel restent disponibles.";
                return;
            }
            IsEpicBusy = true;
            try
            {
                DirectFabStatusSummary = "Réponse Epic reçue · connexion à Fab…";
                using var exchangeTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
                var status = await _directFabClient.ConnectAsync(result.Code, exchangeTimeout.Token).ConfigureAwait(true);
                ApplyDirectFabStatus(status);
                connected = IsDirectFabConnected;
                FabAuthorizationCode = string.Empty;
                StatusMessage = connected ? "Compte Epic/Fab connecté automatiquement, sans copier de code."
                    : "Epic n’a pas confirmé la connexion. Réessayez ou utilisez le mode manuel.";
                if (!connected) IsFabManualSignInExpanded = true;
                AppendRawLog(connected ? "INFO" : "WARN", "Connections/Fab",
                    connected ? "Connexion par le navigateur intégré terminée." : "Connexion intégrée non confirmée.");
            }
            finally { IsEpicBusy = false; }
        }
        catch (Exception)
        {
            // Auth exceptions can contain callback data: never put them in logs or UI.
            IsFabManualSignInExpanded = true;
            DirectFabStatusSummary = "La connexion automatique n’a pas abouti. Réessayez avec une nouvelle autorisation ou utilisez le mode manuel.";
            StatusMessage = DirectFabStatusSummary;
            AppendRawLog("WARN", "Connections/Fab", "Connexion intégrée non terminée ; mode manuel disponible.");
        }
        finally { IsFabWebLoginActive = false; }

        if (connected)
        {
            try { await RefreshDirectFabLibraryAsync().ConfigureAwait(true); }
            catch (Exception) { StatusMessage = "Fab est connecté. Le catalogue n’a pas pu être actualisé ; réessayez depuis la Bibliothèque."; }
        }
    }
}
