using System.Buffers;
using System.Net;
using System.Net.Http.Headers;

namespace CyEngineHub.Fab.Remote;

// No session credentials are sent to signed CDN URLs. A single file may use byte
// ranges only when a strong validator protects against mixing different versions.
internal sealed class SignedFileTransfer(HttpClient client)
{
    private const long MaximumBytes = 16L * 1024 * 1024 * 1024;
    internal Func<int, CancellationToken, Task> RetryDelayAsync { get; set; } =
        (attempt, token) => Task.Delay(TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt))), token);

    internal async Task<long> DownloadAsync(Uri uri, string target, int connections,
        Action<long, long?> report, CancellationToken token)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Adresse de téléchargement non sécurisée.");
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var initial = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                initial.EnsureSuccessStatusCode();
                var length = initial.Content.Headers.ContentLength;
                if (length > MaximumBytes) throw new InvalidOperationException("Un fichier Megascans dépasse 16 Gio.");
                var validator = initial.Headers.ETag;
                var rangeCount = length is >= 8 * 1024 * 1024 ? (int)Math.Min(connections, length.Value / (2 * 1024 * 1024)) : 1;
                if (rangeCount > 1 && validator is { IsWeak: false } &&
                    initial.Headers.AcceptRanges.Contains("bytes") && initial.Content.Headers.ContentEncoding.Count == 0)
                {
                    initial.Dispose();
                    try { return await DownloadRangesAsync(uri, target, length!.Value, rangeCount, validator, report, token).ConfigureAwait(false); }
                    catch (RangeNotSupportedException) { } // Server ignored ranges or changed representation. Restart as one stream.
                    using var fallback = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    fallback.EnsureSuccessStatusCode();
                    return await CopyAsync(fallback, target, report, token).ConfigureAwait(false);
                }
                return await CopyAsync(initial, target, report, token).ConfigureAwait(false);
            }
            catch (Exception error) when (attempt < 5 && !token.IsCancellationRequested && IsRetryable(error))
            {
                // The generated staging target is never a published asset or a user's existing file.
                if (File.Exists(target)) File.Delete(target);
                await RetryDelayAsync(attempt, token).ConfigureAwait(false);
            }
        }
    }

    private static bool IsRetryable(Exception error) => error is IOException or OperationCanceledException ||
        error is HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError };

    private static async Task<long> CopyAsync(HttpResponseMessage response, string target, Action<long, long?> report, CancellationToken token)
    {
        var expected = response.Content.Headers.ContentLength;
        if (expected > MaximumBytes) throw new InvalidOperationException("Un fichier Megascans dépasse 16 Gio.");
        await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
        long received = 0;
        report(0, expected);
        try
        {
            int count;
            while ((count = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                received += count;
                if (received > MaximumBytes || expected is { } size && received > size)
                    throw new IOException("La taille reçue dépasse la taille annoncée.");
                await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                report(received, expected);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        if (expected is { } total && received != total) throw new IOException("Le fichier Megascans reçu est incomplet.");
        report(received, received);
        return received;
    }

    private async Task<long> DownloadRangesAsync(Uri uri, string target, long length, int count, EntityTagHeaderValue validator,
        Action<long, long?> report, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var handle = File.OpenHandle(target, FileMode.Create, FileAccess.Write, FileShare.None, FileOptions.Asynchronous | FileOptions.RandomAccess);
        RandomAccess.SetLength(handle, length);
        var received = new long[count];
        var gate = new object();
        Exception? failure = null;
        report(0, length);
        var tasks = Enumerable.Range(0, count).Select(async index =>
        {
            try
            {
                var start = length * index / count;
                var end = length * (index + 1) / count - 1;
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Range = new RangeHeaderValue(start, end);
                request.Headers.IfRange = new RangeConditionHeaderValue(validator);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestedRangeNotSatisfiable) throw new RangeNotSupportedException();
                response.EnsureSuccessStatusCode();
                var range = response.Content.Headers.ContentRange;
                if (response.StatusCode != HttpStatusCode.PartialContent || range is null || range.Unit != "bytes" ||
                    range.From != start || range.To != end || range.Length != length || response.Headers.ETag?.ToString() != validator.ToString() ||
                    response.Content.Headers.ContentEncoding.Count != 0 ||
                    response.Content.Headers.ContentLength is { } announced && announced != end - start + 1)
                    throw new RangeNotSupportedException();
                await using var input = await response.Content.ReadAsStreamAsync(cancellation.Token).ConfigureAwait(false);
                var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
                long written = 0;
                try
                {
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellation.Token).ConfigureAwait(false)) > 0)
                    {
                        if (written + read > end - start + 1) throw new IOException("Un segment Megascans dépasse sa taille annoncée.");
                        await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), start + written, cancellation.Token).ConfigureAwait(false);
                        written += read;
                        lock (gate) { received[index] = written; report(received.Sum(), length); }
                    }
                    if (written != end - start + 1) throw new IOException("Un segment Megascans reçu est incomplet.");
                }
                finally { ArrayPool<byte>.Shared.Return(buffer); }
            }
            catch (Exception error)
            {
                lock (gate) failure ??= error;
                await cancellation.CancelAsync().ConfigureAwait(false);
                throw;
            }
        }).ToArray();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); }
        catch
        {
            token.ThrowIfCancellationRequested();
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure!).Throw();
            throw;
        }
        return length;
    }

    private sealed class RangeNotSupportedException : Exception;
}
