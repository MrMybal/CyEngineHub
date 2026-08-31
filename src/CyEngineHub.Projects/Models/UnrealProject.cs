namespace CyEngineHub.Projects.Models;

public sealed record ProjectPluginReference(string Name, bool Enabled);

public enum ProjectHealth
{
    Healthy,
    Warning,
    Broken,
}

public sealed record UnrealProject(
    string Id,
    string Name,
    string FilePath,
    string RootPath,
    string? EngineAssociation,
    string? Description,
    string? Category,
    IReadOnlyList<string> Modules,
    IReadOnlyList<ProjectPluginReference> Plugins,
    ProjectHealth Health,
    IReadOnlyList<string> HealthIssues,
    DateTimeOffset DiscoveredAt);

public interface IProjectCatalog
{
    Task<IReadOnlyList<UnrealProject>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task UpsertAsync(UnrealProject project, CancellationToken cancellationToken = default);
}

