namespace CyEngineHub.Epic.Acquisition;

// Sequential reconstruction cache, scoped to one transfer. No static retained asset data.
internal sealed class DecodedChunkCache(long maximumBytes, Func<string, CancellationToken, Task<byte[]>> load) : IDisposable
{
    private readonly Dictionary<string, (byte[] Data, LinkedListNode<string> Node)> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _lru = new();
    private readonly long _maximumBytes = maximumBytes >= 0 ? maximumBytes : throw new ArgumentOutOfRangeException(nameof(maximumBytes));
    public long ResidentBytes { get; private set; }
    public long PeakBytes { get; private set; }
    public long Loads { get; private set; }
    public long Hits { get; private set; }
    public async ValueTask<byte[]> GetAsync(string id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_entries.TryGetValue(id, out var entry))
        {
            _lru.Remove(entry.Node); _lru.AddLast(entry.Node); Hits++; return entry.Data;
        }
        var data = await load(id, cancellationToken).ConfigureAwait(false);
        Loads++;
        if (data.LongLength <= _maximumBytes && _maximumBytes > 0)
        {
            while (ResidentBytes + data.LongLength > _maximumBytes && _lru.First is { } oldest)
            {
                ResidentBytes -= _entries[oldest.Value].Data.LongLength;
                _entries.Remove(oldest.Value); _lru.RemoveFirst();
            }
            _entries.Add(id, (data, _lru.AddLast(id)));
            ResidentBytes += data.LongLength;
            PeakBytes = Math.Max(PeakBytes, ResidentBytes);
        }
        return data;
    }
    public void Dispose() { _entries.Clear(); _lru.Clear(); ResidentBytes = 0; }
    public void Remove(string id)
    {
        if (!_entries.Remove(id, out var entry)) return;
        ResidentBytes -= entry.Data.LongLength; _lru.Remove(entry.Node);
    }
}
