namespace CyEngineHub.Core.Marketplaces;

public sealed record DownloadTelemetrySnapshot(string FilePath, long ReceivedBytes, long? TotalBytes,
    double? Percent, double? BytesPerSecond, TimeSpan? Remaining, TimeSpan Elapsed, string State);

/// <summary>Monotonic, five-second throughput window. Unknown sizes never invent a percentage or ETA.</summary>
public sealed class DownloadTelemetry
{
    private readonly Queue<(TimeSpan At, long Bytes)> _samples = new();
    private TimeSpan? _started;
    private TimeSpan _last;
    private long _previousBytes;

    public DownloadTelemetrySnapshot Update(string path, long received, long? total, TimeSpan now, string state)
    {
        received = Math.Max(0, received);
        total = total > 0 ? total : null;
        if (now < _last) now = _last;
        _started ??= now;
        if (received < _previousBytes || state != "Téléchargement") _samples.Clear();
        _last = now;
        _previousBytes = received;
        double? rate = null;
        if (state == "Téléchargement")
        {
            while (_samples.Count > 1 && now - _samples.Peek().At > TimeSpan.FromSeconds(5)) _samples.Dequeue();
            if (_samples.TryPeek(out var first) && now > first.At)
                rate = Math.Max(0, (received - first.Bytes) / (now - first.At).TotalSeconds);
            _samples.Enqueue((now, received));
        }
        var percent = total is { } size ? Math.Clamp(100d * received / size, 0, 100) : (double?)null;
        var remaining = total is { } length && rate > 0 && received < length
            ? TimeSpan.FromSeconds(Math.Min(TimeSpan.MaxValue.TotalSeconds / 2, (length - received) / rate.Value)) : (TimeSpan?)null;
        return new(path, received, total, percent, rate, remaining, now - _started.Value, state);
    }
}
