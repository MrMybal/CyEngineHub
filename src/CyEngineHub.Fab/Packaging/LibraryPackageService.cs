using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Fab.Packaging;

public enum LibraryPackageStorageMode
{
    Uncompressed,
    ZipStore,
    ZipFastest,
    ZipBalanced,
    ZipHigh,
    ZipUltra,
}

public enum LibraryPackageContentKind
{
    Unknown,
    ContentPack,
    CompleteProject,
    CodePlugin,
    EnginePackage,
    RawAsset,
}

public sealed record LibraryPackageManifest(
    int SchemaVersion,
    string AssetId,
    string Title,
    string Source,
    string ArtifactId,
    string Variant,
    LibraryPackageContentKind ContentKind,
    string DistributionMethod,
    LibraryPackageStorageMode StorageMode,
    string? ArchiveFile,
    int FileCount,
    long UncompressedBytes,
    long StoredBytes,
    IReadOnlyDictionary<string, int> FileExtensions,
    IReadOnlyList<string> EntryPoints,
    IReadOnlyList<string> EngineVersions,
    IReadOnlyList<string> Platforms,
    DirectFabSourceManifest? SourceManifest,
    DateTimeOffset PackagedAt);

public sealed class LibraryPackageService
{
    public const string ManifestFileName = "cyenginehub-package.json";
    public const string ArchiveFileName = "content.zip";
    private const int BufferSize = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<DirectFabDownloadResult> PackageAsync(
        RemoteFabAsset asset,
        RemoteFabProjectVersion version,
        DirectFabDownloadResult download,
        LibraryPackageStorageMode storageMode,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(download);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(download.DestinationDirectory));
        if (!Directory.Exists(root))
        {
            throw new DirectFabException("Le contenu téléchargé à empaqueter est introuvable.");
        }
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
        {
            throw new DirectFabException("Le dossier de package ne peut pas être un lien ou une jonction.");
        }

        var files = EnumerateContentFiles(root, cancellationToken);
        var uncompressedBytes = files.Sum(file => file.Length);
        var entryPoints = files
            .Where(file => IsEntryPoint(file.Extension))
            .Select(file => Path.GetRelativePath(root, file.FullName))
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Take(256)
            .ToArray();
        var extensions = files
            .GroupBy(file => NormalizeExtension(file.Extension), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(128)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var contentKind = Classify(asset, version, files);
        string? archiveFile = null;
        long storedBytes = uncompressedBytes;

        try
        {
            if (IsZipStorageMode(storageMode))
            {
                progress?.Report(new DirectFabDownloadProgress(
                    null,
                    $"Création du package {FormatStorageMode(storageMode)}…",
                    0,
                    uncompressedBytes));
                var archivePath = Path.Combine(root, ArchiveFileName);
                var temporaryArchive = archivePath + ".partial";
                if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive);
                await CreateArchiveAsync(
                        root,
                        files,
                        temporaryArchive,
                        uncompressedBytes,
                        storageMode,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                File.Move(temporaryArchive, archivePath, overwrite: true);
                archiveFile = ArchiveFileName;
                storedBytes = new FileInfo(archivePath).Length;
            }

            var manifest = new LibraryPackageManifest(
                1,
                asset.AssetId,
                asset.Title,
                NormalizeSource(asset),
                version.ArtifactId,
                download.DisplayName,
                contentKind,
                asset.DistributionMethod,
                storageMode,
                archiveFile,
                files.Count,
                uncompressedBytes,
                storedBytes,
                extensions,
                entryPoints,
                version.EngineVersions,
                version.Platforms,
                download.SourceManifest,
                DateTimeOffset.UtcNow);
            await WriteManifestAsync(root, manifest, cancellationToken).ConfigureAwait(false);

            if (IsZipStorageMode(storageMode))
            {
                RemoveArchivedContent(root, files);
            }
            progress?.Report(new DirectFabDownloadProgress(
                1,
                IsZipStorageMode(storageMode)
                    ? $"Package {FormatStorageMode(storageMode)} et manifeste CyEngineHub prêts."
                    : "Manifeste CyEngineHub prêt; fichiers conservés sans compression.",
                storedBytes,
                storedBytes));
            return download with
            {
                DownloadedBytes = storedBytes,
                PackageKind = contentKind.ToString(),
                StorageMode = storageMode.ToString(),
                ManifestPath = Path.Combine(root, ManifestFileName),
                UncompressedBytes = uncompressedBytes,
                StoredBytes = storedBytes,
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            throw new DirectFabException($"Impossible de créer le package CyEngineHub : {exception.Message}", exception);
        }
    }

    private static LibraryPackageContentKind Classify(
        RemoteFabAsset asset,
        RemoteFabProjectVersion version,
        IReadOnlyList<FileInfo> files)
    {
        if (version.ArtifactId.StartsWith("quixel-raw:", StringComparison.OrdinalIgnoreCase))
        {
            return LibraryPackageContentKind.RawAsset;
        }
        if (files.Any(file => file.Extension.Equals(".uproject", StringComparison.OrdinalIgnoreCase)))
        {
            return LibraryPackageContentKind.CompleteProject;
        }
        if (files.Any(file => file.Extension.Equals(".uplugin", StringComparison.OrdinalIgnoreCase)))
        {
            return LibraryPackageContentKind.CodePlugin;
        }
        var hasRawModel = files.Any(file => file.Extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".glb", StringComparison.OrdinalIgnoreCase));
        var hasNativeUnreal = files.Any(file => file.Extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase) ||
                                               file.Extension.Equals(".umap", StringComparison.OrdinalIgnoreCase));
        if (hasRawModel && !hasNativeUnreal) return LibraryPackageContentKind.RawAsset;
        return RemoteFabAssetClassifier.Classify(asset, version).Kind;
    }

    private static List<FileInfo> EnumerateContentFiles(string root, CancellationToken cancellationToken)
    {
        var files = new List<FileInfo>();
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, path);
            if (relative.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
                relative.Equals(ArchiveFileName, StringComparison.OrdinalIgnoreCase) ||
                relative.Equals(ArchiveFileName + ".partial", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var info = new FileInfo(path);
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new DirectFabException("Le contenu du package contient un lien ou point de jonction non autorisé.");
            }
            files.Add(info);
        }
        return files;
    }

    private static async Task CreateArchiveAsync(
        string root,
        IReadOnlyList<FileInfo> files,
        string archivePath,
        long totalBytes,
        LibraryPackageStorageMode storageMode,
        IProgress<DirectFabDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(
            archivePath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        long completedBytes = 0;
        var buffer = new byte[BufferSize];
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, file.FullName).Replace('\\', '/');
            var entry = archive.CreateEntry(relative, ResolveCompressionLevel(storageMode, file.Extension));
            await using var source = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = entry.Open();
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                completedBytes += read;
            }
            progress?.Report(new DirectFabDownloadProgress(
                totalBytes == 0 ? 1 : Math.Clamp((double)completedBytes / totalBytes, 0, 1),
                $"{FormatStorageMode(storageMode)} · {completedBytes / 1024d / 1024d:0.0} MB",
                completedBytes,
                totalBytes));
        }
    }

    private static bool IsZipStorageMode(LibraryPackageStorageMode mode) =>
        mode is not LibraryPackageStorageMode.Uncompressed;

    private static CompressionLevel ResolveCompressionLevel(
        LibraryPackageStorageMode mode,
        string extension)
    {
        return mode switch
        {
            LibraryPackageStorageMode.ZipStore => CompressionLevel.NoCompression,
            LibraryPackageStorageMode.ZipFastest => CompressionLevel.Fastest,
            LibraryPackageStorageMode.ZipBalanced when IsAlreadyCompressed(extension) =>
                CompressionLevel.NoCompression,
            LibraryPackageStorageMode.ZipBalanced => CompressionLevel.Optimal,
            LibraryPackageStorageMode.ZipHigh => CompressionLevel.Optimal,
            LibraryPackageStorageMode.ZipUltra => CompressionLevel.SmallestSize,
            _ => CompressionLevel.NoCompression,
        };
    }

    private static bool IsAlreadyCompressed(string extension) => extension.ToLowerInvariant() switch
    {
        ".7z" or ".avif" or ".exr" or ".gif" or ".gz" or ".jpeg" or ".jpg" or
        ".mp3" or ".mp4" or ".ogg" or ".png" or ".rar" or ".webm" or ".webp" or
        ".zip" => true,
        _ => false,
    };

    private static string FormatStorageMode(LibraryPackageStorageMode mode) => mode switch
    {
        LibraryPackageStorageMode.ZipStore => "ZIP stockage",
        LibraryPackageStorageMode.ZipFastest => "ZIP rapide",
        LibraryPackageStorageMode.ZipBalanced => "ZIP moyen",
        LibraryPackageStorageMode.ZipHigh => "ZIP élevé",
        LibraryPackageStorageMode.ZipUltra => "ZIP ultra",
        _ => "sans compression",
    };

    private static async Task WriteManifestAsync(
        string root,
        LibraryPackageManifest manifest,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, ManifestFileName);
        var temporary = path + ".partial";
        await using (var output = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         BufferSize,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await JsonSerializer.SerializeAsync(output, manifest, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        File.Move(temporary, path, overwrite: true);
    }

    private static void RemoveArchivedContent(string root, IReadOnlyList<FileInfo> files)
    {
        foreach (var file in files)
        {
            if (file.Exists) file.Delete();
        }
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            {
                throw new DirectFabException("Nettoyage refusé : le package contient une jonction.");
            }
            if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }
    }

    private static bool IsEntryPoint(string extension) =>
        extension.Equals(".uproject", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".uplugin", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase) ||
        extension.Equals(".glb", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeExtension(string value) =>
        string.IsNullOrWhiteSpace(value) ? "[sans extension]" : value.ToLowerInvariant();

    private static string NormalizeSource(RemoteFabAsset asset) =>
        asset.Source.Contains("quixel", StringComparison.OrdinalIgnoreCase) ||
        asset.Namespace.Contains("megascans", StringComparison.OrdinalIgnoreCase)
            ? "Quixel"
            : "Fab";
}
