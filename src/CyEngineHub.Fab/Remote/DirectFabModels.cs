namespace CyEngineHub.Fab.Remote;

public enum DirectFabConnectionState
{
    Disconnected,
    Connected,
    Expired,
    Unavailable,
}

public sealed record DirectFabSession(
    string AccountId,
    string DisplayName,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed record DirectFabStatus(
    DirectFabConnectionState State,
    string Message,
    string? AccountId = null,
    string? DisplayName = null);

public sealed record RemoteFabBuildVersion(string Version, string Platform);

public sealed record RemoteFabProjectVersion(
    string ArtifactId,
    IReadOnlyList<RemoteFabBuildVersion> Builds,
    IReadOnlyList<string> EngineVersions,
    IReadOnlyList<string> Platforms);

public sealed record RemoteFabAsset(
    string AssetId,
    string Namespace,
    string Title,
    string Description,
    string Source,
    string DistributionMethod,
    Uri? ListingUri,
    Uri? ThumbnailUri,
    IReadOnlyList<string> Categories,
    IReadOnlyList<RemoteFabProjectVersion> ProjectVersions,
    IReadOnlyList<Uri>? ImageUris = null,
    DateTimeOffset? AcquiredAt = null,
    string? WebLibraryItemId = null,
    bool WebCanDownload = false)
{
    public IReadOnlyList<Uri> GalleryUris => ImageUris is { Count: > 0 }
        ? ImageUris
        : ThumbnailUri is null ? [] : [ThumbnailUri];

    public RemoteFabProjectVersion? PreferredProjectVersion => ProjectVersions
        .OrderByDescending(
            version => version.EngineVersions.Count > 0 ? version.EngineVersions[0] : string.Empty,
            StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();
}

public sealed record DirectFabDownloadProgress(
    double? Fraction,
    string Message,
    long DownloadedBytes,
    long? TotalBytes)
{
    public long CachedBytes { get; init; }
}

public sealed record DirectFabDownloadResult(
    string AssetId,
    string DisplayName,
    string DestinationDirectory,
    int FileCount,
    long DownloadedBytes,
    DateTimeOffset CompletedAt,
    DirectFabSourceManifest? SourceManifest = null,
    string PackageKind = "Unknown",
    string StorageMode = "Uncompressed",
    string? ManifestPath = null,
    long? UncompressedBytes = null,
    long? StoredBytes = null);

public sealed record DirectFabSourceManifest(
    string Format,
    string AppName,
    string BuildVersion,
    uint Version,
    int FileCount,
    long DownloadSize);

public sealed record RemoteFabProductDetails(
    string Description,
    IReadOnlyList<Uri> ImageUris);

public interface IDirectFabSessionStore
{
    Task<DirectFabSession?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(DirectFabSession session, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IDirectFabClient
{
    void SetDownloadConnections(int connections) { }
    Task<DirectFabDownloadEstimate?> EstimateDownloadAsync(RemoteFabAsset asset, RemoteFabProjectVersion version,
        CancellationToken cancellationToken = default) => Task.FromResult<DirectFabDownloadEstimate?>(null);
    Uri AuthorizationUri { get; }

    Task<DirectFabStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<DirectFabStatus> ConnectAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteFabAsset>> GetLibraryAsync(
        CancellationToken cancellationToken = default);

    Task<RemoteFabProductDetails> GetProductDetailsAsync(
        RemoteFabAsset asset,
        CancellationToken cancellationToken = default);

    Task<DirectFabDownloadResult> DownloadAsync(
        RemoteFabAsset asset,
        string libraryRoot,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<DirectFabDownloadResult> DownloadAsync(
        RemoteFabAsset asset,
        RemoteFabProjectVersion projectVersion,
        string libraryRoot,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record DirectFabDownloadEstimate(long DownloadBytes, long UncompressedBytes, int FileCount);

public sealed class DirectFabException : Exception
{
    public DirectFabException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
