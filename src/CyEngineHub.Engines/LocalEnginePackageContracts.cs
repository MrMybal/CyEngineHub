namespace CyEngineHub.Engines;

public enum LocalEnginePackageHealth
{
    Ready,
    Warning,
    Invalid,
}

public sealed record LocalEnginePackage(
    EngineFamily Family,
    string ProviderId,
    string Id,
    string ProjectId,
    string ProjectName,
    string Name,
    string Version,
    string Source,
    string State,
    string RootPath,
    string ManifestPath,
    string Description,
    LocalEnginePackageHealth Health,
    IReadOnlyList<string> Dependencies,
    IReadOnlyList<string> Issues);

public sealed record LocalEnginePackageInventorySnapshot(
    EngineFamily Family,
    IReadOnlyList<LocalEnginePackage> Packages,
    bool IsPartial,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Optional, read-only inventory contract for engine-native packages, add-ons and plug-ins.
/// Installation and removal remain provider-specific capabilities.
/// </summary>
public interface ILocalEnginePackageProvider : IEngineModule
{
    Task<LocalEnginePackageInventorySnapshot> DiscoverPackagesAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken = default);
}
