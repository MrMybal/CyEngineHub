using CyEngineHub.Toolchains.Compatibility;
using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Tests;

public sealed class ToolchainRemediationTests
{
    [Theory]
    [InlineData(ToolchainKind.LinuxToolchain)]
    [InlineData(ToolchainKind.AndroidNdk)]
    [InlineData(ToolchainKind.DotNet)]
    public void EpicGuideKeepsTheSelectedEngineVersion(ToolchainKind kind)
    {
        var action = ToolchainRemediation.For(new(kind, "fixture", ToolchainHealth.Missing, "exact version", "Absent", ""), "5.2.1");
        Assert.Equal("dev.epicgames.com", action.DownloadPage!.Host);
        Assert.Equal("?application_version=5.2", action.DownloadPage.Query);
    }

    [Fact]
    public void GradleRemediationRepairsEngineInsteadOfDownloadingUnrelatedGlobalVersion()
    {
        var action = ToolchainRemediation.For(new(ToolchainKind.Gradle, "Gradle", ToolchainHealth.Missing, "Wrapper UE", "Absent", ""), "5.2.1");
        Assert.Null(action.DownloadPage);
        Assert.Contains("Réparer", action.ButtonLabel);
    }
}
