namespace CyEngineHub.Engines;

public sealed record LocalEngineInstallation(
    EngineFamily Family,
    string ProviderId,
    string Id,
    string DisplayName,
    string Version,
    string RootPath,
    string EditorPath,
    string DiscoverySource);

public sealed record LocalEngineProject(
    EngineFamily Family,
    string ProviderId,
    string Id,
    string Name,
    string RootPath,
    string MarkerPath,
    string RequiredEditorVersion,
    string Description);

public sealed record LocalEngineDiscoveryRequest(
    IReadOnlyCollection<string> EngineRoots,
    IReadOnlyCollection<string> ProjectRoots);

public sealed record LocalEngineDiscoverySnapshot(
    EngineFamily Family,
    IReadOnlyList<LocalEngineInstallation> Installations,
    IReadOnlyList<LocalEngineProject> Projects,
    bool IsPartial,
    IReadOnlyList<string> Warnings);

public sealed record LocalEngineLaunchRequest(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory);

public interface ILocalEngineProvider : IEngineModule
{
    Task<LocalEngineDiscoverySnapshot> DiscoverAsync(
        LocalEngineDiscoveryRequest request,
        CancellationToken cancellationToken = default);

    LocalEngineLaunchRequest CreateEditorLaunch(LocalEngineInstallation installation);

    LocalEngineLaunchRequest CreateProjectLaunch(
        LocalEngineProject project,
        IReadOnlyCollection<LocalEngineInstallation> availableInstallations);
}

public sealed record BoundedProjectScanResult(
    IReadOnlyList<string> ProjectDirectories,
    int InspectedDirectoryCount,
    bool IsPartial);

public static class BoundedProjectDirectoryScanner
{
    private static readonly HashSet<string> SkippedDirectoryNames = new(
        [
            ".git", ".hg", ".svn", ".vs", ".idea", ".vscode",
            "Binaries", "DerivedDataCache", "Intermediate", "Library", "Logs",
            "node_modules", "obj", "Packages", "Saved", "Temp", "UserSettings",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static BoundedProjectScanResult Find(
        IEnumerable<string> roots,
        Func<string, bool> isProjectDirectory,
        int maximumDepth = 8,
        int maximumDirectories = 25_000,
        int maximumProjects = 500,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(isProjectDirectory);
        if (maximumDepth < 0 || maximumDirectories <= 0 || maximumProjects <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDepth));
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var pending = new Stack<(string Directory, int Depth)>();
        var visited = new HashSet<string>(comparer);
        var projects = new HashSet<string>(comparer);
        foreach (var candidate in roots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalized = NormalizeDirectory(candidate);
            if (normalized is not null && IsSafeDirectory(normalized))
            {
                pending.Push((normalized, 0));
            }
        }

        var inspected = 0;
        var partial = false;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            if (!visited.Add(directory))
            {
                continue;
            }

            inspected++;
            if (inspected > maximumDirectories)
            {
                partial = true;
                break;
            }

            try
            {
                if (isProjectDirectory(directory))
                {
                    projects.Add(directory);
                    if (projects.Count >= maximumProjects)
                    {
                        partial = pending.Count > 0;
                        break;
                    }

                    continue;
                }

                if (depth >= maximumDepth)
                {
                    continue;
                }

                foreach (var child in Directory.EnumerateDirectories(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (SkippedDirectoryNames.Contains(Path.GetFileName(child)) ||
                        !IsSafeDirectory(child))
                    {
                        continue;
                    }

                    pending.Push((Path.GetFullPath(child), depth + 1));
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                partial = true;
            }
        }

        return new BoundedProjectScanResult(
            projects.OrderBy(path => path, comparer).ToArray(),
            inspected,
            partial);
    }

    private static string? NormalizeDirectory(string candidate)
    {
        try
        {
            var path = Path.GetFullPath(candidate.Trim().Trim('"'));
            if (File.Exists(path))
            {
                return Path.GetDirectoryName(path);
            }

            return Directory.Exists(path) ? path : null;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsSafeDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) &&
                   (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
