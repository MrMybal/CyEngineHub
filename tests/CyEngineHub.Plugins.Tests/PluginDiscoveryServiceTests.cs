using CyEngineHub.Plugins.Discovery;
using CyEngineHub.Plugins.Models;
using CyEngineHub.Plugins.Parsing;

namespace CyEngineHub.Plugins.Tests;

public sealed class PluginDiscoveryServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Plugin catalog {Guid.NewGuid():N}");

    [Fact]
    public async Task DiscoverySkipsBuildOutputDirectories()
    {
        var pluginRoot = Path.Combine(_testDirectory, "CyPlugin");
        var ignoredRoot = Path.Combine(_testDirectory, "Intermediate", "Ignored");
        Directory.CreateDirectory(pluginRoot);
        Directory.CreateDirectory(ignoredRoot);
        await File.WriteAllTextAsync(
            Path.Combine(pluginRoot, "CyPlugin.uplugin"),
            "{ \"FileVersion\": 3, \"CanContainContent\": true }");
        await File.WriteAllTextAsync(
            Path.Combine(ignoredRoot, "Ignored.uplugin"),
            "{ \"FileVersion\": 3, \"CanContainContent\": true }");

        var result = await new PluginDiscoveryService(new UpluginDescriptorParser()).RefreshAsync(
            new PluginDiscoveryRequest([], [], [_testDirectory]));

        var plugin = Assert.Single(result);
        Assert.Equal("CyPlugin", plugin.Name);
    }

    [Fact]
    public async Task BrokenDescriptorKeepsStableIdentifierAcrossRefreshes()
    {
        Directory.CreateDirectory(_testDirectory);
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "Broken.uplugin"), "not json");
        var service = new PluginDiscoveryService(new UpluginDescriptorParser());
        var request = new PluginDiscoveryRequest([], [], [_testDirectory]);

        var first = Assert.Single(await service.RefreshAsync(request));
        var second = Assert.Single(await service.RefreshAsync(request));

        Assert.Equal(PluginHealth.Broken, first.Health);
        Assert.Equal(first.Id, second.Id);
        Assert.StartsWith("BROKEN-", first.Id, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
