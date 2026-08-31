using CyEngineHub.Core.Processes;
using CyEngineHub.Infrastructure.Processes;

namespace CyEngineHub.Core.Tests;

public sealed class SystemProcessRunnerTests
{
    [Fact]
    public async Task RunnerCapturesOutputWithABoundedBuffer()
    {
        var runner = new SystemProcessRunner();
        var result = await runner.RunAsync(new ProcessRequest
        {
            FileName = "dotnet",
            Arguments = ["--info"],
            MaxCapturedLines = 5,
        });

        Assert.True(result.Succeeded);
        Assert.InRange(result.CapturedOutput.Count, 1, 5);
        Assert.True(result.ProcessId > 0);
    }
}

