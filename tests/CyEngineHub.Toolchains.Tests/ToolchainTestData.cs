using CyEngineHub.Toolchains.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Toolchains.Tests;

internal static class ToolchainTestData
{
    public static UnrealEngineInstallation CreateEngine(string root, string id = "ENGINE") => new(
        id,
        "5.8",
        "Unreal Engine Test",
        root,
        Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
        new EngineVersion(5, 8, 0),
        EngineInstallationType.SourceFork,
        "test",
        null,
        null,
        "test",
        EngineHealth.Healthy,
        [],
        DateTimeOffset.UtcNow);

    public static ToolchainInstallation Installation(
        ToolchainKind kind,
        string version,
        IReadOnlyDictionary<string, string>? details = null) => new(
        $"{kind}-{version}",
        kind,
        $"{kind} {version}",
        version,
        Path.Combine("C:\\Tools", kind.ToString(), version),
        "Test",
        ToolchainHealth.Ready,
        [],
        details ?? new Dictionary<string, string>());
}
