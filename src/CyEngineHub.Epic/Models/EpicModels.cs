namespace CyEngineHub.Epic.Models;

public enum EpicArtifactKind
{
    UnrealEngine,
    FabIntegration,
    UnrealPlugin,
    UnrealContent,
    EpicApplication,
    Unknown,
}

public enum EpicAuthenticationState
{
    ExternalBrowserRequired,
    Connected,
    Unavailable,
}

public enum EpicCapabilityState
{
    Available,
    ExternalOnly,
    ResearchRequired,
    Unavailable,
}

public sealed record EpicLocalArtifact(
    string Id,
    string AppName,
    string DisplayName,
    string Version,
    string InstallLocation,
    string CatalogItemId,
    string CatalogNamespace,
    EpicArtifactKind Kind,
    bool IsInstalled,
    string Source);

public sealed record EpicLocalSnapshot(
    bool IsLauncherInstalled,
    bool IsLauncherRunning,
    string? LauncherPath,
    string ManifestDirectory,
    string VaultCachePath,
    int VaultItemCount,
    string FabPluginsPath,
    int FabPluginPackageCount,
    IReadOnlyList<EpicLocalArtifact> Artifacts,
    IReadOnlyList<string> Diagnostics,
    DateTimeOffset RefreshedAt);

public sealed record EpicAuthenticationStatus(
    EpicAuthenticationState State,
    string Provider,
    string Message,
    bool CanVerifyIdentity,
    bool CanQueryGlobalEntitlements);

public sealed record EpicCapability(
    string Name,
    EpicCapabilityState State,
    string Description);

public sealed record EpicCatalogProgress(int Completed, int Total, string Message);

public sealed class EpicOperationException : Exception
{
    public EpicOperationException(
        string message,
        string operation,
        string? path = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        Path = path;
    }

    public string Operation { get; }

    public string? Path { get; }
}
