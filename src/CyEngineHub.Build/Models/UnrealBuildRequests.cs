using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Build.Models;

public enum UnrealBuildAction
{
    Build,
    Clean,
    Rebuild,
}

public enum UnrealProjectFileFormat
{
    Default,
    VisualStudio2022,
    Rider,
}

public sealed record SetupEngineRequest(
    UnrealEngineInstallation Engine,
    IReadOnlyList<string>? AdditionalArguments = null);

public sealed record GenerateProjectFilesRequest(
    UnrealEngineInstallation Engine,
    UnrealProject? Project = null,
    UnrealProjectFileFormat Format = UnrealProjectFileFormat.Default,
    bool IncludeEngineSource = true);

public sealed record UnrealTargetBuildRequest(
    UnrealEngineInstallation Engine,
    string Target,
    string Platform = "Win64",
    string Configuration = "Development",
    UnrealBuildAction Action = UnrealBuildAction.Build,
    UnrealProject? Project = null,
    int? MaximumParallelActions = null,
    IReadOnlyList<string>? AdditionalArguments = null);

public sealed record UnrealPackageRequest(
    UnrealEngineInstallation Engine,
    UnrealProject Project,
    string TargetPlatform,
    string Configuration,
    string ArchiveDirectory,
    bool Build = true,
    bool Cook = true,
    bool Stage = true,
    bool Package = true,
    bool Archive = true,
    bool Pak = true,
    bool IoStore = true,
    bool Compressed = false,
    bool Distribution = false,
    bool IncludePrerequisites = true,
    string? CookFlavor = null,
    IReadOnlyList<string>? AdditionalArguments = null);

public sealed record BuildPluginRequest(
    UnrealEngineInstallation Engine,
    string PluginDescriptorPath,
    string PackageDirectory,
    IReadOnlyList<string> TargetPlatforms,
    bool NoHostPlatform = false,
    bool StrictIncludes = false,
    bool Unversioned = false);

public enum BuildLogSeverity
{
    Trace,
    Information,
    Warning,
    Error,
    Fatal,
}

public sealed record BuildLogLine(
    DateTimeOffset Timestamp,
    BuildLogSeverity Severity,
    string Text);

public sealed record UnrealBuildExecutionResult(
    int ExitCode,
    TimeSpan Duration,
    int WarningCount,
    int ErrorCount,
    IReadOnlyList<BuildLogLine> Tail)
{
    public bool Succeeded => ExitCode == 0 && ErrorCount == 0;
}
