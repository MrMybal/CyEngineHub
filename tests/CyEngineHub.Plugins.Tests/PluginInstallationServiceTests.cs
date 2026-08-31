using CyEngineHub.Plugins.Compatibility;
using CyEngineHub.Plugins.Installation;
using CyEngineHub.Plugins.Models;
using CyEngineHub.Plugins.Parsing;

namespace CyEngineHub.Plugins.Tests;

public sealed class PluginInstallationServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Installation plugin é {Guid.NewGuid():N}");

    [Fact]
    public async Task InstallReplacesThroughStagingAndRetainsBackup()
    {
        var source = Path.Combine(_testDirectory, "Source plugin");
        var pluginsDirectory = Path.Combine(_testDirectory, "Projet cible", "Plugins");
        var destination = Path.Combine(pluginsDirectory, "CyPlugin");
        Directory.CreateDirectory(Path.Combine(source, "Source", "CyPlugin"));
        Directory.CreateDirectory(Path.Combine(source, "Content"));
        Directory.CreateDirectory(Path.Combine(source, "Binaries", "Win64"));
        Directory.CreateDirectory(destination);
        await File.WriteAllTextAsync(
            Path.Combine(source, "CyPlugin.uplugin"),
            "{ \"FileVersion\": 3, \"EngineVersion\": \"5.8.0\", \"CanContainContent\": true, \"Modules\": [{ \"Name\": \"CyPlugin\" }] }");
        await File.WriteAllTextAsync(Path.Combine(source, "Source", "CyPlugin", "CyPlugin.Build.cs"), "// source");
        await File.WriteAllTextAsync(Path.Combine(source, "Content", "Asset.uasset"), "asset");
        await File.WriteAllTextAsync(Path.Combine(source, "Binaries", "Win64", "stale.dll"), "binary");
        await File.WriteAllTextAsync(Path.Combine(destination, "old-version.txt"), "old");
        var parser = new UpluginDescriptorParser();
        var plugin = await parser.ParseAsync(new PluginCandidate(
            Path.Combine(source, "CyPlugin.uplugin"),
            PluginSourceKind.LocalLibrary,
            "Tests"));
        var engine = PluginTestData.CreateEngine(Path.Combine(_testDirectory, "Engine"));
        var target = new PluginInstallTarget(
            "PROJECT",
            "Projet cible",
            PluginInstallTargetKind.Project,
            pluginsDirectory,
            engine);
        var service = new PluginInstallationService(new PluginCompatibilityService(), parser);

        var plan = await service.PlanAsync(plugin, target);
        var result = await service.InstallAsync(plan);

        Assert.True(plan.WillReplaceExisting);
        Assert.Equal(3, plan.FileCount);
        Assert.True(File.Exists(Path.Combine(destination, "CyPlugin.uplugin")));
        Assert.True(File.Exists(Path.Combine(destination, "Content", "Asset.uasset")));
        Assert.False(File.Exists(Path.Combine(destination, "Binaries", "Win64", "stale.dll")));
        Assert.NotNull(result.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.BackupDirectory!, "old-version.txt")));
        Assert.Empty(Directory.GetDirectories(pluginsDirectory, ".cyenginehub-staging-*"));
    }

    [Fact]
    public async Task PlanRejectsSourceDestinationOverlap()
    {
        var source = Path.Combine(_testDirectory, "Plugins", "CyPlugin");
        Directory.CreateDirectory(source);
        var descriptorPath = Path.Combine(source, "CyPlugin.uplugin");
        await File.WriteAllTextAsync(
            descriptorPath,
            "{ \"FileVersion\": 3, \"CanContainContent\": true }");
        var parser = new UpluginDescriptorParser();
        var plugin = await parser.ParseAsync(new PluginCandidate(
            descriptorPath,
            PluginSourceKind.Project,
            "Tests"));
        var target = new PluginInstallTarget(
            "PROJECT",
            "Même projet",
            PluginInstallTargetKind.Project,
            Path.Combine(_testDirectory, "Plugins"),
            PluginTestData.CreateEngine(Path.Combine(_testDirectory, "Engine")));
        var service = new PluginInstallationService(new PluginCompatibilityService(), parser);

        var exception = await Assert.ThrowsAsync<PluginOperationException>(() =>
            service.PlanAsync(plugin, target));

        Assert.Contains("chevauchent", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
