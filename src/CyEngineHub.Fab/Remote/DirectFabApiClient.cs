using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CyEngineHub.Fab.Remote;

public sealed partial class DirectFabApiClient : IDirectFabClient, IDisposable
{
    private const string LauncherClientId = "34a02cf8f4414e29b15921876da36f9a";
    private const string LauncherClientSecret = "daafbccc737745039dffe53d94fc76cf";
    private const string TokenEndpoint =
        "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token";
    private const int MaximumLibraryPages = 100;
    private const int MaximumManifestBytes = 64 * 1024 * 1024;
    private const int MaximumChunkBytes = 256 * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly IDirectFabSessionStore _sessionStore;
    private readonly SemaphoreSlim _sessionGate = new(1, 1);

    public DirectFabApiClient(HttpClient httpClient, IDirectFabSessionStore sessionStore)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
    }

    public Uri AuthorizationUri { get; } = new(
        "https://www.epicgames.com/id/login?redirectUrl=" +
        "https%3A%2F%2Fwww.epicgames.com%2Fid%2Fapi%2Fredirect%3FclientId%3D" +
        LauncherClientId + "%26responseType%3Dcode");

    public async Task<DirectFabStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _sessionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (stored is null)
        {
            return new DirectFabStatus(
                DirectFabConnectionState.Disconnected,
                "Aucun compte Epic/Fab connecté à CyEngineHub.");
        }
        if (stored.RefreshTokenExpiresAt <= DateTimeOffset.UtcNow)
        {
            return new DirectFabStatus(
                DirectFabConnectionState.Expired,
                "La session Fab a expiré. Reconnectez le compte.",
                stored.AccountId,
                stored.DisplayName);
        }

        try
        {
            var session = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            return new DirectFabStatus(
                DirectFabConnectionState.Connected,
                $"Connecté à Fab en tant que {session.DisplayName}.",
                session.AccountId,
                session.DisplayName);
        }
        catch (DirectFabException exception)
        {
            return new DirectFabStatus(
                DirectFabConnectionState.Unavailable,
                exception.Message,
                stored.AccountId,
                stored.DisplayName);
        }
    }

    public async Task<DirectFabStatus> ConnectAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default)
    {
        var code = ExtractAuthorizationCode(authorizationCode);
        var session = await ExchangeTokenAsync(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["token_type"] = "eg1",
                },
                cancellationToken)
            .ConfigureAwait(false);
        await _sessionStore.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        return new DirectFabStatus(
            DirectFabConnectionState.Connected,
            $"Connecté à Fab en tant que {session.DisplayName}.",
            session.AccountId,
            session.DisplayName);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        _sessionStore.ClearAsync(cancellationToken);

    public async Task<IReadOnlyList<RemoteFabAsset>> GetLibraryAsync(
        CancellationToken cancellationToken = default)
    {
        var session = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        var assets = new Dictionary<string, RemoteFabAsset>(StringComparer.OrdinalIgnoreCase);
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < MaximumLibraryPages; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = new StringBuilder("https://www.fab.com/e/accounts/")
                .Append(Uri.EscapeDataString(session.AccountId))
                .Append("/ue/library?count=100");
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                url.Append("&cursor=").Append(Uri.EscapeDataString(cursor));
            }

            using var document = await GetAuthorizedJsonAsync(
                    url.ToString(),
                    session,
                    cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("results", out var results) &&
                results.ValueKind == JsonValueKind.Array)
            {
                foreach (var result in results.EnumerateArray())
                {
                    var asset = ParseLibraryAsset(result);
                    if (asset is not null) assets[asset.AssetId] = asset;
                }
            }

            cursor = document.RootElement.TryGetProperty("cursors", out var cursors) &&
                     cursors.ValueKind == JsonValueKind.Object &&
                     cursors.TryGetProperty("next", out var next) &&
                     next.ValueKind == JsonValueKind.String
                ? next.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor)) break;
        }

        return assets.Values
            .OrderBy(asset => asset.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public async Task<RemoteFabProductDetails> GetProductDetailsAsync(
        RemoteFabAsset asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var session = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        var listingId = asset.ListingUri is { Segments.Length: > 0 } listingUri
            ? listingUri.Segments[^1].Trim('/')
            : asset.AssetId;
        using var document = await GetAuthorizedJsonAsync(
                $"https://www.fab.com/i/listings/{Uri.EscapeDataString(listingId)}",
                session,
                cancellationToken)
            .ConfigureAwait(false);
        var descriptions = new List<string>();
        CollectNamedStrings(
            document.RootElement,
            descriptions,
            0,
            "description",
            "longDescription",
            "body");
        var rawDescription = descriptions
            .OrderByDescending(value => value.Length)
            .FirstOrDefault() ?? asset.RichDescription ?? asset.Description;
        var images = new List<Uri>(asset.GalleryUris);
        CollectFabMediaUris(document.RootElement, images, 0);
        return new RemoteFabProductDetails(
            NormalizeRichText(rawDescription),
            images
                .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .Take(32)
                .ToArray()) { RichDescription = rawDescription };
    }

    public async Task<DirectFabDownloadEstimate?> EstimateDownloadAsync(RemoteFabAsset asset, RemoteFabProjectVersion version,
        CancellationToken cancellationToken = default)
    {
        var session = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        var distribution = await GetDistributionAsync(asset, version, session, cancellationToken).ConfigureAwait(false);
        var bytes = await ReadBoundedBytesAsync(distribution.ManifestUri, MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
        var manifest = EpicManifestParser.Parse(bytes, distribution.BaseUri);
        if (manifest.Files.Count == 0 || manifest.Chunks.Count == 0) throw new DirectFabException("Manifest vide : taille inconnue.");
        return new(manifest.DownloadSize, manifest.Files.Sum(file => file.Size), manifest.Files.Count);
    }

    public async Task<DirectFabSourceManifest?> GetSourceManifestAsync(RemoteFabAsset asset, RemoteFabProjectVersion version,
        CancellationToken cancellationToken = default)
    {
        var session = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        var distribution = await GetDistributionAsync(asset, version, session, cancellationToken).ConfigureAwait(false);
        var bytes = await ReadBoundedBytesAsync(distribution.ManifestUri, MaximumManifestBytes, cancellationToken).ConfigureAwait(false);
        var manifest = EpicManifestParser.Parse(bytes, distribution.BaseUri);
        if (manifest.Files.Count == 0) throw new DirectFabException("Manifest Fab vide : révision non vérifiable.");
        return FabPackageRevision.FromManifest(manifest);
    }

    public async Task<DirectFabDownloadResult> DownloadAsync(
        RemoteFabAsset asset,
        string libraryRoot,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var projectVersion = asset.PreferredProjectVersion ??
            throw new DirectFabException(
                "Cet élément Fab n’expose pas de version téléchargeable par manifest Epic.");
        return await DownloadAsync(
                asset,
                projectVersion,
                libraryRoot,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DirectFabDownloadResult> DownloadAsync(
        RemoteFabAsset asset,
        RemoteFabProjectVersion projectVersion,
        string libraryRoot,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(projectVersion);
        if (string.IsNullOrWhiteSpace(projectVersion.ArtifactId))
        {
            throw new DirectFabException("L’élément Fab ne fournit aucun identifiant d’artefact.");
        }

        var root = NormalizeLibraryRoot(libraryRoot);
        var session = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report(new DirectFabDownloadProgress(null, "Récupération du manifest signé…", 0, null));
        var distribution = await GetDistributionAsync(
                asset,
                projectVersion,
                session,
                cancellationToken)
            .ConfigureAwait(false);
        var manifestBytes = await ReadBoundedBytesAsync(
                distribution.ManifestUri,
                MaximumManifestBytes,
                cancellationToken)
            .ConfigureAwait(false);
        var manifest = EpicManifestParser.Parse(manifestBytes, distribution.BaseUri);
        if (manifest.Files.Count == 0 || manifest.Chunks.Count == 0)
        {
            throw new DirectFabException("Le manifest Fab ne contient aucun fichier téléchargeable.");
        }

        var shortId = asset.AssetId[..Math.Min(8, asset.AssetId.Length)];
        var stableName = $"{SanitizeFileName(asset.Title)}-{shortId}";
        var stagingRoot = Path.Combine(root, ".cyenginehub-downloads", SanitizeFileName(asset.AssetId));
        var stagingFiles = Path.Combine(stagingRoot, "files");
        var chunkCache = Path.Combine(stagingRoot, "chunks");
        Directory.CreateDirectory(stagingFiles);
        Directory.CreateDirectory(chunkCache);

        var totalBytes = Math.Max(1, manifest.DownloadSize);
        var (completedBytes, cachedBytes) = await new EpicChunkTransfer(_httpClient,
            Volatile.Read(ref _downloadConnections), "Fab", ChunkRetryDelayAsync)
            .DownloadAsync(manifest.Chunks.Values.ToArray(), chunkCache, totalBytes, 0.72, progress, cancellationToken)
            .ConfigureAwait(false);

        var fileIndex = 0;
        var lastReconstructionReport = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                                 128 * 1024,
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
                            throw new DirectFabException(
                                $"Le fragment {part.ChunkGuid} dépasse les données du chunk.");
                        }
                        await output.WriteAsync(
                                chunkData.AsMemory(checked((int)part.Offset), checked((int)part.Size)),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                if (!EpicManifestParser.MatchesSha1(temporary, file.Sha1))
                {
                    EpicChunkTransfer.InvalidateFileChunks(chunkCache, file);
                    throw new DirectFabException(
                        $"L’intégrité du fichier {file.RelativePath} est invalide après reconstruction.");
                }
                File.Move(temporary, destination, overwrite: true);
            }

            fileIndex++;
            if (fileIndex != 1 && fileIndex != manifest.Files.Count &&
                System.Diagnostics.Stopwatch.GetElapsedTime(lastReconstructionReport).TotalMilliseconds < 250) continue;
            lastReconstructionReport = System.Diagnostics.Stopwatch.GetTimestamp();
            progress?.Report(new DirectFabDownloadProgress(
                0.72 + 0.27 * fileIndex / manifest.Files.Count,
                $"Reconstruction · {fileIndex:N0}/{manifest.Files.Count:N0} fichier(s)",
                completedBytes,
                totalBytes) { CachedBytes = cachedBytes });
        }

        var fabRoot = Path.Combine(root, "Fab");
        Directory.CreateDirectory(fabRoot);
        var finalDirectory = GetUniqueDirectory(Path.Combine(fabRoot, stableName));
        Directory.Move(stagingFiles, finalDirectory);
        TryRemoveOwnedStaging(stagingRoot, root);
        progress?.Report(new DirectFabDownloadProgress(
            1,
            "Téléchargement Fab terminé et vérifié.",
            completedBytes,
            totalBytes) { CachedBytes = cachedBytes });
        return new DirectFabDownloadResult(
            asset.AssetId,
            asset.Title,
            finalDirectory,
            manifest.Files.Count,
            completedBytes,
            DateTimeOffset.UtcNow,
            FabPackageRevision.FromManifest(manifest));
    }

    private async Task<DirectFabSession> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        await _sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var session = await _sessionStore.LoadAsync(cancellationToken).ConfigureAwait(false) ??
                throw new DirectFabException("Connectez d’abord votre compte Epic/Fab.");
            var now = DateTimeOffset.UtcNow;
            if (session.AccessTokenExpiresAt > now.AddMinutes(2)) return session;
            if (session.RefreshTokenExpiresAt <= now)
            {
                throw new DirectFabException("La session Fab a expiré. Reconnectez le compte.");
            }
            var refreshed = await ExchangeTokenAsync(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = session.RefreshToken,
                        ["token_type"] = "eg1",
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            await _sessionStore.SaveAsync(refreshed, cancellationToken).ConfigureAwait(false);
            return refreshed;
        }
        finally
        {
            _sessionGate.Release();
        }
    }

    private async Task<DirectFabSession> ExchangeTokenAsync(
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(fields),
        };
        var basic = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($"{LauncherClientId}:{LauncherClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        request.Headers.UserAgent.ParseAdd("CyEngineHub/1.0 FabConnector");
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DirectFabException(
                response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                    ? "Le code Epic est invalide ou expiré. Générez-en un nouveau."
                    : $"Epic a refusé la connexion ({(int)response.StatusCode}).");
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var accessToken = RequiredString(root, "access_token");
            var refreshToken = RequiredString(root, "refresh_token");
            var accountId = RequiredString(root, "account_id");
            var displayName = OptionalString(root, "displayName") ?? accountId;
            var accessExpiry = ParseExpiry(root, "expires_at", "expires_in", TimeSpan.FromHours(8));
            var refreshExpiry = ParseExpiry(
                root,
                "refresh_expires_at",
                "refresh_expires",
                TimeSpan.FromDays(30));
            return new DirectFabSession(
                accountId,
                displayName,
                accessToken,
                accessExpiry,
                refreshToken,
                refreshExpiry);
        }
        catch (JsonException exception)
        {
            throw new DirectFabException("La réponse d’authentification Epic est invalide.", exception);
        }
    }

    private async Task<JsonDocument> GetAuthorizedJsonAsync(
        string url,
        DirectFabSession session,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("CyEngineHub/1.0 FabConnector");
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DirectFabException(
                response.StatusCode == HttpStatusCode.Unauthorized
                    ? "La session Epic/Fab n’est plus autorisée. Reconnectez le compte."
                    : $"Impossible de lire la bibliothèque Fab ({(int)response.StatusCode}).");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<FabDistribution> GetDistributionAsync(
        RemoteFabAsset asset,
        RemoteFabProjectVersion version,
        DirectFabSession session,
        CancellationToken cancellationToken)
    {
        var url = $"https://www.fab.com/e/artifacts/{Uri.EscapeDataString(version.ArtifactId)}/manifest";
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    item_id = asset.AssetId,
                    @namespace = asset.Namespace,
                     platform = version.Platforms.Count > 0 ? version.Platforms[0] : "Windows",
                }),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.UserAgent.ParseAdd("CyEngineHub/1.0 FabConnector");
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DirectFabException(
                response.StatusCode == HttpStatusCode.Forbidden
                    ? "Fab n’a pas généré le manifest à temps. Réessayez dans quelques secondes."
                    : $"Impossible d’obtenir le manifest Fab ({(int)response.StatusCode}).");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("downloadInfo", out var infos) ||
            infos.ValueKind != JsonValueKind.Array)
        {
            throw new DirectFabException("Fab n’a retourné aucune information de téléchargement.");
        }
        foreach (var info in infos.EnumerateArray())
        {
            var baseUris = info.TryGetProperty("distributionPointBaseUrls", out var bases) &&
                           bases.ValueKind == JsonValueKind.Array
                ? bases.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => TryHttpsUri(item.GetString()))
                    .OfType<Uri>()
                    .ToArray()
                : [];
            if (!info.TryGetProperty("distributionPoints", out var points) ||
                points.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var point in points.EnumerateArray())
            {
                var manifestUri = point.TryGetProperty("manifestUrl", out var manifestValue)
                    ? TryHttpsUri(manifestValue.GetString())
                    : null;
                var expiry = point.TryGetProperty("signatureExpiration", out var expiryValue) &&
                             expiryValue.ValueKind == JsonValueKind.String &&
                             DateTimeOffset.TryParse(
                                 expiryValue.GetString(),
                                 CultureInfo.InvariantCulture,
                                 DateTimeStyles.AssumeUniversal,
                                 out var parsedExpiry)
                    ? parsedExpiry
                    : DateTimeOffset.MaxValue;
                if (manifestUri is null || expiry <= DateTimeOffset.UtcNow) continue;
                var baseUri = baseUris.FirstOrDefault(candidate =>
                    manifestUri.AbsoluteUri.StartsWith(candidate.AbsoluteUri, StringComparison.OrdinalIgnoreCase));
                if (baseUri is not null) return new FabDistribution(baseUri, manifestUri);
            }
        }
        throw new DirectFabException("Aucun point de distribution Fab HTTPS valide n’est disponible.");
    }

    private async Task<ReadOnlyMemory<byte>> ReadBoundedBytesAsync(
        Uri uri,
        int maximumBytes,
        CancellationToken cancellationToken,
        Action<long>? onBytes = null)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new DirectFabException("Fab a retourné une adresse de téléchargement non sécurisée.");
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("CyEngineHub/1.0 FabConnector");
        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new DirectFabException(
                $"Échec du téléchargement Fab ({(int)response.StatusCode}).");
        }
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new DirectFabException("Le téléchargement Fab dépasse la limite de sécurité.");
        }
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = new byte[128 * 1024];
        var lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
        onBytes?.Invoke(0);
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (destination.Length + read > maximumBytes)
            {
                throw new DirectFabException("Le téléchargement Fab dépasse la limite de sécurité.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                .ConfigureAwait(false);
            if (System.Diagnostics.Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 250)
            {
                onBytes?.Invoke(destination.Length);
                lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
            }
        }
        onBytes?.Invoke(destination.Length);
        return destination.ToArray();
    }

    private static RemoteFabAsset? ParseLibraryAsset(JsonElement element)
    {
        var assetId = OptionalString(element, "assetId");
        var assetNamespace = OptionalString(element, "assetNamespace");
        var title = OptionalString(element, "title");
        if (string.IsNullOrWhiteSpace(assetId) ||
            string.IsNullOrWhiteSpace(assetNamespace) ||
            string.IsNullOrWhiteSpace(title))
        {
            return null;
        }
        var categories = element.TryGetProperty("categories", out var categoryArray) &&
                         categoryArray.ValueKind == JsonValueKind.Array
            ? categoryArray.EnumerateArray()
                .Select(category => OptionalString(category, "name") ?? OptionalString(category, "id"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray()
            : [];
        Uri[] imageUris = [];
        if (element.TryGetProperty("images", out var images) && images.ValueKind == JsonValueKind.Array)
        {
            imageUris = images.EnumerateArray()
                .Select(image => TryHttpsUri(OptionalString(image, "url")))
                .Where(uri => uri is not null)
                .Cast<Uri>()
                .DistinctBy(uri => uri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        var thumbnail = imageUris.FirstOrDefault();
        var versions = new List<RemoteFabProjectVersion>();
        if (element.TryGetProperty("projectVersions", out var projectVersions) &&
            projectVersions.ValueKind == JsonValueKind.Array)
        {
            foreach (var version in projectVersions.EnumerateArray())
            {
                var artifactId = OptionalString(version, "artifactId");
                if (string.IsNullOrWhiteSpace(artifactId)) continue;
                var builds = version.TryGetProperty("buildVersions", out var buildVersions) &&
                             buildVersions.ValueKind == JsonValueKind.Array
                    ? buildVersions.EnumerateArray()
                        .Select(build => new RemoteFabBuildVersion(
                            OptionalString(build, "buildVersion") ?? string.Empty,
                            OptionalString(build, "platform") ?? "Windows"))
                        .ToArray()
                    : [];
                versions.Add(new RemoteFabProjectVersion(
                    artifactId,
                    builds,
                    ReadStringArray(version, "engineVersions"),
                    ReadStringArray(version, "targetPlatforms")));
            }
        }
        var rawUri = OptionalString(element, "url");
        Uri? listingUri = TryHttpsUri(rawUri);
        if (listingUri is null && !string.IsNullOrWhiteSpace(rawUri))
        {
            listingUri = Uri.TryCreate(new Uri("https://www.fab.com/"), rawUri, out var combined)
                ? combined
                : null;
        }
        return new RemoteFabAsset(
            assetId,
            assetNamespace,
            title,
            MarketplaceRichText.ToPlainText(OptionalString(element, "description")),
            OptionalString(element, "source") ?? "Fab",
            OptionalString(element, "distributionMethod") ?? string.Empty,
            listingUri,
            thumbnail,
            categories,
            versions,
            imageUris,
            ReadOptionalDate(element, "acquiredAt", "acquisitionDate", "createdAt", "entitledAt"))
            { RichDescription = OptionalString(element, "description") };
    }

    private static DateTimeOffset? ReadOptionalDate(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var raw = OptionalString(element, name);
            if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    private static string ExtractAuthorizationCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new DirectFabException("Collez le code d’autorisation Epic.");
        }
        var trimmed = input.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                trimmed = OptionalString(document.RootElement, "authorizationCode") ??
                          OptionalString(document.RootElement, "code") ??
                          string.Empty;
            }
            catch (JsonException exception)
            {
                throw new DirectFabException("La réponse d’autorisation Epic est invalide.", exception);
            }
        }
        trimmed = trimmed.Trim().Trim('"');
        if (trimmed.Length is < 8 or > 4096 || trimmed.Any(char.IsWhiteSpace))
        {
            throw new DirectFabException("Le code d’autorisation Epic est invalide.");
        }
        return trimmed;
    }

    private static void CollectNamedStrings(
        JsonElement element,
        List<string> results,
        int depth,
        params string[] propertyNames)
    {
        if (depth > 16 || results.Count >= 128)
        {
            return;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    results.Add(property.Value.GetString()!);
                }
                CollectNamedStrings(property.Value, results, depth + 1, propertyNames);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectNamedStrings(item, results, depth + 1, propertyNames);
            }
        }
    }

    private static void CollectFabMediaUris(JsonElement element, List<Uri> images, int depth)
    {
        if (depth > 12 || images.Count >= 64)
        {
            return;
        }
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    (property.Name.Contains("url", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Contains("image", StringComparison.OrdinalIgnoreCase) ||
                     property.Name.Contains("src", StringComparison.OrdinalIgnoreCase)) &&
                    TryHttpsUri(property.Value.GetString()) is { } uri &&
                    (uri.Host.Equals("media.fab.com", StringComparison.OrdinalIgnoreCase) ||
                     uri.Host.EndsWith(".media.fab.com", StringComparison.OrdinalIgnoreCase)))
                {
                    images.Add(uri);
                }
                CollectFabMediaUris(property.Value, images, depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectFabMediaUris(item, images, depth + 1);
            }
        }
    }

    private static string NormalizeRichText(string value) => MarketplaceRichText.ToPlainText(value);

    private static string NormalizeLibraryRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DirectFabException("Choisissez le dossier de téléchargement de la bibliothèque.");
        }
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim().Trim('"')));
        Directory.CreateDirectory(path);
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DirectFabException("Le dossier de bibliothèque ne peut pas être un lien ou point de réanalyse.");
        }
        return path;
    }

    private static string GetSafeDestination(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new DirectFabException("Le manifest Fab contient un chemin de fichier invalide.");
        }
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var destination = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new DirectFabException("Le manifest Fab tente d’écrire hors du dossier de téléchargement.");
        }
        return destination;
    }

    private static string GetUniqueDirectory(string candidate)
    {
        if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        for (var index = 2; index < 10_000; index++)
        {
            var alternate = candidate + $"-{index:N0}";
            if (!Directory.Exists(alternate) && !File.Exists(alternate)) return alternate;
        }
        throw new DirectFabException("Impossible de réserver un dossier unique pour le téléchargement Fab.");
    }

    private static void TryRemoveOwnedStaging(string stagingRoot, string libraryRoot)
    {
        try
        {
            var normalizedLibrary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryRoot));
            var normalizedStaging = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot));
            var expectedPrefix = Path.Combine(normalizedLibrary, ".cyenginehub-downloads") +
                                 Path.DirectorySeparatorChar;
            if (normalizedStaging.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(normalizedStaging))
            {
                Directory.Delete(normalizedStaging, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Les chunks restent récupérables et seront réutilisés au prochain téléchargement.
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value
            .Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character)
            .ToArray())
            .Trim(' ', '.');
        return string.IsNullOrWhiteSpace(sanitized)
            ? "FabAsset"
            : sanitized[..Math.Min(96, sanitized.Length)];
    }

    private static DateTimeOffset ParseExpiry(
        JsonElement root,
        string absoluteName,
        string secondsName,
        TimeSpan fallback)
    {
        if (root.TryGetProperty(absoluteName, out var absolute) &&
            absolute.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                absolute.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed))
        {
            return parsed;
        }
        if (root.TryGetProperty(secondsName, out var seconds) && seconds.TryGetInt64(out var value))
        {
            return DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, value));
        }
        return DateTimeOffset.UtcNow.Add(fallback);
    }

    private static string RequiredString(JsonElement root, string name) =>
        OptionalString(root, name) ??
        throw new DirectFabException($"La réponse Epic ne contient pas le champ {name}.");

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[] ReadStringArray(JsonElement root, string name) =>
        root.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray()
            : [];

    private static Uri? TryHttpsUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : null;

    private sealed record FabDistribution(Uri BaseUri, Uri ManifestUri);

    public void Dispose()
    {
        _sessionGate.Dispose();
    }
}
