namespace CyEngineHub.Updates;

public sealed record ApplicationReleaseAsset(
    string Name,
    Uri DownloadUri,
    long SizeBytes);

public sealed record ApplicationRelease(
    Version Version,
    string TagName,
    string Title,
    string Notes,
    Uri ReleaseUri,
    DateTimeOffset? PublishedAt,
    ApplicationReleaseAsset? Installer,
    ApplicationReleaseAsset? Checksum,
    string? ExpectedSha256)
{
    public bool CanInstall =>
        Installer is not null &&
        (!string.IsNullOrWhiteSpace(ExpectedSha256) || Checksum is not null);
}

public sealed record ApplicationUpdateProgress(
    string Stage,
    double Fraction,
    long BytesProcessed,
    long? TotalBytes);

public sealed record VerifiedApplicationUpdate(
    string InstallerPath,
    string Sha256);

public interface IApplicationUpdateService
{
    Task<ApplicationRelease?> CheckForUpdateAsync(
        string repository,
        Version currentVersion,
        CancellationToken cancellationToken = default);

    Task<ApplicationRelease?> CheckForUpdateAsync(
        string repository,
        string currentVersion,
        bool includePrereleases,
        CancellationToken cancellationToken = default);

    Task<VerifiedApplicationUpdate> DownloadAndVerifyAsync(
        ApplicationRelease release,
        string destinationDirectory,
        IProgress<ApplicationUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ApplicationUpdateException : Exception
{
    public ApplicationUpdateException(string message)
        : base(message)
    {
    }

    public ApplicationUpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
