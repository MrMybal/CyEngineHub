using CyEngineHub.Core.Tasks;

namespace CyEngineHub.Core.Tests;

public sealed class HubTaskSchedulerTests
{
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

