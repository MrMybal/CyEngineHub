using CyEngineHub.Build.Execution;
using CyEngineHub.Build.Models;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Build.Tests;

public sealed class BuildLogClassifierTests
{
    [Theory]
    [InlineData("warning C4996: deprecated", BuildLogSeverity.Warning)]
    [InlineData("error C2065: identifier not found", BuildLogSeverity.Error)]
    [InlineData("BUILD FAILED", BuildLogSeverity.Fatal)]
    [InlineData("Building 42 actions", BuildLogSeverity.Information)]
    public void ClassifierRecognizesCommonUnrealBuildLines(
        string text,
        BuildLogSeverity expected)
    {
        var line = new ProcessOutputLine(
            DateTimeOffset.UtcNow,
            ProcessOutputChannel.StandardOutput,
            text);

        var classified = BuildLogClassifier.Classify(line);

        Assert.Equal(expected, classified.Severity);
    }

    [Fact]
    public void DiagnoseRecognizesAndroidToolchainFailure()
    {
        var diagnosis = BuildLogClassifier.Diagnose("ERROR: Android SDK or NDK is missing");

        Assert.Contains("Android SDK", diagnosis, StringComparison.Ordinal);
    }
}
