using CyEngineHub.Core.Tasks;

namespace CyEngineHub.Core.Tests;

public sealed class HubTaskSchedulerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TaskLaneIsExplicitAndSurvivesProgressAndCompletion(bool isDownload)
    {
        await using var scheduler = new HubTaskScheduler();
        var changes = new System.Collections.Concurrent.ConcurrentQueue<HubTaskSnapshot>();
        scheduler.TaskChanged += (_, change) => changes.Enqueue(change.Task);
        var handle = scheduler.Enqueue(new HubTaskDefinition("Any category", "Misleading download install title", (context, _) =>
        { context.Report(.5, "Working"); return Task.CompletedTask; }) { IsDownload = isDownload });
        var result = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(isDownload, result.IsDownload);
        Assert.Equal(isDownload, scheduler.GetSnapshot().Single().IsDownload);
        Assert.All(changes, item => Assert.Equal(isDownload, item.IsDownload));
    }
    [Fact]
    public async Task CancelQueuedTaskCompletesImmediatelyWithoutExecutingIt()
    {
        await using var scheduler = new HubTaskScheduler();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = scheduler.Enqueue(new HubTaskDefinition("Test", "First", async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var executed = false;
        var queued = scheduler.Enqueue(new HubTaskDefinition("Test", "Second", (_, _) =>
        {
            executed = true;
            return Task.CompletedTask;
        }));
        queued.Cancel();
        var canceled = await queued.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(HubTaskStatus.Canceled, canceled.Status);
        Assert.Null(canceled.StartedAt);
        Assert.False(blocking.Completion.IsCompleted);
        Assert.False(executed);
        blocking.Cancel();
        await blocking.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        var barrier = scheduler.Enqueue(new HubTaskDefinition("Test", "Barrier", (_, _) => Task.CompletedTask));
        await barrier.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(executed);
    }

    [Fact]
    public async Task NavigationTargetsSurviveProgressAndCompletion()
    {
        await using var scheduler = new HubTaskScheduler();
        HubTaskLink[] links = [new("product", "asset-123", "Produit", "Fab"), new("project", "C:/Test/Test.uproject", "Projet")];
        var task = scheduler.Enqueue(new HubTaskDefinition("Test", "Install", links, (context, _) =>
        {
            context.Report(0.5, "Installing");
            return Task.CompletedTask;
        }));
        var completed = await task.Completion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(links, completed.Links);
        Assert.Equal(links, scheduler.GetSnapshot().Single().Links);
    }

    [Fact]
    public async Task ShutdownCompletesPendingHandles()
    {
        var scheduler = new HubTaskScheduler();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scheduler.Enqueue(new HubTaskDefinition("Test", "Blocking", async (_, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var queued = scheduler.Enqueue(new HubTaskDefinition("Test", "Queued", (_, _) => Task.CompletedTask));
        await scheduler.DisposeAsync();
        Assert.Equal(HubTaskStatus.Canceled, (await queued.Completion.WaitAsync(TimeSpan.FromSeconds(3))).Status);
    }

    [Fact]
    public async Task EnqueuedTaskCompletesAndReportsProgress()
    {
        await using var scheduler = new HubTaskScheduler();
        var handle = scheduler.Enqueue(new HubTaskDefinition(
            "Test",
            "Progress",
            (context, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.Report(0.5, "Half way");
                return Task.CompletedTask;
            }));

        var completed = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(HubTaskStatus.Completed, completed.Status);
        Assert.Equal(1, completed.Progress);
        Assert.Equal("Half way", completed.Message);
        Assert.NotNull(completed.StartedAt);
        Assert.NotNull(completed.FinishedAt);
    }

    [Fact]
    public async Task RunningTaskCanBeCanceled()
    {
        await using var scheduler = new HubTaskScheduler();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handle = scheduler.Enqueue(new HubTaskDefinition(
            "Test",
            "Cancellation",
            async (_, cancellationToken) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        handle.Cancel();
        var completed = await handle.Completion.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal(HubTaskStatus.Canceled, completed.Status);
    }
}
