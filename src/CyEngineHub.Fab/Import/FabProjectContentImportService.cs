using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

namespace CyEngineHub.Fab.Import;

public interface IFabProjectContentImportService
{
    Task<FabProjectImportPlan> PrepareAsync(
        string sourcePath,
        string targetProjectPath,
        CancellationToken cancellationToken = default);

    Task<FabProjectImportResult> ExecuteAsync(
        FabProjectImportPlan plan,
        IProgress<FabProjectImportProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FabImportReceipt>> LoadHistoryAsync(
        IEnumerable<string> targetProjectPaths,
        CancellationToken cancellationToken = default);

    Task<FabImportRollbackPlan> PrepareRollbackAsync(
        FabImportReceipt receipt,
        CancellationToken cancellationToken = default);

    Task<FabImportRollbackResult> ExecuteRollbackAsync(
        FabImportRollbackPlan plan,
        IProgress<FabProjectImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class FabProjectContentImportService : IFabProjectContentImportService
{
    private const int MaximumFileCount = 100_000;
    private const long MaximumTotalBytes = 100L * 1024 * 1024 * 1024;
    private const long MaximumReceiptBytes = 8L * 1024 * 1024;
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions ReceiptJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };
    private static readonly HashSet<string> AllowedExtensions = new(
        [
            ".uasset", ".umap", ".uexp", ".ubulk", ".uptnl",
            ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".exr", ".hdr", ".dds",
            ".tif", ".tiff", ".fbx", ".obj", ".gltf", ".glb", ".usd", ".usda",
            ".usdc", ".abc", ".wav", ".ogg", ".mp3", ".flac", ".json", ".csv",
            ".txt", ".md",
        ],
        StringComparer.OrdinalIgnoreCase);

    public Task<FabProjectImportPlan> PrepareAsync(
        string sourcePath,
        string targetProjectPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => Prepare(sourcePath, targetProjectPath, cancellationToken),
            cancellationToken);

    public async Task<FabProjectImportResult> ExecuteAsync(
        FabProjectImportPlan plan,
        IProgress<FabProjectImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (DateTimeOffset.UtcNow > plan.ExpiresAt)
        {
            throw new FabContentOperationException(
                "Le plan d’import Fab a expiré; relancez l’analyse.",
                "ExecuteImport",
                plan.SourceContentDirectory);
        }

        ValidateProject(plan.TargetProjectPath);
        var sourceContent = NormalizeDirectory(plan.SourceContentDirectory);
        var targetContent = NormalizeDirectory(plan.TargetContentDirectory, requireExisting: false);
        if (!Directory.Exists(sourceContent))
        {
            throw new FabContentOperationException(
                "Le dossier Content source n’existe plus.",
                "ExecuteImport",
                sourceContent);
        }

        var targetRoot = Path.GetDirectoryName(plan.TargetProjectPath)!;
        ValidateSeparatedRoots(sourceContent, targetRoot);
        var stagingRoot = CombineContained(
            targetRoot,
            Path.Combine("Saved", "CyEngineHubFabStaging", plan.Id.ToString("N")));
        var backupRoot = NormalizeDirectory(plan.BackupDirectory, requireExisting: false);
        EnsureUnderRoot(targetRoot, stagingRoot);
        EnsureUnderRoot(targetRoot, backupRoot);
        EnsureNoExistingReparsePoint(targetRoot, targetContent);
        EnsureNoExistingReparsePoint(targetRoot, stagingRoot);
        EnsureNoExistingReparsePoint(targetRoot, backupRoot);

        var writtenDestinations = new List<(string Destination, bool Existed)>();
        var receiptFiles = new List<FabImportedFileReceipt>(plan.Files.Count);
        var backedUpCount = 0;
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var completed = 0;
            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = CombineContained(sourceContent, file.RelativePath);
                ValidateSourceFile(source, file);
                var staged = CombineContained(stagingRoot, file.RelativePath);
                EnsureNoExistingReparsePoint(stagingRoot, staged);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                await CopyFileAsync(source, staged, overwrite: false, cancellationToken)
                    .ConfigureAwait(false);
                completed++;
                progress?.Report(new FabProjectImportProgress(
                    completed,
                    plan.Files.Count * 2,
                    $"Préparation · {file.RelativePath}"));
            }

            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = CombineContained(targetContent, file.RelativePath);
                EnsureNoExistingReparsePoint(targetRoot, destination);
                var exists = File.Exists(destination);
                if (exists && !file.WillReplaceExisting)
                {
                    throw new FabContentOperationException(
                        $"Un nouveau conflit est apparu : {file.RelativePath}",
                        "ExecuteImport",
                        destination);
                }

                long? originalLength = null;
                DateTimeOffset? originalLastWriteTimeUtc = null;
                string? originalSha256 = null;
                if (exists)
                {
                    var originalInfo = new FileInfo(destination);
                    originalLength = originalInfo.Length;
                    originalLastWriteTimeUtc = originalInfo.LastWriteTimeUtc;
                    var backup = CombineContained(backupRoot, file.RelativePath);
                    EnsureNoExistingReparsePoint(targetRoot, backup);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    originalSha256 = await CopyFileWithSha256Async(
                            destination,
                            backup,
                            overwrite: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                    File.SetLastWriteTimeUtc(backup, originalInfo.LastWriteTimeUtc);
                    backedUpCount++;
                }

                writtenDestinations.Add((destination, exists));
                var staged = CombineContained(stagingRoot, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var importedSha256 = await CopyFileWithSha256Async(
                        staged,
                        destination,
                        overwrite: true,
                        cancellationToken)
                    .ConfigureAwait(false);
                File.SetLastWriteTimeUtc(destination, file.LastWriteTimeUtc.UtcDateTime);
                var importedInfo = new FileInfo(destination);
                receiptFiles.Add(new FabImportedFileReceipt(
                    file.RelativePath,
                    exists,
                    importedInfo.Length,
                    importedInfo.LastWriteTimeUtc,
                    importedSha256,
                    originalLength,
                    originalLastWriteTimeUtc,
                    originalSha256));
                completed++;
                progress?.Report(new FabProjectImportProgress(
                    completed,
                    plan.Files.Count * 2,
                    $"Installation · {file.RelativePath}"));
            }

            var receipt = new FabImportReceipt(
                plan.Id,
                sourceContent,
                plan.SourceDisplayName,
                plan.TargetProjectPath,
                targetContent,
                receiptFiles,
                plan.TotalBytes,
                backedUpCount == 0 ? null : backupRoot,
                plan.CompatibilitySummary,
                DateTimeOffset.UtcNow,
                null);
            receipt = await SaveReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
            return new FabProjectImportResult(
                plan.Files.Count,
                backedUpCount,
                plan.TotalBytes,
                backedUpCount == 0 ? null : backupRoot,
                receipt);
        }
        catch (OperationCanceledException)
        {
            await RollbackAsync(writtenDestinations, backupRoot, targetContent)
                .ConfigureAwait(false);
            throw;
        }
        catch (Exception exception) when (exception is not FabContentOperationException)
        {
            var rollbackErrors = await RollbackAsync(
                    writtenDestinations,
                    backupRoot,
                    targetContent)
                .ConfigureAwait(false);
            var suffix = rollbackErrors.Count == 0
                ? " Les modifications partielles ont été annulées."
                : $" Rollback incomplet : {string.Join(" · ", rollbackErrors.Take(3))}";
            throw new FabContentOperationException(
                $"Échec de l’import Fab : {exception.Message}.{suffix}",
                "ExecuteImport",
                targetContent,
                exception);
        }
        catch (FabContentOperationException)
        {
            await RollbackAsync(writtenDestinations, backupRoot, targetContent)
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            DeleteTemporaryDirectory(stagingRoot, targetRoot);
            if (backedUpCount == 0)
            {
                DeleteTemporaryDirectory(backupRoot, targetRoot);
            }
        }
    }

    public async Task<IReadOnlyList<FabImportReceipt>> LoadHistoryAsync(
        IEnumerable<string> targetProjectPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetProjectPaths);
        var receipts = new List<FabImportReceipt>();
        var seenProjects = new HashSet<string>(PathComparer);
        var seenReceipts = new HashSet<string>(PathComparer);
        foreach (var candidate in targetProjectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string projectPath;
            try
            {
                projectPath = ValidateProject(candidate);
            }
            catch (Exception exception) when (
                exception is ArgumentException or FabContentOperationException)
            {
                continue;
            }

            if (!seenProjects.Add(projectPath))
            {
                continue;
            }

            var targetRoot = Path.GetDirectoryName(projectPath)!;
            foreach (var historyRoot in GetHistoryRoots(targetRoot))
            {
                if (!Directory.Exists(historyRoot))
                {
                    continue;
                }

                try
                {
                    EnsureNoExistingReparsePoint(targetRoot, historyRoot);
                }
                catch (FabContentOperationException)
                {
                    continue;
                }

                foreach (var path in Directory.EnumerateFiles(
                             historyRoot,
                             "*.json",
                             SearchOption.TopDirectoryOnly).Take(1_001))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        RejectReparsePoint(path);
                        var info = new FileInfo(path);
                        if (info.Length is <= 0 or > MaximumReceiptBytes)
                        {
                            continue;
                        }

                        await using var stream = new FileStream(
                            path,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            64 * 1024,
                            FileOptions.Asynchronous | FileOptions.SequentialScan);
                        var receipt = await JsonSerializer.DeserializeAsync<FabImportReceipt>(
                                stream,
                                ReceiptJsonOptions,
                                cancellationToken)
                            .ConfigureAwait(false);
                        var receiptKey = receipt is null
                            ? string.Empty
                            : $"{projectPath}|{receipt.Id:N}";
                        if (receipt is null ||
                            !ValidateReceiptIdentity(receipt, projectPath, path) ||
                            !seenReceipts.Add(receiptKey))
                        {
                            continue;
                        }

                        receipts.Add(receipt with { ReceiptPath = path });
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException or JsonException or
                            FabContentOperationException)
                    {
                        // Un reçu isolé et corrompu ne doit pas masquer l’historique valide.
                    }
                }
            }
        }

        return receipts
            .OrderByDescending(receipt => receipt.ImportedAt)
            .ToArray();
    }

    public async Task<FabImportRollbackPlan> PrepareRollbackAsync(
        FabImportReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        await ValidateReceiptForRollbackAsync(
                receipt,
                "PrepareRollback",
                cancellationToken)
            .ConfigureAwait(false);
        var createdAt = DateTimeOffset.UtcNow;
        return new FabImportRollbackPlan(
            Guid.NewGuid(),
            receipt,
            createdAt,
            createdAt.Add(PlanLifetime));
    }

    public async Task<FabImportRollbackResult> ExecuteRollbackAsync(
        FabImportRollbackPlan plan,
        IProgress<FabProjectImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (DateTimeOffset.UtcNow > plan.ExpiresAt)
        {
            throw new FabContentOperationException(
                "Le plan d’annulation Fab a expiré; relancez l’analyse.",
                "ExecuteRollback",
                plan.Receipt.TargetProjectPath);
        }

        await ValidateReceiptForRollbackAsync(
                plan.Receipt,
                "ExecuteRollback",
                cancellationToken)
            .ConfigureAwait(false);
        var receipt = plan.Receipt;
        var targetRoot = Path.GetDirectoryName(receipt.TargetProjectPath)!;
        var targetContent = Path.Combine(targetRoot, "Content");
        var stagingRoot = CombineContained(
            targetRoot,
            Path.Combine("Saved", "CyEngineHubFabRollbackStaging", plan.Id.ToString("N")));
        EnsureNoExistingReparsePoint(targetRoot, stagingRoot);
        var stagedDestinations = new List<(string Staged, string Destination, DateTime LastWriteTimeUtc)>();
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var completed = 0;
            foreach (var file in receipt.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = CombineContained(targetContent, file.RelativePath);
                var staged = CombineContained(stagingRoot, file.RelativePath);
                EnsureNoExistingReparsePoint(stagingRoot, staged);
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                await CopyFileAsync(destination, staged, overwrite: false, cancellationToken)
                    .ConfigureAwait(false);
                var lastWriteTimeUtc = File.GetLastWriteTimeUtc(destination);
                File.SetLastWriteTimeUtc(staged, lastWriteTimeUtc);
                stagedDestinations.Add((staged, destination, lastWriteTimeUtc));
                completed++;
                progress?.Report(new FabProjectImportProgress(
                    completed,
                    receipt.Files.Count * 2,
                    $"Sauvegarde de sécurité · {file.RelativePath}"));
            }

            foreach (var file in receipt.Files.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = CombineContained(targetContent, file.RelativePath);
                if (file.ReplacedExisting)
                {
                    var backupRoot = receipt.BackupDirectory!;
                    var backup = CombineContained(backupRoot, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await CopyFileAsync(backup, destination, overwrite: true, cancellationToken)
                        .ConfigureAwait(false);
                    File.SetLastWriteTimeUtc(
                        destination,
                        file.OriginalLastWriteTimeUtc!.Value.UtcDateTime);
                }
                else
                {
                    File.Delete(destination);
                }

                completed++;
                progress?.Report(new FabProjectImportProgress(
                    completed,
                    receipt.Files.Count * 2,
                    $"Annulation · {file.RelativePath}"));
            }

            var rolledBackReceipt = await SaveReceiptAsync(
                    receipt with { RolledBackAt = DateTimeOffset.UtcNow },
                    cancellationToken)
                .ConfigureAwait(false);
            return new FabImportRollbackResult(
                receipt.ReplacedFileCount,
                receipt.CreatedFileCount,
                rolledBackReceipt);
        }
        catch (OperationCanceledException)
        {
            await RestoreRollbackStagingAsync(stagedDestinations).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            var restoreErrors = await RestoreRollbackStagingAsync(stagedDestinations)
                .ConfigureAwait(false);
            var suffix = restoreErrors.Count == 0
                ? " Les fichiers importés ont été remis en place."
                : $" Restauration incomplète : {string.Join(" · ", restoreErrors.Take(3))}";
            throw new FabContentOperationException(
                $"Échec de l’annulation Fab : {exception.Message}.{suffix}",
                "ExecuteRollback",
                receipt.TargetProjectPath,
                exception);
        }
        finally
        {
            DeleteTemporaryDirectory(stagingRoot, targetRoot);
        }
    }

    private static FabProjectImportPlan Prepare(
        string sourcePath,
        string targetProjectPath,
        CancellationToken cancellationToken)
    {
        var source = ResolveSourceContent(sourcePath);
        var sourceContent = source.ContentDirectory;
        var projectPath = ValidateProject(targetProjectPath);
        var targetRoot = Path.GetDirectoryName(projectPath)!;
        ValidateSeparatedRoots(sourceContent, targetRoot);
        EnsureNoExistingReparsePoint(sourceContent, sourceContent);
        EnsureNoExistingReparsePoint(targetRoot, targetRoot);

        var targetContent = Path.Combine(targetRoot, "Content");
        var compatibility = EvaluateCompatibility(
            ReadEngineAssociation(source.ProjectPath),
            ReadEngineAssociation(projectPath),
            sourceContent);
        var inventory = InventorySource(sourceContent, cancellationToken);
        if (!inventory.Files.Any(file =>
                string.Equals(Path.GetExtension(file.RelativePath), ".uasset", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(file.RelativePath), ".umap", StringComparison.OrdinalIgnoreCase)))
        {
            throw new FabContentOperationException(
                "La source ne contient aucun .uasset ou .umap. Utilisez l’intégration Fab de l’Editor pour les formats FBX, GLTF ou USD.",
                "PrepareImport",
                sourceContent);
        }

        var files = inventory.Files
            .Select(file => file with
            {
                WillReplaceExisting = File.Exists(CombineContained(targetContent, file.RelativePath)),
            })
            .ToArray();
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var sourceRoot = Directory.GetParent(sourceContent)?.FullName ?? sourceContent;
        var sourceDisplayName = Path.GetFileName(sourceRoot);
        var backupDirectory = Path.Combine(
            targetRoot,
            "Saved",
            "CyEngineHubFabBackups",
            $"{createdAt:yyyyMMdd-HHmmss}-{id:N}");
        return new FabProjectImportPlan(
            id,
            sourceContent,
            sourceDisplayName,
            projectPath,
            targetContent,
            files,
            inventory.SkippedFileCount,
            inventory.TotalBytes,
            compatibility,
            backupDirectory,
            createdAt,
            createdAt.Add(PlanLifetime));
    }

    private static SourceInventory InventorySource(
        string sourceContent,
        CancellationToken cancellationToken)
    {
        var files = new List<FabContentFile>();
        var directories = new Stack<string>();
        directories.Push(sourceContent);
        var skipped = 0;
        long totalBytes = 0;
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            RejectReparsePoint(directory);
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                RejectReparsePoint(child);
                directories.Push(child);
            }

            foreach (var path in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectReparsePoint(path);
                if (!AllowedExtensions.Contains(Path.GetExtension(path)))
                {
                    skipped++;
                    continue;
                }

                if (files.Count >= MaximumFileCount)
                {
                    throw new FabContentOperationException(
                        $"Le contenu dépasse la limite de {MaximumFileCount:N0} fichiers.",
                        "PrepareImport",
                        sourceContent);
                }

                var info = new FileInfo(path);
                totalBytes = checked(totalBytes + info.Length);
                if (totalBytes > MaximumTotalBytes)
                {
                    throw new FabContentOperationException(
                        "Le contenu dépasse la limite de 100 GB.",
                        "PrepareImport",
                        sourceContent);
                }

                var relativePath = Path.GetRelativePath(sourceContent, path);
                CombineContained(sourceContent, relativePath);
                files.Add(new FabContentFile(
                    relativePath,
                    info.Length,
                    info.LastWriteTimeUtc,
                    false));
            }
        }

        if (files.Count == 0)
        {
            throw new FabContentOperationException(
                "Aucun fichier de contenu Unreal pris en charge n’a été trouvé.",
                "PrepareImport",
                sourceContent);
        }

        return new SourceInventory(files, skipped, totalBytes);
    }

    private static ResolvedSource ResolveSourceContent(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var fullPath = Path.GetFullPath(sourcePath.Trim().Trim('"'));
        string? explicitProjectPath = null;
        if (File.Exists(fullPath))
        {
            if (!string.Equals(Path.GetExtension(fullPath), ".uproject", StringComparison.OrdinalIgnoreCase))
            {
                throw new FabContentOperationException(
                    "Sélectionnez un dossier Content, un projet extrait ou son fichier .uproject.",
                    "PrepareImport",
                    fullPath);
            }

            explicitProjectPath = fullPath;
            fullPath = Path.GetDirectoryName(fullPath)!;
        }

        if (!Directory.Exists(fullPath))
        {
            throw new FabContentOperationException(
                "La source Fab locale n’existe pas.",
                "PrepareImport",
                fullPath);
        }

        if (string.Equals(Path.GetFileName(fullPath), "Content", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(fullPath)?.FullName;
            var project = explicitProjectPath ?? FindSingleProjectFile(parent);
            return new ResolvedSource(NormalizeDirectory(fullPath), project);
        }

        var direct = Path.Combine(fullPath, "Content");
        if (Directory.Exists(direct))
        {
            var project = explicitProjectPath ?? FindSingleProjectFile(fullPath);
            return new ResolvedSource(NormalizeDirectory(direct), project);
        }

        var nestedCandidates = Directory.EnumerateDirectories(fullPath)
            .Take(102)
            .Where(directory => Directory.Exists(Path.Combine(directory, "Content")))
            .Where(directory => Directory.EnumerateFiles(directory, "*.uproject", SearchOption.TopDirectoryOnly).Any())
            .Take(2)
            .ToArray();
        if (nestedCandidates.Length == 1)
        {
            return new ResolvedSource(
                NormalizeDirectory(Path.Combine(nestedCandidates[0], "Content")),
                FindSingleProjectFile(nestedCandidates[0]));
        }

        throw new FabContentOperationException(
            "Aucun dossier Content Unreal non ambigu n’a été trouvé dans la source.",
            "PrepareImport",
            fullPath);
    }

    private static string? FindSingleProjectFile(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        var projects = Directory.EnumerateFiles(directory, "*.uproject", SearchOption.TopDirectoryOnly)
            .Take(2)
            .ToArray();
        return projects.Length == 1 ? projects[0] : null;
    }

    private static string EvaluateCompatibility(
        string? sourceAssociation,
        string? targetAssociation,
        string sourceContent)
    {
        if (TryParseEngineVersion(sourceAssociation, out var sourceVersion) &&
            TryParseEngineVersion(targetAssociation, out var targetVersion))
        {
            if (sourceVersion > targetVersion)
            {
                throw new FabContentOperationException(
                    $"Le contenu source UE {sourceVersion} est plus récent que le projet cible UE {targetVersion}.",
                    "ValidateCompatibility",
                    sourceContent);
            }

            return $"Source UE {sourceVersion} → cible UE {targetVersion} · compatibilité de version acceptable";
        }

        return "Compatibilité moteur non vérifiable automatiquement; contrôlez le pack dans l’Editor après import.";
    }

    private static string? ReadEngineAssociation(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || !File.Exists(projectPath))
        {
            return null;
        }

        var info = new FileInfo(projectPath);
        if (info.Length > 2 * 1024 * 1024)
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(
                projectPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.TryGetProperty("EngineAssociation", out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool TryParseEngineVersion(string? association, out Version version)
    {
        var normalized = association?
            .Replace("UE_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();
        if (Version.TryParse(normalized, out var parsed) && parsed.Major > 0)
        {
            version = new Version(parsed.Major, Math.Max(parsed.Minor, 0));
            return true;
        }

        version = new Version(0, 0);
        return false;
    }

    private static async Task<FabImportReceipt> SaveReceiptAsync(
        FabImportReceipt receipt,
        CancellationToken cancellationToken)
    {
        var projectPath = ValidateProject(receipt.TargetProjectPath);
        var targetRoot = Path.GetDirectoryName(projectPath)!;
        var historyRoot = GetHistoryRoot(targetRoot);
        var receiptPath = GetReceiptPath(targetRoot, receipt.Id);
        var previousReceiptPath = receipt.ReceiptPath;
        if (!ValidateReceiptIdentity(receipt, projectPath, receiptPath))
        {
            throw new FabContentOperationException(
                "Le reçu d’import Fab est incohérent avec le projet cible.",
                "SaveReceipt",
                receiptPath);
        }

        EnsureNoExistingReparsePoint(targetRoot, historyRoot);
        Directory.CreateDirectory(historyRoot);
        EnsureNoExistingReparsePoint(targetRoot, historyRoot);
        var temporaryPath = receiptPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
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

            File.Move(temporaryPath, receiptPath, overwrite: true);
            DeleteLegacyReceiptAfterMigration(
                targetRoot,
                receipt.Id,
                previousReceiptPath,
                receiptPath);
            return receipt with { ReceiptPath = receiptPath };
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Le fichier temporaire reste contenu dans Saved; ne pas masquer le résultat principal.
            }
        }
    }

    private static bool ValidateReceiptIdentity(
        FabImportReceipt receipt,
        string projectPath,
        string receiptPath)
    {
        if (receipt.Id == Guid.Empty ||
            receipt.Files is null ||
            receipt.Files.Count is <= 0 or > MaximumFileCount ||
            receipt.Files.Any(file => file is null) ||
            receipt.ImportedBytes is < 0 or > MaximumTotalBytes ||
            string.IsNullOrWhiteSpace(receipt.SourceDisplayName) ||
            string.IsNullOrWhiteSpace(receipt.SourceContentDirectory) ||
            string.IsNullOrWhiteSpace(receipt.TargetProjectPath) ||
            string.IsNullOrWhiteSpace(receipt.TargetContentDirectory))
        {
            return false;
        }

        try
        {
            var targetRoot = Path.GetDirectoryName(projectPath)!;
            var expectedContent = Path.Combine(targetRoot, "Content");
            var expectedReceipt = GetReceiptPath(targetRoot, receipt.Id);
            var legacyReceipt = GetLegacyReceiptPath(targetRoot, receipt.Id);
            return SamePath(receipt.TargetProjectPath, projectPath) &&
                   SamePath(receipt.TargetContentDirectory, expectedContent) &&
                   (SamePath(receiptPath, expectedReceipt) ||
                    SamePath(receiptPath, legacyReceipt));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private static async Task ValidateReceiptForRollbackAsync(
        FabImportReceipt receipt,
        string operation,
        CancellationToken cancellationToken)
    {
        if (!receipt.IsApplied)
        {
            throw new FabContentOperationException(
                "Cet import Fab a déjà été annulé.",
                operation,
                receipt.TargetProjectPath);
        }

        var projectPath = ValidateProject(receipt.TargetProjectPath);
        var targetRoot = Path.GetDirectoryName(projectPath)!;
        var receiptPath = ResolveExistingReceiptPath(targetRoot, receipt);
        if (!ValidateReceiptIdentity(receipt, projectPath, receiptPath) ||
            !File.Exists(receiptPath))
        {
            throw new FabContentOperationException(
                "Le reçu d’import Fab est absent ou incohérent.",
                operation,
                receiptPath);
        }

        EnsureNoExistingReparsePoint(targetRoot, receiptPath);
        var targetContent = Path.Combine(targetRoot, "Content");
        var seenPaths = new HashSet<string>(PathComparer);
        foreach (var file in receipt.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(file.RelativePath) ||
                Path.IsPathRooted(file.RelativePath) ||
                !AllowedExtensions.Contains(Path.GetExtension(file.RelativePath)) ||
                !seenPaths.Add(file.RelativePath) ||
                file.ImportedLength < 0 ||
                !IsSha256(file.ImportedSha256))
            {
                throw new FabContentOperationException(
                    "Le reçu contient une entrée de fichier invalide.",
                    operation,
                    receiptPath);
            }

            var destination = CombineContained(targetContent, file.RelativePath);
            EnsureNoExistingReparsePoint(targetRoot, destination);
            var info = new FileInfo(destination);
            if (!info.Exists ||
                info.Length != file.ImportedLength ||
                info.LastWriteTimeUtc != file.ImportedLastWriteTimeUtc.UtcDateTime)
            {
                throw new FabContentOperationException(
                    $"Le fichier a changé depuis l’import : {file.RelativePath}. L’annulation est bloquée.",
                    operation,
                    destination);
            }

            var importedSha256 = await ComputeSha256Async(destination, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    importedSha256,
                    file.ImportedSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FabContentOperationException(
                    $"Le contenu du fichier a changé depuis l’import : {file.RelativePath}. L’annulation est bloquée.",
                    operation,
                    destination);
            }

            if (!file.ReplacedExisting)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(receipt.BackupDirectory) ||
                file.OriginalLength is null ||
                file.OriginalLastWriteTimeUtc is null ||
                !IsSha256(file.OriginalSha256))
            {
                throw new FabContentOperationException(
                    $"Les métadonnées de backup sont incomplètes : {file.RelativePath}.",
                    operation,
                    receiptPath);
            }

            var backupRoot = NormalizeDirectory(receipt.BackupDirectory, requireExisting: false);
            EnsureUnderRoot(targetRoot, backupRoot);
            var backup = CombineContained(backupRoot, file.RelativePath);
            EnsureNoExistingReparsePoint(targetRoot, backup);
            var backupInfo = new FileInfo(backup);
            if (!backupInfo.Exists ||
                backupInfo.Length != file.OriginalLength.Value ||
                backupInfo.LastWriteTimeUtc != file.OriginalLastWriteTimeUtc.Value.UtcDateTime)
            {
                throw new FabContentOperationException(
                    $"Le backup est absent ou a changé : {file.RelativePath}.",
                    operation,
                    backup);
            }

            var backupSha256 = await ComputeSha256Async(backup, cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    backupSha256,
                    file.OriginalSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new FabContentOperationException(
                    $"Le backup a été modifié : {file.RelativePath}.",
                    operation,
                    backup);
            }
        }
    }

    private static string GetHistoryRoot(string targetRoot) =>
        CombineContained(targetRoot, Path.Combine("Saved", "CyEngineHubFabImports"));

    private static string GetLegacyHistoryRoot(string targetRoot) =>
        CombineContained(targetRoot, Path.Combine("Saved", "CyUEHubFabImports"));

    private static IEnumerable<string> GetHistoryRoots(string targetRoot)
    {
        yield return GetHistoryRoot(targetRoot);
        yield return GetLegacyHistoryRoot(targetRoot);
    }

    private static string GetReceiptPath(string targetRoot, Guid id) =>
        CombineContained(GetHistoryRoot(targetRoot), $"{id:N}.json");

    private static string GetLegacyReceiptPath(string targetRoot, Guid id) =>
        CombineContained(GetLegacyHistoryRoot(targetRoot), $"{id:N}.json");

    private static string ResolveExistingReceiptPath(
        string targetRoot,
        FabImportReceipt receipt)
    {
        var currentPath = GetReceiptPath(targetRoot, receipt.Id);
        var legacyPath = GetLegacyReceiptPath(targetRoot, receipt.Id);
        if (!string.IsNullOrWhiteSpace(receipt.ReceiptPath))
        {
            // La validation suivante doit voir le chemin fourni tel quel afin de
            // rejeter un reçu forgé au lieu de retomber sur un fichier valide.
            return receipt.ReceiptPath;
        }

        return File.Exists(currentPath) || !File.Exists(legacyPath)
            ? currentPath
            : legacyPath;
    }

    private static void DeleteLegacyReceiptAfterMigration(
        string targetRoot,
        Guid id,
        string previousReceiptPath,
        string currentReceiptPath)
    {
        if (string.IsNullOrWhiteSpace(previousReceiptPath) ||
            SamePath(previousReceiptPath, currentReceiptPath) ||
            !SamePath(previousReceiptPath, GetLegacyReceiptPath(targetRoot, id)))
        {
            return;
        }

        try
        {
            RejectReparsePoint(previousReceiptPath);
            File.Delete(previousReceiptPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                FabContentOperationException)
        {
            // Le reçu courant est déjà écrit; l'historique déduplique l'ancien si sa suppression échoue.
        }
    }

    private static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static string ValidateProject(string targetProjectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetProjectPath);
        var fullPath = Path.GetFullPath(targetProjectPath.Trim().Trim('"'));
        if (!File.Exists(fullPath) ||
            !string.Equals(Path.GetExtension(fullPath), ".uproject", StringComparison.OrdinalIgnoreCase))
        {
            throw new FabContentOperationException(
                "Le projet cible .uproject n’existe pas.",
                "ValidateTarget",
                fullPath);
        }

        return fullPath;
    }

    private static void ValidateSeparatedRoots(string sourceContent, string targetRoot)
    {
        if (IsSameOrChild(sourceContent, targetRoot) || IsSameOrChild(targetRoot, sourceContent))
        {
            throw new FabContentOperationException(
                "La source Fab et le projet cible doivent être dans des arborescences séparées.",
                "ValidateRoots",
                sourceContent);
        }
    }

    private static void ValidateSourceFile(string path, FabContentFile expected)
    {
        var info = new FileInfo(path);
        if (!info.Exists ||
            info.Length != expected.Length ||
            info.LastWriteTimeUtc != expected.LastWriteTimeUtc.UtcDateTime)
        {
            throw new FabContentOperationException(
                $"La source a changé depuis la préparation : {expected.RelativePath}",
                "ExecuteImport",
                path);
        }

        RejectReparsePoint(path);
    }

    private static async Task CopyFileAsync(
        string source,
        string destination,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var destinationMode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            destinationMode,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 1024 * 1024, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> CopyFileWithSha256Async(
        string source,
        string destination,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        var destinationMode = overwrite ? FileMode.Create : FileMode.CreateNew;
        await using var input = new FileStream(
            source,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            destination,
            destinationMode,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 1024);
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
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
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static async Task<IReadOnlyList<string>> RestoreRollbackStagingAsync(
        IReadOnlyList<(string Staged, string Destination, DateTime LastWriteTimeUtc)> stagedFiles)
    {
        var errors = new List<string>();
        foreach (var (staged, destination, lastWriteTimeUtc) in stagedFiles)
        {
            try
            {
                if (!File.Exists(staged))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(staged, destination, overwrite: true);
                File.SetLastWriteTimeUtc(destination, lastWriteTimeUtc);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add(exception.Message);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return errors;
    }

    private static async Task<IReadOnlyList<string>> RollbackAsync(
        IReadOnlyList<(string Destination, bool Existed)> writtenDestinations,
        string backupRoot,
        string targetContent)
    {
        var errors = new List<string>();
        foreach (var (destination, existed) in writtenDestinations.Reverse())
        {
            try
            {
                if (existed)
                {
                    var relative = Path.GetRelativePath(targetContent, destination);
                    var backup = CombineContained(backupRoot, relative);
                    if (File.Exists(backup))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(backup, destination, overwrite: true);
                    }
                }
                else if (File.Exists(destination))
                {
                    File.Delete(destination);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add(exception.Message);
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return errors;
    }

    private static void DeleteTemporaryDirectory(string path, string targetRoot)
    {
        try
        {
            EnsureUnderRoot(targetRoot, path);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A retained staging/backup directory is safer than masking the operation result.
        }
    }

    private static string CombineContained(string root, string relativePath)
    {
        var normalizedRoot = NormalizeDirectory(root, requireExisting: false);
        var candidate = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        EnsureUnderRoot(normalizedRoot, candidate);
        return candidate;
    }

    private static void EnsureUnderRoot(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedCandidate = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(normalizedRoot, normalizedCandidate, comparison))
        {
            return;
        }

        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!normalizedCandidate.StartsWith(prefix, comparison))
        {
            throw new FabContentOperationException(
                "Un chemin de contenu sort de la racine autorisée.",
                "ValidatePath",
                normalizedCandidate);
        }
    }

    private static void EnsureNoExistingReparsePoint(string root, string candidate)
    {
        EnsureUnderRoot(root, candidate);
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        RejectReparsePoint(current);
        var relative = Path.GetRelativePath(current, Path.GetFullPath(candidate));
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current) || File.Exists(current))
            {
                RejectReparsePoint(current);
            }
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new FabContentOperationException(
                "Les liens et points de réanalyse ne sont pas autorisés pendant un import Fab.",
                "ValidatePath",
                path);
        }
    }

    private static bool IsSameOrChild(string path, string possibleParent)
    {
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(possibleParent));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedPath, normalizedParent, comparison) ||
               normalizedPath.StartsWith(normalizedParent + Path.DirectorySeparatorChar, comparison);
    }

    private static bool SamePath(string first, string second) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
            PathComparison);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string NormalizeDirectory(string path, bool requireExisting = true)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (requireExisting && !Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException(normalized);
        }

        return normalized;
    }

    private sealed record SourceInventory(
        IReadOnlyList<FabContentFile> Files,
        int SkippedFileCount,
        long TotalBytes);

    private sealed record ResolvedSource(string ContentDirectory, string? ProjectPath);
}
