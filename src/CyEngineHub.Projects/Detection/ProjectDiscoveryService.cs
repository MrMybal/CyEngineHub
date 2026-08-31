using CyEngineHub.Core.Diagnostics;
using CyEngineHub.Projects.Models;
using CyEngineHub.Projects.Parsing;

namespace CyEngineHub.Projects.Detection;

public sealed class ProjectDiscoveryService
{
    private readonly IReadOnlyList<IProjectDiscoverySource> _sources;
    private readonly UprojectDescriptorParser _parser;
    private readonly IProjectCatalog _catalog;
    private readonly IAppLogger _logger;

    public ProjectDiscoveryService(
        IEnumerable<IProjectDiscoverySource> sources,
        UprojectDescriptorParser parser,
        IProjectCatalog catalog,
        IAppLogger? logger = null)
    {
        _sources = sources.ToArray();
        _parser = parser;
        _catalog = catalog;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<IReadOnlyList<UnrealProject>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var paths = new HashSet<string>(comparer);
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var path in await source.DiscoverAsync(cancellationToken).ConfigureAwait(false))
                {
                    paths.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _logger.WriteAsync(
                    AppLogLevel.Warning,
                    nameof(ProjectDiscoveryService),
                    $"Project source '{source.Name}' failed.",
                    exception,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var projects = new List<UnrealProject>();
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var project = await _parser.ParseAsync(path, cancellationToken).ConfigureAwait(false);
                await _catalog.UpsertAsync(project, cancellationToken).ConfigureAwait(false);
                projects.Add(project);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _logger.WriteAsync(
                    AppLogLevel.Warning,
                    nameof(ProjectDiscoveryService),
                    $"Could not inspect project '{path}'.",
                    exception,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return projects
            .OrderBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
