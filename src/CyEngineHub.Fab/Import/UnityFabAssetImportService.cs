using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyEngineHub.Fab.Import;

public interface IUnityFabAssetImportService
{
    Task<UnityFabAssetImportPlan> PrepareAsync(string sourcePath, string targetProjectRoot, CancellationToken cancellationToken = default);
    Task<UnityFabAssetImportResult> ExecuteAsync(UnityFabAssetImportPlan plan, IProgress<UnityFabAssetImportProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UnityFabAssetImportReceipt>> LoadHistoryAsync(IEnumerable<string> targetProjectRoots, CancellationToken cancellationToken = default);
    Task<UnityFabAssetRollbackPlan> PrepareRollbackAsync(UnityFabAssetImportReceipt receipt, CancellationToken cancellationToken = default);
    Task<UnityFabAssetRollbackResult> ExecuteRollbackAsync(UnityFabAssetRollbackPlan plan, IProgress<UnityFabAssetImportProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class UnityFabAssetImportService : IUnityFabAssetImportService
{
    private const int MaximumFileCount = 50_000;
    private const long MaximumTotalBytes = 20L * 1024 * 1024 * 1024;
    private const long MaximumReceiptBytes = 8L * 1024 * 1024;
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public Task<UnityFabAssetImportPlan> PrepareAsync(string sourcePath, string targetProjectRoot, CancellationToken cancellationToken = default) =>
        Task.Run(() => Prepare(sourcePath, targetProjectRoot, cancellationToken), cancellationToken);

    public async Task<UnityFabAssetImportResult> ExecuteAsync(UnityFabAssetImportPlan plan, IProgress<UnityFabAssetImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureFresh(plan.ExpiresAt, "Le plan d’import Unity/Fab a expiré; relancez l’analyse.");
        var projectRoot = ValidateUnityProject(plan.TargetProjectRoot);
        var sourceRoot = NormalizeDirectory(plan.SourceAssetsDirectory, true);
        var targetRoot = NormalizeDirectory(plan.TargetImportDirectory, false);
        EnsureUnderRoot(projectRoot, targetRoot);
        EnsureSeparated(sourceRoot, projectRoot);
        EnsureNoReparsePoint(projectRoot, targetRoot);

        var targetExistsAtExecution = Directory.Exists(targetRoot);
        if (targetExistsAtExecution != plan.ReplacesExistingImport)
        {
            throw Failure("La destination a changé depuis l’analyse.", "ExecuteUnityImport", targetRoot);
        }
        var targetTreeSha = targetExistsAtExecution
            ? ComputeTreeFingerprint(targetRoot, cancellationToken)
            : null;
        if (!string.Equals(targetTreeSha, plan.ExistingTargetTreeSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("La destination Unity a changé depuis l’analyse.", "ExecuteUnityImport", targetRoot);
        }

        var current = Inventory(sourceRoot, cancellationToken);
        EnsureSameInventory(plan.Files, current.Files);
        if (current.SkippedFileCount != plan.SkippedFileCount)
        {
            throw Failure("Le pack source a changé depuis l’analyse.", "ExecuteUnityImport", sourceRoot);
        }

        var workspace = GetWorkspaceRoot(projectRoot);
        var staging = CombineContained(workspace, Path.Combine("staging", plan.Id.ToString("N")));
        var backup = NormalizeDirectory(plan.BackupDirectory, false);
        EnsureUnderRoot(workspace, staging);
        EnsureUnderRoot(workspace, backup);
        EnsureNoReparsePoint(workspace, staging);
        EnsureNoReparsePoint(workspace, backup);
        if (Directory.Exists(staging) || Directory.Exists(backup))
        {
            throw Failure("Le staging ou backup existe déjà.", "ExecuteUnityImport", workspace);
        }

        Directory.CreateDirectory(staging);
        var targetMoved = false;
        var stagedMoved = false;
        try
        {
            var completed = 0;
            foreach (var file in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = CombineContained(sourceRoot, file.RelativePath);
                ValidateSourceFile(source, file, cancellationToken);
                var destination = CombineContained(staging, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await CopyAndVerifyAsync(source, destination, file.Sha256, cancellationToken).ConfigureAwait(false);
                progress?.Report(new UnityFabAssetImportProgress(++completed, plan.Files.Count, $"Staging Unity {completed:N0}/{plan.Files.Count:N0}"));
            }

            var targetExists = Directory.Exists(targetRoot);

            var targetMeta = targetRoot + ".meta";
            if (!targetExists && File.Exists(targetMeta))
            {
                throw Failure("Un fichier .meta orphelin occupe la destination Unity.", "ExecuteUnityImport", targetMeta);
            }

            var metaInitiallyExisted = File.Exists(targetMeta);
            Directory.CreateDirectory(Path.GetDirectoryName(targetRoot)!);
            if (targetExists)
            {
                EnsureNoReparsePoint(projectRoot, targetRoot);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                Directory.Move(targetRoot, backup);
                targetMoved = true;
            }

            Directory.Move(staging, targetRoot);
            stagedMoved = true;
            var receipt = new UnityFabAssetImportReceipt(plan.Id, sourceRoot, plan.SourceDisplayName, projectRoot, targetRoot, plan.Files, plan.TotalBytes, targetExists, targetExists ? backup : null, targetTreeSha, metaInitiallyExisted, DateTimeOffset.UtcNow, null);
            receipt = await SaveReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
            return new UnityFabAssetImportResult(plan.Files.Count, plan.TotalBytes, receipt);
        }
        catch
        {
            try
            {
                if (stagedMoved && Directory.Exists(targetRoot)) Directory.Delete(targetRoot, true);
                if (targetMoved && Directory.Exists(backup) && !Directory.Exists(targetRoot)) Directory.Move(backup, targetRoot);
            }
            catch { }
            throw;
        }
        finally
        {
            DeleteDirectoryIfExists(staging);
        }
    }

    public Task<IReadOnlyList<UnityFabAssetImportReceipt>> LoadHistoryAsync(IEnumerable<string> targetProjectRoots, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<UnityFabAssetImportReceipt>>(() => LoadHistory(targetProjectRoots, cancellationToken), cancellationToken);

    public Task<UnityFabAssetRollbackPlan> PrepareRollbackAsync(UnityFabAssetImportReceipt receipt, CancellationToken cancellationToken = default) =>
        Task.Run(() => PrepareRollback(receipt, cancellationToken), cancellationToken);

    public async Task<UnityFabAssetRollbackResult> ExecuteRollbackAsync(UnityFabAssetRollbackPlan plan, IProgress<UnityFabAssetImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureFresh(plan.ExpiresAt, "Le plan d’annulation Unity/Fab a expiré; relancez l’analyse.");
        var receipt = plan.Receipt;
        ValidateReceiptTrust(receipt);
        var projectRoot = ValidateUnityProject(receipt.TargetProjectRoot);
        ValidateImportedTree(receipt, cancellationToken);
        ValidateGeneratedMeta(receipt, plan.GeneratedTargetMetaSha256, cancellationToken);
        var rollbackStaging = CombineContained(GetWorkspaceRoot(projectRoot), Path.Combine("rollback", plan.Id.ToString("N")));
        EnsureNoReparsePoint(projectRoot, receipt.TargetImportDirectory);
        if (Directory.Exists(rollbackStaging))
        {
            throw Failure("Le staging d’annulation existe déjà.", "RollbackUnityImport", rollbackStaging);
        }

        var targetMeta = receipt.TargetImportDirectory + ".meta";
        var rollbackMeta = rollbackStaging + ".meta";
        var importedMoved = false;
        var generatedMetaMoved = false;
        var backupRestored = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(rollbackStaging)!);
            Directory.Move(receipt.TargetImportDirectory, rollbackStaging);
            importedMoved = true;
            if (!receipt.TargetMetaInitiallyExisted && File.Exists(targetMeta))
            {
                File.Move(targetMeta, rollbackMeta);
                generatedMetaMoved = true;
            }
            if (receipt.ReplacedExistingImport)
            {
                Directory.Move(receipt.BackupDirectory!, receipt.TargetImportDirectory);
                backupRestored = true;
            }

            var updated = receipt with { RolledBackAt = DateTimeOffset.UtcNow };
            updated = await SaveReceiptAsync(updated, cancellationToken).ConfigureAwait(false);
            try
            {
                DeleteDirectoryIfExists(rollbackStaging);
                if (generatedMetaMoved && File.Exists(rollbackMeta)) File.Delete(rollbackMeta);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The target and receipt already represent a completed rollback;
                // stale staging is safer than undoing that committed state.
            }
            progress?.Report(new UnityFabAssetImportProgress(1, 1, "Import Unity annulé"));
            return new UnityFabAssetRollbackResult(backupRestored, updated);
        }
        catch
        {
            try
            {
                if (backupRestored && Directory.Exists(receipt.TargetImportDirectory)) Directory.Move(receipt.TargetImportDirectory, receipt.BackupDirectory!);
                if (importedMoved && Directory.Exists(rollbackStaging) && !Directory.Exists(receipt.TargetImportDirectory)) Directory.Move(rollbackStaging, receipt.TargetImportDirectory);
                if (generatedMetaMoved && File.Exists(rollbackMeta) && !File.Exists(targetMeta)) File.Move(rollbackMeta, targetMeta);
            }
            catch { }
            throw;
        }
    }

    private static UnityFabAssetImportPlan Prepare(string sourcePath, string targetProjectRoot, CancellationToken cancellationToken)
    {
        var projectRoot = ValidateUnityProject(targetProjectRoot);
        var sourceRoot = ResolveSourceAssets(sourcePath);
        EnsureSeparated(sourceRoot, projectRoot);
        var inventory = Inventory(sourceRoot, cancellationToken);
        if (inventory.Files.Count == 0)
        {
            throw Failure("Le pack ne contient aucun asset Unity pris en charge.", "PrepareUnityImport", sourceRoot);
        }

        var displayRoot = string.Equals(Path.GetFileName(sourceRoot), "Assets", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(sourceRoot)!
            : sourceRoot;
        var displayName = Path.GetFileName(displayRoot.TrimEnd(Path.DirectorySeparatorChar));
        var target = CombineContained(projectRoot, Path.Combine("Assets", "CyEngineHubImports", SanitizeDirectoryName(displayName)));
        EnsureNoReparsePoint(projectRoot, target);
        var targetExists = Directory.Exists(target);
        if (!targetExists && File.Exists(target + ".meta"))
        {
            throw Failure("Un fichier .meta orphelin occupe la destination Unity.", "PrepareUnityImport", target + ".meta");
        }

        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var backup = CombineContained(GetWorkspaceRoot(projectRoot), Path.Combine("backups", id.ToString("N")));
        var existingTreeSha = targetExists ? ComputeTreeFingerprint(target, cancellationToken) : null;
        return new UnityFabAssetImportPlan(id, sourceRoot, displayName, projectRoot, target, inventory.Files, inventory.SkippedFileCount, inventory.TotalBytes, targetExists, existingTreeSha, backup, created, created.Add(PlanLifetime));
    }

    private static UnityFabAssetRollbackPlan PrepareRollback(UnityFabAssetImportReceipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateReceiptTrust(receipt);
        if (!receipt.IsApplied)
        {
            throw Failure("Cet import Unity a déjà été annulé.", "PrepareUnityRollback", receipt.ReceiptPath);
        }

        ValidateUnityProject(receipt.TargetProjectRoot);
        ValidateImportedTree(receipt, cancellationToken);
        if (receipt.ReplacedExistingImport)
        {
            if (string.IsNullOrWhiteSpace(receipt.BackupDirectory) || !Directory.Exists(receipt.BackupDirectory))
            {
                throw Failure("Le backup de l’import Unity est introuvable.", "PrepareUnityRollback", receipt.BackupDirectory);
            }
            EnsureNoReparsePoint(GetWorkspaceRoot(receipt.TargetProjectRoot), receipt.BackupDirectory);
            var backupTreeSha = ComputeTreeFingerprint(receipt.BackupDirectory, cancellationToken);
            if (!string.Equals(backupTreeSha, receipt.BackupTreeSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("Le backup de l’import Unity a été modifié.", "PrepareUnityRollback", receipt.BackupDirectory);
            }
        }

        var targetMeta = receipt.TargetImportDirectory + ".meta";
        string? generatedMetaSha = null;
        if (!receipt.TargetMetaInitiallyExisted && File.Exists(targetMeta))
        {
            EnsureRegularFile(targetMeta, "PrepareUnityRollback");
            generatedMetaSha = ComputeSha256(targetMeta, cancellationToken);
        }

        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        return new UnityFabAssetRollbackPlan(id, receipt, generatedMetaSha, created, created.Add(PlanLifetime));
    }

    private static (IReadOnlyList<UnityFabAssetFile> Files, int SkippedFileCount, long TotalBytes) Inventory(string sourceRoot, CancellationToken cancellationToken)
    {
        var files = new List<UnityFabAssetFile>();
        var skipped = 0;
        long bytes = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(sourceRoot));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop();
            EnsureNotReparsePoint(directory, "InventoryUnityPack");
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNotReparsePoint(entry, "InventoryUnityPack");
                if (entry is DirectoryInfo child)
                {
                    stack.Push(child);
                    continue;
                }

                var file = (FileInfo)entry;
                if (UnityFabAssetPolicy.IsBlocked(file.Extension))
                {
                    throw Failure($"Le pack contient du code ou un binaire bloqué ({file.Extension}).", "InventoryUnityPack", file.FullName);
                }
                if (!UnityFabAssetPolicy.IsSupported(file.Extension))
                {
                    skipped++;
                    continue;
                }
                if (files.Count >= MaximumFileCount)
                {
                    throw Failure($"Le pack dépasse la limite de {MaximumFileCount:N0} fichiers.", "InventoryUnityPack", sourceRoot);
                }

                bytes = checked(bytes + file.Length);
                if (bytes > MaximumTotalBytes)
                {
                    throw Failure("Le pack dépasse la limite de 20 Gio.", "InventoryUnityPack", sourceRoot);
                }
                files.Add(new UnityFabAssetFile(Path.GetRelativePath(sourceRoot, file.FullName), file.Length, file.LastWriteTimeUtc, ComputeSha256(file.FullName, cancellationToken)));
            }
        }

        return (files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(), skipped, bytes);
    }

    private static UnityFabAssetImportReceipt[] LoadHistory(IEnumerable<string> targetProjectRoots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetProjectRoots);
        var receipts = new List<UnityFabAssetImportReceipt>();
        foreach (var candidate in targetProjectRoots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root;
            try { root = ValidateUnityProject(candidate); }
            catch (FabContentOperationException) { continue; }

            var receiptRoot = Path.Combine(GetWorkspaceRoot(root), "receipts");
            if (!Directory.Exists(receiptRoot)) continue;
            EnsureNoReparsePoint(GetWorkspaceRoot(root), receiptRoot);
            foreach (var path in Directory.EnumerateFiles(receiptRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    EnsureRegularFile(path, "LoadUnityImportHistory");
                    if (new FileInfo(path).Length > MaximumReceiptBytes) continue;
                    var receipt = JsonSerializer.Deserialize<UnityFabAssetImportReceipt>(File.ReadAllText(path), JsonOptions);
                    if (receipt is not null && string.Equals(NormalizeDirectory(receipt.TargetProjectRoot, true), root, StringComparison.OrdinalIgnoreCase))
                    {
                        receipts.Add(receipt with { ReceiptPath = path });
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or FabContentOperationException) { }
            }
        }
        return receipts.OrderByDescending(receipt => receipt.ImportedAt).ToArray();
    }

    private static void ValidateReceiptTrust(UnityFabAssetImportReceipt receipt)
    {
        var projectRoot = ValidateUnityProject(receipt.TargetProjectRoot);
        var workspace = GetWorkspaceRoot(projectRoot);
        var expectedReceiptPath = CombineContained(workspace, Path.Combine("receipts", receipt.Id.ToString("N") + ".json"));
        if (string.IsNullOrWhiteSpace(receipt.ReceiptPath) ||
            !string.Equals(Path.GetFullPath(receipt.ReceiptPath), expectedReceiptPath, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Le reçu Unity ne provient pas de l’historique sécurisé du projet.", "ValidateUnityReceipt", receipt.ReceiptPath);
        }
        EnsureRegularFile(expectedReceiptPath, "ValidateUnityReceipt");
        if (new FileInfo(expectedReceiptPath).Length > MaximumReceiptBytes)
        {
            throw Failure("Le reçu Unity dépasse la taille autorisée.", "ValidateUnityReceipt", expectedReceiptPath);
        }

        UnityFabAssetImportReceipt? stored;
        try { stored = JsonSerializer.Deserialize<UnityFabAssetImportReceipt>(File.ReadAllText(expectedReceiptPath), JsonOptions); }
        catch (JsonException exception)
        {
            throw new FabContentOperationException("Le reçu Unity est illisible.", "ValidateUnityReceipt", expectedReceiptPath, exception);
        }
        if (stored is null || !ReceiptsMatch(receipt, stored))
        {
            throw Failure("Le reçu Unity ne correspond plus au reçu enregistré.", "ValidateUnityReceipt", expectedReceiptPath);
        }

        var importsRoot = CombineContained(projectRoot, Path.Combine("Assets", "CyEngineHubImports"));
        var target = NormalizeDirectory(receipt.TargetImportDirectory, true);
        EnsureUnderRoot(importsRoot, target);
        if (string.Equals(importsRoot, target, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Le reçu cible la racine partagée des imports Unity.", "ValidateUnityReceipt", target);
        }

        if (receipt.ReplacedExistingImport)
        {
            var expectedBackup = CombineContained(workspace, Path.Combine("backups", receipt.Id.ToString("N")));
            if (!string.Equals(receipt.BackupDirectory, expectedBackup, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("Le chemin de backup du reçu Unity est invalide.", "ValidateUnityReceipt", receipt.BackupDirectory);
            }
        }
        else if (receipt.BackupDirectory is not null)
        {
            throw Failure("Un nouvel import Unity ne doit pas déclarer de backup.", "ValidateUnityReceipt", receipt.BackupDirectory);
        }
    }

    private static bool ReceiptsMatch(UnityFabAssetImportReceipt left, UnityFabAssetImportReceipt right)
    {
        if (left.Id != right.Id ||
            !string.Equals(left.SourceAssetsDirectory, right.SourceAssetsDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.SourceDisplayName, right.SourceDisplayName, StringComparison.Ordinal) ||
            !string.Equals(left.TargetProjectRoot, right.TargetProjectRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.TargetImportDirectory, right.TargetImportDirectory, StringComparison.OrdinalIgnoreCase) ||
            left.ImportedBytes != right.ImportedBytes || left.ReplacedExistingImport != right.ReplacedExistingImport ||
            !string.Equals(left.BackupDirectory, right.BackupDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.BackupTreeSha256, right.BackupTreeSha256, StringComparison.OrdinalIgnoreCase) ||
            left.TargetMetaInitiallyExisted != right.TargetMetaInitiallyExisted ||
            left.ImportedAt != right.ImportedAt || left.RolledBackAt != right.RolledBackAt ||
            left.Files.Count != right.Files.Count)
        {
            return false;
        }
        for (var index = 0; index < left.Files.Count; index++)
        {
            if (left.Files[index] != right.Files[index]) return false;
        }
        return true;
    }

    private static void ValidateImportedTree(UnityFabAssetImportReceipt receipt, CancellationToken cancellationToken)
    {
        var target = NormalizeDirectory(receipt.TargetImportDirectory, true);
        EnsureUnderRoot(ValidateUnityProject(receipt.TargetProjectRoot), target);
        EnsureNoReparsePoint(receipt.TargetProjectRoot, target);
        var expected = receipt.Files.ToDictionary(file => NormalizeRelative(file.RelativePath), StringComparer.OrdinalIgnoreCase);
        var generatedMetas = BuildGeneratedMetaPaths(receipt.Files);
        foreach (var filePath in EnumerateRegularFiles(target, "ValidateUnityImport"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeRelative(Path.GetRelativePath(target, filePath));
            if (expected.Remove(relative, out var file))
            {
                if (new FileInfo(filePath).Length != file.Length || !string.Equals(ComputeSha256(filePath, cancellationToken), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw Failure("Un asset importé a été modifié; annulation refusée.", "ValidateUnityImport", filePath);
                }
            }
            else if (!generatedMetas.Contains(relative))
            {
                throw Failure("Le dossier importé contient un fichier ajouté; annulation refusée.", "ValidateUnityImport", filePath);
            }
        }
        if (expected.Count > 0)
        {
            throw Failure("Un asset importé manque; annulation refusée.", "ValidateUnityImport", target);
        }
    }

    private static HashSet<string> BuildGeneratedMetaPaths(IEnumerable<UnityFabAssetFile> files)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var relative = NormalizeRelative(file.RelativePath);
            if (!relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) result.Add(relative + ".meta");
            var directory = Path.GetDirectoryName(relative);
            while (!string.IsNullOrWhiteSpace(directory))
            {
                result.Add(NormalizeRelative(directory) + ".meta");
                directory = Path.GetDirectoryName(directory);
            }
        }
        return result;
    }

    private static IEnumerable<string> EnumerateRegularFiles(string root, string operation)
    {
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(root));
        while (stack.Count > 0)
        {
            var directory = stack.Pop();
            EnsureNotReparsePoint(directory, operation);
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                EnsureNotReparsePoint(entry, operation);
                if (entry is DirectoryInfo child) stack.Push(child);
                else yield return entry.FullName;
            }
        }
    }

    private static async Task<UnityFabAssetImportReceipt> SaveReceiptAsync(UnityFabAssetImportReceipt receipt, CancellationToken cancellationToken)
    {
        var receiptRoot = CombineContained(GetWorkspaceRoot(receipt.TargetProjectRoot), "receipts");
        Directory.CreateDirectory(receiptRoot);
        EnsureNoReparsePoint(GetWorkspaceRoot(receipt.TargetProjectRoot), receiptRoot);
        var path = CombineContained(receiptRoot, receipt.Id.ToString("N") + ".json");
        var temporary = path + ".tmp";
        var json = JsonSerializer.Serialize(receipt, JsonOptions);
        if (json.Length > MaximumReceiptBytes)
        {
            throw Failure("Le reçu d’import Unity dépasse la taille autorisée.", "SaveUnityReceipt", path);
        }
        await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
        return receipt with { ReceiptPath = path };
    }

    private static string ResolveSourceAssets(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw Failure("Sélectionnez un dossier de pack Unity extrait.", "PrepareUnityImport");
        }
        var root = NormalizeDirectory(sourcePath, true);
        var assets = Path.Combine(root, "Assets");
        if (Directory.Exists(assets)) root = NormalizeDirectory(assets, true);
        EnsureNoReparsePoint(root, root);
        return root;
    }

    private static string ValidateUnityProject(string targetProjectRoot)
    {
        var root = NormalizeDirectory(targetProjectRoot, true);
        var marker = Path.Combine(root, "ProjectSettings", "ProjectVersion.txt");
        var assets = Path.Combine(root, "Assets");
        if (!File.Exists(marker) || !Directory.Exists(assets))
        {
            throw Failure("La cible n’est pas un projet Unity valide.", "ValidateUnityProject", root);
        }
        EnsureRegularFile(marker, "ValidateUnityProject");
        EnsureNoReparsePoint(root, assets);
        return root;
    }

    private static void ValidateSourceFile(string path, UnityFabAssetFile expected, CancellationToken cancellationToken)
    {
        EnsureRegularFile(path, "ExecuteUnityImport");
        var info = new FileInfo(path);
        if (info.Length != expected.Length || info.LastWriteTimeUtc != expected.LastWriteTimeUtc.UtcDateTime || !string.Equals(ComputeSha256(path, cancellationToken), expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Un asset source a changé depuis l’analyse.", "ExecuteUnityImport", path);
        }
    }

    private static async Task CopyAndVerifyAsync(string source, string destination, string expectedSha, CancellationToken cancellationToken)
    {
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await input.CopyToAsync(output, 1024 * 128, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        if (!string.Equals(ComputeSha256(destination, cancellationToken), expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("La vérification du staging a échoué.", "StageUnityImport", destination);
        }
    }

    private static string ComputeSha256(string path, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(1024 * 128);
        try
        {
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                hash.AppendData(buffer, 0, read);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static string ComputeTreeFingerprint(string root, CancellationToken cancellationToken)
    {
        var normalizedRoot = NormalizeDirectory(root, true);
        var entries = new List<string>();
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(normalizedRoot));
        var count = 0;
        long bytes = 0;
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop();
            EnsureNotReparsePoint(directory, "FingerprintUnityTree");
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNotReparsePoint(entry, "FingerprintUnityTree");
                if (++count > MaximumFileCount)
                {
                    throw Failure($"La destination dépasse la limite de {MaximumFileCount:N0} entrées.", "FingerprintUnityTree", normalizedRoot);
                }
                var relative = NormalizeRelative(Path.GetRelativePath(normalizedRoot, entry.FullName));
                if (entry is DirectoryInfo child)
                {
                    entries.Add($"D|{relative}");
                    stack.Push(child);
                    continue;
                }
                var file = (FileInfo)entry;
                bytes = checked(bytes + file.Length);
                if (bytes > MaximumTotalBytes)
                {
                    throw Failure("La destination dépasse la limite de 20 Gio.", "FingerprintUnityTree", normalizedRoot);
                }
                entries.Add($"F|{relative}|{file.Length}|{ComputeSha256(file.FullName, cancellationToken)}");
            }
        }

        using var aggregate = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in entries.Order(StringComparer.OrdinalIgnoreCase))
        {
            aggregate.AppendData(Encoding.UTF8.GetBytes(entry));
            aggregate.AppendData([0]);
        }
        return Convert.ToHexString(aggregate.GetHashAndReset());
    }

    private static void EnsureSameInventory(IReadOnlyList<UnityFabAssetFile> expected, IReadOnlyList<UnityFabAssetFile> current)
    {
        if (expected.Count != current.Count)
        {
            throw Failure("Le pack source a changé depuis l’analyse.", "ExecuteUnityImport");
        }
        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(expected[index].RelativePath, current[index].RelativePath, StringComparison.OrdinalIgnoreCase) ||
                expected[index].Length != current[index].Length ||
                expected[index].LastWriteTimeUtc != current[index].LastWriteTimeUtc ||
                !string.Equals(expected[index].Sha256, current[index].Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("Le pack source a changé depuis l’analyse.", "ExecuteUnityImport", current[index].RelativePath);
            }
        }
    }

    private static void ValidateGeneratedMeta(UnityFabAssetImportReceipt receipt, string? expectedSha, CancellationToken cancellationToken)
    {
        if (receipt.TargetMetaInitiallyExisted) return;
        var path = receipt.TargetImportDirectory + ".meta";
        if (expectedSha is null)
        {
            if (File.Exists(path)) throw Failure("Le .meta de destination a changé depuis l’analyse.", "RollbackUnityImport", path);
            return;
        }
        EnsureRegularFile(path, "RollbackUnityImport");
        if (!string.Equals(ComputeSha256(path, cancellationToken), expectedSha, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Le .meta de destination a changé depuis l’analyse.", "RollbackUnityImport", path);
        }
    }

    private static string GetWorkspaceRoot(string projectRoot) =>
        CombineContained(projectRoot, Path.Combine(".cyenginehub", "FabImports", "Unity"));

    private static void EnsureFresh(DateTimeOffset expiresAt, string message)
    {
        if (DateTimeOffset.UtcNow > expiresAt) throw Failure(message, "ValidatePlan");
    }

    private static string NormalizeDirectory(string path, bool requireExisting)
    {
        if (string.IsNullOrWhiteSpace(path)) throw Failure("Un chemin requis est vide.", "ValidatePath", path);
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (requireExisting && !Directory.Exists(full)) throw Failure("Le dossier n’existe pas.", "ValidatePath", full);
        return full;
    }

    private static string CombineContained(string root, string relative)
    {
        var normalizedRoot = NormalizeDirectory(root, false);
        var combined = Path.GetFullPath(Path.Combine(normalizedRoot, relative));
        EnsureUnderRoot(normalizedRoot, combined);
        return combined;
    }

    private static void EnsureUnderRoot(string root, string candidate)
    {
        var normalizedRoot = NormalizeDirectory(root, false);
        var normalizedCandidate = Path.GetFullPath(candidate);
        var prefix = normalizedRoot + Path.DirectorySeparatorChar;
        if (!string.Equals(normalizedCandidate, normalizedRoot, StringComparison.OrdinalIgnoreCase) && !normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Un chemin sort de la racine autorisée.", "ValidatePath", candidate);
        }
    }

    private static void EnsureSeparated(string source, string target)
    {
        var sourceRoot = NormalizeDirectory(source, false);
        var targetRoot = NormalizeDirectory(target, false);
        if (IsUnderOrEqual(sourceRoot, targetRoot) || IsUnderOrEqual(targetRoot, sourceRoot))
        {
            throw Failure("La source et le projet cible doivent être séparés.", "ValidatePath", source);
        }
    }

    private static bool IsUnderOrEqual(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void EnsureNoReparsePoint(string root, string candidate)
    {
        var normalizedRoot = NormalizeDirectory(root, false);
        var normalizedCandidate = Path.GetFullPath(candidate);
        EnsureUnderRoot(normalizedRoot, normalizedCandidate);
        var current = normalizedRoot;
        if (Directory.Exists(current)) EnsureNotReparsePoint(new DirectoryInfo(current), "ValidatePath");
        var relative = Path.GetRelativePath(normalizedRoot, normalizedCandidate);
        foreach (var segment in relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current)) EnsureNotReparsePoint(new DirectoryInfo(current), "ValidatePath");
            else if (File.Exists(current)) EnsureNotReparsePoint(new FileInfo(current), "ValidatePath");
        }
    }

    private static void EnsureRegularFile(string path, string operation)
    {
        if (!File.Exists(path)) throw Failure("Un fichier requis est introuvable.", operation, path);
        EnsureNotReparsePoint(new FileInfo(path), operation);
    }

    private static void EnsureNotReparsePoint(FileSystemInfo info, string operation)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw Failure("Les liens symboliques et jonctions ne sont pas autorisés.", operation, info.FullName);
        }
    }

    private static string SanitizeDirectoryName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(sanitized)) sanitized = "FabAssetPack";
        return sanitized.Length > 64 ? sanitized[..64].TrimEnd() : sanitized;
    }

    private static string NormalizeRelative(string value) => value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }

    private static FabContentOperationException Failure(string message, string operation, string? path = null) => new(message, operation, path);
}
