using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Plugins.Models;

public enum PluginSourceKind
{
    Engine,
    Project,
    LocalLibrary,
}

public enum PluginHealth
{
    Healthy,
    Warning,
    Broken,
}

public sealed record PluginCandidate(
    string DescriptorPath,
    PluginSourceKind SourceKind,
    string SourceLabel,
    string? SourceOwnerId = null);

public sealed record UnrealPluginModule(
    string Name,
    string Type,
    string LoadingPhase,
    IReadOnlyList<string> PlatformAllowList,
    IReadOnlyList<string> PlatformDenyList);

public sealed record UnrealPluginDependency(string Name, bool Enabled, bool Optional);

public sealed record UnrealPluginDescriptor(
    string Id,
    string Name,
    string FriendlyName,
    string Description,
    string Category,
    string DescriptorPath,
    string RootPath,
    PluginSourceKind SourceKind,
    string SourceLabel,
    string? SourceOwnerId,
    int FileVersion,
    int Version,
    string VersionName,
    string? EngineVersion,
    bool CanContainContent,
    bool Installed,
    bool HasSource,
    IReadOnlyList<UnrealPluginModule> Modules,
    IReadOnlyList<string> SupportedTargetPlatforms,
    IReadOnlyList<UnrealPluginDependency> Dependencies,
    PluginHealth Health,
    IReadOnlyList<string> Issues,
    DateTimeOffset DiscoveredAt,
    bool? EnabledByDefault = null);

public enum PluginInstallTargetKind
{
    Engine,
    Project,
}

public sealed record PluginInstallTarget(
    string Id,
    string DisplayName,
    PluginInstallTargetKind Kind,
    string PluginsDirectory,
    UnrealEngineInstallation Engine);

public sealed record PluginCompatibility(
    bool IsCompatible,
    bool RequiresRebuild,
    IReadOnlyList<string> Warnings);

public sealed record PluginInstallPlan(
    UnrealPluginDescriptor Plugin,
    PluginInstallTarget Target,
    string TargetPlatform,
    string DestinationDirectory,
    bool WillReplaceExisting,
    long FileCount,
    long TotalBytes,
    int SkippedReparsePoints,
    PluginCompatibility Compatibility);

public sealed record PluginInstallProgress(long CompletedFiles, long TotalFiles, string Message);

public sealed record PluginInstallResult(
    string DestinationDirectory,
    string? BackupDirectory,
    long CopiedFiles,
    long CopiedBytes,
    TimeSpan Duration);

public sealed class PluginOperationException : Exception
{
    public PluginOperationException(
        string message,
        string operation,
        string pluginPath,
        string? targetPath = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        PluginPath = pluginPath;
        TargetPath = targetPath;
    }

    public string Operation { get; }

    public string PluginPath { get; }

    public string? TargetPath { get; }
}
