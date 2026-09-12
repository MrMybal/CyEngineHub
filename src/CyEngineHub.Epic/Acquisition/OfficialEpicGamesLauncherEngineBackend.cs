using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Acquisition;

public enum EpicBinaryEngineBackendState
{
    DirectDownloadReady,
    ExternalLauncherRequired,
    Unavailable,
}

public sealed record EpicBinaryEngineBackendStatus(
    EpicBinaryEngineBackendState State,
    string Message,
    bool SupportsInAppDownloads);

public sealed record EpicBinaryEngineRelease(
    string CatalogItemId,
    string AppName,
    string Version,
    string DisplayName);

public sealed record EpicBinaryEngineDownloadProgress(
    double? Fraction,
    string Message,
    long DownloadedBytes,
    long? TotalBytes);

public sealed record EpicBinaryEngineDownloadResult(
    EpicBinaryEngineRelease Release,
    string DestinationDirectory,
    string BuildVersion,
    int FileCount,
    long DownloadedBytes);

public sealed record EpicBinaryEngineComponentUpdateResult(
    EpicBinaryEngineRelease Release,
    string DestinationDirectory,
    string BuildVersion,
    int InstalledFileCount,
    long DownloadedBytes,
    IReadOnlyList<string> Components);

public interface IEpicBinaryEngineBackend
{
    Uri InstallationGuideUri { get; }

    Task<EpicBinaryEngineBackendStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EpicBinaryEngineRelease>> GetAvailableReleasesAsync(
        CancellationToken cancellationToken = default);

    Task<EpicBinaryEngineDownloadResult> DownloadAsync(
        EpicBinaryEngineRelease release,
        string destinationDirectory,
        IProgress<EpicBinaryEngineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<EpicBinaryEngineComponentUpdateResult> UpdateComponentsAsync(
        EpicBinaryEngineRelease release,
        string existingDirectory,
        IReadOnlyCollection<string> components,
        IProgress<EpicBinaryEngineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class OfficialEpicGamesLauncherEngineBackend : IEpicBinaryEngineBackend
{
    private const int MaximumManifestBytes = 64 * 1024 * 1024;
    private const int MaximumChunkBytes = 256 * 1024 * 1024;
    private readonly HttpClient? _httpClient;
    private readonly IDirectFabClient? _directEpicClient;
    private readonly IDirectFabSessionStore? _sessionStore;

    public OfficialEpicGamesLauncherEngineBackend()
    {
    }

    public OfficialEpicGamesLauncherEngineBackend(
        HttpClient httpClient,
        IDirectFabClient directEpicClient,
        IDirectFabSessionStore sessionStore)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _directEpicClient = directEpicClient ?? throw new ArgumentNullException(nameof(directEpicClient));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    }

    public Uri InstallationGuideUri { get; } = new(
        "https://dev.epicgames.com/documentation/en-us/unreal-engine/install-unreal-engine");

    public async Task<EpicBinaryEngineBackendStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new EpicBinaryEngineBackendStatus(
                EpicBinaryEngineBackendState.ExternalLauncherRequired,
                "Le backend direct n’est pas configuré dans ce contexte.",
                false);
        }

        var status = await _directEpicClient!.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return status.State == DirectFabConnectionState.Connected
            ? new EpicBinaryEngineBackendStatus(
                EpicBinaryEngineBackendState.DirectDownloadReady,
                "Catalogue et manifests Unreal accessibles directement avec la connexion Epic sécurisée.",
                true)
            : new EpicBinaryEngineBackendStatus(
                EpicBinaryEngineBackendState.Unavailable,
                "Connectez votre compte Epic dans Paramètres pour afficher et télécharger les versions officielles.",
                false);
    }

    public async Task<IReadOnlyList<EpicBinaryEngineRelease>> GetAvailableReleasesAsync(
        CancellationToken cancellationToken = default)
    {
        var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
        var releases = new Dictionary<string, EpicBinaryEngineRelease>(StringComparer.OrdinalIgnoreCase);
        var catalogQueries = new[]
        {
            "category=engines%2Fue5",
            "category=engines%2Fue4",
            "category=engines",
            "keywords=Unreal%20Engine%205.0",
        };
        foreach (var query in catalogQueries)
        {
            var url = "https://catalog-public-service-prod06.ol.epicgames.com/catalog/api/shared/namespace/ue/items" +
                      $"?start=0&count=100&{query}&country=US&locale=fr";
            using var document = await GetAuthorizedJsonAsync(url, session, cancellationToken)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("elements", out var elements) ||
                elements.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var element in elements.EnumerateArray())
            {
                var catalogId = OptionalString(element, "id");
                if (string.IsNullOrWhiteSpace(catalogId) ||
                    !element.TryGetProperty("releaseInfo", out var releaseInfos) ||
                    releaseInfos.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }
                foreach (var releaseInfo in releaseInfos.EnumerateArray())
                {
                    var appName = OptionalString(releaseInfo, "appId");
                    if (string.IsNullOrWhiteSpace(appName) ||
                        !appName.StartsWith("UE_", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var version = appName[3..];
                    if (!System.Version.TryParse(version, out _))
                    {
                        continue;
                    }
                    releases[appName] = new EpicBinaryEngineRelease(
                        catalogId,
                        appName,
                        version,
                        $"Unreal Engine {version}");
                }
            }
        }

        return releases.Values
            .OrderByDescending(item => ParseVersion(item.Version))
            .ToArray();
    }

    public async Task<EpicBinaryEngineDownloadResult> DownloadAsync(
        EpicBinaryEngineRelease release,
        string destinationDirectory,
        IProgress<EpicBinaryEngineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        var target = ValidateDestination(destinationDirectory);
        var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new EpicBinaryEngineDownloadProgress(
            null,
            "Récupération du manifest Unreal signé…",
            0,
            null));
        var distribution = await GetDistributionAsync(release, session, cancellationToken)
            .ConfigureAwait(false);
        var manifestBytes = await ReadBoundedBytesAsync(
                distribution.ManifestUri,
                MaximumManifestBytes,
                cancellationToken)
            .ConfigureAwait(false);
        var manifest = EpicManifestParser.Parse(manifestBytes, distribution.BaseUri);
        if (manifest.Files.Count == 0 || manifest.Chunks.Count == 0)
        {
            throw new InvalidOperationException("Le manifest Unreal officiel est vide.");
        }

        EnsureEnoughDiskSpace(target, manifest);
        var stagingRoot = target + ".cyenginehub-download";
        var stagingFiles = Path.Combine(stagingRoot, "files");
        var chunkCache = Path.Combine(stagingRoot, "chunks");
        EnsureSafeStaging(target, stagingRoot);
        Directory.CreateDirectory(stagingFiles);
        Directory.CreateDirectory(chunkCache);

        long completedBytes = 0;
        var totalBytes = Math.Max(1, manifest.DownloadSize);
        foreach (var chunk in manifest.Chunks.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cachePath = Path.Combine(chunkCache, chunk.ChunkId + ".chunk");
            if (!File.Exists(cachePath))
            {
                var bytes = await ReadBoundedBytesAsync(
                        chunk.DownloadUri,
                        MaximumChunkBytes,
                        cancellationToken,
                        received => progress?.Report(new EpicBinaryEngineDownloadProgress(
                            Math.Clamp((double)(completedBytes + received) / totalBytes * 0.74, 0, 0.74),
                            "Téléchargement des chunks Unreal", completedBytes + received, totalBytes)))
                    .ConfigureAwait(false);
                await File.WriteAllBytesAsync(cachePath, bytes.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
            }
            completedBytes = checked(completedBytes + Math.Max(0, new FileInfo(cachePath).Length));
            progress?.Report(new EpicBinaryEngineDownloadProgress(
                Math.Clamp((double)completedBytes / totalBytes * 0.74, 0, 0.74),
                $"Chunks Unreal · {completedBytes / 1024d / 1024d / 1024d:0.00} GB",
                completedBytes,
                totalBytes));
        }

        for (var index = 0; index < manifest.Files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = manifest.Files[index];
            var destination = GetSafeDestination(stagingFiles, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!EpicManifestParser.MatchesSha1(destination, file.Sha1))
            {
                var temporary = destination + ".partial";
                await using (var output = new FileStream(
                                 temporary,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 1024 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    foreach (var part in file.Parts)
                    {
                        var chunkInfo = manifest.Chunks[part.ChunkGuid];
                        var rawChunk = await File.ReadAllBytesAsync(
                                Path.Combine(chunkCache, part.ChunkGuid + ".chunk"),
                                cancellationToken)
                            .ConfigureAwait(false);
                        var chunkData = EpicManifestParser.ParseChunk(rawChunk, chunkInfo);
                        var end = checked((long)part.Offset + part.Size);
                        if (end > chunkData.LongLength)
                        {
                            throw new InvalidOperationException(
                                $"Le fragment {part.ChunkGuid} dépasse son chunk.");
                        }
                        await output.WriteAsync(
                                chunkData.AsMemory(checked((int)part.Offset), checked((int)part.Size)),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                if (!EpicManifestParser.MatchesSha1(temporary, file.Sha1))
                {
                    throw new InvalidOperationException(
                        $"L’intégrité de {file.RelativePath} est invalide après reconstruction.");
                }
                File.Move(temporary, destination, overwrite: true);
            }

            progress?.Report(new EpicBinaryEngineDownloadProgress(
                0.74 + 0.25 * (index + 1d) / manifest.Files.Count,
                $"Reconstruction · {index + 1:N0}/{manifest.Files.Count:N0} fichier(s)",
                completedBytes,
                totalBytes));
        }

        if (Directory.Exists(target))
        {
            if (Directory.EnumerateFileSystemEntries(target).Any())
            {
                throw new InvalidOperationException("Le dossier cible n’est plus vide.");
            }
            Directory.Delete(target);
        }
        Directory.Move(stagingFiles, target);
        TryDeleteCompletedStaging(stagingRoot, target);
        progress?.Report(new EpicBinaryEngineDownloadProgress(
            1,
            "Installation Unreal téléchargée et vérifiée.",
            completedBytes,
            totalBytes));
        return new EpicBinaryEngineDownloadResult(
            release,
            target,
            manifest.BuildVersion,
            manifest.Files.Count,
            completedBytes);
    }

    public async Task<EpicBinaryEngineComponentUpdateResult> UpdateComponentsAsync(
        EpicBinaryEngineRelease release,
        string existingDirectory,
        IReadOnlyCollection<string> components,
        IProgress<EpicBinaryEngineDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(components);
        var target = ValidateExistingDestination(existingDirectory);
        var requested = components
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var allowed = new HashSet<string>(
            ["source", "metahuman", "symbols", "android", "ios", "linux", "mac"],
            StringComparer.OrdinalIgnoreCase);
        if (requested.Length == 0 || requested.Any(value => !allowed.Contains(value)))
        {
            throw new ArgumentException("La sélection des composants Unreal est vide ou invalide.");
        }

        var session = await GetSessionAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new EpicBinaryEngineDownloadProgress(
            null,
            "Récupération du manifest et calcul des composants…",
            0,
            null));
        var distribution = await GetDistributionAsync(release, session, cancellationToken)
            .ConfigureAwait(false);
        var manifestBytes = await ReadBoundedBytesAsync(
                distribution.ManifestUri,
                MaximumManifestBytes,
                cancellationToken)
            .ConfigureAwait(false);
        var manifest = EpicManifestParser.Parse(manifestBytes, distribution.BaseUri);
        var selectedFiles = manifest.Files
            .Where(file => requested.Any(component => MatchesComponent(file, component)))
            .ToArray();
        if (selectedFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "Le manifest Epic ne contient aucun fichier correspondant aux composants sélectionnés.");
        }

        var selectedChunkIds = selectedFiles
            .SelectMany(file => file.Parts)
            .Select(part => part.ChunkGuid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedChunks = manifest.Chunks.Values
            .Where(chunk => selectedChunkIds.Contains(chunk.ChunkId))
            .ToArray();
        EnsureEnoughDiskSpace(target, selectedFiles, selectedChunks);

        var stagingRoot = target + ".cyenginehub-components";
        var stagingFiles = Path.Combine(stagingRoot, "files");
        var chunkCache = Path.Combine(stagingRoot, "chunks");
        EnsureSafeStaging(target, stagingRoot);
        Directory.CreateDirectory(stagingFiles);
        Directory.CreateDirectory(chunkCache);

        long completedBytes = 0;
        var totalBytes = Math.Max(1, selectedChunks.Sum(chunk => Math.Max(0, chunk.DownloadSize)));
        foreach (var chunk in selectedChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cachePath = Path.Combine(chunkCache, chunk.ChunkId + ".chunk");
            if (!File.Exists(cachePath))
            {
                var bytes = await ReadBoundedBytesAsync(
                        chunk.DownloadUri,
                        MaximumChunkBytes,
                        cancellationToken,
                        received => progress?.Report(new EpicBinaryEngineDownloadProgress(
                            Math.Clamp((double)(completedBytes + received) / totalBytes * 0.68, 0, 0.68),
                            "Téléchargement des composants Unreal", completedBytes + received, totalBytes)))
                    .ConfigureAwait(false);
                await File.WriteAllBytesAsync(cachePath, bytes.ToArray(), cancellationToken)
                    .ConfigureAwait(false);
            }
            completedBytes = checked(completedBytes + Math.Max(0, new FileInfo(cachePath).Length));
            progress?.Report(new EpicBinaryEngineDownloadProgress(
                Math.Clamp((double)completedBytes / totalBytes * 0.68, 0, 0.68),
                $"Composants Unreal · {completedBytes / 1024d / 1024d / 1024d:0.00} GB",
                completedBytes,
                totalBytes));
        }

        for (var index = 0; index < selectedFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = selectedFiles[index];
            var destination = GetSafeDestination(stagingFiles, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            if (!EpicManifestParser.MatchesSha1(destination, file.Sha1))
            {
                var temporary = destination + ".partial";
                await using (var output = new FileStream(
                                 temporary,
                                 FileMode.Create,
                                 FileAccess.Write,
                                 FileShare.None,
                                 1024 * 1024,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    foreach (var part in file.Parts)
                    {
                        var chunkInfo = manifest.Chunks[part.ChunkGuid];
                        var rawChunk = await File.ReadAllBytesAsync(
                                Path.Combine(chunkCache, part.ChunkGuid + ".chunk"),
                                cancellationToken)
                            .ConfigureAwait(false);
                        var chunkData = EpicManifestParser.ParseChunk(rawChunk, chunkInfo);
                        var end = checked((long)part.Offset + part.Size);
                        if (end > chunkData.LongLength)
                        {
                            throw new InvalidOperationException(
                                $"Le fragment {part.ChunkGuid} dépasse son chunk.");
                        }
                        await output.WriteAsync(
                                chunkData.AsMemory(checked((int)part.Offset), checked((int)part.Size)),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                if (!EpicManifestParser.MatchesSha1(temporary, file.Sha1))
                {
                    throw new InvalidOperationException(
                        $"L’intégrité de {file.RelativePath} est invalide après reconstruction.");
                }
                File.Move(temporary, destination, overwrite: true);
            }
            progress?.Report(new EpicBinaryEngineDownloadProgress(
                0.68 + 0.24 * (index + 1d) / selectedFiles.Length,
                $"Préparation · {index + 1:N0}/{selectedFiles.Length:N0} fichier(s)",
                completedBytes,
                totalBytes));
        }

        for (var index = 0; index < selectedFiles.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = selectedFiles[index];
            var source = GetSafeDestination(stagingFiles, file.RelativePath);
            var destination = GetSafeDestination(target, file.RelativePath);
            if (!EpicManifestParser.MatchesSha1(destination, file.Sha1))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var temporary = destination + ".cyenginehub-update";
                File.Copy(source, temporary, overwrite: true);
                if (!EpicManifestParser.MatchesSha1(temporary, file.Sha1))
                {
                    File.Delete(temporary);
                    throw new InvalidOperationException(
                        $"La copie vérifiée de {file.RelativePath} a échoué.");
                }
                File.Move(temporary, destination, overwrite: true);
            }
            progress?.Report(new EpicBinaryEngineDownloadProgress(
                0.92 + 0.08 * (index + 1d) / selectedFiles.Length,
                $"Installation · {index + 1:N0}/{selectedFiles.Length:N0} fichier(s)",
                completedBytes,
                totalBytes));
        }

        TryDeleteCompletedStaging(stagingRoot, target);
        progress?.Report(new EpicBinaryEngineDownloadProgress(
            1,
            "Composants Unreal installés et vérifiés.",
            completedBytes,
            totalBytes));
        return new EpicBinaryEngineComponentUpdateResult(
            release,
            target,
            manifest.BuildVersion,
            selectedFiles.Length,
            completedBytes,
            requested);
    }

    private bool IsConfigured => _httpClient is not null && _directEpicClient is not null && _sessionStore is not null;

    private async Task<DirectFabSession> GetSessionAsync(CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Le téléchargement Epic direct n’est pas configuré.");
        }
        var status = await _directEpicClient!.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        if (status.State != DirectFabConnectionState.Connected)
        {
            throw new InvalidOperationException(
                "Connectez votre compte Epic dans Paramètres avant d’accéder aux moteurs officiels.");
        }
        return await _sessionStore!.LoadAsync(cancellationToken).ConfigureAwait(false)
               ?? throw new InvalidOperationException("La session Epic sécurisée est introuvable.");
    }

    private async Task<EpicDistribution> GetDistributionAsync(
        EpicBinaryEngineRelease release,
        DirectFabSession session,
        CancellationToken cancellationToken)
    {
        var url = "https://launcher-public-service-prod06.ol.epicgames.com/launcher/api/public/assets/v2/" +
                  $"platform/Windows/namespace/ue/catalogItem/{Uri.EscapeDataString(release.CatalogItemId)}/" +
                  $"app/{Uri.EscapeDataString(release.AppName)}/label/Live";
        using var document = await GetAuthorizedJsonAsync(url, session, cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("elements", out var elements) ||
            elements.ValueKind != JsonValueKind.Array || elements.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Epic n’a retourné aucun manifest pour cette version.");
        }
        var element = elements[0];
        if (!element.TryGetProperty("manifests", out var manifests) ||
            manifests.ValueKind != JsonValueKind.Array || manifests.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("Epic n’a retourné aucun point de distribution.");
        }
        var descriptor = manifests[0];
        var rawUri = OptionalString(descriptor, "uri");
        if (!Uri.TryCreate(rawUri, UriKind.Absolute, out var manifestUri) ||
            manifestUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("L’adresse du manifest Unreal est invalide.");
        }

        var query = new List<string>();
        if (descriptor.TryGetProperty("queryParams", out var parameters) &&
            parameters.ValueKind == JsonValueKind.Array)
        {
            foreach (var parameter in parameters.EnumerateArray())
            {
                var name = OptionalString(parameter, "name");
                var value = OptionalString(parameter, "value");
                if (!string.IsNullOrWhiteSpace(name) && value is not null)
                {
                    query.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
                }
            }
        }
        var manifestBuilder = new UriBuilder(manifestUri) { Query = string.Join("&", query) };
        var slash = manifestBuilder.Path.LastIndexOf('/');
        if (slash < 0)
        {
            throw new InvalidOperationException("Le chemin de distribution Unreal est invalide.");
        }
        var baseBuilder = new UriBuilder(manifestBuilder.Uri)
        {
            Path = manifestBuilder.Path[..(slash + 1)],
        };
        return new EpicDistribution(baseBuilder.Uri, manifestBuilder.Uri);
    }

    private async Task<JsonDocument> GetAuthorizedJsonAsync(
        string url,
        DirectFabSession session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("CyEngineHub/1.0 EpicEngineConnector");
        using var response = await _httpClient!.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Epic a refusé la requête moteur ({(int)response.StatusCode}).");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ReadOnlyMemory<byte>> ReadBoundedBytesAsync(
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken,
        Action<long>? onBytes = null)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Epic a retourné une adresse non sécurisée.");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CyEngineHub/1.0 EpicEngineConnector");
        using var response = await _httpClient!.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var declared = response.Content.Headers.ContentLength;
        if (declared is > 0 && declared > maximumBytes)
        {
            throw new InvalidOperationException("Un fichier Epic dépasse la taille de sécurité autorisée.");
        }
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var output = new MemoryStream(declared is > 0 ? checked((int)declared.Value) : 0);
        var buffer = new byte[128 * 1024];
        var lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
        onBytes?.Invoke(0);
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes)
            {
                throw new InvalidOperationException("Un fichier Epic dépasse la taille de sécurité autorisée.");
            }
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            if (System.Diagnostics.Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 250)
            {
                onBytes?.Invoke(output.Length);
                lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }
        onBytes?.Invoke(output.Length);
        return output.ToArray();
    }

    private static string ValidateDestination(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim().Trim('"')));
        var parent = Path.GetDirectoryName(target)
                     ?? throw new ArgumentException("Le dossier cible doit avoir un dossier parent.");
        if (!Directory.Exists(parent))
        {
            throw new DirectoryNotFoundException(parent);
        }
        if (File.Exists(target) ||
            Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            throw new ArgumentException("Le dossier cible doit être absent ou vide.");
        }
        return target;
    }

    private static string ValidateExistingDestination(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim().Trim('"')));
        if (!Directory.Exists(target) ||
            !Directory.Exists(Path.Combine(target, "Engine")))
        {
            throw new DirectoryNotFoundException(
                "L’installation Unreal cible ou son dossier Engine est introuvable.");
        }
        if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("L’installation Unreal cible ne peut pas être un lien.");
        }
        return target;
    }

    private static void EnsureEnoughDiskSpace(string target, EpicDownloadManifest manifest)
    {
        var root = Path.GetPathRoot(target)
                   ?? throw new InvalidOperationException("Impossible de déterminer le disque cible.");
        var drive = new DriveInfo(root);
        var required = checked(manifest.DownloadSize + manifest.Files.Sum(file => file.Size));
        required = checked((long)(required * 1.05));
        if (drive.AvailableFreeSpace < required)
        {
            throw new IOException(
                $"Espace insuffisant : {required / 1024d / 1024d / 1024d:0.0} GB requis, " +
                $"{drive.AvailableFreeSpace / 1024d / 1024d / 1024d:0.0} GB disponible(s).");
        }
    }

    private static void EnsureEnoughDiskSpace(
        string target,
        IReadOnlyCollection<EpicManifestFile> files,
        IReadOnlyCollection<EpicManifestChunk> chunks)
    {
        var root = Path.GetPathRoot(target)
                   ?? throw new InvalidOperationException("Impossible de déterminer le disque cible.");
        var drive = new DriveInfo(root);
        var required = checked(chunks.Sum(chunk => Math.Max(0, chunk.DownloadSize)) +
                               files.Sum(file => Math.Max(0, file.Size)));
        required = checked((long)(required * 1.05));
        if (drive.AvailableFreeSpace < required)
        {
            throw new IOException(
                $"Espace insuffisant : {required / 1024d / 1024d / 1024d:0.0} GB requis, " +
                $"{drive.AvailableFreeSpace / 1024d / 1024d / 1024d:0.0} GB disponible(s).");
        }
    }

    private static void EnsureSafeStaging(string target, string staging)
    {
        var parent = Path.GetDirectoryName(target)!;
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var normalizedStaging = Path.GetFullPath(staging);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!normalizedStaging.StartsWith(normalizedParent + Path.DirectorySeparatorChar, comparison) ||
            (!normalizedStaging.EndsWith(".cyenginehub-download", comparison) &&
             !normalizedStaging.EndsWith(".cyenginehub-components", comparison)))
        {
            throw new InvalidOperationException("Le dossier de reprise calculé n’est pas sûr.");
        }
        if (Directory.Exists(normalizedStaging) &&
            (File.GetAttributes(normalizedStaging) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Le dossier de reprise ne peut pas être un lien.");
        }
    }

    private static string GetSafeDestination(string root, string relativePath)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var destination = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!destination.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException("Un chemin du manifest sort du dossier moteur.");
        }
        return destination;
    }

    private static void TryDeleteCompletedStaging(string stagingRoot, string target)
    {
        try
        {
            EnsureSafeStaging(target, stagingRoot);
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Le moteur final est déjà validé; conserver un cache résiduel est plus sûr qu’échouer.
        }
    }

    private static System.Version ParseVersion(string value) =>
        System.Version.TryParse(value, out var version) ? version : new System.Version(0, 0);

    private static string? OptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool MatchesComponent(EpicManifestFile file, string component)
    {
        var path = file.RelativePath.Replace('\\', '/').ToLowerInvariant();
        var tags = string.Join(' ', file.InstallTags).ToLowerInvariant();
        return component switch
        {
            "source" => path.StartsWith("engine/source/", StringComparison.Ordinal) ||
                        HasTag(tags, "source"),
            "metahuman" => path.Contains("metahuman", StringComparison.Ordinal) ||
                           HasTag(tags, "metahuman"),
            "symbols" => path.EndsWith(".pdb", StringComparison.Ordinal) ||
                         HasTag(tags, "symbol") || HasTag(tags, "debug"),
            "android" => HasPathSegment(path, "android") || HasTag(tags, "android"),
            "ios" => HasPathSegment(path, "ios") || HasTag(tags, "ios"),
            "linux" => HasPathSegment(path, "linux") || HasTag(tags, "linux"),
            "mac" => HasPathSegment(path, "mac") || HasPathSegment(path, "macos") ||
                     HasTag(tags, "mac"),
            _ => false,
        };

        static bool HasPathSegment(string path, string value) =>
            path.Contains($"/{value}/", StringComparison.Ordinal) ||
            path.StartsWith($"{value}/", StringComparison.Ordinal);
        static bool HasTag(string tags, string value) =>
            tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(tag => tag.Contains(value, StringComparison.Ordinal));
    }

    private sealed record EpicDistribution(Uri BaseUri, Uri ManifestUri);
}
