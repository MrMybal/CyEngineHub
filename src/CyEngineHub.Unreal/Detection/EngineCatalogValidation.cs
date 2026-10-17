using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Unreal.Detection;

public static class EngineCatalogValidation
{
    // Do not erase cached records: disconnected drives can return later.
    // Confirmed missing directories are omitted from the current snapshot only.
    public static Task<IReadOnlyList<UnrealEngineInstallation>> ValidateAsync(
        IReadOnlyList<UnrealEngineInstallation> cached, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<UnrealEngineInstallation>>(async () =>
        {
            var result = new List<UnrealEngineInstallation>();
            var inspector = new EngineInspector();
            foreach (var engine in cached)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    _ = File.GetAttributes(engine.RootPath);
                    var inspected = await inspector.InspectAsync(new EngineCandidate(
                        engine.RootPath, engine.DiscoverySource, engine.AssociationId, engine.DisplayName), cancellationToken).ConfigureAwait(false);
                    // Parent traversal must not resolve a different engine for a stale child path.
                    result.Add(inspected is not null && Path.GetFullPath(inspected.RootPath).Equals(
                        Path.GetFullPath(engine.RootPath), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                        ? inspected with { Id = engine.Id, Type = engine.Type }
                        : Unavailable(engine, "Installation incomplète : Build.version ou éditeur absent."));
                }
                catch (Exception exception) when (exception is DirectoryNotFoundException or FileNotFoundException)
                {
                    if (!Directory.Exists(Path.GetPathRoot(engine.RootPath)))
                        result.Add(Unavailable(engine, "Lecteur indisponible : présence du moteur non vérifiable."));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException or NotSupportedException)
                {
                    result.Add(Unavailable(engine, "Installation inaccessible ou métadonnées invalides."));
                }
            }
            return result;
        }, cancellationToken);

    public static bool IsPresent(UnrealEngineInstallation engine) =>
        Directory.Exists(engine.RootPath) && File.Exists(Path.Combine(engine.RootPath, "Engine", "Build", "Build.version")) &&
        (File.Exists(engine.EditorPath) || Directory.Exists(engine.EditorPath));

    private static UnrealEngineInstallation Unavailable(UnrealEngineInstallation engine, string issue) =>
        engine with { Health = EngineHealth.Broken, HealthIssues = [issue] };
}
