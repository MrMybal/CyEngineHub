namespace CyEngineHub.Projects.Detection;

public sealed class ConfiguredProjectRootDiscoverySource : IProjectDiscoverySource
{
    private static readonly HashSet<string> ExcludedDirectories = new(
        [
            ".git",
            "Binaries",
            "Content",
            "DerivedDataCache",
            "Engine",
            "Intermediate",
            "Saved",
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly Func<CancellationToken, Task<IReadOnlyCollection<string>>> _getRoots;
    private readonly int _maximumDepth;

    public ConfiguredProjectRootDiscoverySource(
        Func<CancellationToken, Task<IReadOnlyCollection<string>>> getRoots,
        int maximumDepth = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumDepth);

        _getRoots = getRoots;
        _maximumDepth = maximumDepth;
    }

    public string Name => "Configured project roots";

    public async Task<IReadOnlyList<string>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var roots = await _getRoots(cancellationToken).ConfigureAwait(false);
        return await Task.Run(
            () => ScanRoots(roots, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private List<string> ScanRoots(
        IReadOnlyCollection<string> roots,
        CancellationToken cancellationToken)
    {
        var projects = new List<string>();
        foreach (var configuredRoot in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(configuredRoot) || !Directory.Exists(configuredRoot))
            {
                continue;
            }

            var queue = new Queue<(string Path, int Depth)>();
            queue.Enqueue((Path.GetFullPath(configuredRoot), 0));
            while (queue.TryDequeue(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryAddProjectFiles(current.Path, projects);
                if (current.Depth >= _maximumDepth)
                {
                    continue;
                }

                foreach (var directory in TryEnumerateDirectories(current.Path))
                {
                    if (!ExcludedDirectories.Contains(Path.GetFileName(directory)))
                    {
                        queue.Enqueue((directory, current.Depth + 1));
                    }
                }
            }
        }

        return projects;
    }

    private static void TryAddProjectFiles(string directory, List<string> projects)
    {
        try
        {
            projects.AddRange(Directory.EnumerateFiles(
                directory,
                "*.uproject",
                SearchOption.TopDirectoryOnly));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
        }
    }

    private static string[] TryEnumerateDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return [];
        }
    }
}
