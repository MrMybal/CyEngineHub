using CyEngineHub.Core.Marketplaces;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class BrowserDownloadViewModel : ObservableObject
{
    private DownloadTelemetrySnapshot _snapshot;
    public BrowserDownloadViewModel(DownloadTelemetrySnapshot snapshot) => _snapshot = snapshot;
    public string FilePath => _snapshot.FilePath;
    public string Name => Path.GetFileName(FilePath);
    public double Percent => _snapshot.Percent ?? 0;
    public string Status => _snapshot.State;
    public bool IsIndeterminate => _snapshot.Percent is null && _snapshot.State == "Téléchargement";
    public string Summary => $"{_snapshot.State} · {DownloadProgressText.Summary(_snapshot)}";
    public string Timing => DownloadProgressText.Timing(_snapshot);
    public void Update(DownloadTelemetrySnapshot snapshot)
    {
        _snapshot = snapshot;
        OnPropertyChanged(nameof(Percent)); OnPropertyChanged(nameof(IsIndeterminate)); OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(Timing));
    }
}
