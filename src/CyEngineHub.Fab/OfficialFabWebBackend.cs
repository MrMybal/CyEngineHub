namespace CyEngineHub.Fab;

public enum FabBackendState
{
    WebOnly,
    Connected,
    Unavailable,
}

public sealed record FabBackendStatus(
    FabBackendState State,
    string Name,
    string Message,
    bool SupportsRemoteLibrary,
    bool SupportsDownloads);

public interface IFabBackend
{
    Uri HomeUri { get; }

    Uri LibraryUri { get; }

    Uri ExportGuideUri { get; }

    Task<FabBackendStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public sealed class OfficialFabWebBackend : IFabBackend
{
    public Uri HomeUri { get; } = new("https://www.fab.com/");

    public Uri LibraryUri { get; } = new("https://www.fab.com/library");

    public Uri ExportGuideUri { get; } = new(
        "https://dev.epicgames.com/documentation/en-us/fab/exporting-assets-from-fab-in-launcher");

    public Task<FabBackendStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new FabBackendStatus(
            FabBackendState.WebOnly,
            "Fab officiel",
            "My Library est disponible dans le navigateur officiel; aucune API desktop publique stable n’expose la bibliothèque globale à CyEngineHub.",
            false,
            false));
    }
}
