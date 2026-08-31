namespace CyEngineHub.Unreal.Models;

public readonly record struct EngineVersion(int Major, int Minor, int Patch)
{
    public static EngineVersion Unknown { get; } = new(0, 0, 0);

    public bool IsKnown => Major > 0;

    public override string ToString() => IsKnown ? $"{Major}.{Minor}.{Patch}" : "Unknown";
}

public enum EngineInstallationType
{
    EpicBinary,
    InstalledBuild,
    Source,
    SourceFork,
    Custom,
}

public enum EngineHealth
{
    Healthy,
    Warning,
    Broken,
}

public sealed record UnrealEngineInstallation(
    string Id,
    string AssociationId,
    string DisplayName,
    string RootPath,
    string EditorPath,
    EngineVersion Version,
    EngineInstallationType Type,
    string? Branch,
    long? Changelist,
    string? BuildId,
    string DiscoverySource,
    EngineHealth Health,
    IReadOnlyList<string> HealthIssues,
    DateTimeOffset DiscoveredAt);

public interface IEngineCatalog
{
    Task<IReadOnlyList<UnrealEngineInstallation>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        UnrealEngineInstallation engine,
        CancellationToken cancellationToken = default);
}

