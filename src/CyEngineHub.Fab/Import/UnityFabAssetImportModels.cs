using System.Text.Json.Serialization;

namespace CyEngineHub.Fab.Import;

public sealed record UnityFabAssetFile(string RelativePath, long Length, DateTimeOffset LastWriteTimeUtc, string Sha256);

public sealed record UnityFabAssetImportPlan(Guid Id, string SourceAssetsDirectory, string SourceDisplayName, string TargetProjectRoot, string TargetImportDirectory, IReadOnlyList<UnityFabAssetFile> Files, int SkippedFileCount, long TotalBytes, bool ReplacesExistingImport, string? ExistingTargetTreeSha256, string BackupDirectory, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record UnityFabAssetImportReceipt(Guid Id, string SourceAssetsDirectory, string SourceDisplayName, string TargetProjectRoot, string TargetImportDirectory, IReadOnlyList<UnityFabAssetFile> Files, long ImportedBytes, bool ReplacedExistingImport, string? BackupDirectory, string? BackupTreeSha256, bool TargetMetaInitiallyExisted, DateTimeOffset ImportedAt, DateTimeOffset? RolledBackAt)
{
    [JsonIgnore]
    public string ReceiptPath { get; init; } = string.Empty;
    public bool IsApplied => RolledBackAt is null;
}

public sealed record UnityFabAssetImportResult(int ImportedFileCount, long ImportedBytes, UnityFabAssetImportReceipt Receipt);

public sealed record UnityFabAssetRollbackPlan(Guid Id, UnityFabAssetImportReceipt Receipt, string? GeneratedTargetMetaSha256, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record UnityFabAssetRollbackResult(bool RestoredPreviousImport, UnityFabAssetImportReceipt Receipt);

public sealed record UnityFabAssetImportProgress(int Completed, int Total, string Message);
