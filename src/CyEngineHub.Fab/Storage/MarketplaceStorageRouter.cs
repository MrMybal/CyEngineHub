using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Fab.Storage;

public sealed record MarketplaceLibraryLocation(
    string Path,
    long MaximumBytes = 0,
    bool Enabled = true);

public sealed record MarketplaceStorageProfile(
    string Marketplace,
    string CacheDirectory,
    string DownloadDirectory,
    IReadOnlyList<MarketplaceLibraryLocation> Libraries);

public sealed record MarketplaceStorageChoice(
    string LibraryRoot,
    long UsedBytes,
    long? QuotaRemainingBytes,
    long DiskFreeBytes,
    int Priority);

public static class MarketplaceStorageRouter
{
    private const int BufferSize = 1024 * 1024;
    private static readonly SemaphoreSlim PlacementGate = new(1, 1);

    public static async Task<DirectFabDownloadResult> PlaceAsync(
        DirectFabDownloadResult download,
        MarketplaceStorageProfile profile,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(profile);
        await PlacementGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var requiredBytes = Math.Max(
                0,
                download.StoredBytes ?? download.DownloadedBytes);
            var choice = await SelectLibraryAsync(profile, requiredBytes, cancellationToken)
                .ConfigureAwait(false);
            return await MoveIntoLibraryAsync(download, choice, progress, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            PlacementGate.Release();
        }
    }

    public static async Task<MarketplaceStorageChoice> SelectLibraryAsync(
        MarketplaceStorageProfile profile,
        long requiredBytes,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        for (var index = 0; index < profile.Libraries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var location = profile.Libraries[index];
            if (!location.Enabled || string.IsNullOrWhiteSpace(location.Path)) continue;
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.Path));
            Directory.CreateDirectory(root);
            var usedBytes = await Task.Run(
                    () => MeasureDirectory(root, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);
            var quotaRemaining = location.MaximumBytes > 0
                ? Math.Max(0, location.MaximumBytes - usedBytes)
                : (long?)null;
            var diskFree = GetDiskFreeBytes(root);
            var available = quotaRemaining.HasValue
                ? Math.Min(quotaRemaining.Value, diskFree)
                : diskFree;
            if (available >= requiredBytes)
            {
                return new MarketplaceStorageChoice(
                    root,
                    usedBytes,
                    quotaRemaining,
                    diskFree,
                    index + 1);
            }
            failures.Add($"#{index + 1} {root} ({FormatBytes(available)} disponible)");
        }

        throw new IOException(
            $"Aucune bibliothèque {profile.Marketplace} ne dispose de {FormatBytes(requiredBytes)}. " +
            (failures.Count == 0
                ? "Ajoutez au moins un emplacement actif dans Paramètres → Marketplaces."
                : string.Join(" · ", failures)));
    }

    private static async Task<DirectFabDownloadResult> MoveIntoLibraryAsync(
        DirectFabDownloadResult download,
        MarketplaceStorageChoice choice,
        IProgress<DirectFabDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(download.DestinationDirectory));
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        if (IsChildOrSame(choice.LibraryRoot, source)) return download;

        var sourceGroup = Path.GetFileName(Path.GetDirectoryName(source));
        if (string.IsNullOrWhiteSpace(sourceGroup)) sourceGroup = "Packages";
        var destinationParent = Path.Combine(choice.LibraryRoot, sourceGroup);
        Directory.CreateDirectory(destinationParent);
        var destination = GetUniqueDirectory(Path.Combine(destinationParent, Path.GetFileName(source)));
        var incoming = destination + $".incoming-{Guid.NewGuid():N}";
        progress?.Report(new DirectFabDownloadProgress(
            null,
            $"Stockage dans la bibliothèque #{choice.Priority}…",
            0,
            download.StoredBytes ?? download.DownloadedBytes));

        try
        {
            if (string.Equals(
                    Path.GetPathRoot(source),
                    Path.GetPathRoot(destination),
                    StringComparison.OrdinalIgnoreCase))
            {
                Directory.Move(source, incoming);
            }
            else
            {
                await CopyDirectoryAsync(source, incoming, progress, cancellationToken)
                    .ConfigureAwait(false);
                Directory.Delete(source, recursive: true);
            }
            Directory.Move(incoming, destination);
        }
        catch
        {
            if (Directory.Exists(incoming) && Directory.Exists(source))
            {
                Directory.Delete(incoming, recursive: true);
            }
            // Si la source a déjà été déplacée, le dossier .incoming est volontairement
            // conservé : aucune donnée validée ne doit être supprimée après un échec final.
            throw;
        }

        var manifestPath = string.IsNullOrWhiteSpace(download.ManifestPath)
            ? null
            : Path.Combine(destination, Path.GetRelativePath(source, download.ManifestPath));
        progress?.Report(new DirectFabDownloadProgress(
            1,
            $"Package placé dans la bibliothèque #{choice.Priority}.",
            download.StoredBytes ?? download.DownloadedBytes,
            download.StoredBytes ?? download.DownloadedBytes));
        return download with
        {
            DestinationDirectory = destination,
            ManifestPath = manifestPath,
        };
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        IProgress<DirectFabDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        var files = EnumerateSafeFiles(source, cancellationToken).ToArray();
        var totalBytes = files.Sum(file => file.Length);
        long completedBytes = 0;
        var buffer = new byte[BufferSize];
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file.FullName);
            var target = Path.Combine(destination, relative);
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
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                completedBytes += read;
                progress?.Report(new DirectFabDownloadProgress(
                    totalBytes == 0 ? 1 : (double)completedBytes / totalBytes,
                    "Déplacement inter-disques…",
                    completedBytes,
                    totalBytes));
            }
        }
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
                if ((info.Attributes & FileAttributes.ReparsePoint) == 0) yield return info;
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

    private static long MeasureDirectory(string root, CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (var file in EnumerateSafeFiles(root, cancellationToken))
        {
            total = checked(total + file.Length);
        }
        return total;
    }

    private static long GetDiskFreeBytes(string path)
    {
        var volume = Path.GetPathRoot(path)
            ?? throw new IOException($"Volume introuvable pour {path}.");
        return new DriveInfo(volume).AvailableFreeSpace;
    }

    private static bool IsChildOrSame(string root, string target)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(target));
        return relative == "." ||
               (!Path.IsPathRooted(relative) &&
                !relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static string GetUniqueDirectory(string desired)
    {
        if (!Directory.Exists(desired) && !File.Exists(desired)) return desired;
        for (var index = 2; index < 10_000; index++)
        {
            var candidate = $"{desired}-{index}";
            if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
        }
        throw new IOException("Impossible de réserver un nom de dossier de bibliothèque.");
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.##} {units[index]}";
    }
}
