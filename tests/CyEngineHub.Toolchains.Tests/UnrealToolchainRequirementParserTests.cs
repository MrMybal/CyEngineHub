using CyEngineHub.Toolchains.Detection;

namespace CyEngineHub.Toolchains.Tests;

public sealed class UnrealToolchainRequirementParserTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Toolchain engine é {Guid.NewGuid():N}");

    [Fact]
    public async Task ParserReadsModernSdkJsonFiles()
    {
        var windows = CreateConfig("Windows", "Windows_SDK.json");
        var android = CreateConfig("Android", "Android_SDK.json");
        var linux = CreateConfig("Linux", "Linux_SDK.json");
        await File.WriteAllTextAsync(
            windows,
            """
            {
              "MainVersion": "10.0.22621.0",
              "MinSoftwareVersion": "10.0.19041.0",
              "MinimumVisualCppVersion": "14.38.33130",
              "PreferredVisualCppVersions": ["14.38.33130-14.38.99999"],
              "BannedVisualCppVersions": ["14.39.0-14.40.99999"],
              "MinimumVisualStudio2022Version": "17.8",
            }
            """);
        await File.WriteAllTextAsync(
            android,
            """
            {
              "MainVersion": "r25b",
              "MinVersion": "r25b",
              "MaxVersion": "r27",
              "platforms": "android-34",
              "build-tools": "34.0.0",
              "cmake": "3.22.1",
              "ndk": "25.1.8937393",
            }
            """);
        await File.WriteAllTextAsync(
            linux,
            "{ \"MainVersion\": \"v23_clang-18.1.0-rockylinux8\" }");

        var result = await new UnrealToolchainRequirementParser().ParseAsync(
            ToolchainTestData.CreateEngine(_testDirectory));

        Assert.Equal("14.38.33130", result.Windows!.MinimumMsvcVersion);
        Assert.Equal(["14.38.33130-14.38.99999"], result.Windows.PreferredMsvcRanges);
        Assert.Equal("android-34", result.Android!.Platform);
        Assert.Equal("25.1.8937393", result.Android.NdkVersion);
        Assert.Equal("v23_clang-18.1.0-rockylinux8", result.Linux!.MainVersion);
    }

    [Fact]
    public async Task ParserFallsBackToLegacyVersionSources()
    {
        var windows = CreateSource("Windows", "MicrosoftPlatformSDK.Versions.cs");
        var android = CreateSource("Android", "AndroidPlatformSDK.Versions.cs");
        var linux = CreateSource("Linux", "LinuxPlatformSDK.Versions.cs");
        await File.WriteAllTextAsync(
            windows,
            """
            PreferredWindowsSdkVersions = { VersionNumber.Parse("10.0.18362.0") };
            MinimumWindowsSDKVersion = new VersionNumber(10, 0, 18362, 0);
            PreferredVisualCppVersions = { VersionNumberRange.Parse("14.36.32532", "14.36.99999") };
            MinimumRequiredClangVersion = null;
            BannedVisualCppVersions = { VersionNumberRange.Parse("14.30.0", "14.33.99999") };
            MinimumVisualCppVersion = new VersionNumber(14, 29, 30133);
            """);
        await File.WriteAllTextAsync(
            android,
            """
            GetMainVersionInternal() { return "r25b"; }
            GetValidVersionRange(out string MinVersion, out string MaxVersion) {
              MinVersion = "r25b"; MaxVersion = "r25b";
            }
            case "platforms": return "android-33";
            case "build-tools": return "33.0.1";
            case "cmake": return "3.10.2.4988404";
            case "ndk": return "25.1.8937393";
            """);
        await File.WriteAllTextAsync(
            linux,
            """
            GetMainVersionInternal() { return "v22_clang-16.0.6-centos7"; }
            MinVersion = "v22_clang-16.0.6-centos7";
            MaxVersion = "v22_clang-16.0.6-centos7";
            """);

        var result = await new UnrealToolchainRequirementParser().ParseAsync(
            ToolchainTestData.CreateEngine(_testDirectory));

        Assert.Equal("10.0.18362.0", result.Windows!.MainWindowsSdkVersion);
        Assert.Equal("14.29.30133", result.Windows.MinimumMsvcVersion);
        Assert.Equal("android-33", result.Android!.Platform);
        Assert.Equal("v22_clang-16.0.6-centos7", result.Linux!.MainVersion);
    }

    [Fact]
    public async Task ParserReadsLegacyLinuxMainVersionMethod()
    {
        var source = CreateSource("Linux", "LinuxPlatformSDK.Versions.cs");
        await File.WriteAllTextAsync(source, "public override string GetMainVersion() { return \"v21_clang-15.0.1-centos7\"; }");
        var result = await new UnrealToolchainRequirementParser().ParseAsync(ToolchainTestData.CreateEngine(_testDirectory));
        Assert.Equal("v21_clang-15.0.1-centos7", result.Linux!.MainVersion);
    }

    private string CreateConfig(string platform, string fileName)
    {
        var directory = Path.Combine(_testDirectory, "Engine", "Config", platform);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    private string CreateSource(string platform, string fileName)
    {
        var directory = Path.Combine(
            _testDirectory,
            "Engine",
            "Source",
            "Programs",
            "UnrealBuildTool",
            "Platform",
            platform);
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
