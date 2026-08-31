using System.Text.Json;
using CyEngineHub.Epic.Models;

namespace CyEngineHub.Epic.Local;

public interface IEpicLocalCatalogService
{
    Task<EpicLocalSnapshot> RefreshAsync(
        IProgress<EpicCatalogProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class EpicLocalCatalogService : IEpicLocalCatalogService
{
    private const int MaximumManifestCount = 5_000;
    private const long MaximumInstalledDataBytes = 10 * 1024 * 1024;
    private const int MaximumTopLevelItemCount = 10_000;

    private readonly string _manifestDirectory;
    private readonly string _launcherInstalledPath;
    private readonly string _vaultCachePath;
    private readonly string _fabPluginsPath;
    private readonly IEpicLauncherLocator _launcherLocator;
    private readonly IEpicManifestParser _manifestParser;

    public EpicLocalCatalogService(
        string? commonApplicationData = null,
        string? localApplicationData = null,
        IEpicLauncherLocator? launcherLocator = null,
        IEpicManifestParser? manifestParser = null)
    {
        var commonData = commonApplicationData ??
                         Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var localData = localApplicationData ??
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _manifestDirectory = Path.Combine(
            commonData,
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests");
        _launcherInstalledPath = Path.Combine(
            commonData,
            "Epic",
            "UnrealEngineLauncher",
            "LauncherInstalled.dat");
        _vaultCachePath = Path.Combine(
            commonData,
            "Epic",
            "EpicGamesLauncher",
            "VaultCache");
        _fabPluginsPath = Path.Combine(localData, "FabPlugins");
        _launcherLocator = launcherLocator ?? new EpicLauncherLocator();
        _manifestParser = manifestParser ?? new EpicManifestParser();
    }

    public async Task<EpicLocalSnapshot> RefreshAsync(
        IProgress<EpicCatalogProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<string>();
        var artifacts = new Dictionary<string, EpicLocalArtifact>(StringComparer.OrdinalIgnoreCase);
        var manifestPaths = Directory.Exists(_manifestDirectory)
            ? Directory.EnumerateFiles(_manifestDirectory, "*.item", SearchOption.TopDirectoryOnly)
                .Take(MaximumManifestCount + 1)
                .ToArray()
            : [];
        if (manifestPaths.Length > MaximumManifestCount)
        {
            diagnostics.Add($"Plus de {MaximumManifestCount:N0} manifests Epic; analyse tronquée.");
            manifestPaths = manifestPaths[..MaximumManifestCount];
        }

        var total = manifestPaths.Length + 1;
        progress?.Report(new EpicCatalogProgress(0, total, "Installations déclarées par Epic"));
        foreach (var artifact in await ReadLauncherInstalledAsync(diagnostics, cancellationToken)
                     .ConfigureAwait(false))
        {
            Merge(artifacts, artifact);
        }

        var completed = 1;
        foreach (var manifestPath in manifestPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new EpicCatalogProgress(
                completed,
                total,
                $"Manifeste · {Path.GetFileName(manifestPath)}"));
            try
            {
                var artifact = await _manifestParser.ParseAsync(manifestPath, cancellationToken)
                    .ConfigureAwait(false);
                if (artifact is not null)
                {
                    Merge(artifacts, artifact);
                }
            }
            catch (EpicOperationException exception)
            {
                diagnostics.Add(exception.Message);
            }

            completed++;
        }

        var launcherPath = _launcherLocator.FindLauncherExecutable();
        var vaultItemCount = CountTopLevelDirectories(_vaultCachePath, "Vault Cache", diagnostics);
        var fabPluginCount = CountTopLevelDirectories(_fabPluginsPath, "FabPlugins", diagnostics);
        progress?.Report(new EpicCatalogProgress(total, total, "Catalogue local Epic/Fab actualisé"));
        return new EpicLocalSnapshot(
            launcherPath is not null,
            _launcherLocator.IsLauncherRunning(),
            launcherPath,
            _manifestDirectory,
            _vaultCachePath,
            vaultItemCount,
            _fabPluginsPath,
            fabPluginCount,
            artifacts.Values
                .OrderBy(artifact => artifact.Kind)
                .ThenBy(artifact => artifact.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            diagnostics,
            DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<EpicLocalArtifact>> ReadLauncherInstalledAsync(
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(_launcherInstalledPath);
        if (!info.Exists)
        {
            return [];
        }

        if (info.Length > MaximumInstalledDataBytes)
        {
            diagnostics.Add("LauncherInstalled.dat dépasse la limite de 10 MB.");
            return [];
        }

        try
        {
            await using var stream = new FileStream(
                _launcherInstalledPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("InstallationList", out var list) ||
                list.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var results = new List<EpicLocalArtifact>();
            foreach (var item in list.EnumerateArray().Take(MaximumManifestCount))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var appName = GetString(item, "AppName");
                var artifactId = GetString(item, "ArtifactId") ?? appName;
                if (string.IsNullOrWhiteSpace(artifactId))
                {
                    continue;
                }

                var installLocation = GetString(item, "InstallLocation") ?? string.Empty;
                var catalogNamespace = GetString(item, "NamespaceId") ?? string.Empty;
                results.Add(new EpicLocalArtifact(
                    EpicManifestParser.StableId(artifactId),
                    appName ?? artifactId,
                    appName ?? artifactId,
                    "—",
                    installLocation,
                    string.Empty,
                    catalogNamespace,
                    EpicManifestParser.Classify(appName, null, catalogNamespace),
                    !string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation),
                    Path.GetFileName(_launcherInstalledPath)));
            }

            return results;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            diagnostics.Add($"LauncherInstalled.dat : {exception.Message}");
            return [];
        }
    }

    private static void Merge(
        Dictionary<string, EpicLocalArtifact> artifacts,
        EpicLocalArtifact incoming)
    {
        if (!artifacts.TryGetValue(incoming.Id, out var current))
        {
            artifacts[incoming.Id] = incoming;
            return;
        }

        artifacts[incoming.Id] = current with
        {
            AppName = Prefer(incoming.AppName, current.AppName),
            DisplayName = Prefer(incoming.DisplayName, current.DisplayName),
            Version = Prefer(incoming.Version, current.Version),
            InstallLocation = Prefer(incoming.InstallLocation, current.InstallLocation),
            CatalogItemId = Prefer(incoming.CatalogItemId, current.CatalogItemId),
            CatalogNamespace = Prefer(incoming.CatalogNamespace, current.CatalogNamespace),
            Kind = incoming.Kind != EpicArtifactKind.Unknown ? incoming.Kind : current.Kind,
            IsInstalled = incoming.IsInstalled || current.IsInstalled,
            Source = string.Equals(incoming.Source, current.Source, StringComparison.OrdinalIgnoreCase)
                ? current.Source
                : $"{current.Source} · {incoming.Source}",
        };
    }

    private static string Prefer(string candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) || candidate == "—" ? fallback : candidate;

    private static int CountTopLevelDirectories(
        string path,
        string label,
        List<string> diagnostics)
    {
        if (!Directory.Exists(path))
        {
            return 0;
        }

        try
        {
            var count = Directory.EnumerateDirectories(path)
                .Take(MaximumTopLevelItemCount + 1)
                .Count();
            if (count > MaximumTopLevelItemCount)
            {
                diagnostics.Add($"{label} contient plus de {MaximumTopLevelItemCount:N0} dossiers.");
                return MaximumTopLevelItemCount;
            }

            return count;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add($"{label} : {exception.Message}");
            return 0;
        }
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
