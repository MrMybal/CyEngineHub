using CyEngineHub.Core.Settings;
using CyEngineHub.Epic.Models;
using CyEngineHub.Epic.Persistence;

namespace CyEngineHub.Epic.Tests;

public sealed class EpicSnapshotCacheTests
{
    [Fact]
    public async Task CacheRoundTripsOnlyLocalInventorySnapshot()
    {
        var settings = new MemorySettingsStore();
        var cache = new EpicSnapshotCache(settings);
        var snapshot = new EpicLocalSnapshot(
            true,
            false,
            "D:\\Epic\\EpicGamesLauncher.exe",
            "C:\\ProgramData\\Epic\\Manifests",
            "C:\\ProgramData\\Epic\\VaultCache",
            3,
            "C:\\Local\\FabPlugins",
            2,
            [new EpicLocalArtifact(
                "ID",
                "UE_5.5",
                "Unreal Engine 5.5",
                "5.5.4",
                "D:\\UE_5.5",
                "CATALOG",
                "ue",
                EpicArtifactKind.UnrealEngine,
                true,
                "manifest.item")],
            [],
            DateTimeOffset.UtcNow);

        await cache.SaveAsync(snapshot);
        var restored = await new EpicSnapshotCache(settings).LoadAsync();

        Assert.NotNull(restored);
        Assert.Equal(3, restored.VaultItemCount);
        Assert.Equal(2, restored.FabPluginPackageCount);
        Assert.Equal(EpicArtifactKind.UnrealEngine, Assert.Single(restored.Artifacts).Kind);
        Assert.DoesNotContain("token", settings.Values[EpicSnapshotCache.SettingsKey], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", settings.Values[EpicSnapshotCache.SettingsKey], StringComparison.OrdinalIgnoreCase);
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Values.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Values[key] = value;
            return Task.CompletedTask;
        }
    }
}
