using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Unreal.Detection;

public sealed record EngineCandidate(
    string Path,
    string Source,
    string? Identifier = null,
    string? DisplayName = null);

public interface IEngineDiscoverySource
{
    string Name { get; }

    Task<IReadOnlyList<EngineCandidate>> DiscoverAsync(
        CancellationToken cancellationToken = default);
}

public interface IEngineInspector
{
    Task<UnrealEngineInstallation?> InspectAsync(
        EngineCandidate candidate,
        CancellationToken cancellationToken = default);
}

