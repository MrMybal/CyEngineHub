using System.Runtime.InteropServices;
using CyEngineHub.Fab.Packaging;
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
    private static readonly Dictionary<string, TaskCompletionSource> ActivePlacements = [];

    public static Task<DirectFabDownloadResult> PlaceAsync(
        DirectFabDownloadResult download,
        MarketplaceStorageProfile profile,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        PlaceAsync(download, profile, destinationGroup: null, progress, cancellationToken);

    internal static async Task<DirectFabDownloadResult> PlaceAsync(
        DirectFabDownloadResult download, MarketplaceStorageProfile profile, string? destinationGroup,
        IProgress<DirectFabDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(download);
        ArgumentNullException.ThrowIfNull(profile);
        var requiredBytes = Math.Max(0, download.StoredBytes ?? download.DownloadedBytes);
        var source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(download.DestinationDirectory));
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source);
        var diskProgress = new LibraryPackageProgress(progress, DirectFabDownloadPhase.Placement);
        diskProgress.Stage("Sélection de la bibliothèque de destination…");
        var failures = new List<string>();
        foreach (var (location, root, priority) in EnabledLibraries(profile))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await ReserveLibraryAsync(root, cancellationToken).ConfigureAwait(false);
            try
            {
                var choice = await InspectLibraryAsync(location, root, priority, measureUsage: location.MaximumBytes > 0, cancellationToken)
                    .ConfigureAwait(false);
                var alreadyInside = IsChildOrSame(root, source);
                var quotaGrowth = alreadyInside ? 0 : requiredBytes;
                var sameVolume = string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase);
                if (choice.QuotaRemainingBytes < quotaGrowth || (alreadyInside && location.MaximumBytes > 0 && choice.UsedBytes > location.MaximumBytes) ||
                    (!sameVolume && choice.DiskFreeBytes < requiredBytes))
                {
                    failures.Add(DescribeSpace(choice));
                    continue;
                }
                if (alreadyInside && destinationGroup is null)
                {
                    diskProgress.Stage("Package disponible dans la bibliothèque.", finished: true);
                    return download;
                }
                if (IsChildOrSame(source, root)) throw new IOException("La bibliothèque ne peut pas se trouver dans le package à déplacer.");
                var placed = await MoveIntoLibraryAsync(download, choice, source, destinationGroup, sameVolume, diskProgress, cancellationToken)
                    .ConfigureAwait(false);
                if (placed is not null) return placed;
                failures.Add(DescribeSpace(choice));
            }
            finally
            {
                lock (ActivePlacements)
                {
                    ActivePlacements.Remove(root, out var reservation);
                    reservation!.SetResult();
                }
            }
        }
        throw InsufficientSpace(profile, requiredBytes, failures);
    }

    // Hold the quota check and publication together, including when two configured roots overlap.
    // Independent libraries can accept packages simultaneously.
    private static async Task ReserveLibraryAsync(string root, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task busy;
            lock (ActivePlacements)
            {
                var conflict = ActivePlacements.FirstOrDefault(active =>
                    IsChildOrSame(root, active.Key) || IsChildOrSame(active.Key, root)).Value;
                if (conflict is null)
                {
                    ActivePlacements.Add(root, new(TaskCreationOptions.RunContinuationsAsynchronously));
                    return;
                }
                busy = conflict.Task;
            }
            await busy.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public static async Task<MarketplaceStorageChoice> SelectLibraryAsync(
        MarketplaceStorageProfile profile,
        long requiredBytes,
        CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        foreach (var (location, root, priority) in EnabledLibraries(profile))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var choice = await InspectLibraryAsync(location, root, priority, measureUsage: true, cancellationToken)
                .ConfigureAwait(false);
            if ((choice.QuotaRemainingBytes ?? long.MaxValue) >= requiredBytes && choice.DiskFreeBytes >= requiredBytes) return choice;
            failures.Add(DescribeSpace(choice));
        }
        throw InsufficientSpace(profile, requiredBytes, failures);
    }

    private static IEnumerable<(MarketplaceLibraryLocation Location, string Root, int Priority)> EnabledLibraries(MarketplaceStorageProfile profile)
    {
        for (var index = 0; index < profile.Libraries.Count; index++)
        {
            var location = profile.Libraries[index];
            if (location.Enabled && !string.IsNullOrWhiteSpace(location.Path))
                yield return (location, Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.Path)), index + 1);
        }
    }

    private static async Task<MarketplaceStorageChoice> InspectLibraryAsync(MarketplaceLibraryLocation location, string root, int priority,
        bool measureUsage, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var used = measureUsage ? await Task.Run(() => MeasureDirectory(root, cancellationToken), cancellationToken).ConfigureAwait(false) : 0;
        long? remaining = location.MaximumBytes > 0 ? Math.Max(0, location.MaximumBytes - used) : null;
        return new(root, used, remaining, GetDiskFreeBytes(root), priority);
    }

    private static string DescribeSpace(MarketplaceStorageChoice choice) =>
        $"#{choice.Priority} {choice.LibraryRoot} ({FormatBytes(Math.Min(choice.QuotaRemainingBytes ?? long.MaxValue, choice.DiskFreeBytes))} disponible)";

    private static IOException InsufficientSpace(MarketplaceStorageProfile profile, long requiredBytes, List<string> failures) =>
        new(
            $"Aucune bibliothèque {profile.Marketplace} ne dispose de {FormatBytes(requiredBytes)}. " +
            (failures.Count == 0
                ? "Ajoutez au moins un emplacement actif dans Paramètres → Marketplaces."
                : string.Join(" · ", failures)));

    private static async Task<DirectFabDownloadResult?> MoveIntoLibraryAsync(
        DirectFabDownloadResult download,
        MarketplaceStorageChoice choice,
        string source,
        string? destinationGroup,
        bool sameVolume,
        LibraryPackageProgress progress,
        CancellationToken cancellationToken)
    {
        var sourceGroup = destinationGroup ?? Path.GetFileName(Path.GetDirectoryName(source));
        if (string.IsNullOrWhiteSpace(sourceGroup)) sourceGroup = "Packages";
        var destinationParent = Path.Combine(choice.LibraryRoot, sourceGroup);
        Directory.CreateDirectory(destinationParent);
        var destination = GetUniqueDirectory(Path.Combine(destinationParent, Path.GetFileName(source)));
        progress.Stage($"Stockage dans la bibliothèque #{choice.Priority}…");

        cancellationToken.ThrowIfCancellationRequested();
        string? cleanupWarning = null;
        if (sameVolume)
        {
            try { Directory.Move(source, destination); }
            catch (IOException exception) when ((exception.HResult & 0xffff) == (OperatingSystem.IsWindows() ? 17 : 18))
            {
                // A mount point can put two paths with the same apparent root on different volumes.
                sameVolume = false;
            }
        }
        if (!sameVolume)
        {
            if (GetDiskFreeBytes(destinationParent) < Math.Max(0, download.StoredBytes ?? download.DownloadedBytes))
                return null;
            cleanupWarning = await CopyPublishAndCleanAsync(source, destination, progress, cancellationToken).ConfigureAwait(false);
        }

        var manifestPath = string.IsNullOrWhiteSpace(download.ManifestPath)
            ? null
            : Path.Combine(destination, Path.GetRelativePath(source, download.ManifestPath));
        progress.Stage(cleanupWarning ?? $"Package placé dans la bibliothèque #{choice.Priority}.", finished: true);
        return download with
        {
            DestinationDirectory = destination,
            ManifestPath = manifestPath,
        };
    }

    internal static async Task<string?> CopyPublishAndCleanAsync(string source, string destination,
        LibraryPackageProgress progress, CancellationToken cancellationToken)
    {
        var incoming = destination + $".incoming-{Guid.NewGuid():N}";
        progress.Stage("Inventaire des fichiers à copier…");
        var tree = PackageFileTree.Read(source, cancellationToken);
        try
        {
            await CopyDirectoryAsync(source, incoming, tree, progress, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            progress.Stage("Publication du package dans la bibliothèque…");
            Directory.Move(incoming, destination);
        }
        catch
        {
            if (Directory.Exists(incoming)) Directory.Delete(incoming, recursive: true);
            throw;
        }
        try
        {
            progress.Stage("Nettoyage de la copie source…");
            tree.RemoveCopiedContent(source);
            PackageFileTree.RequirePlainPath(source, source);
            Directory.Delete(source);
            return null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return $"Package publié dans {destination}. Nettoyage incomplet de {source} : {exception.Message}";
        }
    }

    private static async Task CopyDirectoryAsync(
        string source,
        string destination,
        PackageFileTree tree,
        LibraryPackageProgress progress,
        CancellationToken cancellationToken)
    {
        foreach (var directory in tree.Directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        var totalBytes = tree.Files.Sum(file => file.Length);
        const string message = "Copie vers la bibliothèque sur l’autre disque";
        progress.Bytes(message, 0, totalBytes, force: true);
        long completedBytes = 0;
        var buffer = new byte[BufferSize];
        foreach (var file in tree.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PackageFileTree.RequirePlainPath(source, file.FullName);
            var relative = Path.GetRelativePath(source, file.FullName);
            var target = Path.Combine(destination, relative);
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
                progress.Bytes(message, completedBytes, totalBytes);
            }
        }
        progress.Bytes(message, completedBytes, totalBytes, force: true);
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
        if (OperatingSystem.IsWindows())
        {
            if (GetDiskFreeSpaceExW(path, out var freeBytes, out _, out _))
                return (long)Math.Min((ulong)long.MaxValue, freeBytes);
            throw new IOException($"Espace libre inaccessible pour {path}.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        }
        return DriveInfo.GetDrives().Where(drive => IsChildOrSame(drive.Name, path))
            .MaxBy(drive => drive.Name.Length)?.AvailableFreeSpace ?? throw new IOException($"Volume introuvable pour {path}.");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern bool GetDiskFreeSpaceExW(string directory, out ulong available, out ulong total, out ulong free);

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
