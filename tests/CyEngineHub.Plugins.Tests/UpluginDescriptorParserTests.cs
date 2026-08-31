using CyEngineHub.Plugins.Models;
using CyEngineHub.Plugins.Parsing;

namespace CyEngineHub.Plugins.Tests;

public sealed class UpluginDescriptorParserTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Plugin démo {Guid.NewGuid():N}");

    [Fact]
    public async Task ParserReadsMetadataModulesPlatformsAndDependencies()
    {
        Directory.CreateDirectory(Path.Combine(_testDirectory, "Source", "CyPlugin"));
        var descriptorPath = Path.Combine(_testDirectory, "CyPlugin.uplugin");
        await File.WriteAllTextAsync(
            descriptorPath,
            """
            {
              // Unreal descriptors commonly contain trailing commas.
              "FileVersion": 3,
              "Version": 12,
              "VersionName": "1.2.0",
              "FriendlyName": "Cy Plugin",
              "EngineVersion": "5.7.1",
              "CanContainContent": true,
              "SupportedTargetPlatforms": ["Win64", "Linux", "Win64"],
              "Modules": [
                {
                  "Name": "CyPlugin",
                  "Type": "Runtime",
                  "LoadingPhase": "Default",
                  "WhitelistPlatforms": ["Win64", "Linux"],
                  "PlatformDenyList": ["Android"],
                },
              ],
              "Plugins": [
                { "Name": "GameplayAbilities", "Enabled": true },
                { "Name": "OptionalTools", "Optional": true },
              ],
            }
            """);

        var result = await new UpluginDescriptorParser().ParseAsync(new PluginCandidate(
            descriptorPath,
            PluginSourceKind.LocalLibrary,
            "Bibliothèque"));

        Assert.Equal("CyPlugin", result.Name);
        Assert.Equal("Cy Plugin", result.FriendlyName);
        Assert.Equal(12, result.Version);
        Assert.Equal("1.2.0", result.VersionName);
        Assert.True(result.CanContainContent);
        Assert.True(result.HasSource);
        Assert.Null(result.EnabledByDefault);
        Assert.Equal(["Win64", "Linux"], result.SupportedTargetPlatforms);
        Assert.Equal(["Win64", "Linux"], result.Modules.Single().PlatformAllowList);
        Assert.Equal(["Android"], result.Modules.Single().PlatformDenyList);
        Assert.Equal(2, result.Dependencies.Count);
        Assert.Equal(PluginHealth.Healthy, result.Health);
    }

    [Fact]
    public async Task ParserWrapsMalformedJsonInTypedException()
    {
        Directory.CreateDirectory(_testDirectory);
        var descriptorPath = Path.Combine(_testDirectory, "Broken.uplugin");
        await File.WriteAllTextAsync(descriptorPath, "{ this is not JSON }");

        var exception = await Assert.ThrowsAsync<PluginOperationException>(() =>
            new UpluginDescriptorParser().ParseAsync(new PluginCandidate(
                descriptorPath,
                PluginSourceKind.LocalLibrary,
                "Tests")));

        Assert.Equal("Parse", exception.Operation);
        Assert.Equal(descriptorPath, exception.PluginPath);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", null)]
    public async Task ParserPreservesEngineDefaultActivation(string jsonValue, bool? expected)
    {
        Directory.CreateDirectory(_testDirectory);
        var path = Path.Combine(_testDirectory, "DefaultPlugin.uplugin");
        await File.WriteAllTextAsync(path,
            $$"""{"FileVersion":3,"CanContainContent":true,"EnabledByDefault":{{jsonValue}}}""");
        var plugin = await new UpluginDescriptorParser().ParseAsync(
            new PluginCandidate(path, PluginSourceKind.Engine, "Engine"));
        Assert.Equal(expected, plugin.EnabledByDefault);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
