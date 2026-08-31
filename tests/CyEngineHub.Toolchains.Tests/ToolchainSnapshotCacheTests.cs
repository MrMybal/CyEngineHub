using CyEngineHub.Core.Settings;
using CyEngineHub.Toolchains.Models;
using CyEngineHub.Toolchains.Persistence;

namespace CyEngineHub.Toolchains.Tests;

public sealed class ToolchainSnapshotCacheTests
{
    [Fact]
    public async Task CacheRoundTripsInventoryAndComputedEngineHealth()
    {
        var settings = new MemorySettingsStore();
        var cache = new ToolchainSnapshotCache(settings);
        var snapshot = new ToolchainSnapshot(
            [ToolchainTestData.Installation(ToolchainKind.Msvc, "14.38.33130")],
            [new EngineToolchainSnapshot(
                "ENGINE",
                "UE Test",
                "5.8.0",
                [new ToolchainCompatibilityCheck(
                    ToolchainKind.Msvc,
                    "MSVC",
                    ToolchainHealth.Ready,
                    "14.38",
                    "14.38.33130",
                    "Version préférée")],
                [])],
            [new EnvironmentVariableSnapshot("ANDROID_HOME", "D:\\Android", null, null)],
            [new AndroidDeviceSnapshot(
                "QUEST-1",
                "device",
                "Quest 3",
                "eureka",
                "eureka",
                "4",
                true)],
            [],
            DateTimeOffset.UtcNow);

        await cache.SaveAsync(snapshot);
        var restored = await new ToolchainSnapshotCache(settings).LoadAsync();

        Assert.NotNull(restored);
        var installation = Assert.Single(restored.Installations);
        Assert.Equal(ToolchainKind.Msvc, installation.Kind);
        Assert.Equal("14.38.33130", installation.Version);
        Assert.Equal("C:\\Tools\\Msvc\\14.38.33130", installation.Path);
        Assert.Equal(ToolchainHealth.Ready, Assert.Single(restored.Engines).OverallHealth);
        Assert.True(Assert.Single(restored.AndroidDevices).IsQuest);
        Assert.Equal("D:\\Android", Assert.Single(restored.EnvironmentVariables).ProcessValue);
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }
    }
}
