using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyEngineHub.Fab.Remote;

public sealed partial class QuixelMegascansApiClient : IQuixelMegascansClient
{
    private const string EpicClientId = "b9101103b8814baa9bb4e79e5eb107d0";
    private const string AccountsBase = "https://accounts.quixel.com/api";
    private const string QuixelBase = "https://quixel.com";
    private const string AlgoliaApplicationId = "6UJ1I5A072";
    private readonly string? _algoliaSearchKey;
    public bool IsCatalogSearchConfigured => !string.IsNullOrWhiteSpace(_algoliaSearchKey);
    private string AlgoliaSearchKey => !string.IsNullOrWhiteSpace(_algoliaSearchKey)
        ? _algoliaSearchKey
        : throw new QuixelException("La recherche du catalogue Megascans est désactivée : aucune clé autorisée n'est configurée. Consultez la documentation de configuration locale.");
    private const int AlgoliaBatchSize = 250;
    private const int MaximumAcquisitions = 100_000;
    private const long MaximumSingleFileBytes = 16L * 1024 * 1024 * 1024;
    private static readonly int[] RawResolutions = [4096, 8192, 2048, 1024];
    private static readonly string[] PreferredRawComponentTypes =
    [
        "albedo",
        "diffuse",
        "normal",
        "normalbump",
        "displacement",
        "ao",
        "metalness",
        "roughness",
        "opacity",
        "fuzz",
        "translucency",
        "transmission",
    ];
    private static readonly Uri EpicExchangeUri = new(
        "https://account-public-service-prod.ol.epicgames.com/account/api/oauth/exchange");
    private readonly HttpClient _httpClient;
    private readonly IQuixelSessionStore _sessionStore;
    private readonly IDirectFabSessionStore _epicSessionStore;

    public QuixelMegascansApiClient(
        HttpClient httpClient,
        IQuixelSessionStore sessionStore,
        IDirectFabSessionStore epicSessionStore,
        string? algoliaSearchKey = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _epicSessionStore = epicSessionStore ?? throw new ArgumentNullException(nameof(epicSessionStore));
        _algoliaSearchKey = (algoliaSearchKey ?? Environment.GetEnvironmentVariable("CYENGINEHUB_QUIXEL_ALGOLIA_KEY"))?.Trim();
    }

    public Uri AuthorizationUri { get; } = new(
        "https://www.epicgames.com/id/login?redirectUrl=" +
        Uri.EscapeDataString($"https://www.epicgames.com/id/api/redirect?clientId={EpicClientId}&responseType=code"));

    public async Task<QuixelStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var session = await _sessionStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return new QuixelStatus(QuixelConnectionState.Disconnected, "Compte Epic/Quixel non connecté.");
        }
        try
        {
            session = await EnsureSessionAsync(session, cancellationToken).ConfigureAwait(false);
            using var response = await SendAuthorizedAsync(
                    HttpMethod.Get,
                    new Uri($"{QuixelBase}/v1/users/self"),
                    session,
                    content: null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new QuixelStatus(
                    response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? QuixelConnectionState.Expired
                        : QuixelConnectionState.Unavailable,
                    await ReadErrorAsync(response, "Quixel a refusé la vérification du compte.", cancellationToken)
                        .ConfigureAwait(false),
                    session.AccountId,
                    session.DisplayName);
            }
            return ConnectedStatus(session, "Session Megascans historique active.");
        }
        catch (QuixelException exception)
        {
            return new QuixelStatus(
                QuixelConnectionState.Unavailable,
                exception.Message,
                session.AccountId,
                session.DisplayName);
        }
    }

    public Task<QuixelStatus> ConnectAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default) =>
        ExchangeQuixelCodeAsync(ExtractAuthorizationCode(authorizationCode), cancellationToken);

    public async Task<QuixelStatus> ConnectFromEpicSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var epicSession = await _epicSessionStore.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new QuixelException(
                "Connectez d’abord Epic/Fab dans CyEngineHub, ou utilisez l’autorisation Quixel officielle.");
        if (epicSession.AccessTokenExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            throw new QuixelException(
                "La session Epic/Fab doit être actualisée avant de pouvoir tenter son échange vers Quixel.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, EpicExchangeUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", epicSession.AccessToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new QuixelException(
                await ReadErrorAsync(response, "Epic n’a pas fourni de code d’échange.", cancellationToken)
                    .ConfigureAwait(false));
        }
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        var code = ReadString(document.RootElement, "code")
            ?? throw new QuixelException("La réponse Epic ne contient aucun code d’échange.");
        return await ExchangeQuixelCodeAsync(code, cancellationToken).ConfigureAwait(false);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default) =>
        _sessionStore.ClearAsync(cancellationToken);

    public async Task<QuixelLibraryResult> GetLibraryAsync(
        CancellationToken cancellationToken = default)
    {
        _ = AlgoliaSearchKey; // Fail before reading sessions or sending requests when disabled.
        var session = await RequireSessionAsync(cancellationToken).ConfigureAwait(false);
        using var response = await SendAuthorizedAsync(
                HttpMethod.Get,
                new Uri($"{QuixelBase}/v1/assets/acquired"),
                session,
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Impossible de lire les acquisitions Megascans.", cancellationToken)
            .ConfigureAwait(false);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        var acquisitionIds = ParseAcquisitionIds(document.RootElement)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaximumAcquisitions)
            .ToArray();
        var assets = new List<RemoteFabAsset>(acquisitionIds.Length);
        foreach (var batch in acquisitionIds.Chunk(AlgoliaBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            assets.AddRange(await SearchAssetsAsync(batch, cancellationToken).ConfigureAwait(false));
        }

        var uniqueAssets = assets
            .GroupBy(asset => asset.AssetId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(asset => asset.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new QuixelLibraryResult(
            acquisitionIds.Length,
            uniqueAssets,
            Math.Max(0, acquisitionIds.Length - uniqueAssets.Length));
    }

    public async Task<RemoteFabProductDetails> GetProductDetailsAsync(
        RemoteFabAsset asset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        var session = await RequireSessionAsync(cancellationToken).ConfigureAwait(false);
        using var response = await SendAuthorizedAsync(
                HttpMethod.Get,
                new Uri($"{QuixelBase}/v1/assets/{Uri.EscapeDataString(asset.AssetId)}"),
                session,
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "La fiche Megascans est indisponible.", cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("asset", out var nested)) root = nested;
        var description = ReadFirstString(root, "description", "about", "summary") ?? asset.Description;
        var images = CollectImageUris(root).ToArray();
        return new RemoteFabProductDetails(
            description,
            images.Length == 0 ? asset.GalleryUris : images);
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
        var session = await RequireSessionAsync(cancellationToken).ConfigureAwait(false);
        var request = projectVersion.ArtifactId.StartsWith("quixel-raw:", StringComparison.OrdinalIgnoreCase)
            ? await BuildRawDownloadRequestAsync(asset, projectVersion.ArtifactId, session, cancellationToken)
                .ConfigureAwait(false)
            : BuildUAssetDownloadRequest(asset, projectVersion.ArtifactId);
        using var response = await SendAuthorizedAsync(
                HttpMethod.Post,
                request.Endpoint,
                session,
                JsonContent.Create(request.Payload),
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Quixel n’a pas fourni le téléchargement demandé.", cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        var urls = CollectHttpsUris(document.RootElement).Distinct().Take(2_000).ToArray();
        if (urls.Length == 0)
        {
            throw new QuixelException("La réponse Quixel ne contient aucun fichier téléchargeable.");
        }

        var root = PrepareLibraryRoot(libraryRoot);
        var finalDirectory = ReserveDirectory(Path.Combine(
            root,
            "Megascans",
            MakeSafeName(asset.Title, asset.AssetId),
            request.DirectoryName));
        var stagingDirectory = finalDirectory + ".partial-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingDirectory);
        long totalDownloaded = 0;
        var fileCount = 0;
        try
        {
            for (var index = 0; index < urls.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var uri = urls[index];
                progress?.Report(new DirectFabDownloadProgress(
                    (double)index / urls.Length,
                    $"Fichier {index + 1:N0}/{urls.Length:N0}",
                    totalDownloaded,
                    null));
                using var fileResponse = await _httpClient.GetAsync(
                        uri,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken)
                    .ConfigureAwait(false);
                await EnsureSuccessAsync(fileResponse, "Échec d’un fichier Megascans signé.", cancellationToken)
                    .ConfigureAwait(false);
                var announced = fileResponse.Content.Headers.ContentLength;
                if (announced > MaximumSingleFileBytes)
                {
                    throw new QuixelException("Un fichier Megascans dépasse la limite de sécurité de 16 Gio.");
                }
                var target = Path.Combine(stagingDirectory, MakeSafeFileName(uri, index));
                await using var source = await fileResponse.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using var output = new FileStream(
                    target,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1024 * 128,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                var buffer = new byte[1024 * 128];
                long fileBytes = 0;
                var lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
                void ReportTransfer()
                {
                    var ratio = announced > 0 ? Math.Clamp((double)fileBytes / announced.Value, 0, 1) : 0;
                    var fileStatus = announced > 0 ? $" · {ratio * 100:N1} % du fichier" : " · taille du fichier inconnue";
                    // Earlier URLs have no known sizes: never extrapolate a total from file count.
                    long? knownTotal = index == urls.Length - 1 && announced > 0 ? totalDownloaded + announced.Value : null;
                    progress?.Report(new DirectFabDownloadProgress((index + ratio) / urls.Length,
                        $"Fichier {index + 1:N0}/{urls.Length:N0}{fileStatus}", totalDownloaded + fileBytes, knownTotal));
                }
                ReportTransfer();
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    fileBytes += read;
                    if (fileBytes > MaximumSingleFileBytes)
                    {
                        throw new QuixelException("Un fichier Megascans dépasse la limite de sécurité de 16 Gio.");
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    if (System.Diagnostics.Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 250)
                    {
                        ReportTransfer();
                        lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
                    }
                }
                ReportTransfer();
                totalDownloaded += fileBytes;
                fileCount++;
            }
            Directory.Move(stagingDirectory, finalDirectory);
        }
        catch
        {
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
            throw;
        }

        progress?.Report(new DirectFabDownloadProgress(
            1,
            "Téléchargement Megascans terminé.",
            totalDownloaded,
            totalDownloaded));
        return new DirectFabDownloadResult(
            asset.AssetId,
            $"{asset.Title} · {request.DisplayName}",
            finalDirectory,
            fileCount,
            totalDownloaded,
            DateTimeOffset.UtcNow);
    }

    private static QuixelDownloadRequest BuildUAssetDownloadRequest(RemoteFabAsset asset, string artifactId)
    {
        var tier = ParseTier(artifactId);
        return new QuixelDownloadRequest(
            new Uri($"{QuixelBase}/v1/downloads/uasset"),
            new { asset = asset.AssetId, tiers = new[] { tier } },
            $"Tier-{tier}",
            $"UAsset · Tier {tier}");
    }

    private async Task<QuixelDownloadRequest> BuildRawDownloadRequestAsync(
        RemoteFabAsset asset,
        string artifactId,
        QuixelSession session,
        CancellationToken cancellationToken)
    {
        var raw = ParseRawVariant(artifactId, asset.AssetId);
        using var detailsResponse = await SendAuthorizedAsync(
                HttpMethod.Get,
                new Uri($"{QuixelBase}/v1/assets/{Uri.EscapeDataString(asset.AssetId)}"),
                session,
                content: null,
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(
                detailsResponse,
                "Les métadonnées RAW de cet asset Megascans sont indisponibles.",
                cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(
            await detailsResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("asset", out var nested)) root = nested;

        var assetType = ReadAssetType(root, asset.Categories);
        var version = ReadInt(root, "version") ?? 1;
        var isModel = assetType.StartsWith("3d", StringComparison.OrdinalIgnoreCase);
        if (!isModel && raw.Format is not "textures")
        {
            throw new QuixelException("Cet asset Megascans ne contient pas de maillage RAW.");
        }

        var componentTypes = ReadAvailableComponentTypes(root);
        var components = PreferredRawComponentTypes
            .Where(type => componentTypes.Count == 0 || componentTypes.Contains(type))
            .Select(type => new RawComponentRequest(
                type,
                $"{raw.Resolution}x{raw.Resolution}",
                "image/jpeg"))
            .ToArray();
        var payload = new Dictionary<string, object?>
        {
            ["asset"] = asset.AssetId,
        };
        if (components.Length > 0) payload["components"] = components;

        var meshMimeType = raw.Format switch
        {
            "fbx" => "application/x-fbx",
            "obj" => "application/x-obj",
            _ => "application/x-fbx",
        };
        if (isModel && version == 2)
        {
            var lods = ReadAvailableLods(root);
            payload["meshes"] = lods
                .Select(lod => new RawMeshRequest(meshMimeType, lod))
                .ToArray();
        }
        else
        {
            payload["config"] = new RawLegacyConfig(
                Highpoly: false,
                Ztool: false,
                LowerlodNormals: false,
                AlbedoLods: false,
                MeshMimeType: meshMimeType,
                Brushes: false,
                Lods: isModel ? ReadAvailableTriangleCounts(root) : []);
        }

        var resolutionLabel = FormatRawResolution(raw.Resolution);
        var formatLabel = raw.Format == "textures"
            ? "Textures"
            : $"{raw.Format.ToUpperInvariant()} + textures";
        return new QuixelDownloadRequest(
            new Uri($"{QuixelBase}/v1/downloads"),
            payload,
            $"RAW-{resolutionLabel}-{raw.Format.ToUpperInvariant()}",
            $"RAW {resolutionLabel} · {formatLabel}");
    }

    private async Task<QuixelStatus> ExchangeQuixelCodeAsync(
        string code,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
                new Uri($"{AccountsBase}/v1/login/epic"),
                new { code, realm = "megascans" },
                cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = await ReadErrorAsync(
                    response,
                    "La connexion Epic → Quixel a été refusée.",
                    cancellationToken)
                .ConfigureAwait(false);
            if (message.Contains("age requirements", StringComparison.OrdinalIgnoreCase))
            {
                message =
                    "Epic refuse l’accès Quixel car les exigences d’âge du compte ne sont pas validées. " +
                    "Vérifiez la date de naissance et les conditions Epic, puis réessayez avec l’autorisation Quixel officielle.";
            }
            throw new QuixelException(message);
        }

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        var root = document.RootElement;
        var token = ReadString(root, "token")
            ?? throw new QuixelException("Quixel n’a retourné aucun jeton de session.");
        var refreshToken = ReadString(root, "refreshToken")
            ?? throw new QuixelException("Quixel n’a retourné aucun jeton de renouvellement.");
        var claims = ReadJwtClaims(token);
        var session = new QuixelSession(
            token,
            refreshToken,
            claims.ExpiresAt,
            ReadFirstString(root, "id", "userId", "epicAccountId") ?? claims.AccountId,
            ReadFirstString(root, "displayName", "name") ?? claims.DisplayName,
            ReadFirstString(root, "email") ?? claims.Email);
        await _sessionStore.SaveAsync(session, cancellationToken).ConfigureAwait(false);
        return ConnectedStatus(session, "Compte Epic/Quixel connecté · bibliothèque historique disponible.");
    }

    private async Task<QuixelSession> RequireSessionAsync(CancellationToken cancellationToken)
    {
        var session = await _sessionStore.LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new QuixelException(
                "Connectez d’abord le compte Epic/Quixel dans Paramètres → Connexions → Megascans.");
        return await EnsureSessionAsync(session, cancellationToken).ConfigureAwait(false);
    }

    private async Task<QuixelSession> EnsureSessionAsync(
        QuixelSession session,
        CancellationToken cancellationToken)
    {
        if (session.AccessTokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return session;
        using var response = await _httpClient.PostAsJsonAsync(
                new Uri($"{AccountsBase}/v1/login"),
                new
                {
                    email = session.Email,
                    token = session.AccessToken,
                    refreshToken = session.RefreshToken,
                    realm = "megascans",
                },
                cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "La session Megascans a expiré; reconnectez le compte.", cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        var token = ReadString(document.RootElement, "token")
            ?? throw new QuixelException("Le renouvellement Quixel ne contient aucun jeton.");
        var updated = session with
        {
            AccessToken = token,
            RefreshToken = ReadString(document.RootElement, "refreshToken") ?? session.RefreshToken,
            AccessTokenExpiresAt = ReadJwtClaims(token).ExpiresAt,
        };
        await _sessionStore.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        return updated;
    }

    private async Task<IReadOnlyList<RemoteFabAsset>> SearchAssetsAsync(
        IReadOnlyCollection<string> ids,
        CancellationToken cancellationToken)
    {
        var safeIds = ids.Where(id => SafeAssetIdRegex().IsMatch(id)).ToArray();
        if (safeIds.Length == 0) return [];
        var filter = "(" + string.Join(" OR ", safeIds.Select(id => $"objectID:{id}")) + ")";
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"https://{AlgoliaApplicationId}-2.algolianet.com/1/indexes/assets/query"));
        request.Headers.Add("X-Algolia-Application-Id", AlgoliaApplicationId);
        request.Headers.Add("X-Algolia-API-Key", AlgoliaSearchKey);
        request.Content = JsonContent.Create(new
        {
            query = string.Empty,
            page = 0,
            hitsPerPage = safeIds.Length,
            filters = filter,
        });
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Le catalogue public Megascans est indisponible.", cancellationToken)
            .ConfigureAwait(false);
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array)
        {
            return [];
        }
        return hits.EnumerateArray()
            .Select(ParseSearchHit)
            .Where(asset => asset is not null)
            .Cast<RemoteFabAsset>()
            .ToArray();
    }

    private static RemoteFabAsset? ParseSearchHit(JsonElement hit)
    {
        var id = ReadFirstString(hit, "objectID", "id");
        if (string.IsNullOrWhiteSpace(id)) return null;
        var title = ReadFirstString(hit, "name", "title") ?? id;
        var categories = CollectCategoryValues(hit)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToArray();
        var gallery = CollectImageUris(hit).ToArray();
        var thumbnail = FindPreferredThumbnail(hit) ?? gallery.FirstOrDefault();
        var tiers = ReadIntArray(hit, "uAssetTiers").Distinct().OrderBy(value => value).ToArray();
        if (tiers.Length == 0 && ReadBoolean(hit, "hasUAsset")) tiers = [0, 1, 2, 3, 4];
        var uassetVersions = tiers.Select(tier => new RemoteFabProjectVersion(
            $"quixel-tier:{id}:{tier}",
            [new RemoteFabBuildVersion($"Tier {tier}", "Unreal UAsset")],
            [],
            ["Unreal UAsset"]));
        var isModel = categories.Any(category =>
            category.Equals("3d", StringComparison.OrdinalIgnoreCase) ||
            category.Equals("3dplant", StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith("3D asset", StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith("3D plant", StringComparison.OrdinalIgnoreCase));
        var isMetaHuman = categories.Any(category =>
            category.Contains("metahuman", StringComparison.OrdinalIgnoreCase));
        string[] rawFormats = isMetaHuman ? [] : isModel ? ["fbx", "obj"] : ["textures"];
        var rawVersions = RawResolutions.SelectMany(resolution => rawFormats.Select(format =>
            new RemoteFabProjectVersion(
                $"quixel-raw:{id}:{resolution}:{format}",
                [new RemoteFabBuildVersion(
                    $"RAW {FormatRawResolution(resolution)}",
                    format == "textures" ? "Textures source" : $"{format.ToUpperInvariant()} + textures")],
                [],
                format == "textures"
                    ? new List<string> { "Multi-moteur", "Textures" }
                    : new List<string> { "Multi-moteur", format.ToUpperInvariant() })));
        var versions = uassetVersions.Concat(rawVersions).ToArray();
        return new RemoteFabAsset(
            id,
            "megascans",
            title,
            ReadFirstString(hit, "description", "about", "summary") ?? title,
            "quixel",
            "quixel-download",
            new Uri($"https://quixel.com/megascans/home?assetId={Uri.EscapeDataString(id)}"),
            thumbnail,
            categories.Length == 0 ? ["Megascans"] : ["Megascans", .. categories],
            versions,
            gallery);
    }

    private static IEnumerable<string> ParseAcquisitionIds(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("assets", out var assets)) root = assets;
        if (root.ValueKind != JsonValueKind.Array) yield break;
        foreach (var item in root.EnumerateArray())
        {
            var id = item.ValueKind switch
            {
                JsonValueKind.String => item.GetString(),
                JsonValueKind.Object => ReadFirstString(item, "assetID", "assetId", "id", "objectID"),
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(id)) yield return id;
        }
    }

    private static IEnumerable<string> CollectCategoryValues(JsonElement root)
    {
        foreach (var name in new[]
                 {
                     "type", "category", "subCategory", "assetCategories", "descriptive", "theme", "contains",
                 })
        {
            if (!root.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                yield return value.GetString()!;
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        yield return item.GetString()!;
                    }
                }
            }
        }
    }

    private static IEnumerable<Uri> CollectImageUris(JsonElement root) =>
        CollectHttpsUris(root)
            .Where(uri => IsImagePath(uri.AbsolutePath))
            .Distinct()
            .Take(32);

    private static Uri? FindPreferredThumbnail(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Contains("thumb", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    Uri.TryCreate(property.Value.GetString(), UriKind.Absolute, out var uri) &&
                    uri.Scheme == Uri.UriSchemeHttps)
                {
                    return uri;
                }
                var nested = FindPreferredThumbnail(property.Value);
                if (nested is not null) return nested;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var nested = FindPreferredThumbnail(item);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private static IEnumerable<Uri> CollectHttpsUris(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.String &&
            Uri.TryCreate(root.GetString(), UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps)
        {
            yield return uri;
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                foreach (var nested in CollectHttpsUris(property.Value)) yield return nested;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                foreach (var nested in CollectHttpsUris(item)) yield return nested;
            }
        }
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        HttpMethod method,
        Uri uri,
        QuixelSession session,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        throw new QuixelException(await ReadErrorAsync(response, fallback, cancellationToken).ConfigureAwait(false));
    }

    private static async Task<string> ReadErrorAsync(
        HttpResponseMessage response,
        string fallback,
        CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
            var detail = ReadFirstString(document.RootElement, "message", "error_description", "error", "detail");
            if (!string.IsNullOrWhiteSpace(detail)) return detail;
        }
        catch (JsonException)
        {
        }
        return $"{fallback} (HTTP {(int)response.StatusCode}).";
    }

    private static QuixelStatus ConnectedStatus(QuixelSession session, string message) =>
        new(QuixelConnectionState.Connected, message, session.AccountId, session.DisplayName);

    private static int ParseTier(string artifactId)
    {
        var value = artifactId.Split(':').LastOrDefault();
        if (!int.TryParse(value, out var tier) || tier is < 0 or > 16)
        {
            throw new QuixelException("La résolution Megascans sélectionnée est invalide.");
        }
        return tier;
    }

    private static RawVariant ParseRawVariant(string artifactId, string expectedAssetId)
    {
        var parts = artifactId.Split(':');
        if (parts.Length != 4 ||
            !parts[0].Equals("quixel-raw", StringComparison.OrdinalIgnoreCase) ||
            !parts[1].Equals(expectedAssetId, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(parts[2], out var resolution) ||
            !RawResolutions.Contains(resolution))
        {
            throw new QuixelException("La variante RAW Megascans sélectionnée est invalide.");
        }
        var format = parts[3].ToLowerInvariant();
        if (format is not ("fbx" or "obj" or "textures"))
        {
            throw new QuixelException("Le format RAW Megascans sélectionné est invalide.");
        }
        return new RawVariant(resolution, format);
    }

    private static string ReadAssetType(JsonElement root, IReadOnlyList<string> fallbackCategories)
    {
        var type = ReadFirstString(root, "type", "assetType");
        if (!string.IsNullOrWhiteSpace(type)) return type;
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("categories", out var categories) &&
            categories.ValueKind == JsonValueKind.Array)
        {
            foreach (var category in categories.EnumerateArray())
            {
                if (category.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(category.GetString()))
                {
                    return category.GetString()!;
                }
            }
        }
        return fallbackCategories.FirstOrDefault(category =>
                   category.Equals("3d", StringComparison.OrdinalIgnoreCase) ||
                   category.Equals("3dplant", StringComparison.OrdinalIgnoreCase))
               ?? "surface";
    }

    private static HashSet<string> ReadAvailableComponentTypes(JsonElement root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object) return result;
        foreach (var propertyName in new[] { "maps", "components", "billboards" })
        {
            if (!root.TryGetProperty(propertyName, out var values) || values.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            foreach (var value in values.EnumerateArray())
            {
                var type = ReadString(value, "type");
                if (!string.IsNullOrWhiteSpace(type)) result.Add(type);
            }
        }
        return result;
    }

    private static int[] ReadAvailableLods(JsonElement root)
    {
        var lods = new SortedSet<int>();
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("models", out var models) &&
            models.ValueKind == JsonValueKind.Array)
        {
            foreach (var model in models.EnumerateArray())
            {
                if (!string.Equals(ReadString(model, "type"), "lod", StringComparison.OrdinalIgnoreCase)) continue;
                var lod = ReadInt(model, "lod");
                if (lod is >= 0 and <= 32) lods.Add(lod.Value);
            }
        }
        return lods.Count == 0 ? [0] : lods.ToArray();
    }

    private static int[] ReadAvailableTriangleCounts(JsonElement root)
    {
        var values = new SortedSet<int>(Comparer<int>.Create((left, right) => right.CompareTo(left)));
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("meshes", out var meshes) &&
            meshes.ValueKind == JsonValueKind.Array)
        {
            foreach (var mesh in meshes.EnumerateArray())
            {
                if (!string.Equals(ReadString(mesh, "type"), "lod", StringComparison.OrdinalIgnoreCase)) continue;
                var triangles = ReadInt(mesh, "tris");
                if (triangles is > 0) values.Add(triangles.Value);
            }
        }
        return values.ToArray();
    }

    private static int? ReadInt(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.TryGetInt32(out var number)
            ? number
            : null;

    private static string FormatRawResolution(int resolution) => resolution switch
    {
        1024 => "1K",
        2048 => "2K",
        4096 => "4K",
        8192 => "8K",
        _ => $"{resolution}px",
    };

    private static string ExtractAuthorizationCode(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new QuixelException("Collez la réponse d’autorisation Epic/Quixel.");
        }
        var trimmed = value.Trim();
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                return ReadFirstString(document.RootElement, "authorizationCode", "code")
                    ?? throw new QuixelException("La réponse Epic/Quixel ne contient aucun code.");
            }
            catch (JsonException exception)
            {
                throw new QuixelException("La réponse Epic/Quixel est invalide.", exception);
            }
        }
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in query)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && (parts[0] == "code" || parts[0] == "authorizationCode"))
                {
                    return Uri.UnescapeDataString(parts[1]);
                }
            }
        }
        if (trimmed.Length is < 8 or > 4096)
        {
            throw new QuixelException("Le code Epic/Quixel est invalide.");
        }
        return trimmed;
    }

    private static JwtClaims ReadJwtClaims(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return JwtClaims.Empty;
            var encoded = parts[1].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(encoded));
            var root = document.RootElement;
            var expiresAt = root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : DateTimeOffset.UtcNow.AddHours(1);
            return new JwtClaims(
                ReadFirstString(root, "sub", "accountId", "epicAccountId") ?? string.Empty,
                ReadFirstString(root, "name", "displayName") ?? string.Empty,
                ReadFirstString(root, "email") ?? string.Empty,
                expiresAt);
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            return JwtClaims.Empty;
        }
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadFirstString(JsonElement root, params string[] names)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var name in names)
        {
            var value = ReadString(root, name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return null;
    }

    private static IEnumerable<int> ReadIntArray(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty(name, out var values) ||
            values.ValueKind != JsonValueKind.Array) yield break;
        foreach (var value in values.EnumerateArray())
        {
            if (value.TryGetInt32(out var number)) yield return number;
        }
    }

    private static bool ReadBoolean(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.True;

    private static string PrepareLibraryRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new QuixelException("Choisissez le dossier de téléchargement.");
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim().Trim('"')));
        Directory.CreateDirectory(root);
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new QuixelException("Le dossier de bibliothèque ne peut pas être un lien ou une jonction.");
        }
        return root;
    }

    private static string ReserveDirectory(string desired)
    {
        if (!Directory.Exists(desired) && !File.Exists(desired)) return desired;
        for (var index = 2; index <= 10_000; index++)
        {
            var candidate = desired + $"-{index}";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        throw new QuixelException("Impossible de réserver un dossier unique pour le téléchargement Megascans.");
    }

    private static string MakeSafeName(string title, string id)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var value = new string(title
                .Where(character => !invalid.Contains(character) && !char.IsControl(character))
                .ToArray())
            .Trim()
            .TrimEnd('.');
        if (value.Length > 80) value = value[..80].TrimEnd();
        return string.IsNullOrWhiteSpace(value) ? id : $"{value}-{id}";
    }

    private static string MakeSafeFileName(Uri uri, int index)
    {
        var candidate = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(candidate)) candidate = $"megascans-{index + 1:D4}.bin";
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        candidate = new string(candidate
                .Where(character => !invalid.Contains(character) && !char.IsControl(character))
                .ToArray())
            .Trim()
            .TrimEnd('.');
        if (candidate.Length > 160) candidate = candidate[^160..];
        return $"{index + 1:D4}-{candidate}";
    }

    private static bool IsImagePath(string path) =>
        path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeAssetIdRegex();

    private sealed record JwtClaims(
        string AccountId,
        string DisplayName,
        string Email,
        DateTimeOffset ExpiresAt)
    {
        public static JwtClaims Empty { get; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            DateTimeOffset.UtcNow.AddHours(1));
    }

    private sealed record QuixelDownloadRequest(
        Uri Endpoint,
        object Payload,
        string DirectoryName,
        string DisplayName);

    private sealed record RawVariant(int Resolution, string Format);

    private sealed record RawComponentRequest(string Type, string Resolution, string MimeType);

    private sealed record RawMeshRequest(string MimeType, int Lod);

    private sealed record RawLegacyConfig(
        bool Highpoly,
        bool Ztool,
        bool LowerlodNormals,
        bool AlbedoLods,
        string MeshMimeType,
        bool Brushes,
        IReadOnlyList<int> Lods);
}
