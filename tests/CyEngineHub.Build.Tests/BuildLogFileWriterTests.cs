using CyEngineHub.Build.Execution;
using CyEngineHub.Build.Models;

namespace CyEngineHub.Build.Tests;

public sealed class BuildLogFileWriterTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"OperationLogs-{Guid.NewGuid():N}");

    [Fact]
    public async Task DisposeFlushesClassifiedLogToDisk()
    {
        string filePath;
        await using (var writer = BuildLogFileWriter.Create(_testDirectory, "Build Beyond/Editor"))
        {
            filePath = writer.FilePath;
            writer.Write(new BuildLogLine(
                new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.FromHours(2)),
                BuildLogSeverity.Warning,
                "warning C4996: deprecated"));
        }

        var contents = await File.ReadAllTextAsync(filePath);

        Assert.Contains("Warning", contents, StringComparison.Ordinal);
        Assert.Contains("warning C4996: deprecated", contents, StringComparison.Ordinal);
        Assert.DoesNotContain('\uFEFF', contents);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
