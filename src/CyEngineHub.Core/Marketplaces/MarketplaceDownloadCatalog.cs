using System.IO.Compression;
using System.Text.Json;

namespace CyEngineHub.Core.Marketplaces;

public sealed record MarketplaceLocalAsset(string Id, string MarketplaceId, string FilePath,
    string Name, string Format, string AssetKind, long SizeBytes, DateTimeOffset DownloadedAt, string Notes);

/// <summary>Indexes completed local files only. Never persists browser URLs, cookies or page contents.</summary>
public sealed class MarketplaceDownloadCatalog(string catalogPath)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly string _catalogPath = Path.GetFullPath(catalogPath);

    public async Task<IReadOnlyList<MarketplaceLocalAsset>> LoadAsync()
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try { return await ReadAsync().ConfigureAwait(false); }
        finally { Gate.Release(); }
    }

    public async Task<MarketplaceLocalAsset> RegisterAsync(string marketplaceId, string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(marketplaceId);
        var fullPath = Path.GetFullPath(filePath);
        // Includes ZIP directory parsing: never run it on the UI thread.
        var metadata = await Task.Run(() => DescribeFile(fullPath)).ConfigureAwait(false);
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync().ConfigureAwait(false);
            var existing = entries.FindIndex(item => item.MarketplaceId.Equals(marketplaceId, StringComparison.OrdinalIgnoreCase)
                && item.FilePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
            var asset = existing >= 0
                ? entries[existing] with { SizeBytes = metadata.Size, AssetKind = metadata.Kind }
                : new MarketplaceLocalAsset(Guid.NewGuid().ToString("N"), marketplaceId, fullPath,
                    Path.GetFileNameWithoutExtension(fullPath).Replace('_', ' '), Path.GetExtension(fullPath).ToUpperInvariant(),
                    metadata.Kind, metadata.Size, DateTimeOffset.UtcNow, "");
            if (existing >= 0) entries[existing] = asset;
            else entries.Add(asset);
            await WriteAsync(entries).ConfigureAwait(false);
            return asset;
        }
        finally { Gate.Release(); }
    }

    public async Task<MarketplaceLocalAsset> RenameAsync(string id, string name, string notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var entries = await ReadAsync().ConfigureAwait(false);
            var index = entries.FindIndex(item => item.Id == id);
            if (index < 0) throw new InvalidOperationException("Fiche locale introuvable.");
            var asset = entries[index] with { Name = name.Trim(), Notes = notes.Trim() };
            entries[index] = asset;
            await WriteAsync(entries).ConfigureAwait(false);
            return asset;
        }
        finally { Gate.Release(); }
    }

    private async Task<List<MarketplaceLocalAsset>> ReadAsync()
    {
        if (!File.Exists(_catalogPath)) return [];
        await using var stream = File.OpenRead(_catalogPath);
        // Corrupt catalogs must not be silently replaced by an empty index.
        return await JsonSerializer.DeserializeAsync<List<MarketplaceLocalAsset>>(stream).ConfigureAwait(false)
            ?? throw new InvalidDataException("Catalogue local invalide.");
    }

    private async Task WriteAsync(List<MarketplaceLocalAsset> entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_catalogPath)!);
        var temporary = _catalogPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
                await JsonSerializer.SerializeAsync(stream, entries).ConfigureAwait(false);
            File.Move(temporary, _catalogPath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static (long Size, string Kind) DescribeFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Length == 0) throw new InvalidDataException("Le fichier est absent ou vide.");
        var kind = "Fichier local · type non déterminé";
        if (file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var archive = ZipFile.OpenRead(path);
                var extensions = archive.Entries.Select(item => Path.GetExtension(item.FullName)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                kind = extensions.Contains(".uproject") ? "Projet Unreal"
                    : extensions.Contains(".uplugin") ? "Plugin Unreal"
                    : extensions.Contains(".uasset") || extensions.Contains(".umap") ? "Pack Unreal"
                    : extensions.Overlaps([".fbx", ".obj", ".gltf", ".glb"]) ? "Assets RAW"
                    : "Archive ZIP";
            }
            catch (InvalidDataException) { kind = "ZIP illisible · à vérifier"; }
        }
        else if (file.Extension.Equals(".unitypackage", StringComparison.OrdinalIgnoreCase)) kind = "Package Unity";
        return (file.Length, kind);
    }
}
