using CyEngineHub.Core.Tasks;

namespace CyEngineHub.Core.Tests;

public sealed class HubInstallationSchedulerTests
{
    private sealed class Disks : IHubTaskDiskResolver
    {
        public IReadOnlyList<HubTaskDisk> Resolve(string path) => [new(path, path, path == "network" ? 1 : 2)];
    }

    private sealed class Job
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HubTaskHandle Queue(HubTaskScheduler scheduler, string project, string disk,
            string[]? keys = null, string[]? shared = null, bool download = false, bool fail = false) =>
            scheduler.Enqueue(new("Test", project, async (_, token) =>
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(token);
                if (fail) throw new IOException("Test failure");
            })
            {
                IsInstallation = !download, IsDownload = download,
                Resources = new() { ExclusivePaths = [Path.Combine(Path.GetTempPath(), "CyHubTaskQA", project)],
                    DiskPaths = [disk], ExclusiveKeys = keys ?? [], SharedKeys = shared ?? [] },
            });
    }

    private static async Task<HubTaskSnapshot> Waiting(HubTaskScheduler scheduler, HubTaskHandle handle, string reason)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var snapshot = scheduler.GetSnapshot().Single(task => task.Id == handle.Id);
            if (snapshot.Status == HubTaskStatus.Queued && snapshot.Message?.Contains(reason, StringComparison.Ordinal) == true) return snapshot;
            await Task.Delay(10, deadline.Token);
        }
    }

    [Fact]
    public async Task DifferentProjectsShareTwoSlotsPerDiskAndBlockedHeadDoesNotBlockOtherDisks()
    {
        await using var scheduler = new HubTaskScheduler(disks: new Disks());
        var first = new Job(); var second = new Job(); var third = new Job(); var independent = new Job();
        first.Queue(scheduler, "A", "disk1"); second.Queue(scheduler, "B", "disk1");
        await Task.WhenAll(first.Started.Task, second.Started.Task).WaitAsync(TimeSpan.FromSeconds(5));
        var blocked = third.Queue(scheduler, "C", "disk1");
        await Waiting(scheduler, blocked, "disk1");
        independent.Queue(scheduler, "D", "disk2");
        await independent.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(third.Started.Task.IsCompleted);
        first.Release.TrySetResult();
        await third.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        second.Release.TrySetResult(); third.Release.TrySetResult(); independent.Release.TrySetResult();
    }

    [Fact]
    public async Task SameProjectAndChildDirectoriesNeverWriteConcurrently()
    {
        await using var scheduler = new HubTaskScheduler(disks: new Disks());
        var first = new Job(); first.Queue(scheduler, "Project", "disk1");
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = new Job(); var blocked = second.Queue(scheduler, "Project/Content", "disk2");
        await Waiting(scheduler, blocked, "projet/dossier");
        Assert.False(second.Started.Task.IsCompleted);
        first.Release.TrySetResult(); await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); second.Release.TrySetResult();
    }

    [Fact]
    public async Task LocalReadersMaySharePackButDownloaderMustWaitAcrossLanes()
    {
        await using var scheduler = new HubTaskScheduler(disks: new Disks());
        var first = new Job(); var second = new Job();
        first.Queue(scheduler, "A", "disk1", shared: ["pack"]); second.Queue(scheduler, "B", "disk1", shared: ["pack"]);
        await Task.WhenAll(first.Started.Task, second.Started.Task).WaitAsync(TimeSpan.FromSeconds(5));
        var writer = new Job(); var blocked = writer.Queue(scheduler, "Download", "disk2", keys: ["PACK"], download: true);
        await Waiting(scheduler, blocked, "pack/ressource");
        first.Release.TrySetResult(); second.Release.TrySetResult();
        await writer.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); writer.Release.TrySetResult();
    }

    [Fact]
    public async Task FailureAndCancellationReleaseAllReservations()
    {
        await using var scheduler = new HubTaskScheduler(disks: new Disks());
        var failing = new Job(); var failed = failing.Queue(scheduler, "Project", "disk1", keys: ["pack"], fail: true);
        await failing.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var canceled = new Job(); var pending = canceled.Queue(scheduler, "Project", "disk1");
        await Waiting(scheduler, pending, "projet/dossier"); pending.Cancel();
        Assert.Equal(HubTaskStatus.Canceled, (await pending.Completion).Status);
        failing.Release.TrySetResult(); Assert.Equal(HubTaskStatus.Failed, (await failed.Completion).Status);
        var next = new Job(); var handle = next.Queue(scheduler, "Project", "disk1", keys: ["pack"]);
        await next.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); next.Release.TrySetResult();
        Assert.Equal(HubTaskStatus.Completed, (await handle.Completion).Status);
    }

    [Fact]
    public async Task FourInstallationLimitAndSingleNetworkBudgetAreRespected()
    {
        await using var scheduler = new HubTaskScheduler(disks: new Disks());
        var jobs = Enumerable.Range(0, 5).Select(_ => new Job()).ToArray();
        var handles = jobs.Select((job, index) => job.Queue(scheduler, "Project" + index, "disk" + index)).ToArray();
        await Task.WhenAll(jobs.Take(4).Select(job => job.Started.Task)).WaitAsync(TimeSpan.FromSeconds(5));
        await Waiting(scheduler, handles[4], "4 installations");
        Assert.False(jobs[4].Started.Task.IsCompleted);
        foreach (var job in jobs) job.Release.TrySetResult();
        await Task.WhenAll(handles.Select(handle => handle.Completion)).WaitAsync(TimeSpan.FromSeconds(5));
        var first = new Job(); first.Queue(scheduler, "NetworkA", "network");
        await first.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = new Job(); var secondHandle = second.Queue(scheduler, "NetworkB", "network");
        await Waiting(scheduler, secondHandle, "network"); first.Release.TrySetResult();
        await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); second.Release.TrySetResult();
    }

    [Fact]
    public async Task GeneralActionsKeepTheirSerialLaneWithoutBlockingIndependentInstallations()
    {
        await using var scheduler = new HubTaskScheduler(disks: new Disks());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var general = scheduler.Enqueue(new("Test", "General", (_, token) => release.Task.WaitAsync(token)));
        var install = new Job(); install.Queue(scheduler, "Independent", "disk1");
        await install.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(general.Completion.IsCompleted);
        install.Release.TrySetResult(); release.TrySetResult();
    }
}
