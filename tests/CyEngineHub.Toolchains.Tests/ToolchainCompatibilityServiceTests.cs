using CyEngineHub.Toolchains.Compatibility;
using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Tests;

public sealed class ToolchainCompatibilityServiceTests
{
    [Theory]
    [InlineData("14.38.33130", "14.38.33130", 0)]
    [InlineData("14.44.35207", "14.38.33130", 1)]
    [InlineData("10.0.19041.0", "10.0.22621.0", -1)]
    public void NumericVersionComparisonHandlesToolchainVersions(
        string left,
        string right,
        int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(ToolchainVersion.Compare(left, right)));
    }

    [Fact]
    public void CompatibilitySelectsPreferredMsvcInsteadOfNewestInstallation()
    {
        var requirements = CreateRequirements(new WindowsToolchainRequirement(
            "10.0.22621.0",
            "10.0.19041.0",
            "10.9.99999.0",
            "14.38.33130",
            ["14.38.33130-14.38.99999"],
            ["14.39.33519-14.40.99999"],
            "17.8",
            "Windows_SDK.json"));
        var installations = new[]
        {
            ToolchainTestData.Installation(ToolchainKind.VisualStudio, "17.14.18"),
            ToolchainTestData.Installation(ToolchainKind.Msvc, "14.38.33130"),
            ToolchainTestData.Installation(ToolchainKind.Msvc, "14.44.35207"),
            ToolchainTestData.Installation(ToolchainKind.WindowsSdk, "10.0.22621.0"),
            ToolchainTestData.Installation(ToolchainKind.DotNet, "8.0.413"),
        };

        var result = new ToolchainCompatibilityService().Evaluate(requirements, installations);
        var msvc = result.Checks.Single(check => check.Kind == ToolchainKind.Msvc);

        Assert.Equal(ToolchainHealth.Ready, msvc.Health);
        Assert.Equal("14.38.33130", msvc.Installed);
    }

    [Fact]
    public void CompatibilityReportsMissingAndroidComponentsPrecisely()
    {
        var requirements = new UnrealEngineToolchainRequirements(
            "ENGINE",
            "UE Test",
            "5.8.0",
            "C:\\Engine",
            null,
            new AndroidToolchainRequirement(
                "r25b",
                "r25b",
                "r27",
                "android-34",
                "34.0.0",
                "3.22.1",
                "25.1.8937393",
                "Android_SDK.json"),
            null,
            []);
        var sdk = ToolchainTestData.Installation(
            ToolchainKind.AndroidSdk,
            "android-33",
            new Dictionary<string, string>
            {
                ["Platforms"] = "android-33",
                ["BuildTools"] = "33.0.1",
            });

        var result = new ToolchainCompatibilityService().Evaluate(requirements, [sdk]);

        Assert.Equal(
            ToolchainHealth.Missing,
            result.Checks.Single(check => check.Kind == ToolchainKind.AndroidSdk).Health);
        Assert.Equal(
            "25.1.8937393",
            result.Checks.Single(check => check.Kind == ToolchainKind.AndroidNdk).Required);
    }

    private static UnrealEngineToolchainRequirements CreateRequirements(
        WindowsToolchainRequirement windows) => new(
        "ENGINE",
        "UE Test",
        "5.8.0",
        "C:\\Engine",
        windows,
        null,
        null,
        []);
}
