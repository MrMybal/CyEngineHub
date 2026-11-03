using System.IO.Compression;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;
using System.Text.Json;

namespace CyEngineHub.Fab.Packaging;

/// <summary>Imports completed browser files by copy. Never repackages or deletes the user's original.</summary>
public static class ConnectorLibraryPackageService
{
    public static IReadOnlyList<string> DiscoverPackages(string marketplaceId, IEnumerable<string> libraries)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var queue = new Queue<string>(libraries.Where(Directory.Exists).Select(Path.GetFullPath));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<string> packages = [];
        while (queue.TryDequeue(out var directory))
        {
            if (!visited.Add(directory)) continue;
            if (visited.Count > 10_000 || watch.Elapsed > TimeSpan.FromSeconds(10)) throw new IOException("Indexation des packages limitée : relancez depuis la bibliothèque.");
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            var manifest = Path.Combine(directory, LibraryPackageService.ManifestFileName);
            if (File.Exists(manifest))
            {
                try
                {
                    if (new FileInfo(manifest).Length > 1024 * 1024) continue;
                    using var json = JsonDocument.Parse(File.ReadAllText(manifest));
                    if (json.RootElement.GetProperty("source").GetString() == marketplaceId) packages.Add(directory);
                }
                catch (Exception error) when (error is JsonException or KeyNotFoundException or InvalidOperationException or IOException or UnauthorizedAccessException) { }
                continue;
            }
            foreach (var child in Directory.EnumerateDirectories(directory)) queue.Enqueue(child);
        }
        return packages;
    }

    /// <summary>Copies one explicitly selected kit, preserving the entire relative dependency tree.</summary>
    public static async Task MaterializeDirectoryAsync(string source, string destination, CancellationToken token = default)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        if (root == Path.GetPathRoot(root) || target.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Choisissez le dossier d’un kit, pas une racine disque ni un parent du staging.");
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("Le staging doit être vide.");
        var queue = new Queue<string>(); queue.Enqueue(root);
        var files = new List<string>();
        long size = 0; var entries = 0;
        // Preflight before copying. Do not follow junctions/symlinks into another kit or user folder.
        while (queue.TryDequeue(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Lien de dossier non autorisé dans le kit.");
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (++entries > 100_000) throw new InvalidDataException("Kit au-delà de 100 000 entrées.");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Lien de fichier ou dossier non autorisé dans le kit.");
                if ((attributes & FileAttributes.Directory) != 0) { queue.Enqueue(path); continue; }
                size = checked(size + new FileInfo(path).Length);
                if (size > 200L * 1024 * 1024 * 1024) throw new InvalidDataException("Kit au-delà de 200 Gio.");
                files.Add(path);
            }
        }
        if (files.Count == 0) throw new InvalidDataException("Le kit sélectionné est vide.");
        Directory.CreateDirectory(target);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Le kit a changé pendant la copie.");
            var outputPath = Path.Combine(target, Path.GetRelativePath(root, file));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await using var input = File.OpenRead(file);
            await using var output = new FileStream(outputPath, FileMode.CreateNew);
            await input.CopyToAsync(output, token).ConfigureAwait(false);
        }
    }

    public static async Task MaterializeFileAsync(string file, string destination, CancellationToken token = default)
    {
        if (!File.Exists(file)) throw new FileNotFoundException("Fichier local absent.", file);
        if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new IOException("Le staging doit être vide.");
        Directory.CreateDirectory(destination);
        if (!Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await using var input = File.OpenRead(file);
            await using var output = File.Create(Path.Combine(destination, Path.GetFileName(file)));
            await input.CopyToAsync(output, token); return;
        }
        using var zip = ZipFile.OpenRead(file);
        if (zip.Entries.Count > 100_000 || zip.Entries.Sum(e => e.Length) > 200L * 1024 * 1024 * 1024)
            throw new InvalidDataException("Archive au-delà des limites de sécurité.");
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        // Preflight all names before extracting even one file.
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries)
        {
            var path = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !names.Add(path) ||
                entry.FullName.Contains(':') || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                throw new InvalidDataException("Chemin d’archive non autorisé.");
        }
        foreach (var entry in zip.Entries)
        {
            token.ThrowIfCancellationRequested();
            var path = Path.GetFullPath(Path.Combine(root, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (entry.Name.Length == 0) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using var input = entry.Open(); await using var output = new FileStream(path, FileMode.CreateNew);
            await input.CopyToAsync(output, token);
        }
    }

    public static async Task<DirectFabDownloadResult> ImportAsync(string file, string marketplaceId,
        LibraryPackageStorageMode mode, MarketplaceStorageProfile storage, string scratchRoot,
        IProgress<DirectFabDownloadProgress>? progress = null, CancellationToken token = default)
    {
        var id = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetFullPath(scratchRoot), id);
        if (Directory.Exists(root)) throw new IOException("Staging déjà présent.");
        try
        {
            if (Directory.Exists(file)) await MaterializeDirectoryAsync(file, root, token).ConfigureAwait(false);
            else await MaterializeFileAsync(file, root, token).ConfigureAwait(false);
            var title = Directory.Exists(file) ? Path.GetFileName(Path.TrimEndingDirectorySeparator(file)) : Path.GetFileNameWithoutExtension(file);
            var asset = new RemoteFabAsset(id, "local", title, "", marketplaceId, "ASSET_PACK", null, null, [], []);
            var version = new RemoteFabProjectVersion(id, [], [], []);
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Select(p => new FileInfo(p)).ToArray();
            var result = await LibraryPackageService.PackageAsync(asset, version,
                new(id, asset.Title, root, files.Length, files.Sum(f => f.Length), DateTimeOffset.UtcNow), mode, progress, token).ConfigureAwait(false);
            return await MarketplaceStorageRouter.PlaceAsync(result, storage, progress, token).ConfigureAwait(false);
        }
        finally
        {
            // Only the fresh, owned GUID staging; never the selected source or a library root.
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
