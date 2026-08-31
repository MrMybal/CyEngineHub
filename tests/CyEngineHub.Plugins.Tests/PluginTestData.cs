using CyEngineHub.Plugins.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Plugins.Tests;

internal static class PluginTestData
{
    public static UnrealEngineInstallation CreateEngine(
        string rootPath,
        int major = 5,
        int minor = 8) => new(
        "ENGINE",
        $"{major}.{minor}",
        $"Unreal Engine {major}.{minor}",
        rootPath,
        Path.Combine(rootPath, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
        new EngineVersion(major, minor, 0),
        EngineInstallationType.SourceFork,
        "test",
        null,
        null,
        "test",
        EngineHealth.Healthy,
        [],
        DateTimeOffset.UtcNow);

    public static UnrealPluginDescriptor CreatePlugin(
        string rootPath,
        bool hasSource,
        string? engineVersion = "5.8.0",
        IReadOnlyList<string>? supportedPlatforms = null,
        IReadOnlyList<UnrealPluginModule>? modules = null) => new(
        "PLUGIN",
        "CyPlugin",
        "Cy Plugin",
        string.Empty,
        "Test",
        Path.Combine(rootPath, "CyPlugin.uplugin"),
        rootPath,
        PluginSourceKind.LocalLibrary,
        "Tests",
        null,
        3,
        1,
        "1.0",
        engineVersion,
        false,
        false,
        hasSource,
        modules ?? [],
        supportedPlatforms ?? [],
        [],
        PluginHealth.Healthy,
        [],
        DateTimeOffset.UtcNow);
}
