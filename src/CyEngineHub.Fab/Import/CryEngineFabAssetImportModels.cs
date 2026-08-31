using System.Text.Json.Serialization;

namespace CyEngineHub.Fab.Import;

public sealed record CryEngineFabAssetFile(string RelativePath, long Length, DateTimeOffset LastWriteTimeUtc, string Sha256);

public sealed record CryEngineFabAssetImportPlan(Guid Id, string SourceContentDirectory, string SourceDisplayName, string TargetProjectRoot, string TargetImportDirectory, IReadOnlyList<CryEngineFabAssetFile> Files, int SkippedFileCount, long TotalBytes, bool ReplacesExistingImport, string? ExistingTargetTreeSha256, string BackupDirectory, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record CryEngineFabAssetImportReceipt(Guid Id, string SourceContentDirectory, string SourceDisplayName, string TargetProjectRoot, string TargetImportDirectory, IReadOnlyList<CryEngineFabAssetFile> Files, long ImportedBytes, bool ReplacedExistingImport, string? BackupDirectory, string? BackupTreeSha256, DateTimeOffset ImportedAt, DateTimeOffset? RolledBackAt)
{
    [JsonIgnore]
    public string ReceiptPath { get; init; } = string.Empty;
    public bool IsApplied => RolledBackAt is null;
}

public sealed record CryEngineFabAssetImportResult(int ImportedFileCount, long ImportedBytes, CryEngineFabAssetImportReceipt Receipt);

public sealed record CryEngineFabAssetRollbackPlan(Guid Id, CryEngineFabAssetImportReceipt Receipt, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record CryEngineFabAssetRollbackResult(bool RestoredPreviousImport, CryEngineFabAssetImportReceipt Receipt);

public sealed record CryEngineFabAssetImportProgress(int Completed, int Total, string Message);

