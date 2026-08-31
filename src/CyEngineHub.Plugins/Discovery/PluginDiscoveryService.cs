using CyEngineHub.Plugins.Models;
using CyEngineHub.Plugins.Parsing;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Plugins.Discovery;

public sealed record PluginDiscoveryRequest(
    IReadOnlyList<UnrealEngineInstallation> Engines,
    IReadOnlyList<UnrealProject> Projects,
    IReadOnlyList<string> LibraryRoots,
    int MaximumDepth = 6,
    int MaximumDirectoriesPerRoot = 50_000);

public sealed record PluginDiscoveryProgress(int Completed, int Total, string Message);

public sealed class PluginDiscoveryService
{
    private static readonly HashSet<string> ExcludedDirectories = new(
        [".git", ".vs", "Binaries", "Intermediate", "Saved", "DerivedDataCache", "Content"],
        StringComparer.OrdinalIgnoreCase);

    private readonly IUpluginDescriptorParser _parser;
    private readonly StringComparer _pathComparer;

    public PluginDiscoveryService(IUpluginDescriptorParser parser)
    {
        _parser = parser;
        _pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    public async Task<IReadOnlyList<UnrealPluginDescriptor>> RefreshAsync(
        PluginDiscoveryRequest request,
        IProgress<PluginDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumDepth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.MaximumDirectoriesPerRoot, 1);
        var roots = CreateRoots(request);
        var candidates = new Dictionary<string, PluginCandidate>(_pathComparer);
        for (var index = 0; index < roots.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = roots[index];
            progress?.Report(new PluginDiscoveryProgress(
                index,
                roots.Count,
                $"Recherche · {root.SourceLabel}"));
            foreach (var descriptorPath in EnumerateDescriptors(
                         root.Path,
                         request.MaximumDepth,
                         request.MaximumDirectoriesPerRoot,
                         cancellationToken))
            {
                candidates.TryAdd(descriptorPath, new PluginCandidate(
                    descriptorPath,
                    root.Kind,
                    root.SourceLabel,
                    root.OwnerId));
            }
        }

        var plugins = new List<UnrealPluginDescriptor>();
        var candidateIndex = 0;
        foreach (var candidate in candidates.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PluginDiscoveryProgress(
                candidateIndex,
                candidates.Count,
                $"Lecture · {Path.GetFileName(candidate.DescriptorPath)}"));
            try
            {
                plugins.Add(await _parser.ParseAsync(candidate, cancellationToken).ConfigureAwait(false));
            }
            catch (PluginOperationException exception)
            {
                plugins.Add(CreateBrokenDescriptor(candidate, exception.Message));
            }

            candidateIndex++;
        }

        ApplyCatalogDiagnostics(plugins);
        progress?.Report(new PluginDiscoveryProgress(
            candidates.Count,
            candidates.Count,
            "Catalogue plugins actualisé"));
        return plugins
            .OrderBy(plugin => plugin.FriendlyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(plugin => plugin.SourceKind)
            .ToArray();
    }

    private static List<DiscoveryRoot> CreateRoots(PluginDiscoveryRequest request)
    {
        var roots = new List<DiscoveryRoot>();
        roots.AddRange(request.Engines.Select(engine => new DiscoveryRoot(
            Path.Combine(engine.RootPath, "Engine", "Plugins"),
            PluginSourceKind.Engine,
            engine.DisplayName,
            engine.Id)));
        roots.AddRange(request.Projects.Select(project => new DiscoveryRoot(
            Path.Combine(project.RootPath, "Plugins"),
            PluginSourceKind.Project,
            project.Name,
            project.Id)));
        roots.AddRange(request.LibraryRoots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new DiscoveryRoot(
                Path.GetFullPath(path),
                PluginSourceKind.LocalLibrary,
                $"Bibliothèque · {Path.GetFileName(Path.TrimEndingDirectorySeparator(path))}",
                null)));
        return roots;
    }

    private static IEnumerable<string> EnumerateDescriptors(
        string rootPath,
        int maximumDepth,
        int maximumDirectories,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(rootPath))
        {
            yield break;
        }

        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((Path.GetFullPath(rootPath), 0));
        var visitedDirectories = 0;
        while (queue.Count > 0 && visitedDirectories < maximumDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            visitedDirectories++;
            string[] files;
            try
            {
                files = Directory.GetFiles(current.Path, "*.uplugin", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return Path.GetFullPath(file);
            }

            if (current.Depth >= maximumDepth)
            {
                continue;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current.Path);
            }
            catch (Exception exception) when (
                exception is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                var name = Path.GetFileName(directory);
                if (ExcludedDirectories.Contains(name))
                {
                    continue;
                }

                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch (Exception exception) when (
                    exception is UnauthorizedAccessException or IOException)
                {
                    continue;
                }

                queue.Enqueue((directory, current.Depth + 1));
            }
        }
    }

    private static void ApplyCatalogDiagnostics(List<UnrealPluginDescriptor> plugins)
    {
        var availableNames = plugins
            .Select(plugin => plugin.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicateNames = plugins
            .GroupBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < plugins.Count; index++)
        {
            var plugin = plugins[index];
            var issues = plugin.Issues.ToList();
            foreach (var dependency in plugin.Dependencies.Where(item => item.Enabled && !item.Optional))
            {
                if (!availableNames.Contains(dependency.Name))
                {
                    issues.Add($"Dépendance requise non trouvée dans le catalogue : {dependency.Name}.");
                }
            }

            if (duplicateNames.Contains(plugin.Name))
            {
                issues.Add("Plusieurs plugins du catalogue utilisent ce nom interne.");
            }

            if (issues.Count != plugin.Issues.Count)
            {
                plugins[index] = plugin with
                {
                    Health = plugin.Health == PluginHealth.Broken
                        ? PluginHealth.Broken
                        : PluginHealth.Warning,
                    Issues = issues,
                };
            }
        }
    }

    private static UnrealPluginDescriptor CreateBrokenDescriptor(
        PluginCandidate candidate,
        string error)
    {
        var path = Path.GetFullPath(candidate.DescriptorPath);
        var name = Path.GetFileNameWithoutExtension(path);
        return new UnrealPluginDescriptor(
            $"BROKEN-{UpluginDescriptorParser.CreateStableId(path)}",
            name,
            name,
            string.Empty,
            "Unknown",
            path,
            Path.GetDirectoryName(path)!,
            candidate.SourceKind,
            candidate.SourceLabel,
            candidate.SourceOwnerId,
            0,
            0,
            string.Empty,
            null,
            false,
            false,
            false,
            [],
            [],
            [],
            PluginHealth.Broken,
            [error],
            DateTimeOffset.UtcNow);
    }

    private sealed record DiscoveryRoot(
        string Path,
        PluginSourceKind Kind,
        string SourceLabel,
        string? OwnerId);
}
