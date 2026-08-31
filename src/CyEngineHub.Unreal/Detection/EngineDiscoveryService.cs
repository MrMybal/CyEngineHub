using CyEngineHub.Core.Diagnostics;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Unreal.Detection;

public sealed class EngineDiscoveryService
{
    private readonly IReadOnlyList<IEngineDiscoverySource> _sources;
    private readonly IEngineInspector _inspector;
    private readonly IEngineCatalog _catalog;
    private readonly IAppLogger _logger;

    public EngineDiscoveryService(
        IEnumerable<IEngineDiscoverySource> sources,
        IEngineInspector inspector,
        IEngineCatalog catalog,
        IAppLogger? logger = null)
    {
        _sources = sources.ToArray();
        _inspector = inspector;
        _catalog = catalog;
        _logger = logger ?? NullAppLogger.Instance;
    }

    public async Task<IReadOnlyList<UnrealEngineInstallation>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var candidates = new Dictionary<string, EngineCandidate>(comparer);

        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                foreach (var candidate in await source.DiscoverAsync(cancellationToken).ConfigureAwait(false))
                {
                    var key = NormalizeCandidatePath(candidate.Path);
                    if (key is not null)
                    {
                        candidates.TryAdd(key, candidate);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _logger.WriteAsync(
                    AppLogLevel.Warning,
                    nameof(EngineDiscoveryService),
                    $"Engine source '{source.Name}' failed.",
                    exception,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var engines = new List<UnrealEngineInstallation>();
        foreach (var candidate in candidates.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var engine = await _inspector.InspectAsync(candidate, cancellationToken).ConfigureAwait(false);
                if (engine is null)
                {
                    continue;
                }

                await _catalog.UpsertAsync(engine, cancellationToken).ConfigureAwait(false);
                engines.Add(engine);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await _logger.WriteAsync(
                    AppLogLevel.Warning,
                    nameof(EngineDiscoveryService),
                    $"Could not inspect engine candidate '{candidate.Path}'.",
                    exception,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        return engines
            .OrderByDescending(engine => engine.Version.Major)
            .ThenByDescending(engine => engine.Version.Minor)
            .ThenBy(engine => engine.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? NormalizeCandidatePath(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim().Trim('"')));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
