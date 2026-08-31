using CyEngineHub.Plugins.Compatibility;
using CyEngineHub.Plugins.Models;

namespace CyEngineHub.Plugins.Tests;

public sealed class PluginCompatibilityServiceTests
{
    private readonly PluginCompatibilityService _service = new();

    [Fact]
    public void SourcePluginFromAnotherMinorVersionRequiresRebuildButIsAllowed()
    {
        var plugin = PluginTestData.CreatePlugin("C:\\Plugin", hasSource: true, engineVersion: "5.7.2");
        var engine = PluginTestData.CreateEngine("C:\\Engine", minor: 8);

        var result = _service.Evaluate(plugin, engine);

        Assert.True(result.IsCompatible);
        Assert.True(result.RequiresRebuild);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void BinaryPluginFromAnotherMinorVersionIsRejected()
    {
        var plugin = PluginTestData.CreatePlugin("C:\\Plugin", hasSource: false, engineVersion: "5.7.2");
        var engine = PluginTestData.CreateEngine("C:\\Engine", minor: 8);

        var result = _service.Evaluate(plugin, engine);

        Assert.False(result.IsCompatible);
        Assert.False(result.RequiresRebuild);
    }

    [Fact]
    public void UnsupportedTargetPlatformIsRejected()
    {
        var plugin = PluginTestData.CreatePlugin(
            "C:\\Plugin",
            hasSource: true,
            supportedPlatforms: ["Linux", "Android"],
            modules:
            [
                new UnrealPluginModule("CyPlugin", "Runtime", "Default", ["Linux"], []),
            ]);

        var result = _service.Evaluate(
            plugin,
            PluginTestData.CreateEngine("C:\\Engine"),
            "Win64");

        Assert.False(result.IsCompatible);
        Assert.Contains(result.Warnings, warning => warning.Contains("Win64", StringComparison.Ordinal));
    }
}
