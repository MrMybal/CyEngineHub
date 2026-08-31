using System.Diagnostics;
using CyEngineHub.Git.Models;

namespace CyEngineHub.Git.Analysis;

public sealed class GitLfsStorageAnalyzer
{
    private readonly long _maximumObjectCount;
    private readonly TimeSpan _maximumDuration;

    public GitLfsStorageAnalyzer(
        long maximumObjectCount = 250_000,
        TimeSpan? maximumDuration = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumObjectCount, 1);

        _maximumObjectCount = maximumObjectCount;
        _maximumDuration = maximumDuration ?? TimeSpan.FromSeconds(5);
    }

    public GitLfsSnapshot Analyze(
        string gitCommonDirectory,
        bool isInstalled,
        IReadOnlyList<string> trackedPatterns,
        CancellationToken cancellationToken = default)
    {
        if (!isInstalled)
        {
            return new GitLfsSnapshot(false, 0, 0, trackedPatterns, false);
        }

        var objectsDirectory = Path.Combine(gitCommonDirectory, "lfs", "objects");
        if (!Directory.Exists(objectsDirectory))
        {
            return new GitLfsSnapshot(true, 0, 0, trackedPatterns, false);
        }

        long objectCount = 0;
        long storageBytes = 0;
        var truncated = false;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            foreach (var filePath in Directory.EnumerateFiles(
                         objectsDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (objectCount >= _maximumObjectCount || stopwatch.Elapsed >= _maximumDuration)
                {
                    truncated = true;
                    break;
                }

                var fileName = Path.GetFileName(filePath);
                if (fileName.Length != 64)
                {
                    continue;
                }

                objectCount++;
                storageBytes += new FileInfo(filePath).Length;
            }

            return new GitLfsSnapshot(
                true,
                objectCount,
                storageBytes,
                trackedPatterns,
                truncated);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            return new GitLfsSnapshot(
                true,
                objectCount,
                storageBytes,
                trackedPatterns,
                true,
                exception.Message);
        }
    }
}
