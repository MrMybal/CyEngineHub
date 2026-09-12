using System.Collections.Concurrent;
using System.Text.Json;
using CyEngineHub.Core.Tasks;

namespace CyEngineHub.Core.Tests;

public sealed class PersistentDownloadTests
{
    private static readonly int[] ExpectedStartOrder = [0, 1];
    public sealed record Request(string AssetId, string Variant, string State, int Order);

    [Fact]
    public void JournalRoundTripPreservesRequestsOrderAndTerminalStates()
    {
        var root = Path.Combine(Path.GetTempPath(), "CyHubQueueTest-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "queue.json");
        try
        {
            var journal = new DurableDownloadJournal<Request>(path);
            for (var i = 0; i < 169; i++) journal.Upsert("asset" + i, new("asset" + i, "4K-FBX", "Pending", i));
            journal.Upsert("asset0", new("asset0", "4K-FBX", "Canceled", 0));
            journal.Upsert("asset1", new("asset1", "4K-FBX", "Completed", 1));
            journal.Upsert("asset2", new("asset2", "4K-FBX", "Failed", 2));
            var restored = new DurableDownloadJournal<Request>(path).Read();
            Assert.Equal(169, restored.Count);
            Assert.Equal(166, restored.Values.Count(item => item.State == "Pending"));
            Assert.Equal("Canceled", restored["asset0"].State);
            Assert.Equal(168, restored.Values.OrderBy(item => item.Order).Last().Order);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void CorruptJournalIsNotSilentlyOverwritten()
    {
        var root = Path.Combine(Path.GetTempPath(), "CyHubQueueTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "queue.json");
        try
        {
            File.WriteAllText(path, "broken{");
            Assert.Throws<JsonException>(() => new DurableDownloadJournal<Request>(path).Upsert("x", new("x", "v", "Pending", 0)));
            Assert.Equal("broken{", File.ReadAllText(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ConcurrentJournalUpdatesDoNotLoseEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "CyHubQueueTest-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "queue.json");
        try
        {
            var journal = new DurableDownloadJournal<Request>(path);
            Parallel.For(0, 45, i => journal.Upsert(i.ToString(System.Globalization.CultureInfo.InvariantCulture), new("asset" + i, "v", "Pending", i)));
            Assert.Equal(45, new DurableDownloadJournal<Request>(path).Read().Count);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ParallelDownloadsRespectLimitAndDoNotBlockGeneralTasks()
    {
        await using var scheduler = new HubTaskScheduler();
        scheduler.SetDownloadConcurrency(3);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var threeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = 0;
        var maximum = 0;
        var gate = new object();
        var jobs = Enumerable.Range(0, 12).Select(i => scheduler.Enqueue(new("Download", "Pack " + i, async (_, token) =>
        {
            lock (gate) { running++; maximum = Math.Max(maximum, running); if (running == 3) threeStarted.TrySetResult(); }
            try { await release.Task.WaitAsync(token); }
            finally { lock (gate) running--; }
        }) { IsDownload = true })).ToArray();
        try
        {
            await threeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var general = scheduler.Enqueue(new("Test", "Search/refresh", (_, _) => Task.CompletedTask));
            Assert.Equal(HubTaskStatus.Completed, (await general.Completion.WaitAsync(TimeSpan.FromSeconds(5))).Status);
            Assert.Equal(3, scheduler.GetSnapshot().Count(item => item.Category == "Download" && item.Status == HubTaskStatus.Running));
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(jobs.Select(job => job.Completion)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.InRange(maximum, 1, 3);
    }

    [Fact]
    public async Task DownloadLimitCanIncreaseWithoutRestart()
    {
        await using var scheduler = new HubTaskScheduler();
        scheduler.SetDownloadConcurrency(1);
        var started = new ConcurrentQueue<int>();
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var jobs = Enumerable.Range(0, 2).Select(i => scheduler.Enqueue(new("Download", "Pack " + i, async (_, token) =>
        {
            started.Enqueue(i);
            if (i == 0) firstStarted.TrySetResult(); else secondStarted.TrySetResult();
            await release.Task.WaitAsync(token);
        }) { IsDownload = true })).ToArray();
        try
        {
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(secondStarted.Task.IsCompleted);
            scheduler.SetDownloadConcurrency(2);
            await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ExpectedStartOrder, started.ToArray());
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(jobs.Select(job => job.Completion));
    }

    [Fact]
    public async Task ShutdownCompletesParallelAndQueuedDownloads()
    {
        var scheduler = new HubTaskScheduler();
        scheduler.SetDownloadConcurrency(2);
        var jobs = Enumerable.Range(0, 30).Select(i => scheduler.Enqueue(new("Download", "Pack " + i,
            (_, token) => Task.Delay(Timeout.Infinite, token)) { IsDownload = true })).ToArray();
        await scheduler.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        var results = await Task.WhenAll(jobs.Select(job => job.Completion)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(results, result => Assert.Equal(HubTaskStatus.Canceled, result.Status));
    }
}
