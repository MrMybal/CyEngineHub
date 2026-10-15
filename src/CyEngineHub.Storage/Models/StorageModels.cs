using CyEngineHub.Git.Models;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Storage.Models;

public enum StorageCategory
{
    EngineInstallation,
    Project,
    EngineDerivedDataCache,
    ProjectDerivedDataCache,
    GlobalDerivedDataCache,
    SharedDerivedDataCache,
    ProjectIntermediate,
    ProjectBinaries,
    ProjectLogs,
    ProjectCooked,
    ProjectStagedBuilds,
    VaultCache,
    GitLfs,
    MarketplaceLibrary,
    MarketplaceDownloads,
    MarketplaceCache,
}

public enum StorageEntryRole
{
    Footprint,
    Generated,
    Cache,
    External,
}

public enum StorageHealth
{
    Ready,
    Warning,
    Partial,
}

public sealed record StorageEntry(
    string Id,
    StorageCategory Category,
    StorageEntryRole Role,
    string DisplayName,
    string OwnerName,
    string OwnerRoot,
    string Path,
    long SizeBytes,
    long FileCount,
    long DirectoryCount,
    DateTimeOffset? LastModified,
    StorageHealth Health,
    bool IsSafeToClean,
    string Diagnostic);

public sealed record DriveStorageSnapshot(
    string RootPath,
    string VolumeLabel,
    long TotalBytes,
    long FreeBytes,
    long AvailableFreeBytes)
{
    public bool IsLowSpace => TotalBytes > 0 && AvailableFreeBytes < Math.Min(
        50L * 1024 * 1024 * 1024,
        TotalBytes / 10);
}

public sealed record StorageSnapshot(
    IReadOnlyList<StorageEntry> Entries,
    IReadOnlyList<DriveStorageSnapshot> Drives,
    IReadOnlyList<string> Diagnostics,
    DateTimeOffset RefreshedAt);

public sealed record StorageAnalysisRequest(
    IReadOnlyList<UnrealEngineInstallation> Engines,
    IReadOnlyList<UnrealProject> Projects,
    IReadOnlyList<GitRepositorySnapshot> GitRepositories)
{
    public IReadOnlyList<AdditionalStorageRoot> AdditionalRoots { get; init; } = [];
}

// Explicit provider roots; never eligible for automatic cleanup.
public sealed record AdditionalStorageRoot(StorageCategory Category, string Owner, string Path);

public sealed record StorageAnalysisProgress(int Completed, int Total, string Message);

public sealed record StorageCleanupPlan(
    string EntryId,
    StorageCategory Category,
    string DisplayName,
    string TargetPath,
    string OwnerRoot,
    long EstimatedBytes,
    long EstimatedFileCount,
    long EstimatedDirectoryCount,
    DateTimeOffset PreparedAt);

public sealed record StorageCleanupResult(
    string TargetPath,
    long EstimatedBytes,
    bool TargetRemoved,
    DateTimeOffset CompletedAt);

public sealed class StorageException : Exception
{
    public StorageException(
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
