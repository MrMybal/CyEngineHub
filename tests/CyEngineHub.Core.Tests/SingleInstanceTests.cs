using CyEngineHub.Core.Processes;

namespace CyEngineHub.Core.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public async Task SecondInstanceActivatesFirstWithoutBecomingPrimary()
    {
        var name = "CyHubSingleTest-" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceHost(name);
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.SetActivationHandler(() => activated.TrySetResult());
        using var second = new SingleInstanceHost(name);
        Assert.True(first.IsPrimary);
        Assert.False(second.IsPrimary);
        Assert.True(await second.ActivatePrimaryAsync());
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ActivationDuringStartupIsRetainedUntilWindowIsReady()
    {
        var name = "CyHubSingleTest-" + Guid.NewGuid().ToString("N");
        using var first = new SingleInstanceHost(name);
        using var second = new SingleInstanceHost(name);
        Assert.True(await second.ActivatePrimaryAsync());
        var calls = 0;
        first.SetActivationHandler(() => calls++);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void AppCanRestartAfterPrimaryExits()
    {
        var name = "CyHubSingleTest-" + Guid.NewGuid().ToString("N");
        using (var first = new SingleInstanceHost(name)) Assert.True(first.IsPrimary);
        using var restarted = new SingleInstanceHost(name);
        Assert.True(restarted.IsPrimary);
    }

    [Fact]
    public async Task SimultaneousStartsElectExactlyOnePrimary()
    {
        var name = "CyHubSingleTest-" + Guid.NewGuid().ToString("N");
        var instances = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => new SingleInstanceHost(name))));
        try { Assert.Single(instances, instance => instance.IsPrimary); }
        finally { foreach (var instance in instances) instance.Dispose(); }
    }
}
