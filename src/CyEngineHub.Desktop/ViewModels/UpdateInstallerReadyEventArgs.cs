namespace CyEngineHub.Desktop.ViewModels;

public sealed class UpdateInstallerReadyEventArgs : EventArgs
{
    public UpdateInstallerReadyEventArgs(string installerPath)
    {
        InstallerPath = installerPath;
    }

    public string InstallerPath { get; }
}
