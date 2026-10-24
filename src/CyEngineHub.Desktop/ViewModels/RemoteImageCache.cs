using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class RemoteImageCache
{
    private const long MaximumImageBytes = 16 * 1024 * 1024;
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private static readonly SemaphoreSlim SharedDownloadGate = new(4, 4);
    private static readonly object CacheTrimGate = new();
    internal const long MaximumCacheBytes = 256L * 1024 * 1024;
    private readonly Func<string> _cacheDirectoryResolver;

    public RemoteImageCache(string cacheDirectory)
        : this(() => cacheDirectory)
    {
    }

    public RemoteImageCache(Func<string> cacheDirectoryResolver)
    {
        _cacheDirectoryResolver = cacheDirectoryResolver ??
                                  throw new ArgumentNullException(nameof(cacheDirectoryResolver));
    }

    public Task<Bitmap?> LoadAsync(Uri? uri, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadQueuedAsync(uri, 2048, cancellationToken), cancellationToken);

    public Task<Bitmap?> LoadThumbnailAsync(Uri? uri, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadQueuedAsync(uri, 640, cancellationToken), cancellationToken);

    public Task<Bitmap?> LoadGalleryThumbnailAsync(Uri? uri, CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadQueuedAsync(uri, 192, cancellationToken), cancellationToken);

    private async Task<Bitmap?> LoadQueuedAsync(Uri? uri, int maximumEdge, CancellationToken cancellationToken)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        await SharedDownloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cachePath = GetCachePath(uri);
            var cached = TryLoadCached(cachePath, maximumEdge);
            if (cached is not null)
            {
                try { File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                TrimCache(Path.GetDirectoryName(cachePath)!, cachePath);
                return cached;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Accept.ParseAdd("image/avif,image/webp,image/png,image/jpeg,image/*");
            using var response = await SharedHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri is not { Scheme: "https" })
            {
                return null;
            }
            if (response.Content.Headers.ContentLength is > MaximumImageBytes)
            {
                return null;
            }
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null &&
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(mediaType, "binary/octet-stream", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var temporaryPath = $"{cachePath}.{Guid.NewGuid():N}.partial";
            try
            {
                await using var input = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                await using (var output = new FileStream(
                                 temporaryPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 81_920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[81_920];
                    long total = 0;
                    while (true)
                    {
                        var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (read == 0)
                        {
                            break;
                        }

                        total += read;
                        if (total > MaximumImageBytes)
                        {
                            return null;
                        }
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken)
                            .ConfigureAwait(false);
                    }
                }

                if (!HasSupportedImageSignature(temporaryPath))
                {
                    return null;
                }
                File.Move(temporaryPath, cachePath, true);
                TrimCache(Path.GetDirectoryName(cachePath)!, cachePath);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return TryLoad(cachePath, maximumEdge);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            SharedDownloadGate.Release();
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CyEngineHub/1.0 ThumbnailCache");
        return client;
    }

    private static Bitmap? TryLoadCached(string path, int maximumEdge)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        return TryLoad(path, maximumEdge);
    }

    private static Bitmap? TryLoad(string path, int maximumEdge)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81_920,
                FileOptions.SequentialScan);
            using var codec = SkiaSharp.SKCodec.Create(path) ?? throw new ArgumentException("Unsupported cached image.");
            return codec.Info.Width >= codec.Info.Height
                ? Bitmap.DecodeToWidth(stream, Math.Min(maximumEdge, codec.Info.Width))
                : Bitmap.DecodeToHeight(stream, Math.Min(maximumEdge, codec.Info.Height));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception deleteException) when (
                deleteException is IOException or UnauthorizedAccessException)
            {
            }
            return null;
        }
    }

    private static bool HasSupportedImageSignature(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            header.Length,
            FileOptions.SequentialScan);
        var read = stream.Read(header);
        return read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF ||
               read >= 8 && header[..8].SequenceEqual(
                   new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }) ||
               read >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8) ||
               read >= 6 && (header[..6].SequenceEqual("GIF87a"u8) || header[..6].SequenceEqual("GIF89a"u8));
    }

    private string GetCachePath(Uri uri)
    {
        var cacheDirectory = Path.GetFullPath(_cacheDirectoryResolver());
        Directory.CreateDirectory(cacheDirectory);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        return Path.Combine(cacheDirectory, $"{hash}.image");
    }

    // Only our SHA256-named cached images are eligible. Never touch packs, user files or active .partial transfers.
    internal static void TrimCache(string directory, string? preservePath = null, long maximumBytes = MaximumCacheBytes)
    {
        lock (CacheTrimGate)
        {
            try
            {
                var files = new DirectoryInfo(directory).EnumerateFiles("*.image", SearchOption.TopDirectoryOnly)
                    .Where(file => file.Name.Length == 70 && file.Name[..64].All(Uri.IsHexDigit) &&
                        (file.Attributes & FileAttributes.ReparsePoint) == 0).OrderBy(file => file.LastWriteTimeUtc).ToArray();
                var total = files.Sum(file => file.Length);
                if (total <= maximumBytes) return;
                foreach (var file in files)
                {
                    if (string.Equals(file.FullName, preservePath, StringComparison.OrdinalIgnoreCase)) continue;
                    try { var length = file.Length; file.Delete(); total -= length; }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                    if (total <= maximumBytes * 3 / 4) break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }
}
