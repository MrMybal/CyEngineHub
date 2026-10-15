using CyEngineHub.Fab.Remote;

sealed class FakeQuixel : IQuixelMegascansClient
{
    public Uri AuthorizationUri => new("https://www.epicgames.com/id/login?redirectUrl=" + Uri.EscapeDataString(FakeFab.Callback.AbsoluteUri));
    public string? Received;
    public bool Fail;
    public Task<QuixelStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new QuixelStatus(QuixelConnectionState.Disconnected, "Fixture"));
    public Task<QuixelStatus> ConnectAsync(string code, CancellationToken cancellationToken = default)
    {
        Received = code;
        if (Fail) throw new InvalidOperationException("Do not expose " + FakeFab.Code);
        return Task.FromResult(new QuixelStatus(QuixelConnectionState.Connected, "Fixture", "fixture", "Demo"));
    }
    public Task<QuixelStatus> ConnectFromEpicSessionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<QuixelLibraryResult> GetLibraryAsync(CancellationToken cancellationToken = default) => Task.FromResult(new QuixelLibraryResult(0, [], 0));
    public Task<RemoteFabProductDetails> GetProductDetailsAsync(RemoteFabAsset asset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DirectFabDownloadResult> DownloadAsync(RemoteFabAsset asset, RemoteFabProjectVersion version, string root, IProgress<DirectFabDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
