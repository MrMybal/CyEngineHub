using CyEngineHub.Core.Diagnostics;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly ActivityLogPolicy _activityLogPolicy = new();
    // High-frequency traces must not evict the current session's important events.
    private readonly List<RawLogEntryViewModel> _importantLogEntries = [];
    private bool _showFullRawLog;
    public bool ShowFullRawLog
    {
        get => _showFullRawLog;
        set
        {
            if (!SetProperty(ref _showFullRawLog, value)) return;
            OnPropertyChanged(nameof(RawLogModeLabel));
            ApplyRawLogFilter(scrollToEnd: true);
        }
    }
    public string RawLogModeLabel => ShowFullRawLog ? "JOURNAL COMPLET · DIAGNOSTIC" : "JOURNAL · ÉVÉNEMENTS IMPORTANTS";
}
