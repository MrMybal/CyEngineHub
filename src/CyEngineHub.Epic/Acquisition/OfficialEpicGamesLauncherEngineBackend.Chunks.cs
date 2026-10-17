using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Acquisition;

public sealed partial class OfficialEpicGamesLauncherEngineBackend
{
    private int _chunkConcurrency = 12;
    internal Func<int, CancellationToken, Task> ChunkRetryDelayAsync { get; set; } =
        (attempt, token) => Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))), token);
    public void SetChunkConcurrency(int concurrency)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(concurrency, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(concurrency, 32);
        Volatile.Write(ref _chunkConcurrency, concurrency);
    }

    private Task<(long Bytes, long Cached)> DownloadChunksAsync(IReadOnlyCollection<EpicManifestChunk> chunks,
        string cache, long total, double fraction, IProgress<EpicBinaryEngineDownloadProgress>? progress, CancellationToken cancellationToken) =>
        new EpicChunkTransfer(_httpClient!, Volatile.Read(ref _chunkConcurrency), "Unreal", ChunkRetryDelayAsync)
            .DownloadAsync(chunks, cache, total, fraction, progress is null ? null : new ChunkProgressAdapter(progress), cancellationToken);

    private sealed class ChunkProgressAdapter(IProgress<EpicBinaryEngineDownloadProgress> progress) : IProgress<DirectFabDownloadProgress>
    {
        public void Report(DirectFabDownloadProgress value) => progress.Report(new(value.Fraction, value.Message,
            value.DownloadedBytes, value.TotalBytes) { CachedBytes = value.CachedBytes });
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Le cache de chunks ne peut pas contenir de lien.");
    }
    private static void InvalidateFileChunks(string cache, EpicManifestFile file)
    {
        foreach (var part in file.Parts)
        {
            var path = GetSafeDestination(cache, part.ChunkGuid + ".chunk");
            RejectReparsePoint(path);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
