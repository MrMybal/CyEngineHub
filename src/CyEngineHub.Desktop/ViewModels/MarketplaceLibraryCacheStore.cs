using System.IO.Compression;
using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed record MarketplaceLibraryCacheSnapshot(
    DateTimeOffset UpdatedAt,
    string Source,
    IReadOnlyList<RemoteFabAsset> Assets,
    int TotalCount,
    int UnresolvedCount = 0);

public sealed class MarketplaceLibraryCacheStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<string, string> _rootDirectoryResolver;

    public MarketplaceLibraryCacheStore(string rootDirectory)
        : this(_ => rootDirectory)
    {
    }

    public MarketplaceLibraryCacheStore(Func<string, string> rootDirectoryResolver)
    {
        _rootDirectoryResolver = rootDirectoryResolver ??
                                 throw new ArgumentNullException(nameof(rootDirectoryResolver));
    }

    public async Task<MarketplaceLibraryCacheSnapshot?> LoadAsync(
        string source,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(source);
        if (!File.Exists(path)) return null;
        try
        {
            await using var file = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var compressed = new GZipStream(file, CompressionMode.Decompress);
            return await JsonSerializer.DeserializeAsync<MarketplaceLibraryCacheSnapshot>(
                    compressed,
                    SerializerOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        MarketplaceLibraryCacheSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var path = GetPath(snapshot.Source);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.partial";
        try
        {
            await using (var file = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81_920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var compressed = new GZipStream(file, CompressionLevel.Fastest))
            {
                await JsonSerializer.SerializeAsync(
                        compressed,
                        snapshot,
                        SerializerOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private string GetPath(string source)
    {
        var safeSource = new string(source
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray())
            .ToLowerInvariant();
        if (safeSource.Length == 0)
        {
            throw new ArgumentException("La source marketplace est invalide.", nameof(source));
        }
        var rootDirectory = Path.GetFullPath(_rootDirectoryResolver(safeSource));
        return Path.Combine(rootDirectory, $"{safeSource}-library-v1.json.gz");
    }
}
