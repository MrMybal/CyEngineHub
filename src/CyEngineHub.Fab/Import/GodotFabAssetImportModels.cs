using System.Text.Json.Serialization;

namespace CyEngineHub.Fab.Import;

public sealed record GodotFabAssetFile(string RelativePath, long Length, DateTimeOffset LastWriteTimeUtc, string Sha256);

public sealed record GodotFabAssetImportPlan(Guid Id, string SourceContentDirectory, string SourceDisplayName, string TargetProjectRoot, string TargetImportDirectory, IReadOnlyList<GodotFabAssetFile> Files, int SkippedFileCount, long TotalBytes, bool ReplacesExistingImport, string? ExistingTargetTreeSha256, string BackupDirectory, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record GodotFabAssetImportReceipt(Guid Id, string SourceContentDirectory, string SourceDisplayName, string TargetProjectRoot, string TargetImportDirectory, IReadOnlyList<GodotFabAssetFile> Files, long ImportedBytes, bool ReplacedExistingImport, string? BackupDirectory, string? BackupTreeSha256, DateTimeOffset ImportedAt, DateTimeOffset? RolledBackAt)
{
    [JsonIgnore]
    public string ReceiptPath { get; init; } = string.Empty;
    public bool IsApplied => RolledBackAt is null;
}

public sealed record GodotFabAssetImportResult(int ImportedFileCount, long ImportedBytes, GodotFabAssetImportReceipt Receipt);

public sealed record GodotFabAssetRollbackPlan(Guid Id, GodotFabAssetImportReceipt Receipt, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record GodotFabAssetRollbackResult(bool RestoredPreviousImport, GodotFabAssetImportReceipt Receipt);

public sealed record GodotFabAssetImportProgress(int Completed, int Total, string Message);

