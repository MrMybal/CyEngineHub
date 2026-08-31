using System.Text.Json;

namespace CyEngineHub.Engines;

public static partial class LocalEnginePackageTransaction
{
    private const int RemovalReceiptSchemaVersion = 1;

    public static async Task<LocalEnginePackageRemovalPlan> PrepareRemovalAsync(
        LocalEnginePackage package,
        LocalEngineProject targetProject,
        string packageId,
        string destinationDirectory,
        string workspaceDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(targetProject);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        var destination = NormalizeExistingRealDirectory(
            destinationDirectory,
            "PreparePackageRemoval");
        var destinationParent = Path.GetDirectoryName(destination)
            ?? throw Failure(
                "La destination du package n’a pas de dossier parent.",
                "PreparePackageRemoval",
                destination);
        EnsureSafeDirectChild(
            destinationParent,
            destination,
            packageId,
            "PreparePackageRemoval");
        var workspace = Path.GetFullPath(workspaceDirectory);
        EnsureNotVolumeRoot(workspace, "PreparePackageRemoval");
        ValidateProjectOwnedPaths(
            targetProject.RootPath,
            destination,
            workspace,
            "PreparePackageRemoval");
        EnsureNoOverlap(destination, workspace, "PreparePackageRemoval");

        var files = await InventoryAsync(destination, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return new LocalEnginePackageRemovalPlan(
            Guid.NewGuid(),
            package,
            targetProject,
            packageId,
            destination,
            workspace,
            files,
            ComputeInventoryFingerprint(files),
            files.Sum(file => file.Length),
            now,
            now + PlanLifetime);
    }

    public static async Task<LocalEnginePackageRemovalResult> ExecuteRemovalAsync(
        LocalEnginePackageRemovalPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateRemovalPlanOwnership(plan, "RemovePackage");
        if (plan.Id == Guid.Empty || DateTimeOffset.UtcNow > plan.ExpiresAt)
        {
            throw Failure(
                "Le plan de suppression du package a expiré.",
                "RemovePackage",
                plan.DestinationDirectory);
        }

        var destination = NormalizeExistingRealDirectory(
            plan.DestinationDirectory,
            "RemovePackage");
        var currentFiles = await InventoryAsync(destination, cancellationToken).ConfigureAwait(false);
        if (!InventoriesMatch(plan.Files, currentFiles) ||
            !string.Equals(
                ComputeInventoryFingerprint(currentFiles),
                plan.InstalledFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                "Le package a été modifié après la confirmation de sa suppression.",
                "RemovePackage",
                destination);
        }

        var removedRoot = CombineContained(plan.WorkspaceDirectory, "removed");
        Directory.CreateDirectory(removedRoot);
        EnsureRealDirectory(removedRoot, "RemovePackage");
        var recovery = CombineContained(
            removedRoot,
            $"{plan.PackageId}-{plan.Id:N}");
        if (Directory.Exists(recovery) || File.Exists(recovery))
        {
            throw Failure(
                "La destination de récupération du package existe déjà.",
                "RemovePackage",
                recovery);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(destination, recovery);
        var receipt = new LocalEnginePackageRemovalReceipt(
            RemovalReceiptSchemaVersion,
            plan.Id,
            plan.Package.Family,
            plan.Package.ProviderId,
            plan.TargetProject,
            plan.PackageId,
            plan.Package.Name,
            plan.Package.Version,
            destination,
            plan.WorkspaceDirectory,
            plan.InstalledFingerprint,
            plan.Files.Count,
            plan.TotalBytes,
            DateTimeOffset.UtcNow,
            null,
            recovery);
        try
        {
            await SaveRemovalReceiptAsync(receipt, overwrite: false, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            RestoreFailedRemoval(destination, recovery);
            throw;
        }

        return new LocalEnginePackageRemovalResult(receipt, recovery);
    }

    public static async Task<IReadOnlyList<LocalEnginePackageRemovalReceipt>> LoadRemovalReceiptsAsync(
        LocalEngineProject project,
        EngineFamily family,
        string providerId,
        string workspaceDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var workspace = Path.GetFullPath(workspaceDirectory);
        var root = NormalizeExistingRealDirectory(project.RootPath, "LoadPackageRemovalHistory");
        if (!IsSameOrChild(workspace, root))
        {
            throw Failure(
                "L’historique de suppression doit rester sous le projet.",
                "LoadPackageRemovalHistory",
                workspace);
        }

        var receiptsRoot = CombineContained(workspace, "removal-receipts");
        if (!Directory.Exists(receiptsRoot) || IsReparsePoint(receiptsRoot))
        {
            return [];
        }

        var paths = Directory
            .EnumerateFiles(receiptsRoot, "*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .Take(MaximumReceiptsPerProject)
            .ToArray();
        var receipts = new List<LocalEnginePackageRemovalReceipt>(paths.Length);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receipt = await TryReadRemovalReceiptAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (receipt is not null &&
                IsValidLoadedRemovalReceipt(receipt, project, family, providerId, workspace))
            {
                receipts.Add(receipt);
            }
        }

        return receipts.OrderByDescending(receipt => receipt.RemovedAt).ToArray();
    }

    public static async Task<LocalEnginePackageRestorePlan> PrepareRestoreAsync(
        LocalEnginePackageRemovalReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateRemovalReceiptOwnership(receipt, "PreparePackageRestore");
        var persisted = await ReadPersistedRemovalReceiptAsync(receipt, cancellationToken)
            .ConfigureAwait(false);
        if (persisted != receipt)
        {
            throw Failure(
                "Le reçu de suppression sélectionné ne correspond plus au reçu persistant.",
                "PreparePackageRestore",
                GetRemovalReceiptPath(receipt));
        }

        await ValidateRestoreStateAsync(receipt, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return new LocalEnginePackageRestorePlan(
            Guid.NewGuid(),
            receipt,
            now,
            now + PlanLifetime);
    }

    public static async Task<LocalEnginePackageRestoreResult> ExecuteRestoreAsync(
        LocalEnginePackageRestorePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Id == Guid.Empty || DateTimeOffset.UtcNow > plan.ExpiresAt)
        {
            throw Failure(
                "Le plan de restauration du package a expiré.",
                "RestorePackage",
                plan.Receipt.DestinationDirectory);
        }

        var receipt = plan.Receipt;
        ValidateRemovalReceiptOwnership(receipt, "RestorePackage");
        var persisted = await ReadPersistedRemovalReceiptAsync(receipt, cancellationToken)
            .ConfigureAwait(false);
        if (persisted != receipt)
        {
            throw Failure(
                "Le reçu de suppression a changé après confirmation.",
                "RestorePackage",
                GetRemovalReceiptPath(receipt));
        }

        await ValidateRestoreStateAsync(receipt, cancellationToken).ConfigureAwait(false);
        var destination = Path.GetFullPath(receipt.DestinationDirectory);
        var recovery = Path.GetFullPath(receipt.RecoveryDirectory);
        cancellationToken.ThrowIfCancellationRequested();
        Directory.Move(recovery, destination);
        var updated = receipt with { RestoredAt = DateTimeOffset.UtcNow };
        try
        {
            await SaveRemovalReceiptAsync(updated, overwrite: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            RestoreFailedPackageRestore(destination, recovery);
            throw;
        }

        return new LocalEnginePackageRestoreResult(updated, destination);
    }

    private static void ValidateRemovalPlanOwnership(
        LocalEnginePackageRemovalPlan plan,
        string operation)
    {
        if (plan.Id == Guid.Empty ||
            plan.Package.Family != plan.TargetProject.Family ||
            !string.Equals(plan.Package.ProviderId, plan.TargetProject.ProviderId, StringComparison.Ordinal) ||
            !string.Equals(plan.Package.ProjectId, plan.TargetProject.Id, StringComparison.Ordinal) ||
            plan.Files.Count == 0 ||
            plan.TotalBytes < 0 ||
            !IsSha256(plan.InstalledFingerprint))
        {
            throw Failure(
                "Le plan de suppression du package est incomplet ou incohérent.",
                operation,
                plan.DestinationDirectory);
        }

        var destination = Path.GetFullPath(plan.DestinationDirectory);
        var parent = Path.GetDirectoryName(destination)
            ?? throw Failure("La destination du package n’a pas de parent.", operation, destination);
        EnsureSafeDirectChild(parent, destination, plan.PackageId, operation);
        ValidateProjectOwnedPaths(
            plan.TargetProject.RootPath,
            destination,
            plan.WorkspaceDirectory,
            operation);
        EnsureNoOverlap(destination, plan.WorkspaceDirectory, operation);
        if (!SamePath(plan.Package.RootPath, destination))
        {
            throw Failure("Le chemin inventorié du package est incohérent.", operation, destination);
        }
    }

    private static async Task ValidateRestoreStateAsync(
        LocalEnginePackageRemovalReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (receipt.RestoredAt is not null)
        {
            throw Failure(
                "Ce package supprimé a déjà été restauré.",
                "ValidatePackageRestore",
                receipt.DestinationDirectory);
        }

        var destination = Path.GetFullPath(receipt.DestinationDirectory);
        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw Failure(
                "La destination du package est de nouveau occupée.",
                "ValidatePackageRestore",
                destination);
        }

        var recovery = NormalizeExistingRealDirectory(
            receipt.RecoveryDirectory,
            "ValidatePackageRestore");
        var files = await InventoryAsync(recovery, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                ComputeInventoryFingerprint(files),
                receipt.RemovedFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                "La copie récupérable du package a été modifiée depuis sa suppression.",
                "ValidatePackageRestore",
                recovery);
        }
    }

    private static async Task SaveRemovalReceiptAsync(
        LocalEnginePackageRemovalReceipt receipt,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        ValidateRemovalReceiptOwnership(receipt, "SavePackageRemovalReceipt");
        var receiptsRoot = CombineContained(receipt.WorkspaceDirectory, "removal-receipts");
        Directory.CreateDirectory(receiptsRoot);
        EnsureRealDirectory(receiptsRoot, "SavePackageRemovalReceipt");
        var path = GetRemovalReceiptPath(receipt);
        if (!overwrite && File.Exists(path))
        {
            throw Failure(
                "Un reçu de suppression portant cet identifiant existe déjà.",
                "SavePackageRemovalReceipt",
                path);
        }

        var temporary = CombineContained(
            receiptsRoot,
            $".{receipt.Id:N}-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                        stream,
                        receipt,
                        ReceiptJsonOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite);
        }
        catch (LocalEnginePackageOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw Failure(
                $"Le reçu de suppression n’a pas pu être enregistré : {exception.Message}",
                "SavePackageRemovalReceipt",
                path,
                exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary) && IsDirectChild(receiptsRoot, temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Le résultat transactionnel prime sur le nettoyage du fichier temporaire.
            }
        }
    }

    private static async Task<LocalEnginePackageRemovalReceipt> ReadPersistedRemovalReceiptAsync(
        LocalEnginePackageRemovalReceipt expected,
        CancellationToken cancellationToken)
    {
        var path = GetRemovalReceiptPath(expected);
        var receipt = await TryReadRemovalReceiptAsync(path, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            throw Failure(
                "Le reçu de suppression persistant est absent ou invalide.",
                "ReadPackageRemovalReceipt",
                path);
        }

        ValidateRemovalReceiptOwnership(receipt, "ReadPackageRemovalReceipt");
        return receipt;
    }

    private static async Task<LocalEnginePackageRemovalReceipt?> TryReadRemovalReceiptAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumReceiptBytes || IsReparsePoint(path))
            {
                return null;
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<LocalEnginePackageRemovalReceipt>(
                    stream,
                    ReceiptJsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool IsValidLoadedRemovalReceipt(
        LocalEnginePackageRemovalReceipt receipt,
        LocalEngineProject project,
        EngineFamily family,
        string providerId,
        string workspace)
    {
        try
        {
            ValidateRemovalReceiptOwnership(receipt, "LoadPackageRemovalHistory");
            return receipt.Family == family &&
                   string.Equals(receipt.ProviderId, providerId, StringComparison.Ordinal) &&
                   string.Equals(receipt.TargetProject.Id, project.Id, StringComparison.Ordinal) &&
                   SamePath(receipt.TargetProject.RootPath, project.RootPath) &&
                   SamePath(receipt.WorkspaceDirectory, workspace);
        }
        catch (Exception exception) when (
            exception is LocalEnginePackageOperationException or
                ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return false;
        }
    }

    private static void ValidateRemovalReceiptOwnership(
        LocalEnginePackageRemovalReceipt receipt,
        string operation)
    {
        if (receipt.SchemaVersion != RemovalReceiptSchemaVersion ||
            receipt.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(receipt.ProviderId) ||
            string.IsNullOrWhiteSpace(receipt.PackageId) ||
            string.IsNullOrWhiteSpace(receipt.DisplayName) ||
            receipt.TargetProject.Family != receipt.Family ||
            !string.Equals(receipt.TargetProject.ProviderId, receipt.ProviderId, StringComparison.Ordinal) ||
            !IsSha256(receipt.RemovedFingerprint) ||
            receipt.FileCount <= 0 ||
            receipt.TotalBytes < 0)
        {
            throw Failure(
                "Le reçu de suppression est incomplet ou incohérent.",
                operation,
                receipt.DestinationDirectory);
        }

        var destination = Path.GetFullPath(receipt.DestinationDirectory);
        var workspace = Path.GetFullPath(receipt.WorkspaceDirectory);
        var parent = Path.GetDirectoryName(destination)
            ?? throw Failure("La destination du reçu n’a pas de parent.", operation, destination);
        EnsureSafeDirectChild(parent, destination, receipt.PackageId, operation);
        ValidateProjectOwnedPaths(receipt.TargetProject.RootPath, destination, workspace, operation);
        EnsureNoOverlap(destination, workspace, operation);
        var removedRoot = CombineContained(workspace, "removed");
        if (!IsDirectChild(removedRoot, receipt.RecoveryDirectory))
        {
            throw Failure(
                "Le chemin récupérable du reçu de suppression est invalide.",
                operation,
                receipt.RecoveryDirectory);
        }
    }

    private static string GetRemovalReceiptPath(LocalEnginePackageRemovalReceipt receipt)
    {
        var receiptsRoot = CombineContained(receipt.WorkspaceDirectory, "removal-receipts");
        return CombineContained(receiptsRoot, $"{receipt.Id:N}.json");
    }

    private static void RestoreFailedRemoval(string destination, string recovery)
    {
        try
        {
            if (Directory.Exists(recovery) && !Directory.Exists(destination))
            {
                Directory.Move(recovery, destination);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Failure(
                "La persistance de la suppression a échoué et le package n’a pas pu être restauré automatiquement.",
                "RestoreFailedPackageRemoval",
                destination,
                exception);
        }
    }

    private static void RestoreFailedPackageRestore(string destination, string recovery)
    {
        try
        {
            if (Directory.Exists(destination) && !Directory.Exists(recovery))
            {
                Directory.Move(destination, recovery);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Failure(
                "La persistance de la restauration a échoué et l’état supprimé n’a pas pu être rétabli automatiquement.",
                "RestoreFailedPackageRestore",
                destination,
                exception);
        }
    }
}
