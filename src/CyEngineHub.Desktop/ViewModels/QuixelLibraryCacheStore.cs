using System.IO.Compression;
using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed record QuixelLibraryCacheSnapshot(
    DateTimeOffset UpdatedAt,
    QuixelLibraryResult Library);

public sealed class QuixelLibraryCacheStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;

    public QuixelLibraryCacheStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public async Task<QuixelLibraryCacheSnapshot?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        try
        {
            await using var file = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var compressed = new GZipStream(file, CompressionMode.Decompress);
            return await JsonSerializer.DeserializeAsync<QuixelLibraryCacheSnapshot>(
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
        QuixelLibraryCacheSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Le dossier du cache Megascans est invalide.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.partial";
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

            File.Move(temporaryPath, _path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
