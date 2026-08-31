namespace CyEngineHub.Fab.Remote;

public enum QuixelConnectionState
{
    Disconnected,
    Connected,
    Expired,
    Unavailable,
}

public sealed record QuixelSession(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    string AccountId,
    string DisplayName,
    string Email);

public sealed record QuixelStatus(
    QuixelConnectionState State,
    string Message,
    string? AccountId = null,
    string? DisplayName = null);

public sealed record QuixelLibraryResult(
    int AcquiredCount,
    IReadOnlyList<RemoteFabAsset> Assets,
    int UnresolvedCount);

public interface IQuixelSessionStore
{
    Task<QuixelSession?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(QuixelSession session, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

public interface IQuixelMegascansClient
{
    Uri AuthorizationUri { get; }

    Task<QuixelStatus> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<QuixelStatus> ConnectAsync(
        string authorizationCode,
        CancellationToken cancellationToken = default);

    Task<QuixelStatus> ConnectFromEpicSessionAsync(
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<QuixelLibraryResult> GetLibraryAsync(CancellationToken cancellationToken = default);

    Task<RemoteFabProductDetails> GetProductDetailsAsync(
        RemoteFabAsset asset,
        CancellationToken cancellationToken = default);

    Task<DirectFabDownloadResult> DownloadAsync(
        RemoteFabAsset asset,
        RemoteFabProjectVersion projectVersion,
        string libraryRoot,
        IProgress<DirectFabDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class QuixelException : Exception
{
    public QuixelException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
