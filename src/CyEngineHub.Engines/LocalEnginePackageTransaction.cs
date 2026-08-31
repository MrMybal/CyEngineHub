using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyEngineHub.Engines;

public static partial class LocalEnginePackageTransaction
{
    private const int MaximumFileCount = 25_000;
    private const long MaximumTotalBytes = 2L * 1024 * 1024 * 1024;
    private const int CopyBufferSize = 128 * 1024;
    private const int ReceiptSchemaVersion = 1;
    private const int MaximumReceiptsPerProject = 5_000;
    private const long MaximumReceiptBytes = 1024 * 1024;
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };
    private static readonly HashSet<string> ExcludedDirectories = new(
        [".git", ".hg", ".svn", ".vs", ".idea", ".godot", "Library", "Temp", "obj"],
        StringComparer.OrdinalIgnoreCase);

    public static async Task<LocalEnginePackageInstallPlan> PrepareAsync(
        EngineFamily family,
        string providerId,
        LocalEngineProject targetProject,
        string packageId,
        string displayName,
        string version,
        string sourceDirectory,
        string destinationDirectory,
        string workspaceDirectory,
        string descriptorRelativePath,
        string riskSummary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetProject);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptorRelativePath);
        var source = NormalizeExistingRealDirectory(sourceDirectory, "ValidatePackageSource");
        var destination = Path.GetFullPath(destinationDirectory);
        var destinationParent = Path.GetDirectoryName(destination)
            ?? throw Failure("La destination du package n’a pas de dossier parent.", "PrepareInstall", destination);
        EnsureSafeDirectChild(destinationParent, destination, packageId, "PrepareInstall");
        var workspace = Path.GetFullPath(workspaceDirectory);
        EnsureNotVolumeRoot(workspace, "PrepareInstall");
        ValidateProjectOwnedPaths(targetProject.RootPath, destination, workspace, "PrepareInstall");
        EnsureNoOverlap(source, destination, "PrepareInstall");
        EnsureNoOverlap(source, workspace, "PrepareInstall");
        EnsureNoOverlap(destination, workspace, "PrepareInstall");
        var descriptor = CombineContained(source, descriptorRelativePath);
        if (!File.Exists(descriptor) || IsReparsePoint(descriptor))
        {
            throw Failure("Le manifeste du package source est absent ou non sûr.", "PrepareInstall", descriptor);
        }

        if (File.Exists(destination))
        {
            throw Failure("Un fichier occupe la destination prévue.", "PrepareInstall", destination);
        }

        var files = await InventoryAsync(source, cancellationToken).ConfigureAwait(false);
        var replacesExisting = Directory.Exists(destination);
        string? existingFingerprint = null;
        if (replacesExisting)
        {
            EnsureRealDirectory(destination, "PrepareInstall");
            var targetFiles = await InventoryAsync(destination, cancellationToken).ConfigureAwait(false);
            existingFingerprint = ComputeInventoryFingerprint(targetFiles);
        }

        var now = DateTimeOffset.UtcNow;
        return new LocalEnginePackageInstallPlan(
            Guid.NewGuid(),
            family,
            providerId,
            targetProject,
            packageId,
            displayName,
            version,
            source,
            destination,
            workspace,
            descriptorRelativePath.Replace('\\', '/'),
            files,
            files.Sum(file => file.Length),
            replacesExisting,
            existingFingerprint,
            riskSummary,
            now,
            now + PlanLifetime);
    }

    public static async Task<LocalEnginePackageInstallResult> ExecuteAsync(
        LocalEnginePackageInstallPlan plan,
        Func<string, CancellationToken, Task> validateStagedPackage,
        IProgress<LocalEnginePackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(validateStagedPackage);
        if (plan.Id == Guid.Empty || DateTimeOffset.UtcNow > plan.ExpiresAt)
        {
            throw Failure("Le plan d’installation a expiré.", "ExecuteInstall", plan.SourceDirectory);
        }

        var stopwatch = Stopwatch.StartNew();
        var source = NormalizeExistingRealDirectory(plan.SourceDirectory, "ExecuteInstall");
        var destination = Path.GetFullPath(plan.DestinationDirectory);
        var destinationParent = Path.GetDirectoryName(destination)
            ?? throw Failure("La destination du package n’a pas de dossier parent.", "ExecuteInstall", destination);
        EnsureSafeDirectChild(destinationParent, destination, plan.PackageId, "ExecuteInstall");
        EnsureNoOverlap(source, destination, "ExecuteInstall");
        var workspace = Path.GetFullPath(plan.WorkspaceDirectory);
        EnsureNotVolumeRoot(workspace, "ExecuteInstall");
        ValidateProjectOwnedPaths(plan.TargetProject.RootPath, destination, workspace, "ExecuteInstall");
        EnsureNoOverlap(source, workspace, "ExecuteInstall");
        EnsureNoOverlap(destination, workspace, "ExecuteInstall");
        var currentFiles = await InventoryAsync(source, cancellationToken).ConfigureAwait(false);
        if (!InventoriesMatch(plan.Files, currentFiles))
        {
            throw Failure("Le package source a changé après confirmation.", "ExecuteInstall", source);
        }

        await ValidateExistingTargetAsync(plan, destination, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(destinationParent);
        EnsureRealDirectory(destinationParent, "ExecuteInstall");
        Directory.CreateDirectory(workspace);
        EnsureRealDirectory(workspace, "ExecuteInstall");
        var stagingParent = CombineContained(workspace, "staging");
        var backupsParent = CombineContained(workspace, "backups");
        var staging = CombineContained(stagingParent, plan.Id.ToString("N"));
        var backup = CombineContained(backupsParent, $"{plan.PackageId}-{plan.Id:N}");
        Directory.CreateDirectory(stagingParent);
        EnsureRealDirectory(stagingParent, "ExecuteInstall");
        if (Directory.Exists(staging) || Directory.Exists(backup))
        {
            throw Failure("Le staging ou la sauvegarde de ce plan existe déjà.", "ExecuteInstall", workspace);
        }

        string? retainedBackup = null;
        long copiedFiles = 0;
        long copiedBytes = 0;
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in currentFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var from = CombineContained(source, file.RelativePath);
                var to = CombineContained(staging, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                await CopyFileAsync(from, to, cancellationToken).ConfigureAwait(false);
                copiedFiles++;
                copiedBytes += file.Length;
                progress?.Report(new LocalEnginePackageInstallProgress(
                    copiedFiles,
                    currentFiles.Length,
                    $"Copie · {file.RelativePath}"));
            }

            await validateStagedPackage(staging, cancellationToken).ConfigureAwait(false);
            var stagedFiles = await InventoryAsync(staging, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(
                    ComputeInventoryFingerprint(currentFiles),
                    ComputeInventoryFingerprint(stagedFiles),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("Le staging ne correspond pas au package source.", "VerifyStaging", staging);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(destination))
            {
                Directory.CreateDirectory(backupsParent);
                EnsureRealDirectory(backupsParent, "ExecuteInstall");
                Directory.Move(destination, backup);
                retainedBackup = backup;
            }

            try
            {
                Directory.Move(staging, destination);
            }
            catch
            {
                if (retainedBackup is not null &&
                    Directory.Exists(retainedBackup) &&
                    !Directory.Exists(destination))
                {
                    Directory.Move(retainedBackup, destination);
                    retainedBackup = null;
                }

                throw;
            }

            var receipt = new LocalEnginePackageInstallReceipt(
                ReceiptSchemaVersion,
                plan.Id,
                plan.Family,
                plan.ProviderId,
                plan.TargetProject,
                plan.PackageId,
                plan.DisplayName,
                plan.Version,
                plan.SourceDirectory,
                destination,
                workspace,
                ComputeInventoryFingerprint(currentFiles),
                retainedBackup,
                plan.ExistingTargetFingerprint,
                copiedFiles,
                copiedBytes,
                DateTimeOffset.UtcNow,
                null,
                null);
            try
            {
                await SaveReceiptAsync(receipt, overwrite: false, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                RollbackCommittedInstall(destination, staging, retainedBackup);
                retainedBackup = null;
                throw;
            }

            stopwatch.Stop();
            progress?.Report(new LocalEnginePackageInstallProgress(
                copiedFiles,
                copiedFiles,
                "Package installé"));
            return new LocalEnginePackageInstallResult(
                destination,
                retainedBackup,
                copiedFiles,
                copiedBytes,
                stopwatch.Elapsed,
                receipt);
        }
        catch (LocalEnginePackageOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Failure(
                $"L’installation transactionnelle a échoué : {exception.Message}",
                "ExecuteInstall",
                destination,
                exception);
        }
        finally
        {
            DeleteStaging(staging, stagingParent);
        }
    }

    public static async Task<IReadOnlyList<LocalEnginePackageInstallReceipt>> LoadReceiptsAsync(
        LocalEngineProject project,
        EngineFamily family,
        string providerId,
        string workspaceDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var root = NormalizeExistingRealDirectory(project.RootPath, "LoadPackageHistory");
        var workspace = Path.GetFullPath(workspaceDirectory);
        if (!IsSameOrChild(workspace, root) ||
            string.Equals(
                Path.TrimEndingDirectorySeparator(workspace),
                Path.TrimEndingDirectorySeparator(root),
                PathComparison()))
        {
            throw Failure("L’historique package doit rester sous le projet.", "LoadPackageHistory", workspace);
        }

        EnsureNoExistingReparsePoint(root, workspace, "LoadPackageHistory");
        var receiptsRoot = CombineContained(workspace, "receipts");
        if (!Directory.Exists(receiptsRoot))
        {
            return [];
        }

        EnsureRealDirectory(receiptsRoot, "LoadPackageHistory");
        var paths = Directory
            .EnumerateFiles(receiptsRoot, "*.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumReceiptsPerProject)
            .ToArray();
        var receipts = new List<LocalEnginePackageInstallReceipt>(paths.Length);
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receipt = await TryReadReceiptAsync(path, cancellationToken).ConfigureAwait(false);
            if (receipt is not null &&
                IsValidLoadedReceipt(receipt, project, family, providerId, workspace))
            {
                receipts.Add(receipt);
            }
        }

        return receipts
            .OrderByDescending(receipt => receipt.InstalledAt)
            .ToArray();
    }

    public static async Task<LocalEnginePackageRollbackPlan> PrepareRollbackAsync(
        LocalEnginePackageInstallReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateReceiptOwnership(receipt, "PreparePackageRollback");
        var persisted = await ReadPersistedReceiptAsync(receipt, cancellationToken)
            .ConfigureAwait(false);
        if (persisted != receipt)
        {
            throw Failure(
                "Le reçu d’installation ne correspond plus à sa version persistée.",
                "PreparePackageRollback",
                GetReceiptPath(receipt));
        }

        await ValidateRollbackStateAsync(receipt, cancellationToken).ConfigureAwait(false);
        var recoveryParent = CombineContained(receipt.WorkspaceDirectory, "rolled-back");
        var recovery = CombineContained(
            recoveryParent,
            $"{receipt.PackageId}-{receipt.Id:N}");
        if (Directory.Exists(recovery) || File.Exists(recovery))
        {
            throw Failure(
                "Le dossier de récupération de cette installation existe déjà.",
                "PreparePackageRollback",
                recovery);
        }

        var now = DateTimeOffset.UtcNow;
        return new LocalEnginePackageRollbackPlan(
            Guid.NewGuid(),
            receipt,
            recovery,
            now,
            now + PlanLifetime);
    }

    public static async Task<LocalEnginePackageRollbackResult> RollbackAsync(
        LocalEnginePackageRollbackPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Id == Guid.Empty || DateTimeOffset.UtcNow > plan.ExpiresAt)
        {
            throw Failure(
                "Le plan d’annulation du package a expiré.",
                "RollbackPackage",
                plan.Receipt.DestinationDirectory);
        }

        var receipt = plan.Receipt;
        ValidateReceiptOwnership(receipt, "RollbackPackage");
        var persisted = await ReadPersistedReceiptAsync(receipt, cancellationToken)
            .ConfigureAwait(false);
        if (persisted != receipt)
        {
            throw Failure(
                "Le reçu a changé après confirmation de l’annulation.",
                "RollbackPackage",
                GetReceiptPath(receipt));
        }

        await ValidateRollbackStateAsync(receipt, cancellationToken).ConfigureAwait(false);
        var recoveryParent = CombineContained(receipt.WorkspaceDirectory, "rolled-back");
        var expectedRecovery = CombineContained(
            recoveryParent,
            $"{receipt.PackageId}-{receipt.Id:N}");
        if (!SamePath(expectedRecovery, plan.RecoveryDirectory) ||
            Directory.Exists(expectedRecovery) ||
            File.Exists(expectedRecovery))
        {
            throw Failure(
                "La destination de récupération du plan est incohérente ou occupée.",
                "RollbackPackage",
                plan.RecoveryDirectory);
        }

        Directory.CreateDirectory(recoveryParent);
        EnsureRealDirectory(recoveryParent, "RollbackPackage");
        cancellationToken.ThrowIfCancellationRequested();
        var destination = Path.GetFullPath(receipt.DestinationDirectory);
        var backup = receipt.BackupDirectory is null
            ? null
            : Path.GetFullPath(receipt.BackupDirectory);
        var restoredPrevious = false;
        Directory.Move(destination, expectedRecovery);
        try
        {
            if (backup is not null)
            {
                Directory.Move(backup, destination);
                restoredPrevious = true;
            }

            var updated = receipt with
            {
                RolledBackAt = DateTimeOffset.UtcNow,
                RolledBackPackageDirectory = expectedRecovery,
            };
            try
            {
                await SaveReceiptAsync(updated, overwrite: true, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                RestoreFailedRollback(destination, expectedRecovery, backup, restoredPrevious);
                throw;
            }

            return new LocalEnginePackageRollbackResult(
                updated,
                restoredPrevious,
                expectedRecovery);
        }
        catch
        {
            if (Directory.Exists(expectedRecovery) && !Directory.Exists(destination))
            {
                Directory.Move(expectedRecovery, destination);
            }

            throw;
        }
    }

    private static async Task ValidateExistingTargetAsync(
        LocalEnginePackageInstallPlan plan,
        string destination,
        CancellationToken cancellationToken)
    {
        if (File.Exists(destination))
        {
            throw Failure("Un fichier occupe désormais la destination prévue.", "ValidateTarget", destination);
        }

        var exists = Directory.Exists(destination);
        if (exists != plan.ReplacesExisting)
        {
            throw Failure("La présence de la destination a changé après confirmation.", "ValidateTarget", destination);
        }

        if (!exists)
        {
            return;
        }

        EnsureRealDirectory(destination, "ValidateTarget");
        var current = await InventoryAsync(destination, cancellationToken).ConfigureAwait(false);
        var fingerprint = ComputeInventoryFingerprint(current);
        if (!string.Equals(fingerprint, plan.ExistingTargetFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Le package déjà installé a changé après confirmation.", "ValidateTarget", destination);
        }
    }

    private static async Task<LocalEnginePackageInstallFile[]> InventoryAsync(
        string root,
        CancellationToken cancellationToken)
    {
        var files = new List<LocalEnginePackageInstallFile>();
        var pending = new Stack<string>();
        pending.Push(root);
        long totalBytes = 0;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw Failure("Les liens et points de jonction sont interdits dans un package.", "Inventory", entry);
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (!ExcludedDirectories.Contains(Path.GetFileName(entry)))
                    {
                        pending.Push(entry);
                    }

                    continue;
                }

                var info = new FileInfo(entry);
                totalBytes = checked(totalBytes + info.Length);
                if (files.Count >= MaximumFileCount || totalBytes > MaximumTotalBytes)
                {
                    throw Failure(
                        "Le package dépasse les limites de 25 000 fichiers ou 2 Gio.",
                        "Inventory",
                        root);
                }

                files.Add(new LocalEnginePackageInstallFile(
                    Path.GetRelativePath(root, entry).Replace('\\', '/'),
                    info.Length,
                    info.LastWriteTimeUtc,
                    await ComputeSha256Async(entry, cancellationToken).ConfigureAwait(false)));
            }
        }

        if (files.Count == 0)
        {
            throw Failure("Le package source est vide.", "Inventory", root);
        }

        return files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool InventoriesMatch(
        IReadOnlyList<LocalEnginePackageInstallFile> expected,
        LocalEnginePackageInstallFile[] current) =>
        expected.Count == current.Length && expected.Zip(current).All(pair =>
            string.Equals(pair.First.RelativePath, pair.Second.RelativePath, StringComparison.OrdinalIgnoreCase) &&
            pair.First.Length == pair.Second.Length &&
            pair.First.LastWriteTimeUtc == pair.Second.LastWriteTimeUtc &&
            string.Equals(pair.First.Sha256, pair.Second.Sha256, StringComparison.OrdinalIgnoreCase));

    private static string ComputeInventoryFingerprint(
        IReadOnlyList<LocalEnginePackageInstallFile> files)
    {
        var normalized = string.Join(
            "\n",
            files.Select(file => $"{file.RelativePath}\0{file.Length}\0{file.Sha256}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))
            .ToLowerInvariant();
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string NormalizeExistingRealDirectory(string path, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = Path.GetFullPath(path.Trim().Trim('"'));
        if (!Directory.Exists(normalized))
        {
            throw Failure("Le dossier est introuvable.", operation, normalized);
        }

        EnsureNotVolumeRoot(normalized, operation);
        EnsureRealDirectory(normalized, operation);
        return normalized;
    }

    private static void EnsureSafeDirectChild(
        string parent,
        string destination,
        string packageId,
        string operation)
    {
        if (string.IsNullOrWhiteSpace(packageId) ||
            !string.Equals(packageId, Path.GetFileName(packageId), StringComparison.Ordinal) ||
            packageId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            !IsDirectChild(parent, destination) ||
            !string.Equals(Path.GetFileName(destination), packageId, PathComparison()))
        {
            throw Failure("L’identifiant du package ne forme pas une destination sûre.", operation, destination);
        }
    }

    private static string CombineContained(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            throw Failure("Un chemin absolu est interdit dans le package.", "ResolvePath", relativePath);
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var combined = Path.GetFullPath(Path.Combine(
            normalizedRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsSameOrChild(combined, normalizedRoot))
        {
            throw Failure("Un chemin sort du dossier autorisé.", "ResolvePath", combined);
        }

        return combined;
    }

    private static void EnsureNoOverlap(string left, string right, string operation)
    {
        if (IsSameOrChild(left, right) || IsSameOrChild(right, left))
        {
            throw Failure("La source et la destination de l’opération se chevauchent.", operation, left);
        }
    }

    private static void ValidateProjectOwnedPaths(
        string projectRoot,
        string destination,
        string workspace,
        string operation)
    {
        var root = NormalizeExistingRealDirectory(projectRoot, operation);
        if (!IsSameOrChild(destination, root) ||
            !IsSameOrChild(workspace, root) ||
            string.Equals(
                Path.TrimEndingDirectorySeparator(destination),
                Path.TrimEndingDirectorySeparator(root),
                PathComparison()) ||
            string.Equals(
                Path.TrimEndingDirectorySeparator(workspace),
                Path.TrimEndingDirectorySeparator(root),
                PathComparison()))
        {
            throw Failure(
                "La destination et l’espace transactionnel doivent rester sous le projet cible.",
                operation,
                root);
        }

        EnsureNoExistingReparsePoint(root, destination, operation);
        EnsureNoExistingReparsePoint(root, workspace, operation);
    }

    private static void EnsureNoExistingReparsePoint(
        string root,
        string candidate,
        string operation)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedCandidate);
        var current = normalizedRoot;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current) && !File.Exists(current))
            {
                break;
            }

            if (IsReparsePoint(current))
            {
                throw Failure(
                    "Un segment du chemin cible constitue un lien ou un point de jonction.",
                    operation,
                    current);
            }
        }
    }

    private static void EnsureRealDirectory(string path, string operation)
    {
        if (IsReparsePoint(path))
        {
            throw Failure("Un dossier cible constitue un lien ou un point de jonction.", operation, path);
        }
    }

    private static void EnsureNotVolumeRoot(string path, string operation)
    {
        var normalized = Path.GetFullPath(path);
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(normalized),
                Path.TrimEndingDirectorySeparator(Path.GetPathRoot(normalized)!),
                PathComparison()))
        {
            throw Failure("Une racine de volume ne peut pas être utilisée pour cette opération.", operation, path);
        }
    }

    private static async Task ValidateRollbackStateAsync(
        LocalEnginePackageInstallReceipt receipt,
        CancellationToken cancellationToken)
    {
        if (receipt.RolledBackAt is not null)
        {
            throw Failure(
                "Cette installation a déjà été annulée.",
                "ValidatePackageRollback",
                receipt.DestinationDirectory);
        }

        var destination = Path.GetFullPath(receipt.DestinationDirectory);
        if (!Directory.Exists(destination) || File.Exists(destination))
        {
            throw Failure(
                "Le package installé à annuler est introuvable.",
                "ValidatePackageRollback",
                destination);
        }

        EnsureRealDirectory(destination, "ValidatePackageRollback");
        var installedFiles = await InventoryAsync(destination, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                ComputeInventoryFingerprint(installedFiles),
                receipt.InstalledFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                "Le package installé a été modifié depuis son installation.",
                "ValidatePackageRollback",
                destination);
        }

        if (receipt.BackupDirectory is null)
        {
            if (receipt.BackupFingerprint is not null)
            {
                throw Failure(
                    "Les métadonnées de sauvegarde du reçu sont incohérentes.",
                    "ValidatePackageRollback",
                    GetReceiptPath(receipt));
            }

            return;
        }

        var backup = Path.GetFullPath(receipt.BackupDirectory);
        if (!Directory.Exists(backup) || File.Exists(backup))
        {
            throw Failure(
                "La version précédente sauvegardée est introuvable.",
                "ValidatePackageRollback",
                backup);
        }

        EnsureRealDirectory(backup, "ValidatePackageRollback");
        var backupFiles = await InventoryAsync(backup, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(
                ComputeInventoryFingerprint(backupFiles),
                receipt.BackupFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw Failure(
                "La version précédente sauvegardée a été modifiée.",
                "ValidatePackageRollback",
                backup);
        }
    }

    private static async Task SaveReceiptAsync(
        LocalEnginePackageInstallReceipt receipt,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        ValidateReceiptOwnership(receipt, "SavePackageReceipt");
        var receiptsRoot = CombineContained(receipt.WorkspaceDirectory, "receipts");
        Directory.CreateDirectory(receiptsRoot);
        EnsureRealDirectory(receiptsRoot, "SavePackageReceipt");
        var path = GetReceiptPath(receipt);
        if (!overwrite && File.Exists(path))
        {
            throw Failure("Un reçu portant cet identifiant existe déjà.", "SavePackageReceipt", path);
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
                $"Le reçu d’installation n’a pas pu être enregistré : {exception.Message}",
                "SavePackageReceipt",
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
                // The receipt operation result is more important than temporary cleanup.
            }
        }
    }

    private static async Task<LocalEnginePackageInstallReceipt> ReadPersistedReceiptAsync(
        LocalEnginePackageInstallReceipt expected,
        CancellationToken cancellationToken)
    {
        var path = GetReceiptPath(expected);
        var receipt = await TryReadReceiptAsync(path, cancellationToken).ConfigureAwait(false);
        if (receipt is null)
        {
            throw Failure(
                "Le reçu d’installation persistant est absent ou invalide.",
                "ReadPackageReceipt",
                path);
        }

        ValidateReceiptOwnership(receipt, "ReadPackageReceipt");
        return receipt;
    }

    private static async Task<LocalEnginePackageInstallReceipt?> TryReadReceiptAsync(
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
            return await JsonSerializer.DeserializeAsync<LocalEnginePackageInstallReceipt>(
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

    private static bool IsValidLoadedReceipt(
        LocalEnginePackageInstallReceipt receipt,
        LocalEngineProject project,
        EngineFamily family,
        string providerId,
        string workspace)
    {
        try
        {
            ValidateReceiptOwnership(receipt, "LoadPackageHistory");
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

    private static void ValidateReceiptOwnership(
        LocalEnginePackageInstallReceipt receipt,
        string operation)
    {
        if (receipt.SchemaVersion != ReceiptSchemaVersion ||
            receipt.Id == Guid.Empty ||
            string.IsNullOrWhiteSpace(receipt.ProviderId) ||
            string.IsNullOrWhiteSpace(receipt.PackageId) ||
            string.IsNullOrWhiteSpace(receipt.DisplayName) ||
            receipt.TargetProject.Family != receipt.Family ||
            !string.Equals(receipt.TargetProject.ProviderId, receipt.ProviderId, StringComparison.Ordinal) ||
            !IsSha256(receipt.InstalledFingerprint) ||
            (receipt.BackupFingerprint is not null && !IsSha256(receipt.BackupFingerprint)) ||
            receipt.FileCount <= 0 ||
            receipt.TotalBytes < 0 ||
            (receipt.RolledBackAt is null) != (receipt.RolledBackPackageDirectory is null))
        {
            throw Failure("Le reçu d’installation est incomplet ou incohérent.", operation, receipt.DestinationDirectory);
        }

        var destination = Path.GetFullPath(receipt.DestinationDirectory);
        var workspace = Path.GetFullPath(receipt.WorkspaceDirectory);
        var parent = Path.GetDirectoryName(destination)
            ?? throw Failure("La destination du reçu n’a pas de parent.", operation, destination);
        EnsureSafeDirectChild(parent, destination, receipt.PackageId, operation);
        ValidateProjectOwnedPaths(receipt.TargetProject.RootPath, destination, workspace, operation);
        EnsureNoOverlap(destination, workspace, operation);
        if (receipt.BackupDirectory is not null)
        {
            var backupsRoot = CombineContained(workspace, "backups");
            if (!IsDirectChild(backupsRoot, receipt.BackupDirectory))
            {
                throw Failure("Le chemin de sauvegarde du reçu est invalide.", operation, receipt.BackupDirectory);
            }
        }

        if (receipt.RolledBackPackageDirectory is not null)
        {
            var recoveryRoot = CombineContained(workspace, "rolled-back");
            if (!IsDirectChild(recoveryRoot, receipt.RolledBackPackageDirectory))
            {
                throw Failure(
                    "Le chemin de récupération du reçu est invalide.",
                    operation,
                    receipt.RolledBackPackageDirectory);
            }
        }
    }

    private static string GetReceiptPath(LocalEnginePackageInstallReceipt receipt)
    {
        var receiptsRoot = CombineContained(receipt.WorkspaceDirectory, "receipts");
        return CombineContained(receiptsRoot, $"{receipt.Id:N}.json");
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static void RollbackCommittedInstall(
        string destination,
        string staging,
        string? backup)
    {
        try
        {
            if (Directory.Exists(destination) && !Directory.Exists(staging))
            {
                Directory.Move(destination, staging);
            }

            if (backup is not null && Directory.Exists(backup) && !Directory.Exists(destination))
            {
                Directory.Move(backup, destination);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Failure(
                "L’écriture du reçu a échoué et la restauration automatique n’a pas pu aboutir.",
                "RestoreInstallAfterReceiptFailure",
                destination,
                exception);
        }
    }

    private static void RestoreFailedRollback(
        string destination,
        string recovery,
        string? backup,
        bool restoredPrevious)
    {
        try
        {
            if (restoredPrevious && backup is not null &&
                Directory.Exists(destination) && !Directory.Exists(backup))
            {
                Directory.Move(destination, backup);
            }

            if (Directory.Exists(recovery) && !Directory.Exists(destination))
            {
                Directory.Move(recovery, destination);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw Failure(
                "La persistance du rollback a échoué et l’état initial n’a pas pu être restauré automatiquement.",
                "RestoreFailedPackageRollback",
                destination,
                exception);
        }
    }

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            PathComparison());

    private static bool IsReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsDirectChild(string parent, string child) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)),
            Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(child))),
            PathComparison());

    private static bool IsSameOrChild(string candidate, string root)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(normalizedCandidate, normalizedRoot, PathComparison()) ||
               normalizedCandidate.StartsWith(
                   normalizedRoot + Path.DirectorySeparatorChar,
                   PathComparison());
    }

    private static void DeleteStaging(string staging, string stagingParent)
    {
        try
        {
            if (Directory.Exists(staging) && IsDirectChild(stagingParent, staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // A retained staging directory must not mask the primary operation result.
        }
    }

    private static StringComparison PathComparison() => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static LocalEnginePackageOperationException Failure(
        string message,
        string operation,
        string? path,
        Exception? innerException = null) =>
        new(message, operation, path, innerException);
}
