using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyEngineHub.Fab.Import;

public interface ICryEngineFabAssetImportService
{
    Task<CryEngineFabAssetImportPlan> PrepareAsync(string sourcePath, string targetProjectRoot, CancellationToken cancellationToken = default);
    Task<CryEngineFabAssetImportResult> ExecuteAsync(CryEngineFabAssetImportPlan plan, IProgress<CryEngineFabAssetImportProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CryEngineFabAssetImportReceipt>> LoadHistoryAsync(IEnumerable<string> targetProjectRoots, CancellationToken cancellationToken = default);
    Task<CryEngineFabAssetRollbackPlan> PrepareRollbackAsync(CryEngineFabAssetImportReceipt receipt, CancellationToken cancellationToken = default);
    Task<CryEngineFabAssetRollbackResult> ExecuteRollbackAsync(CryEngineFabAssetRollbackPlan plan, IProgress<CryEngineFabAssetImportProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class CryEngineFabAssetImportService : ICryEngineFabAssetImportService
{
    private const int MaximumFileCount = 50_000;
    private const long MaximumTotalBytes = 20L * 1024 * 1024 * 1024;
    private const long MaximumReceiptBytes = 8L * 1024 * 1024;
    private const long MaximumProjectDescriptorBytes = 1024 * 1024;
    private static readonly TimeSpan PlanLifetime = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly HashSet<string> ExcludedSourceDirectories = new(
        [".git", ".cyenginehub", "Solutions", "USER"],
        StringComparer.OrdinalIgnoreCase);
    public Task<CryEngineFabAssetImportPlan> PrepareAsync(string sourcePath, string targetProjectRoot, CancellationToken cancellationToken = default) =>
        Task.Run(() => Prepare(sourcePath, targetProjectRoot, cancellationToken), cancellationToken);

    public async Task<CryEngineFabAssetImportResult> ExecuteAsync(CryEngineFabAssetImportPlan plan, IProgress<CryEngineFabAssetImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureFresh(plan.ExpiresAt, "Le plan d’import CryEngine/Fab a expiré; relancez l’analyse.");
        var projectRoot = ValidateCryEngineProject(plan.TargetProjectRoot);
        var assetsRoot = ResolveTargetAssetsDirectory(projectRoot);
        var sourceRoot = NormalizeDirectory(plan.SourceContentDirectory, true);
        var targetRoot = NormalizeDirectory(plan.TargetImportDirectory, false);
        EnsureUnderRoot(assetsRoot, targetRoot);
        EnsureSeparated(sourceRoot, projectRoot);
        EnsureNoReparsePoint(projectRoot, targetRoot);

        var targetExistsAtExecution = Directory.Exists(targetRoot);
        if (targetExistsAtExecution != plan.ReplacesExistingImport)
        {
            throw Failure("La destination a changé depuis l’analyse.", "ExecuteCryEngineImport", targetRoot);
        }
        var targetTreeSha = targetExistsAtExecution
            ? ComputeTreeFingerprint(targetRoot, cancellationToken)
            : null;
        if (!string.Equals(targetTreeSha, plan.ExistingTargetTreeSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("La destination CryEngine a changé depuis l’analyse.", "ExecuteCryEngineImport", targetRoot);
        }

        var current = Inventory(sourceRoot, cancellationToken);
        EnsureSameInventory(plan.Files, current.Files);
        if (current.SkippedFileCount != plan.SkippedFileCount)
        {
            throw Failure("Le pack source a changé depuis l’analyse.", "ExecuteCryEngineImport", sourceRoot);
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
            throw Failure("Le staging ou backup existe déjà.", "ExecuteCryEngineImport", workspace);
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
                progress?.Report(new CryEngineFabAssetImportProgress(++completed, plan.Files.Count, $"Staging CryEngine {completed:N0}/{plan.Files.Count:N0}"));
            }

            var targetExists = Directory.Exists(targetRoot);
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
            var receipt = new CryEngineFabAssetImportReceipt(plan.Id, sourceRoot, plan.SourceDisplayName, projectRoot, targetRoot, plan.Files, plan.TotalBytes, targetExists, targetExists ? backup : null, targetTreeSha, DateTimeOffset.UtcNow, null);
            receipt = await SaveReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
            return new CryEngineFabAssetImportResult(plan.Files.Count, plan.TotalBytes, receipt);
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

    public Task<IReadOnlyList<CryEngineFabAssetImportReceipt>> LoadHistoryAsync(IEnumerable<string> targetProjectRoots, CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<CryEngineFabAssetImportReceipt>>(() => LoadHistory(targetProjectRoots, cancellationToken), cancellationToken);

    public Task<CryEngineFabAssetRollbackPlan> PrepareRollbackAsync(CryEngineFabAssetImportReceipt receipt, CancellationToken cancellationToken = default) =>
        Task.Run(() => PrepareRollback(receipt, cancellationToken), cancellationToken);

    public async Task<CryEngineFabAssetRollbackResult> ExecuteRollbackAsync(CryEngineFabAssetRollbackPlan plan, IProgress<CryEngineFabAssetImportProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        EnsureFresh(plan.ExpiresAt, "Le plan d’annulation CryEngine/Fab a expiré; relancez l’analyse.");
        var receipt = plan.Receipt;
        ValidateReceiptTrust(receipt);
        var projectRoot = ValidateCryEngineProject(receipt.TargetProjectRoot);
        ValidateImportedTree(receipt, cancellationToken);
        var rollbackStaging = CombineContained(GetWorkspaceRoot(projectRoot), Path.Combine("rollback", plan.Id.ToString("N")));
        EnsureNoReparsePoint(projectRoot, receipt.TargetImportDirectory);
        if (Directory.Exists(rollbackStaging))
        {
            throw Failure("Le staging d’annulation existe déjà.", "RollbackCryEngineImport", rollbackStaging);
        }

        var importedMoved = false;
        var backupRestored = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(rollbackStaging)!);
            Directory.Move(receipt.TargetImportDirectory, rollbackStaging);
            importedMoved = true;
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
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The target and receipt already represent a completed rollback;
                // stale staging is safer than undoing that committed state.
            }
            progress?.Report(new CryEngineFabAssetImportProgress(1, 1, "Import CryEngine annulé"));
            return new CryEngineFabAssetRollbackResult(backupRestored, updated);
        }
        catch
        {
            try
            {
                if (backupRestored && Directory.Exists(receipt.TargetImportDirectory)) Directory.Move(receipt.TargetImportDirectory, receipt.BackupDirectory!);
                if (importedMoved && Directory.Exists(rollbackStaging) && !Directory.Exists(receipt.TargetImportDirectory)) Directory.Move(rollbackStaging, receipt.TargetImportDirectory);
            }
            catch { }
            throw;
        }
    }

    private static CryEngineFabAssetImportPlan Prepare(string sourcePath, string targetProjectRoot, CancellationToken cancellationToken)
    {
        var projectRoot = ValidateCryEngineProject(targetProjectRoot);
        var assetsRoot = ResolveTargetAssetsDirectory(projectRoot);
        var sourceRoot = ResolveSourceContent(sourcePath);
        EnsureSeparated(sourceRoot, projectRoot);
        var inventory = Inventory(sourceRoot, cancellationToken);
        if (inventory.Files.Count == 0)
        {
            throw Failure("Le pack ne contient aucun asset CryEngine pris en charge.", "PrepareCryEngineImport", sourceRoot);
        }

        var displayName = Path.GetFileName(sourceRoot.TrimEnd(Path.DirectorySeparatorChar));
        var target = CombineContained(assetsRoot, Path.Combine("CyEngineHubImports", SanitizeDirectoryName(displayName)));
        EnsureNoReparsePoint(projectRoot, target);
        var targetExists = Directory.Exists(target);

        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        var backup = CombineContained(GetWorkspaceRoot(projectRoot), Path.Combine("backups", id.ToString("N")));
        var existingTreeSha = targetExists ? ComputeTreeFingerprint(target, cancellationToken) : null;
        return new CryEngineFabAssetImportPlan(id, sourceRoot, displayName, projectRoot, target, inventory.Files, inventory.SkippedFileCount, inventory.TotalBytes, targetExists, existingTreeSha, backup, created, created.Add(PlanLifetime));
    }

    private static CryEngineFabAssetRollbackPlan PrepareRollback(CryEngineFabAssetImportReceipt receipt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ValidateReceiptTrust(receipt);
        if (!receipt.IsApplied)
        {
            throw Failure("Cet import CryEngine a déjà été annulé.", "PrepareCryEngineRollback", receipt.ReceiptPath);
        }

        ValidateCryEngineProject(receipt.TargetProjectRoot);
        ValidateImportedTree(receipt, cancellationToken);
        if (receipt.ReplacedExistingImport)
        {
            if (string.IsNullOrWhiteSpace(receipt.BackupDirectory) || !Directory.Exists(receipt.BackupDirectory))
            {
                throw Failure("Le backup de l’import CryEngine est introuvable.", "PrepareCryEngineRollback", receipt.BackupDirectory);
            }
            EnsureNoReparsePoint(GetWorkspaceRoot(receipt.TargetProjectRoot), receipt.BackupDirectory);
            var backupTreeSha = ComputeTreeFingerprint(receipt.BackupDirectory, cancellationToken);
            if (!string.Equals(backupTreeSha, receipt.BackupTreeSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("Le backup de l’import CryEngine a été modifié.", "PrepareCryEngineRollback", receipt.BackupDirectory);
            }
        }

        var id = Guid.NewGuid();
        var created = DateTimeOffset.UtcNow;
        return new CryEngineFabAssetRollbackPlan(id, receipt, created, created.Add(PlanLifetime));
    }

    private static (IReadOnlyList<CryEngineFabAssetFile> Files, int SkippedFileCount, long TotalBytes) Inventory(string sourceRoot, CancellationToken cancellationToken)
    {
        var files = new List<CryEngineFabAssetFile>();
        var skipped = 0;
        long bytes = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(sourceRoot));
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = stack.Pop();
            EnsureNotReparsePoint(directory, "InventoryCryEnginePack");
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNotReparsePoint(entry, "InventoryCryEnginePack");
                if (entry is DirectoryInfo child)
                {
                    if (!ExcludedSourceDirectories.Contains(child.Name))
                    {
                        stack.Push(child);
                    }
                    continue;
                }

                var file = (FileInfo)entry;
                if (CryEngineFabAssetPolicy.IsBlocked(file.Extension))
                {
                    throw Failure($"Le pack contient du code ou un binaire bloqué ({file.Extension}).", "InventoryCryEnginePack", file.FullName);
                }
                if (!CryEngineFabAssetPolicy.IsSupported(file.Extension))
                {
                    skipped++;
                    continue;
                }
                if (files.Count >= MaximumFileCount)
                {
                    throw Failure($"Le pack dépasse la limite de {MaximumFileCount:N0} fichiers.", "InventoryCryEnginePack", sourceRoot);
                }

                bytes = checked(bytes + file.Length);
                if (bytes > MaximumTotalBytes)
                {
                    throw Failure("Le pack dépasse la limite de 20 Gio.", "InventoryCryEnginePack", sourceRoot);
                }
                files.Add(new CryEngineFabAssetFile(Path.GetRelativePath(sourceRoot, file.FullName), file.Length, file.LastWriteTimeUtc, ComputeSha256(file.FullName, cancellationToken)));
            }
        }

        return (files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray(), skipped, bytes);
    }

    private static CryEngineFabAssetImportReceipt[] LoadHistory(IEnumerable<string> targetProjectRoots, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetProjectRoots);
        var receipts = new List<CryEngineFabAssetImportReceipt>();
        foreach (var candidate in targetProjectRoots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string root;
            try { root = ValidateCryEngineProject(candidate); }
            catch (FabContentOperationException) { continue; }

            var receiptRoot = Path.Combine(GetWorkspaceRoot(root), "receipts");
            if (!Directory.Exists(receiptRoot)) continue;
            EnsureNoReparsePoint(GetWorkspaceRoot(root), receiptRoot);
            foreach (var path in Directory.EnumerateFiles(receiptRoot, "*.json", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    EnsureRegularFile(path, "LoadCryEngineImportHistory");
                    if (new FileInfo(path).Length > MaximumReceiptBytes) continue;
                    var receipt = JsonSerializer.Deserialize<CryEngineFabAssetImportReceipt>(File.ReadAllText(path), JsonOptions);
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

    private static void ValidateReceiptTrust(CryEngineFabAssetImportReceipt receipt)
    {
        var projectRoot = ValidateCryEngineProject(receipt.TargetProjectRoot);
        var workspace = GetWorkspaceRoot(projectRoot);
        var expectedReceiptPath = CombineContained(workspace, Path.Combine("receipts", receipt.Id.ToString("N") + ".json"));
        if (string.IsNullOrWhiteSpace(receipt.ReceiptPath) ||
            !string.Equals(Path.GetFullPath(receipt.ReceiptPath), expectedReceiptPath, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Le reçu CryEngine ne provient pas de l’historique sécurisé du projet.", "ValidateCryEngineReceipt", receipt.ReceiptPath);
        }
        EnsureRegularFile(expectedReceiptPath, "ValidateCryEngineReceipt");
        if (new FileInfo(expectedReceiptPath).Length > MaximumReceiptBytes)
        {
            throw Failure("Le reçu CryEngine dépasse la taille autorisée.", "ValidateCryEngineReceipt", expectedReceiptPath);
        }

        CryEngineFabAssetImportReceipt? stored;
        try { stored = JsonSerializer.Deserialize<CryEngineFabAssetImportReceipt>(File.ReadAllText(expectedReceiptPath), JsonOptions); }
        catch (JsonException exception)
        {
            throw new FabContentOperationException("Le reçu CryEngine est illisible.", "ValidateCryEngineReceipt", expectedReceiptPath, exception);
        }
        if (stored is null || !ReceiptsMatch(receipt, stored))
        {
            throw Failure("Le reçu CryEngine ne correspond plus au reçu enregistré.", "ValidateCryEngineReceipt", expectedReceiptPath);
        }

        var importsRoot = CombineContained(ResolveTargetAssetsDirectory(projectRoot), "CyEngineHubImports");
        var target = NormalizeDirectory(receipt.TargetImportDirectory, true);
        EnsureUnderRoot(importsRoot, target);
        if (string.Equals(importsRoot, target, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Le reçu cible la racine partagée des imports CryEngine.", "ValidateCryEngineReceipt", target);
        }

        if (receipt.ReplacedExistingImport)
        {
            var expectedBackup = CombineContained(workspace, Path.Combine("backups", receipt.Id.ToString("N")));
            if (!string.Equals(receipt.BackupDirectory, expectedBackup, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("Le chemin de backup du reçu CryEngine est invalide.", "ValidateCryEngineReceipt", receipt.BackupDirectory);
            }
        }
        else if (receipt.BackupDirectory is not null)
        {
            throw Failure("Un nouvel import CryEngine ne doit pas déclarer de backup.", "ValidateCryEngineReceipt", receipt.BackupDirectory);
        }
    }

    private static bool ReceiptsMatch(CryEngineFabAssetImportReceipt left, CryEngineFabAssetImportReceipt right)
    {
        if (left.Id != right.Id ||
            !string.Equals(left.SourceContentDirectory, right.SourceContentDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.SourceDisplayName, right.SourceDisplayName, StringComparison.Ordinal) ||
            !string.Equals(left.TargetProjectRoot, right.TargetProjectRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.TargetImportDirectory, right.TargetImportDirectory, StringComparison.OrdinalIgnoreCase) ||
            left.ImportedBytes != right.ImportedBytes || left.ReplacedExistingImport != right.ReplacedExistingImport ||
            !string.Equals(left.BackupDirectory, right.BackupDirectory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(left.BackupTreeSha256, right.BackupTreeSha256, StringComparison.OrdinalIgnoreCase) ||
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

    private static void ValidateImportedTree(CryEngineFabAssetImportReceipt receipt, CancellationToken cancellationToken)
    {
        var target = NormalizeDirectory(receipt.TargetImportDirectory, true);
        var projectRoot = ValidateCryEngineProject(receipt.TargetProjectRoot);
        EnsureUnderRoot(ResolveTargetAssetsDirectory(projectRoot), target);
        EnsureNoReparsePoint(receipt.TargetProjectRoot, target);
        var expected = receipt.Files.ToDictionary(file => NormalizeRelative(file.RelativePath), StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in EnumerateRegularFiles(target, "ValidateCryEngineImport"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeRelative(Path.GetRelativePath(target, filePath));
            if (expected.Remove(relative, out var file))
            {
                if (new FileInfo(filePath).Length != file.Length || !string.Equals(ComputeSha256(filePath, cancellationToken), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw Failure("Un asset importé a été modifié; annulation refusée.", "ValidateCryEngineImport", filePath);
                }
            }
            else
            {
                throw Failure("Le dossier importé contient un fichier ajouté; annulation refusée.", "ValidateCryEngineImport", filePath);
            }
        }
        if (expected.Count > 0)
        {
            throw Failure("Un asset importé manque; annulation refusée.", "ValidateCryEngineImport", target);
        }
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

    private static async Task<CryEngineFabAssetImportReceipt> SaveReceiptAsync(CryEngineFabAssetImportReceipt receipt, CancellationToken cancellationToken)
    {
        var receiptRoot = CombineContained(GetWorkspaceRoot(receipt.TargetProjectRoot), "receipts");
        Directory.CreateDirectory(receiptRoot);
        EnsureNoReparsePoint(GetWorkspaceRoot(receipt.TargetProjectRoot), receiptRoot);
        var path = CombineContained(receiptRoot, receipt.Id.ToString("N") + ".json");
        var temporary = path + ".tmp";
        var json = JsonSerializer.Serialize(receipt, JsonOptions);
        if (json.Length > MaximumReceiptBytes)
        {
            throw Failure("Le reçu d’import CryEngine dépasse la taille autorisée.", "SaveCryEngineReceipt", path);
        }
        await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
        return receipt with { ReceiptPath = path };
    }

    private static string ResolveSourceContent(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw Failure("Sélectionnez un dossier de pack CryEngine extrait.", "PrepareCryEngineImport");
        }
        var full = Path.GetFullPath(sourcePath);
        if (File.Exists(full) && string.Equals(Path.GetExtension(full), ".cryproject", StringComparison.OrdinalIgnoreCase))
        {
            full = Path.GetDirectoryName(full)!;
        }
        var root = NormalizeDirectory(full, true);
        EnsureNoReparsePoint(root, root);
        return root;
    }

    private static string ValidateCryEngineProject(string targetProjectRoot)
    {
        var root = NormalizeDirectory(targetProjectRoot, true);
        var marker = ResolveCryProjectMarker(root);
        EnsureRegularFile(marker, "ValidateCryEngineProject");
        _ = ResolveTargetAssetsDirectory(root);
        return root;
    }

    private static string ResolveCryProjectMarker(string projectRoot)
    {
        try
        {
            var markers = Directory.EnumerateFiles(
                    projectRoot,
                    "*.cryproject",
                    SearchOption.TopDirectoryOnly)
                .Take(2)
                .ToArray();
            if (markers.Length != 1)
            {
                throw Failure(
                    markers.Length == 0
                        ? "La cible n’est pas un projet CRYENGINE valide."
                        : "La cible contient plusieurs descripteurs .cryproject ambigus.",
                    "ValidateCryEngineProject",
                    projectRoot);
            }

            return Path.GetFullPath(markers[0]);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            throw new FabContentOperationException(
                "Le descripteur du projet CRYENGINE est inaccessible.",
                "ValidateCryEngineProject",
                projectRoot,
                exception);
        }
    }

    private static string ResolveTargetAssetsDirectory(string projectRoot)
    {
        var marker = ResolveCryProjectMarker(projectRoot);
        try
        {
            var info = new FileInfo(marker);
            if (info.Length > MaximumProjectDescriptorBytes)
            {
                throw Failure(
                    "Le descripteur CRYENGINE dépasse la taille autorisée.",
                    "ValidateCryEngineProject",
                    marker);
            }

            using var document = JsonDocument.Parse(
                File.ReadAllBytes(marker),
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip,
                    MaxDepth = 64,
                });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(root, "content", out var content) ||
                content.ValueKind != JsonValueKind.Object ||
                !TryGetProperty(content, "assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array)
            {
                throw Failure(
                    "Le projet CRYENGINE ne déclare aucun dossier d’assets.",
                    "ValidateCryEngineProject",
                    marker);
            }

            var assetRelativePath = assets.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()?.Trim())
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
            if (string.IsNullOrWhiteSpace(assetRelativePath) || Path.IsPathRooted(assetRelativePath))
            {
                throw Failure(
                    "Le dossier d’assets CRYENGINE déclaré est invalide.",
                    "ValidateCryEngineProject",
                    marker);
            }

            var assetsRoot = CombineContained(projectRoot, assetRelativePath);
            if (!Directory.Exists(assetsRoot))
            {
                throw Failure(
                    "Le dossier d’assets CRYENGINE déclaré est introuvable.",
                    "ValidateCryEngineProject",
                    assetsRoot);
            }

            EnsureNoReparsePoint(projectRoot, assetsRoot);
            return assetsRoot;
        }
        catch (JsonException exception)
        {
            throw new FabContentOperationException(
                "Le descripteur du projet CRYENGINE est invalide.",
                "ValidateCryEngineProject",
                marker,
                exception);
        }
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static void ValidateSourceFile(string path, CryEngineFabAssetFile expected, CancellationToken cancellationToken)
    {
        EnsureRegularFile(path, "ExecuteCryEngineImport");
        var info = new FileInfo(path);
        if (info.Length != expected.Length || info.LastWriteTimeUtc != expected.LastWriteTimeUtc.UtcDateTime || !string.Equals(ComputeSha256(path, cancellationToken), expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw Failure("Un asset source a changé depuis l’analyse.", "ExecuteCryEngineImport", path);
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
            throw Failure("La vérification du staging a échoué.", "StageCryEngineImport", destination);
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
            EnsureNotReparsePoint(directory, "FingerprintCryEngineTree");
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureNotReparsePoint(entry, "FingerprintCryEngineTree");
                if (++count > MaximumFileCount)
                {
                    throw Failure($"La destination dépasse la limite de {MaximumFileCount:N0} entrées.", "FingerprintCryEngineTree", normalizedRoot);
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
                    throw Failure("La destination dépasse la limite de 20 Gio.", "FingerprintCryEngineTree", normalizedRoot);
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

    private static void EnsureSameInventory(IReadOnlyList<CryEngineFabAssetFile> expected, IReadOnlyList<CryEngineFabAssetFile> current)
    {
        if (expected.Count != current.Count)
        {
            throw Failure("Le pack source a changé depuis l’analyse.", "ExecuteCryEngineImport");
        }
        for (var index = 0; index < expected.Count; index++)
        {
            if (!string.Equals(expected[index].RelativePath, current[index].RelativePath, StringComparison.OrdinalIgnoreCase) ||
                expected[index].Length != current[index].Length ||
                expected[index].LastWriteTimeUtc != current[index].LastWriteTimeUtc ||
                !string.Equals(expected[index].Sha256, current[index].Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw Failure("Le pack source a changé depuis l’analyse.", "ExecuteCryEngineImport", current[index].RelativePath);
            }
        }
    }

    private static string GetWorkspaceRoot(string projectRoot) =>
        CombineContained(projectRoot, Path.Combine(".cyenginehub", "FabImports", "CryEngine"));

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

