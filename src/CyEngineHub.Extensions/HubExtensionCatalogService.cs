namespace CyEngineHub.Extensions;

public interface IHubExtensionCatalogService
{
    Task<IReadOnlyList<HubExtensionSnapshot>> DiscoverAsync(
        string builtInRoot,
        string userRoot,
        CancellationToken cancellationToken = default);
}

public sealed class HubExtensionCatalogService(
    IHubExtensionManifestParser manifestParser) : IHubExtensionCatalogService
{
    private const int MaximumExtensionsPerRoot = 200;

    public async Task<IReadOnlyList<HubExtensionSnapshot>> DiscoverAsync(
        string builtInRoot,
        string userRoot,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<HubExtensionSnapshot>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await DiscoverRootAsync(
                builtInRoot,
                HubExtensionOrigin.BuiltIn,
                ids,
                snapshots,
                cancellationToken)
            .ConfigureAwait(false);
        Directory.CreateDirectory(userRoot);
        await DiscoverRootAsync(
                userRoot,
                HubExtensionOrigin.User,
                ids,
                snapshots,
                cancellationToken)
            .ConfigureAwait(false);
        return snapshots
            .OrderBy(snapshot => snapshot.Health)
            .ThenBy(snapshot => snapshot.Manifest.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task DiscoverRootAsync(
        string candidateRoot,
        HubExtensionOrigin origin,
        HashSet<string> ids,
        List<HubExtensionSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(candidateRoot);
        if (!Directory.Exists(root) || HubExtensionManifestParser.IsReparsePoint(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(
                     root,
                     "*",
                     SearchOption.TopDirectoryOnly).Take(MaximumExtensionsPerRoot + 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshots.Count >= MaximumExtensionsPerRoot * 2)
            {
                break;
            }

            try
            {
                var manifest = await manifestParser.ParseAsync(directory, cancellationToken)
                    .ConfigureAwait(false);
                if (!ids.Add(manifest.Id))
                {
                    snapshots.Add(new HubExtensionSnapshot(
                        manifest,
                        directory,
                        origin,
                        HubExtensionHealth.Invalid,
                        "Identifiant déjà fourni par une autre extension; le provider prioritaire est conservé."));
                    continue;
                }

                snapshots.Add(new HubExtensionSnapshot(
                    manifest,
                    Path.GetFullPath(directory),
                    origin,
                    HubExtensionHealth.Ready,
                    null));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is HubExtensionException or IOException or UnauthorizedAccessException)
            {
                var folderName = Path.GetFileName(directory);
                snapshots.Add(new HubExtensionSnapshot(
                    new HubExtensionManifest
                    {
                        SchemaVersion = 1,
                        Id = $"invalid.{folderName.ToLowerInvariant()}",
                        DisplayName = folderName,
                        Version = "0.0.0",
                        Publisher = "Inconnu",
                        Description = "Extension invalide",
                        Kind = HubExtensionKind.Tool,
                        ExecutionKind = HubExtensionExecutionKind.WebLink,
                        Capabilities = HubExtensionCapability.ProjectTool,
                        SupportedEngines = [Engines.EngineFamily.Custom],
                    },
                    Path.GetFullPath(directory),
                    origin,
                    HubExtensionHealth.Invalid,
                    exception.Message));
            }
        }
    }
}
