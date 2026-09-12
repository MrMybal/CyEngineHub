using CyEngineHub.Core.Processes;
using CyEngineHub.Engines;

namespace CyEngineHub.Extensions;

public enum HubExtensionKind
{
    Tool,
    AssetProcessor,
    EngineIntegration,
    Marketplace,
}

public enum HubExtensionExecutionKind
{
    ExternalProcess,
    WebLink,
}

[Flags]
public enum HubExtensionCapability
{
    None = 0,
    AssetInspect = 1 << 0,
    AssetCopy = 1 << 1,
    AssetConvert = 1 << 2,
    AssetDowngrade = 1 << 3,
    ProjectTool = 1 << 4,
    AssetImport = 1 << 5,
    DependencyValidation = 1 << 6,
    OwnedInventory = 1 << 7,
}

public enum HubExtensionOrigin
{
    BuiltIn,
    User,
}

public enum HubExtensionHealth
{
    Ready,
    Invalid,
}

public enum HubUiPlacement
{
    GlobalToolbar,
    EnginesToolbar,
    ProjectsToolbar,
    PluginsToolbar,
}

public enum HubUiAction
{
    OpenExtension,
    OpenWebsite,
    PrepareExecution,
}

public sealed class HubUiContribution
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public HubUiPlacement Placement { get; init; }

    public HubUiAction Action { get; init; }

    public int Order { get; init; }
}

public sealed class HubExtensionManifest
{
    public int SchemaVersion { get; init; }

    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Publisher { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public HubExtensionKind Kind { get; init; }

    public HubExtensionExecutionKind ExecutionKind { get; init; }

    public HubExtensionCapability Capabilities { get; init; }

    public IReadOnlyList<EngineFamily> SupportedEngines { get; init; } = [];

    public IReadOnlyList<string> SupportedFileExtensions { get; init; } = [];

    public string? EntryPoint { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? Website { get; init; }

    public bool WritesToOutput { get; init; }

    public IReadOnlyList<HubUiContribution> UiContributions { get; init; } = [];

    public HubMarketplaceDescriptor? Marketplace { get; init; }
}

/// <summary>Browser-based access to an account's owned inventory, not a store catalogue.</summary>
public sealed class HubMarketplaceDescriptor
{
    public string InventoryUrl { get; init; } = string.Empty;
}

public sealed record HubExtensionSnapshot(
    HubExtensionManifest Manifest,
    string RootDirectory,
    HubExtensionOrigin Origin,
    HubExtensionHealth Health,
    string? Error)
{
    public bool CanRun =>
        Health == HubExtensionHealth.Ready &&
        Origin == HubExtensionOrigin.BuiltIn &&
        Manifest.ExecutionKind == HubExtensionExecutionKind.ExternalProcess &&
        string.Equals(
            Manifest.Id,
            "cyenginehub.asset-bridge",
            StringComparison.Ordinal);

    public bool CanOpenWebsite =>
        Health == HubExtensionHealth.Ready &&
        Manifest.ExecutionKind == HubExtensionExecutionKind.WebLink;
}

public sealed record HubExtensionPackageFile(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);

public sealed record HubExtensionInstallPlan(
    Guid Id,
    HubExtensionManifest Manifest,
    string SourceDirectory,
    string TargetDirectory,
    IReadOnlyList<HubExtensionPackageFile> Files,
    long TotalBytes,
    bool ReplacesExisting,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record HubExtensionInstallResult(
    HubExtensionSnapshot Extension,
    string? BackupDirectory);

public sealed record HubAssetInspectionSummary(
    int TransferableFileCount,
    long TransferableBytes,
    int EngineNativeFileCount,
    int UnsupportedFileCount,
    int SkippedReparsePointCount);

public sealed record HubExtensionExecutionPlan(
    Guid Id,
    HubExtensionSnapshot Extension,
    string ExecutablePath,
    string ExecutableSha256,
    IReadOnlyList<string> Arguments,
    string InputPath,
    string OutputPath,
    EngineFamily SourceEngine,
    EngineFamily TargetEngine,
    HubAssetInspectionSummary InputSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record HubExtensionExecutionResult(
    HubExtensionExecutionPlan Plan,
    ProcessResult ProcessResult);

public sealed class HubExtensionException : Exception
{
    public HubExtensionException(
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
