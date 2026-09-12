namespace CyEngineHub.Desktop.ViewModels;

public sealed class MarketplaceBrowserRequestEventArgs(string id, string name, Uri inventoryUri,
    string profileDirectory, Func<string> getDownloadDirectory, Func<Task> openDownloadDirectory, Func<Task> openExternal,
    Func<string, Task> registerDownload, Func<string?, Task> showLocalLibrary) : EventArgs
{
    public Func<Func<string, CancellationToken, Task<string>>, CancellationToken, Task>? SynchronizeFabLibrary { get; init; }
    public string Id { get; } = id;
    public string Name { get; } = name;
    public Uri InventoryUri { get; } = inventoryUri;
    public string ProfileDirectory { get; } = profileDirectory;
    public Func<string> GetDownloadDirectory { get; } = getDownloadDirectory;
    public Func<Task> OpenDownloadDirectory { get; } = openDownloadDirectory;
    public Func<Task> OpenExternal { get; } = openExternal;
    public Func<string, Task> RegisterDownload { get; } = registerDownload;
    public Func<string?, Task> ShowLocalLibrary { get; } = showLocalLibrary;
}

public sealed class MarketplaceImportRequestEventArgs : EventArgs
{
    public TaskCompletionSource<IReadOnlyList<string>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
