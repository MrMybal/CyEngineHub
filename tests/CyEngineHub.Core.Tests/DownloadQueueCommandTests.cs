using CyEngineHub.Desktop.ViewModels;

namespace CyEngineHub.Core.Tests;

public sealed class DownloadQueueCommandTests
{
    [Fact]
    public async Task QueueCommandAcceptsSecondBatchWhileFirstIsPending()
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var finished = new TaskCompletionSource();
        var calls = 0;
        var completed = 0;
        var command = new AsyncRelayCommand(
            () => ++calls == 1 ? first.Task : second.Task,
            () => true,
            allowConcurrentExecutions: true);
        command.CanExecuteChanged += (_, _) =>
        {
            if (first.Task.IsCompleted && ++completed == 2) finished.TrySetResult();
        };

        command.Execute(null);
        Assert.True(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal(2, calls);
        first.SetResult();
        second.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task DefaultCommandsStillRejectRepeatedExecutionUntilCompleted()
    {
        var operation = new TaskCompletionSource();
        var finished = new TaskCompletionSource();
        var calls = 0;
        var command = new AsyncRelayCommand(() => { calls++; return operation.Task; });
        command.CanExecuteChanged += (_, _) =>
        {
            if (command.CanExecute(null)) finished.TrySetResult();
        };
        command.Execute(null);
        Assert.False(command.CanExecute(null));
        command.Execute(null);
        Assert.Equal(1, calls);
        operation.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task QueueCommandReportsFailuresAndRemainsAvailable()
    {
        var reported = new TaskCompletionSource<Exception>();
        var hasProduct = false;
        var error = new InvalidOperationException("Download failed");
        var command = new AsyncRelayCommand(
            () => Task.FromException(error), () => hasProduct,
            exception => reported.TrySetResult(exception), allowConcurrentExecutions: true);
        Assert.False(command.CanExecute(null));
        hasProduct = true;
        command.Execute(null);
        Assert.Same(error, await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(command.CanExecute(null));
    }
}
