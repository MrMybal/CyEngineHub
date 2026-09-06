using System.IO.Compression;
using System.Text.Json;

namespace CyEngineHub.Fab.Remote;

public sealed record QuixelCollectionsSnapshot(DateTimeOffset UpdatedAt, IReadOnlyList<QuixelCollection> Collections);

public sealed class QuixelCollectionsCache(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    private readonly string _path = Path.GetFullPath(path);

    public async Task<QuixelCollectionsSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(_path)) return null;
            await using var file = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
            await using var gzip = new GZipStream(file, CompressionMode.Decompress);
            using var content = new MemoryStream();
            var buffer = new byte[16384];
            int read;
            while ((read = await gzip.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (content.Length + read > 16 * 1024 * 1024) return null;
                content.Write(buffer, 0, read);
            }
            content.Position = 0;
            var snapshot = await JsonSerializer.DeserializeAsync<QuixelCollectionsSnapshot>(content, JsonOptions, cancellationToken).ConfigureAwait(false);
            return snapshot?.Collections is { Count: <= 5000 } ? snapshot : null;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(QuixelCollectionsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporaryPath = _path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                await JsonSerializer.SerializeAsync(gzip, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, _path, true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }
}
