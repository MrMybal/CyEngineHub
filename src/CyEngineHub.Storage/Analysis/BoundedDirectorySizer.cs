using System.Diagnostics;

namespace CyEngineHub.Storage.Analysis;

public sealed record DirectorySizeResult(
    long SizeBytes,
    long FileCount,
    long DirectoryCount,
    DateTimeOffset? LastModified,
    bool IsPartial,
    bool Exists,
    string Diagnostic);

public sealed class BoundedDirectorySizer
{
    private readonly long _maximumEntryCount;
    private readonly TimeSpan _maximumDuration;

    public BoundedDirectorySizer(
        long maximumEntryCount = 250_000,
        TimeSpan? maximumDuration = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumEntryCount, 1);
        _maximumEntryCount = maximumEntryCount;
        _maximumDuration = maximumDuration ?? TimeSpan.FromSeconds(5);
    }

    public DirectorySizeResult Analyze(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Directory.Exists(path))
        {
            return new DirectorySizeResult(0, 0, 0, null, false, false, "Dossier absent.");
        }

        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(path));
        var stopwatch = Stopwatch.StartNew();
        long sizeBytes = 0;
        long fileCount = 0;
        long directoryCount = 0;
        long inspectedEntries = 0;
        var inaccessibleCount = 0;
        var reparsePointCount = 0;
        var truncated = false;
        DateTimeOffset? lastModified = null;

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inspectedEntries >= _maximumEntryCount || stopwatch.Elapsed >= _maximumDuration)
            {
                truncated = true;
                break;
            }

            var directory = pending.Pop();
            directoryCount++;
            UpdateLastModified(directory, ref lastModified);
            try
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    inspectedEntries++;
                    if (inspectedEntries > _maximumEntryCount || stopwatch.Elapsed >= _maximumDuration)
                    {
                        truncated = true;
                        break;
                    }

                    try
                    {
                        var attributes = File.GetAttributes(entry);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            reparsePointCount++;
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pending.Push(entry);
                            continue;
                        }

                        var info = new FileInfo(entry);
                        sizeBytes = checked(sizeBytes + info.Length);
                        fileCount++;
                        var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
                        if (lastModified is null || modified > lastModified)
                        {
                            lastModified = modified;
                        }
                    }
                    catch (Exception exception) when (
                        exception is UnauthorizedAccessException or IOException)
                    {
                        inaccessibleCount++;
                    }
                }
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                inaccessibleCount++;
            }
        }

        var diagnostics = new List<string>();
        if (truncated)
        {
            diagnostics.Add($"Résultat partiel après {inspectedEntries:N0} entrées.");
        }

        if (inaccessibleCount > 0)
        {
            diagnostics.Add($"{inaccessibleCount:N0} entrée(s) inaccessible(s).");
        }

        if (reparsePointCount > 0)
        {
            diagnostics.Add($"{reparsePointCount:N0} lien(s) ignoré(s).");
        }

        return new DirectorySizeResult(
            sizeBytes,
            fileCount,
            directoryCount,
            lastModified,
            truncated || inaccessibleCount > 0,
            true,
            diagnostics.Count == 0 ? "Analyse complète." : string.Join(" ", diagnostics));
    }

    private static void UpdateLastModified(string path, ref DateTimeOffset? lastModified)
    {
        try
        {
            var modified = new DateTimeOffset(Directory.GetLastWriteTimeUtc(path), TimeSpan.Zero);
            if (lastModified is null || modified > lastModified)
            {
                lastModified = modified;
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException)
        {
            // File timestamps are informative only; enumeration still provides the useful result.
        }
    }
}
