using System.Security.Cryptography;
using System.Text.Json;
using CyEngineHub.Fab.Library;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Fab.Storage;

public sealed record EpicVaultPackageInspection(
    string DisplayName,
    string BuildVersion,
    int AssetCount,
    int MapCount,
    int FileCount,
    long UncompressedBytes,
    DateTimeOffset LastModifiedAt,
    IReadOnlyList<string> Issues,
    bool IsPartial);

public sealed record EpicVaultCacheMaintenanceResult(
    long BeforeBytes,
    long AfterBytes,
    int RemovedEntries);

public static class EpicVaultNativeStoreService
{
    private const long MaximumManifestBytes = 64L * 1024 * 1024;
    private const int BufferSize = 128 * 1024;
    private const string CacheMarkerFileName = ".cyenginehub-epic-vault-cache.json";
    private static readonly SemaphoreSlim CacheGate = new(1, 1);

    public static EpicVaultPackageInspection Inspect(
        string packageRoot,
        string manifestPath,
        int maximumFiles,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        var manifest = ReadManifest(manifestPath);
        var files = manifest.Files.Take(maximumFiles + 1).ToArray();
        var partial = files.Length > maximumFiles;
        if (partial) files = files[..maximumFiles];
        long bytes = 0;
        var acceptedFiles = new List<EpicManifestFile>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.Size < 0 || file.Size > maximumBytes - bytes)
            {
                partial = true;
                break;
            }
            bytes += file.Size;
            acceptedFiles.Add(file);
        }

        var issues = new List<string>();
        if (partial)
        {
            issues.Add("Inventaire Vault borné; la reconstruction vérifiera le manifest complet.");
        }
        if (manifest.Chunks.Count == 0)
        {
            issues.Add("Le manifest ne référence aucun chunk Epic.");
        }

        return new EpicVaultPackageInspection(
            string.IsNullOrWhiteSpace(manifest.AppName)
                ? Path.GetFileName(Path.TrimEndingDirectorySeparator(packageRoot))
                : manifest.AppName,
            manifest.BuildVersion,
            acceptedFiles.Count(file =>
                Path.GetExtension(file.RelativePath).Equals(".uasset", StringComparison.OrdinalIgnoreCase)),
            acceptedFiles.Count(file =>
                Path.GetExtension(file.RelativePath).Equals(".umap", StringComparison.OrdinalIgnoreCase)),
            acceptedFiles.Count,
            bytes,
            File.GetLastWriteTimeUtc(manifestPath),
            issues,
            partial);
    }

    public static async Task<DirectFabDownloadResult> MaterializeAsync(
        FabLocalContentPack pack,
        string destinationRoot,
        IProgress<DirectFabDownloadProgress>? progress = null,
        long maximumCacheBytes = 0,
        CancellationToken cancellationToken = default)
    {
        if (pack.Kind != FabLocalContentKind.EpicVault ||
            string.IsNullOrWhiteSpace(pack.ProjectPath) ||
            !File.Exists(pack.ProjectPath))
        {
            throw new DirectFabException("Le pack sélectionné n’est pas un package Vault Epic valide.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot));
        Directory.CreateDirectory(root);
        await CacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var manifest = ReadManifest(pack.ProjectPath);
            var fingerprint = await ComputeManifestFingerprintAsync(pack.ProjectPath, cancellationToken)
                .ConfigureAwait(false);
            var totalBytes = Math.Max(1, SumManifestBytes(manifest));
            var finalDirectory = Path.Combine(
                root,
                "EpicVault",
                SanitizeFileName(pack.DisplayName) + "-" + pack.Id[..Math.Min(12, pack.Id.Length)]);
            var cached = await TryOpenCachedAsync(
                    pack,
                    manifest,
                    fingerprint,
                    finalDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
            if (cached is not null)
            {
                progress?.Report(new DirectFabDownloadProgress(
                    1,
                    "Vault natif · reconstruction réutilisée depuis le cache.",
                    totalBytes,
                    totalBytes));
                _ = TrimCacheCore(root, maximumCacheBytes, finalDirectory, cancellationToken);
                return cached;
            }

            if (Directory.Exists(finalDirectory))
            {
                TryDeleteOwnedDirectory(finalDirectory, root);
            }
            var incoming = finalDirectory + $".incoming-{Guid.NewGuid():N}";
            Directory.CreateDirectory(incoming);
            long completedBytes = 0;
            try
            {
                var chunkPaths = BuildChunkIndex(pack.PackageRoot, cancellationToken);
                string? cachedChunkId = null;
                byte[]? cachedChunkData = null;
                for (var fileIndex = 0; fileIndex < manifest.Files.Count; fileIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = manifest.Files[fileIndex];
                    var destination = GetSafeDestination(incoming, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    var temporary = destination + ".partial";
                    await using (var output = new FileStream(
                                     temporary,
                                     FileMode.CreateNew,
                                     FileAccess.Write,
                                     FileShare.None,
                                     BufferSize,
                                     FileOptions.Asynchronous | FileOptions.SequentialScan))
                    {
                        foreach (var part in file.Parts)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!manifest.Chunks.TryGetValue(part.ChunkGuid, out var chunk))
                            {
                                throw new DirectFabException(
                                    $"Le manifest Vault référence un chunk inconnu : {part.ChunkGuid}.");
                            }
                            if (!string.Equals(cachedChunkId, part.ChunkGuid, StringComparison.OrdinalIgnoreCase))
                            {
                                var expectedName = Path.GetFileName(chunk.DownloadUri.LocalPath);
                                if (!chunkPaths.TryGetValue(expectedName, out var chunkPath))
                                {
                                    throw new DirectFabException(
                                        $"Chunk Vault manquant : {expectedName}.");
                                }
                                var rawChunk = await File.ReadAllBytesAsync(chunkPath, cancellationToken)
                                    .ConfigureAwait(false);
                                cachedChunkData = EpicManifestParser.ParseChunk(rawChunk, chunk);
                                cachedChunkId = part.ChunkGuid;
                            }
                            var chunkData = cachedChunkData!;
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
                        throw new DirectFabException(
                            $"L’intégrité du fichier {file.RelativePath} est invalide après reconstruction.");
                    }
                    File.Move(temporary, destination);
                    completedBytes += Math.Max(0, file.Size);
                    progress?.Report(new DirectFabDownloadProgress(
                        Math.Clamp((double)completedBytes / totalBytes, 0, 1),
                        $"Vault natif · {fileIndex + 1:N0}/{manifest.Files.Count:N0} fichier(s)",
                        completedBytes,
                        totalBytes));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(finalDirectory)!);
                Directory.Move(incoming, finalDirectory);
                await WriteCacheMarkerAsync(
                        finalDirectory,
                        new EpicVaultCacheEntry(
                            fingerprint,
                            manifest.AppName,
                            manifest.BuildVersion,
                            manifest.Version,
                            manifest.Files.Count,
                            completedBytes,
                            DateTimeOffset.UtcNow,
                            DateTimeOffset.UtcNow),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                TryDeleteOwnedDirectory(incoming, root);
                throw;
            }

            _ = TrimCacheCore(root, maximumCacheBytes, finalDirectory, cancellationToken);
            return CreateResult(pack, manifest, finalDirectory, completedBytes, DateTimeOffset.UtcNow);
        }
        finally
        {
            CacheGate.Release();
        }
    }

    public static async Task<EpicVaultCacheMaintenanceResult> TrimCacheAsync(
        string destinationRoot,
        long maximumCacheBytes,
        CancellationToken cancellationToken = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot));
        Directory.CreateDirectory(root);
        await CacheGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return TrimCacheCore(root, maximumCacheBytes, null, cancellationToken);
        }
        finally
        {
            CacheGate.Release();
        }
    }

    public static async Task<DirectFabDownloadResult> CopyForConversionAsync(
        DirectFabDownloadResult materialized,
        string destinationRoot,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(materialized.DestinationDirectory));
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(source);
        }
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationRoot));
        Directory.CreateDirectory(root);
        var destination = GetUniqueDirectory(Path.Combine(
            root,
            ".native-store-conversion",
            SanitizeFileName(materialized.DisplayName)));
        var files = EnumerateSafeFiles(source, cancellationToken).ToArray();
        var totalBytes = Math.Max(1, files.Sum(file => file.Length));
        long completedBytes = 0;
        var buffer = new byte[BufferSize];
        try
        {
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(source, file.FullName);
                var target = GetSafeDestination(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var input = new FileStream(
                    file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                    BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(
                    target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                        .ConfigureAwait(false);
                    completedBytes += read;
                    progress?.Report(new DirectFabDownloadProgress(
                        Math.Clamp((double)completedBytes / totalBytes, 0, 1),
                        "Copie de conversion du store natif…",
                        completedBytes,
                        totalBytes));
                }
            }
        }
        catch
        {
            TryDeleteOwnedDirectory(destination, root);
            throw;
        }
        return materialized with
        {
            DestinationDirectory = destination,
            DownloadedBytes = completedBytes,
            CompletedAt = DateTimeOffset.UtcNow,
        };
    }

    private static EpicDownloadManifest ReadManifest(string manifestPath)
    {
        var info = new FileInfo(manifestPath);
        if (!info.Exists || info.Length is < 2 or > MaximumManifestBytes)
        {
            throw new DirectFabException("Le manifest du Vault Epic est absent ou trop volumineux.");
        }
        return EpicManifestParser.Parse(
            File.ReadAllBytes(manifestPath),
            new Uri("https://native-vault.invalid/"));
    }

    private static async Task<string> ComputeManifestFingerprintAsync(
        string manifestPath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            manifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static async Task<DirectFabDownloadResult?> TryOpenCachedAsync(
        FabLocalContentPack pack,
        EpicDownloadManifest manifest,
        string fingerprint,
        string finalDirectory,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(finalDirectory, CacheMarkerFileName);
        if (!Directory.Exists(finalDirectory) || !File.Exists(markerPath))
        {
            return null;
        }
        try
        {
            var marker = JsonSerializer.Deserialize<EpicVaultCacheEntry>(
                await File.ReadAllTextAsync(markerPath, cancellationToken).ConfigureAwait(false));
            if (marker is null ||
                !string.Equals(marker.ManifestFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase) ||
                marker.FileCount != manifest.Files.Count ||
                marker.UncompressedBytes != SumManifestBytes(manifest))
            {
                return null;
            }
            marker = marker with { LastAccessedAt = DateTimeOffset.UtcNow };
            await WriteCacheMarkerAsync(finalDirectory, marker, cancellationToken)
                .ConfigureAwait(false);
            Directory.SetLastWriteTimeUtc(finalDirectory, DateTime.UtcNow);
            return CreateResult(
                pack,
                manifest,
                finalDirectory,
                marker.UncompressedBytes,
                marker.CreatedAt);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static async Task WriteCacheMarkerAsync(
        string directory,
        EpicVaultCacheEntry marker,
        CancellationToken cancellationToken)
    {
        var markerPath = Path.Combine(directory, CacheMarkerFileName);
        var temporary = markerPath + $".tmp-{Guid.NewGuid():N}";
        await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(marker),
                cancellationToken)
            .ConfigureAwait(false);
        File.Move(temporary, markerPath, overwrite: true);
    }

    private static DirectFabDownloadResult CreateResult(
        FabLocalContentPack pack,
        EpicDownloadManifest manifest,
        string finalDirectory,
        long completedBytes,
        DateTimeOffset completedAt) => new(
        pack.Id,
        pack.DisplayName,
        finalDirectory,
        manifest.Files.Count,
        completedBytes,
        completedAt,
        new DirectFabSourceManifest(
            "EpicVaultNative",
            manifest.AppName,
            manifest.BuildVersion,
            manifest.Version,
            manifest.Files.Count,
            manifest.DownloadSize));

    private static long SumManifestBytes(EpicDownloadManifest manifest) => manifest.Files.Aggregate(
        0L,
        (total, file) =>
        {
            var size = Math.Max(0, file.Size);
            return total > long.MaxValue - size ? long.MaxValue : total + size;
        });

    private static EpicVaultCacheMaintenanceResult TrimCacheCore(
        string destinationRoot,
        long maximumCacheBytes,
        string? preservedDirectory,
        CancellationToken cancellationToken)
    {
        var vaultRoot = Path.Combine(destinationRoot, "EpicVault");
        if (!Directory.Exists(vaultRoot))
        {
            return new EpicVaultCacheMaintenanceResult(0, 0, 0);
        }
        var entries = new List<(string Path, long Bytes, DateTime LastAccessed)>();
        foreach (var directory in Directory.EnumerateDirectories(vaultRoot, "*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                !File.Exists(Path.Combine(directory, CacheMarkerFileName)))
            {
                continue;
            }
            entries.Add((
                directory,
                MeasureDirectory(directory, cancellationToken),
                Directory.GetLastWriteTimeUtc(directory)));
        }
        var before = entries.Aggregate(0L, (total, entry) => SaturatingAdd(total, entry.Bytes));
        if (maximumCacheBytes <= 0 || before <= maximumCacheBytes)
        {
            return new EpicVaultCacheMaintenanceResult(before, before, 0);
        }
        var after = before;
        var removed = 0;
        foreach (var entry in entries.OrderBy(entry => entry.LastAccessed))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (after <= maximumCacheBytes)
            {
                break;
            }
            if (preservedDirectory is not null &&
                string.Equals(entry.Path, preservedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            TryDeleteOwnedDirectory(entry.Path, destinationRoot);
            if (!Directory.Exists(entry.Path))
            {
                after = Math.Max(0, after - entry.Bytes);
                removed++;
            }
        }
        return new EpicVaultCacheMaintenanceResult(before, after, removed);
    }

    private static long MeasureDirectory(string root, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var file in EnumerateSafeFiles(root, cancellationToken))
        {
            total = SaturatingAdd(total, file.Length);
        }
        return total;
    }

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - Math.Max(0, right) ? long.MaxValue : left + Math.Max(0, right);

    private static Dictionary<string, string> BuildChunkIndex(
        string packageRoot,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(packageRoot));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current, "*.chunk", SearchOption.TopDirectoryOnly))
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    result.TryAdd(info.Name, info.FullName);
                }
            }
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(directory);
                }
            }
        }
        return result;
    }

    private static IEnumerable<FileInfo> EnumerateSafeFiles(
        string root,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            foreach (var file in Directory.EnumerateFiles(current))
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0 &&
                    !string.Equals(info.Name, CacheMarkerFileName, StringComparison.OrdinalIgnoreCase))
                {
                    yield return info;
                }
            }
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(directory);
                }
            }
        }
    }

    private static string GetSafeDestination(string root, string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new DirectFabException("Le manifest Vault contient un chemin absolu interdit.");
        }
        var destination = Path.GetFullPath(Path.Combine(root, normalized));
        var relative = Path.GetRelativePath(root, destination);
        if (relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new DirectFabException("Le manifest Vault tente de sortir du dossier de destination.");
        }
        return destination;
    }

    private static string GetUniqueDirectory(string desired)
    {
        if (!Directory.Exists(desired) && !File.Exists(desired)) return desired;
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = $"{desired}-{index}";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        throw new IOException("Impossible de réserver un dossier pour le package Vault.");
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "EpicVaultPackage" : sanitized;
    }

    private static void TryDeleteOwnedDirectory(string path, string root)
    {
        try
        {
            var relative = Path.GetRelativePath(root, path);
            if (Directory.Exists(path) && !Path.IsPathRooted(relative) &&
                !relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Le staging incomplet reste récupérable si Windows conserve encore un handle.
        }
    }

    private sealed record EpicVaultCacheEntry(
        string ManifestFingerprint,
        string AppName,
        string BuildVersion,
        uint ManifestVersion,
        int FileCount,
        long UncompressedBytes,
        DateTimeOffset CreatedAt,
        DateTimeOffset LastAccessedAt);
}
