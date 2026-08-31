using System.Text.Json.Serialization;

namespace CyEngineHub.Fab.Import;

public sealed record FabContentFile(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    bool WillReplaceExisting);

public sealed record FabProjectImportPlan(
    Guid Id,
    string SourceContentDirectory,
    string SourceDisplayName,
    string TargetProjectPath,
    string TargetContentDirectory,
    IReadOnlyList<FabContentFile> Files,
    int SkippedFileCount,
    long TotalBytes,
    string CompatibilitySummary,
    string BackupDirectory,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public int ConflictCount => Files.Count(file => file.WillReplaceExisting);
}

public sealed record FabProjectImportResult(
    int ImportedFileCount,
    int ReplacedFileCount,
    long ImportedBytes,
    string? BackupDirectory,
    FabImportReceipt Receipt);

public sealed record FabImportedFileReceipt(
    string RelativePath,
    bool ReplacedExisting,
    long ImportedLength,
    DateTimeOffset ImportedLastWriteTimeUtc,
    string ImportedSha256,
    long? OriginalLength,
    DateTimeOffset? OriginalLastWriteTimeUtc,
    string? OriginalSha256);

public sealed record FabImportReceipt(
    Guid Id,
    string SourceContentDirectory,
    string SourceDisplayName,
    string TargetProjectPath,
    string TargetContentDirectory,
    IReadOnlyList<FabImportedFileReceipt> Files,
    long ImportedBytes,
    string? BackupDirectory,
    string CompatibilitySummary,
    DateTimeOffset ImportedAt,
    DateTimeOffset? RolledBackAt)
{
    [JsonIgnore]
    public string ReceiptPath { get; init; } = string.Empty;

    public bool IsApplied => RolledBackAt is null;

    public int ReplacedFileCount => Files.Count(file => file.ReplacedExisting);

    public int CreatedFileCount => Files.Count - ReplacedFileCount;
}

public sealed record FabImportRollbackPlan(
    Guid Id,
    FabImportReceipt Receipt,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public int RestoreFileCount => Receipt.ReplacedFileCount;

    public int DeleteFileCount => Receipt.CreatedFileCount;
}

public sealed record FabImportRollbackResult(
    int RestoredFileCount,
    int DeletedFileCount,
    FabImportReceipt Receipt);

public sealed record FabProjectImportProgress(int Completed, int Total, string Message);

public sealed class FabContentOperationException : Exception
{
    public FabContentOperationException(
        string message,
        string operation,
        string? path = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        Path = path;
    }

    public string Operation { get; }

    public string? Path { get; }
}
