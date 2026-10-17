using System.Windows.Input;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private ICommand? _showActionsCommand;
    private int _engineDetailsTab;
    public int EngineDetailsTab { get => _engineDetailsTab; set => SetProperty(ref _engineDetailsTab, Math.Clamp(value, 0, 2)); }
    public ICommand ShowActionsCommand => _showActionsCommand ??= new RelayCommand(() => ShowSection(PageSection.Actions));
    public bool IsActionsVisible => _section == PageSection.Actions;
    public bool IsActionsNavigationActive => IsActionsVisible;
    public IEnumerable<TaskItemViewModel> VisibleActivityTasks => Tasks.Where(task => task.IsDownload != IsActionsVisible);
    public int RunningDownloads => Tasks.Count(task => task.IsDownload && task.IsRunning);
    public int QueuedDownloads => Tasks.Count(task => task.IsDownload && task.IsQueued);
    public int RunningActions => Tasks.Count(task => !task.IsDownload && task.IsRunning);
    public int QueuedActions => Tasks.Count(task => !task.IsDownload && task.IsQueued);
    private void NotifyActivityQueues()
    {
        foreach (var property in new[] { nameof(VisibleActivityTasks), nameof(RunningDownloads), nameof(QueuedDownloads), nameof(RunningActions), nameof(QueuedActions) })
            OnPropertyChanged(property);
    }
}
