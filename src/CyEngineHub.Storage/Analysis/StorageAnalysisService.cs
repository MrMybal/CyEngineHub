using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CyEngineHub.Storage.Models;

namespace CyEngineHub.Storage.Analysis;

public interface IStorageAnalysisService
{
    Task<StorageSnapshot> AnalyzeAsync(
        StorageAnalysisRequest request,
        IProgress<StorageAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class StorageAnalysisService : IStorageAnalysisService
{
    private readonly BoundedDirectorySizer _directorySizer;
    private readonly TimeSpan _maximumTotalDuration;

    public StorageAnalysisService(
        BoundedDirectorySizer? directorySizer = null,
        TimeSpan? maximumTotalDuration = null)
    {
        _directorySizer = directorySizer ?? new BoundedDirectorySizer();
        _maximumTotalDuration = maximumTotalDuration ?? TimeSpan.FromMinutes(2);
    }

    public Task<StorageSnapshot> AnalyzeAsync(
        StorageAnalysisRequest request,
        IProgress<StorageAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(
            () => Analyze(request, progress, cancellationToken),
            cancellationToken);
    }

    private StorageSnapshot Analyze(
        StorageAnalysisRequest request,
        IProgress<StorageAnalysisProgress>? progress,
        CancellationToken cancellationToken)
    {
        var candidates = StoragePathDiscovery.Discover(request);
        var total = candidates.Count + request.GitRepositories.Count;
        var completed = 0;
        var entries = new List<StorageEntry>();
        var diagnostics = new List<string>();
        var stopwatch = Stopwatch.StartNew();

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= _maximumTotalDuration)
            {
                diagnostics.Add(
                    $"Budget global de {_maximumTotalDuration.TotalSeconds:N0} secondes atteint; " +
                    "certains emplacements n’ont pas été analysés.");
                break;
            }

            progress?.Report(new StorageAnalysisProgress(
                completed,
                total,
                $"{candidate.OwnerName} · {candidate.DisplayName}"));
            var measurement = _directorySizer.Analyze(candidate.Path, cancellationToken);
            if (measurement.Exists)
            {
                entries.Add(new StorageEntry(
                    StableId(candidate.Category, candidate.Path),
                    candidate.Category,
                    candidate.Role,
                    candidate.DisplayName,
                    candidate.OwnerName,
                    candidate.OwnerRoot,
                    candidate.Path,
                    measurement.SizeBytes,
                    measurement.FileCount,
                    measurement.DirectoryCount,
                    measurement.LastModified,
                    measurement.IsPartial ? StorageHealth.Partial : StorageHealth.Ready,
                    candidate.IsSafeToClean && !measurement.IsPartial,
                    measurement.Diagnostic));
            }

            completed++;
        }

        foreach (var repository in request.GitRepositories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new StorageAnalysisProgress(
                completed,
                total,
                $"Git LFS · {Path.GetFileName(repository.RepositoryRoot)}"));
            if (repository.Lfs.IsInstalled)
            {
                var ownerName = repository.Owners.Count > 0
                    ? repository.Owners[0].DisplayName
                    : Path.GetFileName(repository.RepositoryRoot);
                var health = repository.Lfs.IsTruncated
                    ? StorageHealth.Partial
                    : string.IsNullOrWhiteSpace(repository.Lfs.Error)
                        ? StorageHealth.Ready
                        : StorageHealth.Warning;
                var diagnostic = repository.Lfs.Error ?? (repository.Lfs.IsTruncated
                    ? "Analyse LFS partielle; aucune estimation de nettoyage n’est proposée."
                    : $"{repository.Lfs.ObjectCount:N0} objet(s) LFS mesuré(s).");
                entries.Add(new StorageEntry(
                    StableId(StorageCategory.GitLfs, repository.RepositoryRoot),
                    StorageCategory.GitLfs,
                    StorageEntryRole.External,
                    "Git LFS",
                    ownerName,
                    repository.RepositoryRoot,
                    Path.Combine(repository.RepositoryRoot, ".git", "lfs", "objects"),
                    repository.Lfs.StorageBytes,
                    repository.Lfs.ObjectCount,
                    0,
                    repository.RefreshedAt,
                    health,
                    false,
                    diagnostic));
            }

            completed++;
        }

        progress?.Report(new StorageAnalysisProgress(total, total, "Inventaire Stockage actualisé"));
        return new StorageSnapshot(
            entries
                .OrderBy(entry => entry.Role)
                .ThenByDescending(entry => entry.SizeBytes)
                .ThenBy(entry => entry.OwnerName, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            DetectDrives(entries, request.AdditionalRoots, diagnostics),
            diagnostics,
            DateTimeOffset.UtcNow);
    }

    private static DriveStorageSnapshot[] DetectDrives(
        IReadOnlyList<StorageEntry> entries,
        IReadOnlyList<AdditionalStorageRoot> additionalRoots,
        List<string> diagnostics)
    {
        var roots = entries
            .Select(entry => entry.Path)
            .Concat(additionalRoots.Select(root => root.Path)
                .Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)))
            .Select(Path.GetPathRoot)
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var drives = new List<DriveStorageSnapshot>();
        foreach (var root in roots)
        {
            try
            {
                var drive = new DriveInfo(root!);
                if (!drive.IsReady)
                {
                    continue;
                }

                drives.Add(new DriveStorageSnapshot(
                    drive.RootDirectory.FullName,
                    drive.VolumeLabel,
                    drive.TotalSize,
                    drive.TotalFreeSpace,
                    drive.AvailableFreeSpace));
            }
            catch (Exception exception) when (
                exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                diagnostics.Add($"Volume {root} : {exception.Message}");
            }
        }

        return drives.OrderBy(drive => drive.RootPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string StableId(StorageCategory category, string path)
    {
        var input = Encoding.UTF8.GetBytes($"{category}|{Path.GetFullPath(path).ToUpperInvariant()}");
        return Convert.ToHexString(SHA256.HashData(input));
    }
}
