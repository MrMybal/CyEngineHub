using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Acquisition;

// Read-only reuse of chunks mapped by the installed manifest. All installed files
// stay unchanged until every target file has passed the new manifest's SHA-1.
internal sealed class EpicPatchLocalContent
{
    private sealed record Source(string Path, EpicManifestFile File, EpicManifestFilePart Part);
    private sealed record Slice(string Path, long FileOffset, int Offset, int Length);
    private readonly Dictionary<string, List<Slice>> _slices = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _lengths = new(StringComparer.OrdinalIgnoreCase);
    public long ReusableBytes { get; private set; }
    public bool Contains(string id) => _slices.ContainsKey(id);
    public void Disable(string id) => _slices.Remove(id);

    public static async Task<EpicPatchLocalContent> CreateAsync(string target, EpicDownloadManifest manifest,
        EpicDownloadManifest? installed, IReadOnlyCollection<EpicManifestFile> changed, ISet<string> verified,
        Action<int, int>? progress, CancellationToken token)
    {
        var result = new EpicPatchLocalContent();
        var needed = changed.SelectMany(file => file.Parts).GroupBy(part => part.ChunkGuid, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, List<Source>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in (installed ?? manifest).Files)
        {
            token.ThrowIfCancellationRequested();
            var relevant = file.Parts.Where(part => needed.ContainsKey(part.ChunkGuid) && manifest.Chunks.TryGetValue(part.ChunkGuid, out var chunk) &&
                (installed ?? manifest).Chunks.TryGetValue(part.ChunkGuid, out var old) && chunk.RollingHash == old.RollingHash).ToArray();
            if (relevant.Length == 0) continue;
            var path = SafePath(target, file.RelativePath);
            if (!File.Exists(path) || new FileInfo(path).Length != file.Size) continue;
            foreach (var part in relevant)
            {
                if (!sources.TryGetValue(part.ChunkGuid, out var list)) sources[part.ChunkGuid] = list = [];
                list.Add(new(path, file, part));
            }
        }
        // An unchanged target file can also provide chunks without an older manifest.
        foreach (var file in manifest.Files.Where(file => verified.Contains(file.RelativePath)))
        {
            foreach (var part in file.Parts.Where(part => needed.ContainsKey(part.ChunkGuid)))
            {
                if (!sources.TryGetValue(part.ChunkGuid, out var list)) sources[part.ChunkGuid] = list = [];
                list.Add(new(SafePath(target, file.RelativePath), file, part));
            }
        }
        var valid = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var checkedChunks = 0;
        foreach (var (id, parts) in needed)
        {
            token.ThrowIfCancellationRequested();
            progress?.Invoke(++checkedChunks, needed.Count);
            if (!sources.TryGetValue(id, out var candidates)) continue;
            var slices = new List<Slice>(); var reusable = true;
            foreach (var part in parts.OrderBy(part => part.Offset))
            {
                var offset = (long)part.Offset; var end = checked(offset + part.Size);
                if (end > 256L * 1024 * 1024) { reusable = false; break; }
                while (offset < end)
                {
                    Source? selected = null;
                    foreach (var source in candidates.Where(source => source.Part.Offset <= offset && (long)source.Part.Offset + source.Part.Size > offset)
                                 .OrderByDescending(source => (long)source.Part.Offset + source.Part.Size))
                    {
                        if (!valid.TryGetValue(source.Path, out var matches))
                        {
                            matches = await OfficialEpicGamesLauncherEngineBackend.MatchesPatchFileAsync(source.Path, source.File, token).ConfigureAwait(false);
                            valid[source.Path] = matches;
                        }
                        if (matches) { selected = source; break; }
                    }
                    if (selected is null) { reusable = false; break; }
                    var length = checked((int)(Math.Min(end, (long)selected.Part.Offset + selected.Part.Size) - offset));
                    slices.Add(new(selected.Path, selected.Part.FileOffset + offset - selected.Part.Offset, (int)offset, length));
                    offset += length;
                }
                if (!reusable) break;
            }
            if (!reusable) continue;
            result._slices[id] = slices;
            result._lengths[id] = checked((int)parts.Max(part => (long)part.Offset + part.Size));
            result.ReusableBytes = checked(result.ReusableBytes + parts.Sum(part => (long)part.Size));
        }
        return result;
    }

    public async Task<byte[]?> ReadAsync(string id, CancellationToken token)
    {
        if (!_slices.TryGetValue(id, out var slices)) return null;
        var data = new byte[_lengths[id]];
        try
        {
            foreach (var slice in slices)
            {
                OfficialEpicGamesLauncherEngineBackend.RejectPatchSourceLinks(slice.Path);
                await using var input = new FileStream(slice.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous);
                input.Position = slice.FileOffset;
                await input.ReadExactlyAsync(data.AsMemory(slice.Offset, slice.Length), token).ConfigureAwait(false);
            }
            return data;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { Disable(id); return null; }
    }

    private static string SafePath(string root, string relative) => OfficialEpicGamesLauncherEngineBackend.PatchSourcePath(root, relative);
}

public sealed partial class OfficialEpicGamesLauncherEngineBackend
{
    internal static string PatchSourcePath(string root, string relative)
    {
        var path = GetSafeDestination(root, relative); RejectUpdateLinks(path); return path;
    }
    internal static void RejectPatchSourceLinks(string path) => RejectUpdateLinks(path);
}
