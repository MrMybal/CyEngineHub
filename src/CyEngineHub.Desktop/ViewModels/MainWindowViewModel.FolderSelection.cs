namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public void ReportFolderSelectionError(string message)
    {
        StatusMessage = message;
        if (IsEngineImporterVisible) EngineBinaryDownloadSummary = message;
        AppendRawLog("WARN", "Folders", message);
    }
}
