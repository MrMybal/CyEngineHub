using CyEngineHub.Toolchains.Detection;
using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Tests;

public sealed class EngineToolchainDetectorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyEngineHub.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("windows", "gradle")]
    [InlineData("win-x64", "")]
    public void BundledLegacyAndModernLayoutsAreDetectedWithoutLaunchingTools(string dotnetLayout, string gradleFolder)
    {
        if (!OperatingSystem.IsWindows()) return;
        Touch(Path.Combine(_root, "Engine", "Binaries", "ThirdParty", "DotNet", "6.0.302", dotnetLayout, "dotnet.exe"));
        Touch(Path.Combine(_root, "Engine", "Build", "Android", "Java", gradleFolder, "gradlew.bat"));
        var items = new List<ToolchainInstallation>();
        EngineToolchainDetector.Detect([ToolchainTestData.CreateEngine(_root)], [], items, []);
        Assert.Single(items, item => item.Kind == ToolchainKind.DotNet && item.Details.GetValueOrDefault("EngineId") == "ENGINE");
        Assert.Single(items, item => item.Kind == ToolchainKind.Gradle && item.Details.GetValueOrDefault("EngineId") == "ENGINE");
    }

    [Fact]
    public void LinuxVersionDirectoryWithoutCompilerIsNotInstalled()
    {
        var root = Path.Combine(_root, "SDKs");
        Directory.CreateDirectory(Path.Combine(root, "v21_empty"));
        Touch(Path.Combine(root, "v22_valid", "x86_64-unknown-linux-gnu", "bin", OperatingSystem.IsWindows() ? "clang.exe" : "clang"));
        var items = new List<ToolchainInstallation>();
        EngineToolchainDetector.Detect([], [new("LINUX_MULTIARCH_ROOT", root, null, null)], items, []);
        var local = items.Where(item => item.Path.StartsWith(root, StringComparison.Ordinal)).ToArray();
        Assert.Equal("v22_valid", Assert.Single(local).Version);
        items.Clear();
        EngineToolchainDetector.Detect([], [new("LINUX_MULTIARCH_ROOT", Path.Combine(root, "v22_valid"), null, null)], items, []);
        Assert.Single(items, item => item.Path == Path.Combine(root, "v22_valid"));
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture only - never executed");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
