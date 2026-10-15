using CyEngineHub.Toolchains.Compatibility;
using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Tests;

public sealed class PlatformToolchainAssessmentTests
{
    [Fact]
    public void MissingLinuxDoesNotBlockWindows()
    {
        var snapshot = Snapshot([Check(ToolchainKind.DotNet), Check(ToolchainKind.Msvc), Check(ToolchainKind.WindowsSdk),
            Check(ToolchainKind.LinuxToolchain, ToolchainHealth.Missing)]);
        Assert.True(PlatformToolchainAssessment.CanBuild(snapshot, "Win64"));
        Assert.False(PlatformToolchainAssessment.CanBuild(snapshot, "Linux"));
    }
    [Fact]
    public void UnknownTargetsAndAbsentChecksAreNotReady()
    {
        Assert.False(PlatformToolchainAssessment.CanBuild(Snapshot([]), "Linux"));
        Assert.False(PlatformToolchainAssessment.CanBuild(Snapshot([Check(ToolchainKind.DotNet)]), "Mac"));
    }
    [Fact]
    public void AndroidNeedsSdkNdkJdkCmakeGradleButNotAdb()
    {
        var snapshot = Snapshot([Check(ToolchainKind.DotNet), Check(ToolchainKind.AndroidSdk), Check(ToolchainKind.AndroidNdk),
            Check(ToolchainKind.Java), Check(ToolchainKind.CMake), Check(ToolchainKind.Gradle),
            Check(ToolchainKind.Adb, ToolchainHealth.Missing)]);
        Assert.True(PlatformToolchainAssessment.CanBuild(snapshot, "Android"));
    }
    [Fact]
    public void LinuxVersionMismatchRequiresAttention()
    {
        Assert.False(PlatformToolchainAssessment.CanBuild(Snapshot([Check(ToolchainKind.DotNet),
            Check(ToolchainKind.LinuxToolchain, ToolchainHealth.Warning)]), "Linux"));
    }
    private static EngineToolchainSnapshot Snapshot(IReadOnlyList<ToolchainCompatibilityCheck> checks) => new("fixture", "Fixture", "5.5", checks, []);
    private static ToolchainCompatibilityCheck Check(ToolchainKind kind, ToolchainHealth health = ToolchainHealth.Ready) => new(kind, kind.ToString(), health, "fixture", "fixture", "");
}
