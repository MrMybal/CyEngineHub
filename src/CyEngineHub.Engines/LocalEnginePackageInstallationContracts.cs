namespace CyEngineHub.Engines;

public sealed record LocalEnginePackageInstallFile(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);

public sealed record LocalEnginePackageInstallPlan(
    Guid Id,
    EngineFamily Family,
    string ProviderId,
    LocalEngineProject TargetProject,
    string PackageId,
    string DisplayName,
    string Version,
    string SourceDirectory,
    string DestinationDirectory,
    string WorkspaceDirectory,
    string DescriptorRelativePath,
    IReadOnlyList<LocalEnginePackageInstallFile> Files,
    long TotalBytes,
    bool ReplacesExisting,
    string? ExistingTargetFingerprint,
    string RiskSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record LocalEnginePackageInstallProgress(
    long CompletedFiles,
    long TotalFiles,
    string Message);

public sealed record LocalEnginePackageInstallResult(
    string DestinationDirectory,
    string? BackupDirectory,
    long CopiedFiles,
    long CopiedBytes,
    TimeSpan Duration,
    LocalEnginePackageInstallReceipt Receipt);

public sealed record LocalEnginePackageInstallReceipt(
    int SchemaVersion,
    Guid Id,
    EngineFamily Family,
    string ProviderId,
    LocalEngineProject TargetProject,
    string PackageId,
    string DisplayName,
    string Version,
    string SourceDirectory,
    string DestinationDirectory,
    string WorkspaceDirectory,
    string InstalledFingerprint,
    string? BackupDirectory,
    string? BackupFingerprint,
    long FileCount,
    long TotalBytes,
    DateTimeOffset InstalledAt,
    DateTimeOffset? RolledBackAt,
    string? RolledBackPackageDirectory);

public sealed record LocalEnginePackageRollbackPlan(
    Guid Id,
    LocalEnginePackageInstallReceipt Receipt,
    string RecoveryDirectory,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record LocalEnginePackageRollbackResult(
    LocalEnginePackageInstallReceipt Receipt,
    bool RestoredPreviousVersion,
    string RecoveryDirectory);

public sealed record LocalEnginePackageRemovalPlan(
    Guid Id,
    LocalEnginePackage Package,
    LocalEngineProject TargetProject,
    string PackageId,
    string DestinationDirectory,
    string WorkspaceDirectory,
    IReadOnlyList<LocalEnginePackageInstallFile> Files,
    string InstalledFingerprint,
    long TotalBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record LocalEnginePackageRemovalReceipt(
    int SchemaVersion,
    Guid Id,
    EngineFamily Family,
    string ProviderId,
    LocalEngineProject TargetProject,
    string PackageId,
    string DisplayName,
    string Version,
    string DestinationDirectory,
    string WorkspaceDirectory,
    string RemovedFingerprint,
    long FileCount,
    long TotalBytes,
    DateTimeOffset RemovedAt,
    DateTimeOffset? RestoredAt,
    string RecoveryDirectory);

public sealed record LocalEnginePackageRemovalResult(
    LocalEnginePackageRemovalReceipt Receipt,
    string RecoveryDirectory);

public sealed record LocalEnginePackageRestorePlan(
    Guid Id,
    LocalEnginePackageRemovalReceipt Receipt,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record LocalEnginePackageRestoreResult(
    LocalEnginePackageRemovalReceipt Receipt,
    string DestinationDirectory);

public interface ILocalEnginePackageInstallationProvider : IEngineModule
{
    Task<LocalEnginePackageInstallPlan> PreparePackageInstallAsync(
        string sourceDirectory,
        LocalEngineProject targetProject,
        CancellationToken cancellationToken = default);

    Task<LocalEnginePackageInstallResult> InstallPackageAsync(
        LocalEnginePackageInstallPlan plan,
        IProgress<LocalEnginePackageInstallProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalEnginePackageInstallReceipt>> LoadPackageInstallHistoryAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken = default);

    Task<LocalEnginePackageRollbackPlan> PreparePackageRollbackAsync(
        LocalEnginePackageInstallReceipt receipt,
        CancellationToken cancellationToken = default);

    Task<LocalEnginePackageRollbackResult> RollbackPackageAsync(
        LocalEnginePackageRollbackPlan plan,
        CancellationToken cancellationToken = default);
}

public interface ILocalEnginePackageManagementProvider : ILocalEnginePackageInstallationProvider
{
    Task<LocalEnginePackageRemovalPlan> PreparePackageRemovalAsync(
        LocalEnginePackage package,
        LocalEngineProject targetProject,
        CancellationToken cancellationToken = default);

    Task<LocalEnginePackageRemovalResult> RemovePackageAsync(
        LocalEnginePackageRemovalPlan plan,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LocalEnginePackageRemovalReceipt>> LoadPackageRemovalHistoryAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken = default);

    Task<LocalEnginePackageRestorePlan> PreparePackageRestoreAsync(
        LocalEnginePackageRemovalReceipt receipt,
        CancellationToken cancellationToken = default);

    Task<LocalEnginePackageRestoreResult> RestorePackageAsync(
        LocalEnginePackageRestorePlan plan,
        CancellationToken cancellationToken = default);
}

public sealed class LocalEnginePackageOperationException : Exception
{
    public LocalEnginePackageOperationException(
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
