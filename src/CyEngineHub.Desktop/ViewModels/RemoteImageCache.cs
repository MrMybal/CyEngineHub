using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace CyEngineHub.Desktop.ViewModels;

public sealed class RemoteImageCache
{
    private const long MaximumImageBytes = 16 * 1024 * 1024;
    private static readonly HttpClient SharedHttpClient = CreateHttpClient();
    private static readonly SemaphoreSlim SharedDownloadGate = new(6, 6);
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

    public async Task<Bitmap?> LoadAsync(Uri? uri, CancellationToken cancellationToken = default)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var cachePath = GetCachePath(uri);
        var cached = TryLoadCached(cachePath);
        if (cached is not null)
        {
            return cached;
        }

        await SharedDownloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cached = TryLoadCached(cachePath);
            if (cached is not null)
            {
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
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return TryLoad(cachePath);
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

    private static Bitmap? TryLoadCached(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        return TryLoad(path);
    }

    private static Bitmap? TryLoad(string path)
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
            return new Bitmap(stream);
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
}
