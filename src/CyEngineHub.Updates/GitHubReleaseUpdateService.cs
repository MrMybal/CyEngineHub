using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyEngineHub.Updates;

public sealed class GitHubReleaseUpdateService : IApplicationUpdateService
{
    private const int BufferSize = 1024 * 1024;
    private const int MaximumChecksumDocumentLength = 1024 * 1024;
    private readonly HttpClient _httpClient;

    public GitHubReleaseUpdateService(HttpClient httpClient) => _httpClient = httpClient;

    public Task<ApplicationRelease?> CheckForUpdateAsync(
        string repository,
        Version currentVersion,
        CancellationToken cancellationToken = default) =>
        CheckForUpdateAsync(repository, currentVersion.ToString(), false, cancellationToken);

    public async Task<ApplicationRelease?> CheckForUpdateAsync(
        string repository,
        string currentVersion,
        bool includePrereleases,
        CancellationToken cancellationToken = default)
    {
        var (owner, name) = ParseRepository(repository);
        var current = ReleaseVersion.Parse(currentVersion);
        var route = includePrereleases ? "releases?per_page=100" : "releases/latest";
        var endpoint = new Uri(
            $"https://api.github.com/repos/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/{route}");
        using var request = CreateRequest(endpoint);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode)
        {
            throw new ApplicationUpdateException(
                $"GitHub a répondu {(int)response.StatusCode} ({response.ReasonPhrase}) pour la recherche de mise à jour.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var releases = includePrereleases
            ? document.RootElement.EnumerateArray().ToArray()
            : [document.RootElement];
        JsonElement? selected = null;
        var selectedVersion = current;
        foreach (var candidate in releases)
        {
            if (ReadBoolean(candidate, "draft") || (!includePrereleases && ReadBoolean(candidate, "prerelease")))
                continue;
            ReleaseVersion candidateVersion;
            try { candidateVersion = ReleaseVersion.Parse(ReadRequiredString(candidate, "tag_name")); }
            catch (ApplicationUpdateException) { continue; }
            if (!includePrereleases && candidateVersion.IsPrerelease) continue;
            if (candidateVersion.CompareTo(selectedVersion) <= 0) continue;
            selected = candidate;
            selectedVersion = candidateVersion;
        }

        if (selected is not { } root) return null;

        var tagName = ReadRequiredString(root, "tag_name");

        var assets = ParseAssets(root);
        var installer = SelectInstaller(assets);
        var checksum = installer is null ? null : SelectChecksum(assets, installer.Name);
        var expectedSha256 = installer is null ? null : ReadAssetDigest(root, installer.Name);
        return new ApplicationRelease(
            selectedVersion.Core,
            tagName,
            ReadOptionalString(root, "name") ?? tagName,
            ReadOptionalString(root, "body") ?? "Aucune note de version fournie.",
            new Uri(ReadRequiredString(root, "html_url")),
            ParsePublishedAt(ReadOptionalString(root, "published_at")),
            installer,
            checksum,
            expectedSha256);
    }

    public async Task<VerifiedApplicationUpdate> DownloadAndVerifyAsync(
        ApplicationRelease release,
        string destinationDirectory,
        IProgress<ApplicationUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (release.Installer is null)
        {
            throw new ApplicationUpdateException("Cette release ne contient aucun installateur Windows .exe ou .msi.");
        }

        var expectedSha256 = release.ExpectedSha256;
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            if (release.Checksum is null)
            {
                throw new ApplicationUpdateException(
                    "Cette release ne fournit aucune somme SHA-256. L’installation automatique est refusée.");
            }

            expectedSha256 = await DownloadExpectedChecksumAsync(
                release.Checksum,
                release.Installer.Name,
                cancellationToken).ConfigureAwait(false);
        }

        Directory.CreateDirectory(destinationDirectory);
        var installerName = Path.GetFileName(release.Installer.Name);
        var destinationPath = Path.Combine(destinationDirectory, installerName);
        var partialPath = destinationPath + ".partial";
        try
        {
            await DownloadFileAsync(release.Installer, partialPath, progress, cancellationToken)
                .ConfigureAwait(false);
            var actualSha256 = await ComputeSha256Async(partialPath, progress, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(expectedSha256, actualSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ApplicationUpdateException(
                    $"La vérification SHA-256 a échoué. Attendu : {expectedSha256}; obtenu : {actualSha256}.");
            }

            File.Move(partialPath, destinationPath, overwrite: true);
            progress?.Report(new ApplicationUpdateProgress(
                "Mise à jour vérifiée",
                1,
                release.Installer.SizeBytes,
                release.Installer.SizeBytes));
            return new VerifiedApplicationUpdate(destinationPath, actualSha256);
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
    }

    private static HttpRequestMessage CreateRequest(Uri endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("CyEngineHub-Updater/1.0");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static (string Owner, string Name) ParseRepository(string repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
        {
            throw new ApplicationUpdateException("Configurez d’abord le dépôt GitHub des mises à jour.");
        }

        var value = repository.Trim().TrimEnd('/');
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApplicationUpdateException("Le dépôt doit être hébergé sur github.com.");
            }

            value = uri.AbsolutePath.Trim('/');
        }

        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IsValidRepositoryPart(parts[0]) || !IsValidRepositoryPart(parts[1]))
        {
            throw new ApplicationUpdateException(
                "Adresse de dépôt invalide. Utilisez https://github.com/propriétaire/dépôt ou propriétaire/dépôt.");
        }

        return (parts[0], parts[1]);
    }

    private static bool IsValidRepositoryPart(string value) =>
        value.Length is > 0 and <= 100 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

    private static List<ApplicationReleaseAsset> ParseAssets(JsonElement root)
    {
        var assets = new List<ApplicationReleaseAsset>();
        if (!root.TryGetProperty("assets", out var array) || array.ValueKind != JsonValueKind.Array) return assets;
        foreach (var item in array.EnumerateArray())
        {
            var assetName = ReadOptionalString(item, "name");
            var url = ReadOptionalString(item, "browser_download_url");
            if (string.IsNullOrWhiteSpace(assetName) || !Uri.TryCreate(url, UriKind.Absolute, out var downloadUri))
            {
                continue;
            }

            var size = item.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize)
                ? parsedSize
                : 0;
            assets.Add(new ApplicationReleaseAsset(assetName, downloadUri, Math.Max(0, size)));
        }

        return assets;
    }

    private static ApplicationReleaseAsset? SelectInstaller(IEnumerable<ApplicationReleaseAsset> assets) =>
        assets
            .Where(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                            asset.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(asset => ScoreInstaller(asset.Name))
            .ThenByDescending(asset => asset.SizeBytes)
            .FirstOrDefault();

    private static int ScoreInstaller(string name)
    {
        var score = 0;
        if (name.Contains("setup", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (name.Contains("installer", StringComparison.OrdinalIgnoreCase)) score += 8;
        if (name.Contains("win", StringComparison.OrdinalIgnoreCase)) score += 4;
        if (name.Contains("x64", StringComparison.OrdinalIgnoreCase)) score += 3;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) score++;
        return score;
    }

    private static ApplicationReleaseAsset? SelectChecksum(
        IEnumerable<ApplicationReleaseAsset> assets,
        string installerName)
    {
        var exactNames = new[] { installerName + ".sha256", installerName + ".sha256.txt" };
        var materialized = assets.ToArray();
        return materialized.FirstOrDefault(asset => exactNames.Contains(asset.Name, StringComparer.OrdinalIgnoreCase))
            ?? materialized.FirstOrDefault(asset =>
                asset.Name.Equals("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
                asset.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) ||
                asset.Name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ReadAssetDigest(JsonElement root, string installerName)
    {
        if (!root.TryGetProperty("assets", out var array) || array.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in array.EnumerateArray())
        {
            if (!string.Equals(ReadOptionalString(item, "name"), installerName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var digest = ReadOptionalString(item, "digest");
            if (digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true && IsSha256(digest[7..]))
            {
                return digest[7..].ToLowerInvariant();
            }
        }

        return null;
    }

    private async Task<string> DownloadExpectedChecksumAsync(
        ApplicationReleaseAsset checksumAsset,
        string installerName,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(checksumAsset.DownloadUri);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (content.Length > MaximumChecksumDocumentLength)
        {
            throw new ApplicationUpdateException("Le document SHA-256 de la release est anormalement volumineux.");
        }

        foreach (var rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var separator = line.IndexOfAny([' ', '\t']);
            if (separator <= 0) continue;
            var hash = line[..separator].Trim();
            var fileName = line[separator..].Trim().TrimStart('*');
            if (IsSha256(hash) && string.Equals(fileName, installerName, StringComparison.OrdinalIgnoreCase))
            {
                return hash.ToLowerInvariant();
            }
        }

        var soleValue = content.Trim();
        if (IsSha256(soleValue)) return soleValue.ToLowerInvariant();
        throw new ApplicationUpdateException(
            $"Le document SHA-256 ne contient aucune somme valide pour « {installerName} ».");
    }

    private async Task DownloadFileAsync(
        ApplicationReleaseAsset asset,
        string destinationPath,
        IProgress<ApplicationUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(asset.DownloadUri);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength is > 0
            ? response.Content.Headers.ContentLength
            : asset.SizeBytes > 0 ? asset.SizeBytes : null;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(
            destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                progress?.Report(new ApplicationUpdateProgress(
                    "Téléchargement de l’installateur",
                    total is > 0 ? Math.Clamp((double)received / total.Value, 0, 1) : 0,
                    received,
                    total));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        IProgress<ApplicationUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            long processed = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                processed += read;
                progress?.Report(new ApplicationUpdateProgress(
                    "Vérification SHA-256",
                    stream.Length > 0 ? Math.Clamp((double)processed / stream.Length, 0, 1) : 1,
                    processed,
                    stream.Length));
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);
    private static bool ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;
    private static string ReadRequiredString(JsonElement element, string propertyName) =>
        ReadOptionalString(element, propertyName) ??
        throw new ApplicationUpdateException($"Réponse GitHub incomplète : propriété « {propertyName} » absente.");
    private static string? ReadOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    private static DateTimeOffset? ParsePublishedAt(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result
            : null;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
