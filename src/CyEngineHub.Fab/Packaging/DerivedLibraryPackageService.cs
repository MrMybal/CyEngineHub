using System.IO.Compression;
using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Fab.Packaging;

public sealed record DerivedLibraryPackageRequest(
    RemoteFabAsset Asset,
    RemoteFabProjectVersion SourceVersion,
    string SourceDirectory,
    string StagingRoot,
    string Name,
    string Notes,
    LibraryPackageContentKind ContentKind,
    LibraryPackageStorageMode StorageMode,
    string? ArtifactId = null);

public static class DerivedLibraryPackageService
{
    private const int BufferSize = 128 * 1024;
    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<DirectFabDownloadResult> CreateAsync(
        DerivedLibraryPackageRequest request,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.SourceDirectory));
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Le dossier source ne peut pas être un lien ou une jonction.");
        var name = SanitizeName(request.Name);
        var stagingRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.StagingRoot));
        Directory.CreateDirectory(stagingRoot);
        var destination = Path.Combine(
            stagingRoot,
            $"{SanitizeName(request.Asset.AssetId)}-derived-{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");
        if (IsChildOrSame(source, destination) || IsChildOrSame(destination, source))
            throw new InvalidDataException("Le dossier de dérivé doit être séparé de sa source.");
        Directory.CreateDirectory(destination);
        try
        {
            progress?.Report(new DirectFabDownloadProgress(null, "Préparation du dérivé…", 0, null));
            await MaterializeAsync(
                    source,
                    destination,
                    request.ContentKind,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);

            var files = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).ToArray();
            if (files.Length == 0) throw new InvalidDataException("Le dossier source ne contient aucun fichier à empaqueter.");
            var bytes = files.Sum(path => new FileInfo(path).Length);
            var artifactId = string.IsNullOrWhiteSpace(request.ArtifactId)
                ? $"derived:{Guid.NewGuid():N}"
                : request.ArtifactId.Trim();
            var derivedVersion = request.SourceVersion with { ArtifactId = artifactId };
            var download = new DirectFabDownloadResult(
                request.Asset.AssetId,
                name,
                destination,
                files.Length,
                bytes,
                DateTimeOffset.UtcNow,
                PackageKind: request.ContentKind.ToString(),
                UncompressedBytes: bytes,
                StoredBytes: bytes);
            return await LibraryPackageService.PackageDerivedAsync(
                    request.Asset,
                    derivedVersion,
                    download,
                    request.StorageMode,
                    request.ContentKind,
                    new LibraryPackageDerivation(
                        request.SourceVersion.ArtifactId,
                        name,
                        request.Notes.Trim(),
                        DateTimeOffset.UtcNow),
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            throw;
        }
    }

    public static async Task MaterializeAsync(
        string sourceDirectory,
        string destinationDirectory,
        LibraryPackageContentKind contentKind = LibraryPackageContentKind.Unknown,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDirectory));
        var destination = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destinationDirectory));
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new IOException("Le dossier de destination du dérivé doit être vide.");
        Directory.CreateDirectory(destination);
        var manifest = await ReadSourceManifestAsync(source, cancellationToken).ConfigureAwait(false);
        var archive = manifest?.ArchiveFile;
        if (!string.IsNullOrWhiteSpace(archive))
        {
            await ExtractArchiveAsync(
                    SafeChildPath(source, archive), destination, progress, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var contentSource = contentKind is LibraryPackageContentKind.ContentPack or LibraryPackageContentKind.RawAsset &&
                            Directory.Exists(Path.Combine(source, "Content"))
            ? Path.Combine(source, "Content")
            : source;
        var contentDestination = !string.Equals(contentSource, source, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(destination, "Content")
            : destination;
        await CopySourceAsync(contentSource, contentDestination, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<LibraryPackageManifest?> ReadSourceManifestAsync(
        string source,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(source, LibraryPackageService.ManifestFileName);
        if (!File.Exists(path)) return null;
        await using var input = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<LibraryPackageManifest>(
                input,
                ManifestJsonOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task CopySourceAsync(
        string source,
        string destination,
        IProgress<DirectFabDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = EnumerateSafeFiles(source, cancellationToken)
            .Where(file => !IsPackageSidecar(source, file.FullName))
            .Where(file => !IsGeneratedProjectFile(source, file.FullName))
            .ToArray();
        var total = files.Sum(file => file.Length);
        long completed = 0;
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(source, file.FullName);
            var target = SafeChildPath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
            completed += file.Length;
            progress?.Report(new DirectFabDownloadProgress(
                total == 0 ? 1 : (double)completed / total,
                "Copie du contenu dérivé…",
                completed,
                total));
        }
    }

    private static bool IsGeneratedProjectFile(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var first = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is not null && first.Equals("Saved", StringComparison.OrdinalIgnoreCase) ||
               first is not null && first.Equals("Intermediate", StringComparison.OrdinalIgnoreCase) ||
               first is not null && first.Equals("DerivedDataCache", StringComparison.OrdinalIgnoreCase) ||
               first is not null && first.Equals(".vs", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task ExtractArchiveAsync(
        string archivePath,
        string destination,
        IProgress<DirectFabDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(input, ZipArchiveMode.Read, leaveOpen: true);
        var files = archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)).ToArray();
        long completed = 0;
        foreach (var entry in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = SafeChildPath(destination, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var source = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(output, BufferSize, cancellationToken).ConfigureAwait(false);
            completed += entry.Length;
            progress?.Report(new DirectFabDownloadProgress(null, "Extraction du package source…", completed, null));
        }
    }

    private static IEnumerable<FileInfo> EnumerateSafeFiles(string root, CancellationToken cancellationToken)
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
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Le contenu source contient un lien non autorisé.");
                yield return info;
            }
            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Le contenu source contient une jonction non autorisée.");
                pending.Push(directory);
            }
        }
    }

    private static bool IsPackageSidecar(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative.Equals(LibraryPackageService.ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
               relative.Equals(LibraryPackageService.ArchiveFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeChildPath(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Chemin absolu interdit dans le package.");
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsChildOrSame(root, target) || string.Equals(Path.GetFullPath(root), target, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Le package contient un chemin sortant de son dossier.");
        return target;
    }

    private static bool IsChildOrSame(string root, string target)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(target));
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string SanitizeName(string value)
    {
        var cleaned = new string(value.Trim().Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character).ToArray());
        if (string.IsNullOrWhiteSpace(cleaned)) throw new InvalidDataException("Le nom du dérivé est obligatoire.");
        return cleaned.Length <= 96 ? cleaned : cleaned[..96];
    }
}
