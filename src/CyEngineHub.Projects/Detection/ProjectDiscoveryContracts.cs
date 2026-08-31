namespace CyEngineHub.Projects.Detection;

public interface IProjectDiscoverySource
{
    string Name { get; }

    Task<IReadOnlyList<string>> DiscoverAsync(CancellationToken cancellationToken = default);
}

