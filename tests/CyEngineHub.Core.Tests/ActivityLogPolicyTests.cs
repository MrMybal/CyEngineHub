using CyEngineHub.Core.Diagnostics;
using CyEngineHub.Core.Tasks;

namespace CyEngineHub.Core.Tests;

public sealed class ActivityLogPolicyTests
{
    [Fact]
    public void ProgressIsTechnicalButLifecycleAndFailuresRemainVisible()
    {
        var policy = new ActivityLogPolicy();
        var task = new HubTaskSnapshot(Guid.NewGuid(), "Epic", "Download", HubTaskStatus.Queued, null, null, DateTimeOffset.UtcNow, null, null, null);
        Assert.Equal("TASK", policy.LevelFor(task));
        task = task with { Status = HubTaskStatus.Running };
        Assert.Equal("TASK", policy.LevelFor(task));
        for (var i = 0; i < 100; i++) Assert.Equal("TRACE", policy.LevelFor(task with { Progress = i / 100d, Message = "Chunk " + i }));
        Assert.Equal("TASK", policy.LevelFor(task with { Status = HubTaskStatus.Completed }));
        Assert.Equal("ERROR", policy.LevelFor(task with { Status = HubTaskStatus.Failed }));
        Assert.Equal("WARN", policy.LevelFor(task with { Status = HubTaskStatus.Canceled }));
    }

    [Theory]
    [InlineData("date [TRACE] [Epic] chunk 1", false)]
    [InlineData("date [OUT] [Tool] build output", false)]
    [InlineData("date [TASK] [Epic] Download · Running · chunk 1", false)]
    [InlineData("date [TASK] [Epic] Download · Running · ", true)]
    [InlineData("date [TASK] [Epic] Download · Completed · done", true)]
    [InlineData("date [ERROR] [Epic] interrupted", true)]
    [InlineData("date [WARN] [Epic] canceled", true)]
    [InlineData("date [INFO] [Library] cache restored", true)]
    [InlineData("   at Some.Stack.Trace()", false)]
    public void DefaultViewFiltersTechnicalAndLegacyProgress(string line, bool expected) =>
        Assert.Equal(expected, ActivityLogPolicy.IsImportant(line));
}
