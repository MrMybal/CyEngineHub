using CyEngineHub.Storage.Analysis;

namespace CyEngineHub.Storage.Tests;

public sealed class BoundedDirectorySizerTests
{
    [Fact]
    public void MeasuresFilesRecursively()
    {
        using var directory = new StorageTestDirectory();
        directory.CreateFile("one.bin", 5);
        directory.CreateFile(Path.Combine("Nested", "two.bin"), 7);

        var result = new BoundedDirectorySizer().Analyze(directory.Root);

        Assert.True(result.Exists);
        Assert.False(result.IsPartial);
        Assert.Equal(12, result.SizeBytes);
        Assert.Equal(2, result.FileCount);
        Assert.Equal(2, result.DirectoryCount);
    }

    [Fact]
    public void StopsAtConfiguredEntryLimit()
    {
        using var directory = new StorageTestDirectory();
        directory.CreateFile("one.bin", 5);
        directory.CreateFile("two.bin", 7);

        var result = new BoundedDirectorySizer(
            maximumEntryCount: 1,
            maximumDuration: TimeSpan.FromMinutes(1)).Analyze(directory.Root);

        Assert.True(result.IsPartial);
        Assert.Contains("partiel", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }
}
