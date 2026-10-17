namespace CyEngineHub.Fab.Remote;

public sealed partial class DirectFabApiClient
{
    private int _downloadConnections = 12;
    internal Func<int, CancellationToken, Task>? ChunkRetryDelayAsync { get; set; }
    public void SetDownloadConnections(int connections)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(connections, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(connections, 32);
        Volatile.Write(ref _downloadConnections, connections);
    }
}
