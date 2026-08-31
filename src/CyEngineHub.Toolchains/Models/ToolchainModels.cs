namespace CyEngineHub.Toolchains.Models;

public enum ToolchainKind
{
    VisualStudio,
    Msvc,
    WindowsSdk,
    DotNet,
    CMake,
    Ninja,
    Clang,
    LinuxToolchain,
    AndroidSdk,
    AndroidNdk,
    Java,
    Gradle,
    Adb,
}

public enum ToolchainHealth
{
    Ready,
    Warning,
    Missing,
}

public sealed record ToolchainInstallation(
    string Id,
    ToolchainKind Kind,
    string DisplayName,
    string Version,
    string Path,
    string Source,
    ToolchainHealth Health,
    IReadOnlyList<string> Issues,
    IReadOnlyDictionary<string, string> Details);

public sealed record EnvironmentVariableSnapshot(
    string Name,
    string? ProcessValue,
    string? UserValue,
    string? MachineValue)
{
    public bool HasValue =>
        !string.IsNullOrWhiteSpace(ProcessValue) ||
        !string.IsNullOrWhiteSpace(UserValue) ||
        !string.IsNullOrWhiteSpace(MachineValue);
}

public sealed record WindowsToolchainRequirement(
    string? MainWindowsSdkVersion,
    string? MinimumWindowsSdkVersion,
    string? MaximumWindowsSdkVersion,
    string? MinimumMsvcVersion,
    IReadOnlyList<string> PreferredMsvcRanges,
    IReadOnlyList<string> BannedMsvcRanges,
    string? MinimumVisualStudioVersion,
    string SourcePath);

public sealed record AndroidToolchainRequirement(
    string? MainNdkLabel,
    string? MinimumNdkLabel,
    string? MaximumNdkLabel,
    string? Platform,
    string? BuildToolsVersion,
    string? CMakeVersion,
    string? NdkVersion,
    string SourcePath);

public sealed record LinuxToolchainRequirement(
    string? MainVersion,
    string? MinimumVersion,
    string? MaximumVersion,
    string SourcePath);

public sealed record UnrealEngineToolchainRequirements(
    string EngineId,
    string EngineDisplayName,
    string EngineVersion,
    string EngineRoot,
    WindowsToolchainRequirement? Windows,
    AndroidToolchainRequirement? Android,
    LinuxToolchainRequirement? Linux,
    IReadOnlyList<string> Diagnostics);

public sealed record ToolchainCompatibilityCheck(
    ToolchainKind Kind,
    string Label,
    ToolchainHealth Health,
    string Required,
    string Installed,
    string Diagnostic);

public sealed record EngineToolchainSnapshot(
    string EngineId,
    string EngineDisplayName,
    string EngineVersion,
    IReadOnlyList<ToolchainCompatibilityCheck> Checks,
    IReadOnlyList<string> Diagnostics)
{
    public ToolchainHealth OverallHealth => Checks.Any(check => check.Health == ToolchainHealth.Missing)
        ? ToolchainHealth.Missing
        : Checks.Any(check => check.Health == ToolchainHealth.Warning)
            ? ToolchainHealth.Warning
            : ToolchainHealth.Ready;
}

public sealed record AndroidDeviceSnapshot(
    string Serial,
    string State,
    string Model,
    string Product,
    string Device,
    string TransportId,
    bool IsQuest);

public sealed record ToolchainSnapshot(
    IReadOnlyList<ToolchainInstallation> Installations,
    IReadOnlyList<EngineToolchainSnapshot> Engines,
    IReadOnlyList<EnvironmentVariableSnapshot> EnvironmentVariables,
    IReadOnlyList<AndroidDeviceSnapshot> AndroidDevices,
    IReadOnlyList<string> Diagnostics,
    DateTimeOffset RefreshedAt);

public sealed record ToolchainRefreshProgress(int Completed, int Total, string Message);

public sealed class ToolchainException : Exception
{
    public ToolchainException(
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
