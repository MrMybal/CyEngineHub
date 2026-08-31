using CyEngineHub.Core.Settings;
using CyEngineHub.Git.Models;
using CyEngineHub.Git.Persistence;

namespace CyEngineHub.Git.Tests;

public sealed class GitSnapshotCacheTests
{
    [Fact]
    public async Task CacheRoundTripsSanitizedSnapshot()
    {
        var settings = new MemorySettingsStore();
        var cache = new GitSnapshotCache(settings);
        var snapshot = new GitRepositorySnapshot(
            "D:\\Repo",
            [new GitRepositoryOwner("Beyond", "D:\\Repo", GitRepositoryOwnerKind.Project, "P1")],
            "main",
            "0123456789abcdef",
            "origin/main",
            2,
            3,
            new GitWorkingTreeSummary(1, 2, 3, 0),
            [new GitRemote("origin", "https://example.com/repo.git")],
            new GitLfsSnapshot(true, 4, 1024, ["*.uasset"], false),
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(1));

        await cache.SaveAsync([snapshot]);
        var loaded = await new GitSnapshotCache(settings).LoadAsync();

        var restored = Assert.Single(loaded);
        Assert.Equal(snapshot.RepositoryRoot, restored.RepositoryRoot);
        Assert.Equal(snapshot.WorkingTree, restored.WorkingTree);
        Assert.Equal(snapshot.Remotes, restored.Remotes);
        Assert.Equal(snapshot.Lfs.TrackedPatterns, restored.Lfs.TrackedPatterns);
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
