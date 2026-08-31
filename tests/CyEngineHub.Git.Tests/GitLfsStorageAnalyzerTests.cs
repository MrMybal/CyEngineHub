using CyEngineHub.Git.Analysis;

namespace CyEngineHub.Git.Tests;

public sealed class GitLfsStorageAnalyzerTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"LFS Store {Guid.NewGuid():N}");

    [Fact]
    public async Task AnalysisStopsAtConfiguredObjectLimit()
    {
        var objects = Path.Combine(_testDirectory, "lfs", "objects", "aa", "bb");
        Directory.CreateDirectory(objects);
        for (var index = 0; index < 3; index++)
        {
            var objectName = index.ToString("x64", System.Globalization.CultureInfo.InvariantCulture);
            await File.WriteAllBytesAsync(
                Path.Combine(objects, objectName),
                new byte[index + 10]);
        }

        var snapshot = new GitLfsStorageAnalyzer(
            maximumObjectCount: 2,
            maximumDuration: TimeSpan.FromMinutes(1)).Analyze(
                _testDirectory,
                isInstalled: true,
                ["*.uasset"]);

        Assert.Equal(2, snapshot.ObjectCount);
        Assert.True(snapshot.StorageBytes >= 20);
        Assert.True(snapshot.IsTruncated);
        Assert.Equal(["*.uasset"], snapshot.TrackedPatterns);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
