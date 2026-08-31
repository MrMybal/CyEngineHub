namespace CyEngineHub.Unreal.Detection;

public sealed class ConfiguredEngineDiscoverySource : IEngineDiscoverySource
{
    private readonly Func<CancellationToken, Task<IReadOnlyCollection<string>>> _getPaths;

    public ConfiguredEngineDiscoverySource(
        Func<CancellationToken, Task<IReadOnlyCollection<string>>> getPaths)
    {
        _getPaths = getPaths;
    }

    public string Name => "Configured paths";

    public async Task<IReadOnlyList<EngineCandidate>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        var paths = await _getPaths(cancellationToken).ConfigureAwait(false);
        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => new EngineCandidate(path, Name))
            .ToArray();
    }
}

