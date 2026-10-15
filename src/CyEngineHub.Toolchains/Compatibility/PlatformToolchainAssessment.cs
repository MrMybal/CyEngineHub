using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Compatibility;

public static class PlatformToolchainAssessment
{
    public static IReadOnlyList<ToolchainCompatibilityCheck> ChecksFor(EngineToolchainSnapshot snapshot, string platform) =>
        snapshot.Checks.Where(check => IsRequired(check.Kind, platform)).ToArray();

    public static bool CanBuild(EngineToolchainSnapshot snapshot, string platform)
    {
        var checks = ChecksFor(snapshot, platform);
        var expected = platform switch { "Win64" => 3, "Linux" => 2, "Android" => 6, _ => int.MaxValue };
        return checks.Select(check => check.Kind).Distinct().Count() >= expected &&
            checks.All(check => check.Health == ToolchainHealth.Ready);
    }

    private static bool IsRequired(ToolchainKind kind, string platform) => kind == ToolchainKind.DotNet || platform switch
    {
        "Win64" => kind is ToolchainKind.Msvc or ToolchainKind.WindowsSdk,
        "Linux" => kind == ToolchainKind.LinuxToolchain,
        "Android" => kind is ToolchainKind.AndroidSdk or ToolchainKind.AndroidNdk or ToolchainKind.CMake or ToolchainKind.Java or ToolchainKind.Gradle,
        _ => false,
    };
}
