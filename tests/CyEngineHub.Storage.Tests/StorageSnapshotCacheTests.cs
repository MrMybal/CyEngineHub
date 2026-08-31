using CyEngineHub.Core.Settings;
using CyEngineHub.Storage.Models;
using CyEngineHub.Storage.Persistence;

namespace CyEngineHub.Storage.Tests;

public sealed class StorageSnapshotCacheTests
{
    [Fact]
    public async Task CacheRoundTripsStorageInventory()
    {
        var settings = new MemorySettingsStore();
        var cache = new StorageSnapshotCache(settings);
        var snapshot = new StorageSnapshot(
            [new StorageEntry(
                "ENTRY",
                StorageCategory.ProjectIntermediate,
                StorageEntryRole.Generated,
                "Intermediate",
                "Beyond",
                "D:\\Beyond",
                "D:\\Beyond\\Intermediate",
                1024,
                4,
                2,
                DateTimeOffset.UtcNow,
                StorageHealth.Ready,
                true,
                "Analyse complète.")],
            [new DriveStorageSnapshot("D:\\", "Data", 10_000, 4_000, 3_000)],
            [],
            DateTimeOffset.UtcNow);

        await cache.SaveAsync(snapshot);
        var restored = await new StorageSnapshotCache(settings).LoadAsync();

        Assert.NotNull(restored);
        var entry = Assert.Single(restored.Entries);
        Assert.Equal(StorageCategory.ProjectIntermediate, entry.Category);
        Assert.Equal(1024, entry.SizeBytes);
        Assert.True(entry.IsSafeToClean);
        Assert.Equal(3_000, Assert.Single(restored.Drives).AvailableFreeBytes);
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
