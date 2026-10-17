using System.Diagnostics;
using System.Buffers;
using System.Net;

namespace CyEngineHub.Fab.Remote;

internal sealed class EpicChunkTransfer(HttpClient httpClient, int parallelism, string label,
    Func<int, CancellationToken, Task>? retryDelay = null)
{
    private const int MaximumChunkBytes = 256 * 1024 * 1024;
    private const int MaximumChunkAttempts = 6;
    private static Task DefaultRetryDelayAsync(int attempt, CancellationToken token) =>
        Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))), token);
    internal async Task<(long Bytes, long Cached)> DownloadAsync(IReadOnlyCollection<EpicManifestChunk> chunks,
        string cache, long total, double fraction, IProgress<DirectFabDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        RejectReparsePoint(cache);
        var received = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var gate = new object();
        using var validationGate = new SemaphoreSlim(2, 2); // Bound simultaneous decoded chunks, independently of network streams.
        long aggregate = 0, cached = 0, lastReport = Stopwatch.GetTimestamp();
        void Report(string id, long bytes, bool fromCache = false, bool force = false, string? message = null)
        {
            lock (gate)
            {
                var previous = received.GetValueOrDefault(id);
                var delta = Math.Max(0, bytes - previous); // Retry attempts never move progress backwards.
                received[id] = Math.Max(previous, bytes);
                aggregate += delta;
                if (fromCache) cached += delta;
                if (!force && Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds < 250) return;
                lastReport = Stopwatch.GetTimestamp();
                progress?.Report(new(Math.Clamp((double)aggregate / total * fraction, 0, fraction),
                    message ?? $"Téléchargement des chunks {label} · {parallelism} connexions maximum", aggregate, total) { CachedBytes = cached });
            }
        }
        progress?.Report(new(0, $"Préparation des chunks {label} · {parallelism} connexions maximum", 0, total));
        await Parallel.ForEachAsync(chunks, new ParallelOptions { MaxDegreeOfParallelism = parallelism, CancellationToken = cancellationToken },
            async (chunk, token) =>
            {
                var path = GetSafeDestination(cache, chunk.ChunkId + ".chunk");
                RejectReparsePoint(path);
                if (File.Exists(path))
                {
                    try
                    {
                        await ValidateCachedChunkAsync(path, chunk, validationGate, token).ConfigureAwait(false);
                        Report(chunk.ChunkId, new FileInfo(path).Length, fromCache: true);
                        return;
                    }
                    catch (DirectFabException) { File.Delete(path); } // Invalid cache only; never an installed engine file.
                }
                var partial = path + ".partial";
                RejectReparsePoint(partial);
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        var length = await DownloadChunkFileAsync(chunk, partial, bytes => Report(chunk.ChunkId, bytes), token).ConfigureAwait(false);
                        await ValidateCachedChunkAsync(partial, chunk, validationGate, token).ConfigureAwait(false);
                        token.ThrowIfCancellationRequested();
                        File.Move(partial, path, overwrite: true); // Only complete chunks become reusable cache entries.
                        Report(chunk.ChunkId, length);
                        break;
                    }
                    catch (Exception exception) when (IsRetryableChunkError(exception) && !token.IsCancellationRequested)
                    {
                        if (attempt + 1 >= MaximumChunkAttempts)
                            throw new IOException($"Téléchargement interrompu après {MaximumChunkAttempts} tentatives pour un chunk. Le cache validé est conservé ; cliquez sur Reprendre. {exception.Message}", exception);
                        Report(chunk.ChunkId, 0, force: true,
                            message: $"Incident réseau · nouvelle tentative {attempt + 2}/{MaximumChunkAttempts} · chunks validés conservés");
                        // Release file handles and discard only this attempt before the cancellable backoff.
                        if (File.Exists(partial)) File.Delete(partial);
                        await (retryDelay ?? DefaultRetryDelayAsync)(attempt, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        try { if (File.Exists(partial)) File.Delete(partial); }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                    }
                }
            }).ConfigureAwait(false);
        Report(string.Empty, 0, force: true);
        return (aggregate, cached);
    }

    private async Task<long> DownloadChunkFileAsync(EpicManifestChunk chunk, string partial, Action<long> report, CancellationToken token)
    {
        if (!chunk.DownloadUri.IsAbsoluteUri || chunk.DownloadUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Epic a retourné une adresse non sécurisée.");
        using var request = new HttpRequestMessage(HttpMethod.Get, chunk.DownloadUri);
        request.Headers.UserAgent.ParseAdd("CyEngineHub/1.0 EpicEngineConnector");
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var declared = response.Content.Headers.ContentLength;
        if (declared > MaximumChunkBytes) throw new InvalidOperationException("Un chunk Epic dépasse la taille autorisée.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long length = 0;
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                length += read;
                if (length > MaximumChunkBytes) throw new InvalidOperationException("Un chunk Epic dépasse la taille autorisée.");
                await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                report(length);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        if (declared is { } expected && length != expected)
            throw new IOException($"Le chunk Epic reçu est incomplet : {length:N0} octets reçus, {declared?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "taille HTTP inconnue"} annoncés par le serveur, {chunk.DownloadSize:N0} attendus par le manifest.");
        return length;
    }

    private static async Task ValidateCachedChunkAsync(string path, EpicManifestChunk chunk, SemaphoreSlim gate, CancellationToken token)
    {
        var size = new FileInfo(path).Length;
        if (size is < 41 or > MaximumChunkBytes)
            throw new DirectFabException("Un chunk en cache est incomplet.");
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            _ = EpicManifestParser.ParseChunk(bytes, chunk);
        }
        finally { gate.Release(); }
    }
    private static bool IsRetryableChunkError(Exception exception) => exception is IOException or DirectFabException or OperationCanceledException ||
        exception is HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError };

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Le cache de chunks ne peut pas contenir de lien.");
    }
    internal static void InvalidateFileChunks(string cache, EpicManifestFile file)
    {
        foreach (var part in file.Parts)
        {
            var path = GetSafeDestination(cache, part.ChunkGuid + ".chunk");
            RejectReparsePoint(path);
            if (File.Exists(path)) File.Delete(path);
        }
    }
    private static string GetSafeDestination(string root, string relative)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new DirectFabException("Chemin de chunk invalide.");
        return path;
    }
}
