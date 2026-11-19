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

public sealed record LibraryPackageDerivation(
    string SourceArtifactId,
    string Name,
    string Notes,
    DateTimeOffset CreatedAt);

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
    DateTimeOffset PackagedAt,
    LibraryPackageDerivation? Derivation = null);

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
        => await PackageCoreAsync(
                asset,
                version,
                download,
                storageMode,
                null,
                null,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

    public static async Task<DirectFabDownloadResult> PackageDerivedAsync(
        RemoteFabAsset asset,
        RemoteFabProjectVersion version,
        DirectFabDownloadResult download,
        LibraryPackageStorageMode storageMode,
        LibraryPackageContentKind contentKind,
        LibraryPackageDerivation derivation,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => await PackageCoreAsync(
                asset,
                version,
                download,
                storageMode,
                contentKind,
                derivation,
                progress,
                cancellationToken)
            .ConfigureAwait(false);

    private static async Task<DirectFabDownloadResult> PackageCoreAsync(
        RemoteFabAsset asset,
        RemoteFabProjectVersion version,
        DirectFabDownloadResult download,
        LibraryPackageStorageMode storageMode,
        LibraryPackageContentKind? contentKindOverride,
        LibraryPackageDerivation? derivation,
        IProgress<DirectFabDownloadProgress>? progress,
        CancellationToken cancellationToken)
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

        var diskProgress = new LibraryPackageProgress(progress, DirectFabDownloadPhase.Packaging);
        diskProgress.Stage("Inventaire des fichiers du package…");
        PackageFileTree tree;
        try { tree = PackageFileTree.Read(root, cancellationToken); }
        catch (InvalidDataException exception) { throw new DirectFabException(exception.Message, exception); }
        var files = tree.Files;
        files.RemoveAll(file => string.Equals(file.DirectoryName, root, StringComparison.Ordinal) &&
            (file.Name.Equals(ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
             file.Name.Equals(ManifestFileName + ".partial", StringComparison.OrdinalIgnoreCase) ||
             file.Name.Equals(ArchiveFileName, StringComparison.OrdinalIgnoreCase) ||
             file.Name.Equals(ArchiveFileName + ".partial", StringComparison.OrdinalIgnoreCase)));
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
        var contentKind = contentKindOverride ?? Classify(asset, version, files);
        string? archiveFile = null;
        long storedBytes = uncompressedBytes;

        try
        {
            if (IsZipStorageMode(storageMode))
            {
                var archivePath = Path.Combine(root, ArchiveFileName);
                var temporaryArchive = archivePath + ".partial";
                if (File.Exists(temporaryArchive)) File.Delete(temporaryArchive);
                await CreateArchiveAsync(
                        root,
                        files,
                        temporaryArchive,
                        uncompressedBytes,
                        storageMode,
                        diskProgress,
                        cancellationToken)
                    .ConfigureAwait(false);
                File.Move(temporaryArchive, archivePath, overwrite: true);
                archiveFile = ArchiveFileName;
                storedBytes = new FileInfo(archivePath).Length;
            }

            var manifest = new LibraryPackageManifest(
                derivation is null ? 1 : 2,
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
                DateTimeOffset.UtcNow,
                derivation);
            diskProgress.Stage("Écriture du manifeste du package…");
            await WriteManifestAsync(root, manifest, cancellationToken).ConfigureAwait(false);

            if (IsZipStorageMode(storageMode))
            {
                diskProgress.Stage("Nettoyage des fichiers archivés…");
                tree.RemoveCopiedContent(root);
            }
            diskProgress.Stage(IsZipStorageMode(storageMode)
                    ? $"Package {FormatStorageMode(storageMode)} et manifeste CyEngineHub prêts."
                    : "Manifeste prêt, fichiers conservés sans compression.", finished: true);
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
        var hasRawModel = files.Any(file => file.Extension.Equals(".usd", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".usda", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".usdc", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".usdz", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".obj", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".glb", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".tga", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".exr", StringComparison.OrdinalIgnoreCase) ||
                                           file.Extension.Equals(".wav", StringComparison.OrdinalIgnoreCase));
        var hasNativeUnreal = files.Any(file => file.Extension.Equals(".uasset", StringComparison.OrdinalIgnoreCase) ||
                                               file.Extension.Equals(".umap", StringComparison.OrdinalIgnoreCase));
        var hasSourceDcc = files.Any(file => file.Extension.ToLowerInvariant() is ".blend" or ".max" or ".c4d" or ".ma" or ".mb");
        var hasInterchange = files.Any(file => file.Extension.ToLowerInvariant() is ".fbx" or ".obj" or ".gltf" or ".glb" or ".usd" or ".usda" or ".usdc" or ".usdz");
        if (!hasNativeUnreal && hasSourceDcc && !hasInterchange) return LibraryPackageContentKind.Unknown;
        if (hasRawModel && !hasNativeUnreal) return LibraryPackageContentKind.RawAsset;
        if (!hasNativeUnreal && asset.Source.StartsWith("cyenginehub.marketplace-", StringComparison.Ordinal)) return LibraryPackageContentKind.Unknown;
        return RemoteFabAssetClassifier.Classify(asset, version).Kind;
    }

    private static async Task CreateArchiveAsync(
        string root,
        IReadOnlyList<FileInfo> files,
        string archivePath,
        long totalBytes,
        LibraryPackageStorageMode storageMode,
        LibraryPackageProgress progress,
        CancellationToken cancellationToken)
    {
        var message = $"Création du package {FormatStorageMode(storageMode)}";
        progress.Bytes(message, 0, totalBytes, force: true);
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
            PackageFileTree.RequirePlainPath(root, file.FullName);
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
                progress.Bytes(message, completedBytes, totalBytes);
            }
        }
        progress.Bytes(message, completedBytes, totalBytes, force: true);
        progress.Stage("Fermeture de l’archive ZIP…");
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
        asset.Source.StartsWith("cyenginehub.marketplace-", StringComparison.Ordinal)
            ? asset.Source
            :
        asset.Source.Contains("quixel", StringComparison.OrdinalIgnoreCase) ||
        asset.Namespace.Contains("megascans", StringComparison.OrdinalIgnoreCase)
            ? "Quixel"
            : "Fab";
}
