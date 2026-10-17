using System.Windows.Input;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Epic.Acquisition;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class EngineDownloadCardViewModel : ObservableObject
{
    private HubTaskSnapshot _snapshot;
    private bool _cancellationRequested;
    private string? _registrationFailure;
    private string? _retryError;
    public EngineDownloadCardViewModel(EpicBinaryEngineRelease release, string path, HubTaskSnapshot snapshot,
        Action<Guid> cancel, Action showDownloads, Func<Task>? retry = null)
    {
        Name = release.DisplayName; Version = release.Version; Path = path; _snapshot = snapshot;
        CancelCommand = new RelayCommand(() =>
        {
            MarkCancellationRequested(); cancel(TaskId);
        }, () => CanCancel && !_cancellationRequested);
        OpenDetailsCommand = new RelayCommand(showDownloads);
        RetryCommand = new AsyncRelayCommand(async () =>
        {
            if (retry is not null) await retry().ConfigureAwait(true);
        }, () => retry is not null && CanRetry, onError: exception => { _retryError = exception.Message; Notify(); });
    }
    public string Name { get; }
    public string Version { get; }
    public string Path { get; }
    public Guid TaskId => _snapshot.Id;
    public bool IsOfficialEpic { get; } = true;
    public bool IsInstallationAvailable { get; }
    public bool IsCompleted => _snapshot.Status == HubTaskStatus.Completed && _registrationFailure is null;
    public bool CanCancel => _snapshot.Status is HubTaskStatus.Queued or HubTaskStatus.Running;
    public bool CanRetry => _registrationFailure is null && _snapshot.Status is HubTaskStatus.Failed or HubTaskStatus.Canceled;
    public bool IsCancellationRequested => _cancellationRequested && CanCancel;
    public double ProgressPercent => (_snapshot.Progress ?? 0) * 100;
    public string StatusLabel => _registrationFailure is not null ? "Échec de l’ajout" : IsCancellationRequested ? "Annulation…" : _snapshot.Status switch
    {
        HubTaskStatus.Queued => "En attente", HubTaskStatus.Running => "En téléchargement",
        HubTaskStatus.Completed => "Installation terminée", HubTaskStatus.Canceled => "Annulé",
        HubTaskStatus.Failed => "Échec", _ => "—"
    };
    public string Message => _registrationFailure ?? _retryError ?? _snapshot.Error ?? _snapshot.Message ?? "Préparation du téléchargement…";
    public string EffectiveSizeLabel => Message;
    public string OfficialUpdateLabel => StatusLabel;
    public ICommand CancelCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand OpenDetailsCommand { get; }
    public void ApplySnapshot(HubTaskSnapshot snapshot) { if (_snapshot == snapshot) return; _snapshot = snapshot; Notify(); }
    public void MarkCancellationRequested() { _cancellationRequested = true; Notify(); }
    public void SetRegistrationFailure(string message) { _registrationFailure = message; Notify(); }
    private void Notify()
    {
        OnPropertyChanged(nameof(StatusLabel)); OnPropertyChanged(nameof(Message)); OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(CanCancel)); OnPropertyChanged(nameof(IsCompleted)); OnPropertyChanged(nameof(IsCancellationRequested));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(EffectiveSizeLabel)); OnPropertyChanged(nameof(OfficialUpdateLabel));
        (CancelCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (RetryCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }
}
