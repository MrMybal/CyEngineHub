using CyEngineHub.Storage.Models;

namespace CyEngineHub.Storage.Cleanup;

public interface IStorageCleanupService
{
    StorageCleanupPlan Prepare(StorageEntry entry);

    Task<StorageCleanupResult> ExecuteAsync(
        StorageCleanupPlan plan,
        CancellationToken cancellationToken = default);
}

public sealed class StorageCleanupService : IStorageCleanupService
{
    private static readonly Dictionary<StorageCategory, string[]> AllowedRelativePaths =
        new Dictionary<StorageCategory, string[]>
        {
            [StorageCategory.EngineDerivedDataCache] = ["Engine", "DerivedDataCache"],
            [StorageCategory.ProjectDerivedDataCache] = ["DerivedDataCache"],
            [StorageCategory.ProjectIntermediate] = ["Intermediate"],
            [StorageCategory.ProjectBinaries] = ["Binaries"],
            [StorageCategory.ProjectLogs] = ["Saved", "Logs"],
            [StorageCategory.ProjectCooked] = ["Saved", "Cooked"],
            [StorageCategory.ProjectStagedBuilds] = ["Saved", "StagedBuilds"],
        };

    public StorageCleanupPlan Prepare(StorageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsSafeToClean || entry.Health != StorageHealth.Ready)
        {
            throw new StorageException(
                "Cette entrée n’est pas éligible au Safe Clean.",
                "PrepareCleanup",
                entry.Path);
        }

        var (ownerRoot, targetPath) = ValidateTarget(
            entry.Category,
            entry.OwnerRoot,
            entry.Path,
            requireExisting: true);
        return new StorageCleanupPlan(
            entry.Id,
            entry.Category,
            $"{entry.OwnerName} · {entry.DisplayName}",
            targetPath,
            ownerRoot,
            entry.SizeBytes,
            entry.FileCount,
            entry.DirectoryCount,
            DateTimeOffset.UtcNow);
    }

    public Task<StorageCleanupResult> ExecuteAsync(
        StorageCleanupPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return Task.Run(() => Execute(plan, cancellationToken), cancellationToken);
    }

    private static StorageCleanupResult Execute(
        StorageCleanupPlan plan,
        CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - plan.PreparedAt > TimeSpan.FromMinutes(30))
        {
            throw new StorageException(
                "Le plan de nettoyage a expiré. Relancez l’analyse avant de confirmer.",
                "ExecuteCleanup",
                plan.TargetPath);
        }

        var (_, targetPath) = ValidateTarget(
            plan.Category,
            plan.OwnerRoot,
            plan.TargetPath,
            requireExisting: false);
        if (!Directory.Exists(targetPath))
        {
            return new StorageCleanupResult(
                targetPath,
                plan.EstimatedBytes,
                true,
                DateTimeOffset.UtcNow);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var rootAttributes = File.GetAttributes(targetPath);
            if ((rootAttributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new StorageException(
                    "Le dossier cible est un lien ou une jonction; nettoyage refusé.",
                    "ExecuteCleanup",
                    targetPath);
            }

            DeleteDirectory(targetPath, cancellationToken);
            return new StorageCleanupResult(
                targetPath,
                plan.EstimatedBytes,
                !Directory.Exists(targetPath),
                DateTimeOffset.UtcNow);
        }
        catch (StorageException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new StorageException(
                $"Impossible de nettoyer le dossier : {exception.Message}",
                "ExecuteCleanup",
                targetPath,
                exception);
        }
    }

    private static void DeleteDirectory(string directory, CancellationToken cancellationToken)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attributes = File.GetAttributes(entry);
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
            if (isDirectory)
            {
                if (isReparsePoint)
                {
                    Directory.Delete(entry, false);
                }
                else
                {
                    DeleteDirectory(entry, cancellationToken);
                }

                continue;
            }

            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(entry, attributes & ~FileAttributes.ReadOnly);
            }

            File.Delete(entry);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.Delete(directory, false);
    }

    private static (string OwnerRoot, string TargetPath) ValidateTarget(
        StorageCategory category,
        string ownerRoot,
        string targetPath,
        bool requireExisting)
    {
        if (!AllowedRelativePaths.TryGetValue(category, out var relativeSegments))
        {
            throw new StorageException(
                "Cette catégorie ne peut pas être supprimée par Safe Clean.",
                "ValidateCleanupTarget",
                targetPath);
        }

        var normalizedOwner = Normalize(ownerRoot);
        var normalizedTarget = Normalize(targetPath);
        var ownerVolumeRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(normalizedOwner) ?? string.Empty);
        if (string.Equals(normalizedOwner, ownerVolumeRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageException(
                "Une racine de volume ne peut jamais être utilisée comme propriétaire de nettoyage.",
                "ValidateCleanupTarget",
                normalizedOwner);
        }

        var expected = normalizedOwner;
        foreach (var segment in relativeSegments)
        {
            expected = Path.Combine(expected, segment);
        }

        expected = Normalize(expected);
        if (!string.Equals(expected, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageException(
                "La cible ne correspond pas exactement à un dossier Unreal régénérable autorisé.",
                "ValidateCleanupTarget",
                normalizedTarget);
        }

        if (!normalizedTarget.StartsWith(
                normalizedOwner + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new StorageException(
                "La cible sort de la racine propriétaire.",
                "ValidateCleanupTarget",
                normalizedTarget);
        }

        if (requireExisting && !Directory.Exists(normalizedTarget))
        {
            throw new StorageException(
                "Le dossier à nettoyer n’existe plus.",
                "ValidateCleanupTarget",
                normalizedTarget);
        }

        return (normalizedOwner, normalizedTarget);
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new StorageException(
                "Un chemin absolu est requis.",
                "ValidateCleanupTarget",
                path);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }
}
