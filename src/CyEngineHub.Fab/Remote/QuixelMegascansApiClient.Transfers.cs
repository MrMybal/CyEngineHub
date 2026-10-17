using System.Diagnostics;

namespace CyEngineHub.Fab.Remote;

public sealed partial class QuixelMegascansApiClient
{
    private int _downloadConnections = 12;
    public void SetDownloadConnections(int connections)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(connections, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(connections, 32);
        Volatile.Write(ref _downloadConnections, connections);
    }

    private async Task<long> DownloadSignedFilesAsync(Uri[] urls, string staging,
        IProgress<DirectFabDownloadProgress>? progress, CancellationToken token)
    {
        var connections = Volatile.Read(ref _downloadConnections);
        var parallelFiles = Math.Min(connections, urls.Length);
        var rangesPerFile = Math.Max(1, connections / parallelFiles);
        var received = new long[urls.Length];
        var totals = new long?[urls.Length];
        var results = new long[urls.Length];
        var complete = new bool[urls.Length];
        var gate = new object();
        var lastReport = Stopwatch.GetTimestamp();
        void Report(int index, long bytes, long? total, bool finished = false)
        {
            lock (gate)
            {
                received[index] = Math.Max(received[index], bytes);
                totals[index] = total;
                complete[index] |= finished;
                if (!finished && Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds < 250) return;
                lastReport = Stopwatch.GetTimestamp();
                var ratio = Enumerable.Range(0, urls.Length).Average(i => complete[i] ? 1 : totals[i] is > 0 ? Math.Clamp((double)received[i] / totals[i]!.Value, 0, 1) : 0);
                // Never extrapolate a total from the number of files or their relative sizes.
                long? knownTotal = totals.All(size => size.HasValue) ? totals.Sum(size => size!.Value) : null;
                progress?.Report(new(ratio, $"Téléchargement Megascans · {complete.Count(value => value)}/{urls.Length} fichiers · {connections} connexions maximum",
                    received.Sum(), knownTotal));
            }
        }
        progress?.Report(new(0, $"Préparation Megascans · {connections} connexions maximum", 0, null));
        await Parallel.ForEachAsync(Enumerable.Range(0, urls.Length),
            new ParallelOptions { MaxDegreeOfParallelism = parallelFiles, CancellationToken = token }, async (index, cancellation) =>
            {
                var target = Path.Combine(staging, MakeSafeFileName(urls[index], index));
                results[index] = await new SignedFileTransfer(_httpClient).DownloadAsync(urls[index], target, rangesPerFile,
                    (bytes, total) => Report(index, bytes, total), cancellation).ConfigureAwait(false);
                Report(index, results[index], results[index], finished: true);
            }).ConfigureAwait(false);
        return results.Sum();
    }
}
