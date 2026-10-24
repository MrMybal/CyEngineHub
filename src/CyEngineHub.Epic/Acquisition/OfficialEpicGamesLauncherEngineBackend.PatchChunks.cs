using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Acquisition;

public sealed partial class OfficialEpicGamesLauncherEngineBackend
{
    // A small prefetch window instead of downloading every compressed chunk before
    // writing any file. Installed engine files remain read-only during this phase.
    private sealed class PatchChunkSource
    {
        private readonly OfficialEpicGamesLauncherEngineBackend _owner;
        private readonly EpicDownloadManifest _manifest;
        private readonly EpicPatchLocalContent _local;
        private readonly string _cache;
        private readonly string[] _order;
        private readonly Dictionary<string, int> _positions;
        private readonly IProgress<EpicBinaryEngineDownloadProgress>? _progress;
        private readonly EpicEnginePatchPlan _plan;
        public long Downloaded { get; private set; }
        public long Cached { get; private set; }
        public double Fraction { get; set; }

        public PatchChunkSource(OfficialEpicGamesLauncherEngineBackend owner, EpicDownloadManifest manifest,
            EpicPatchLocalContent local, string cache, IReadOnlyCollection<EpicManifestFile> files,
            EpicEnginePatchPlan plan, IProgress<EpicBinaryEngineDownloadProgress>? progress)
        {
            _owner = owner; _manifest = manifest; _local = local; _cache = cache; _plan = plan; _progress = progress;
            _order = files.SelectMany(file => file.Parts).Select(part => part.ChunkGuid)
                .Where(id => !local.Contains(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            _positions = _order.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<byte[]> LoadAsync(string id, CancellationToken token)
        {
            if (await _local.ReadAsync(id, token).ConfigureAwait(false) is { } data) return data;
            var path = GetSafeDestination(_cache, id + ".chunk");
            RejectUpdateLinks(path);
            if (File.Exists(path))
            {
                try { var ready = EpicManifestParser.ParseChunk(await File.ReadAllBytesAsync(path, token).ConfigureAwait(false), _manifest.Chunks[id]); File.SetLastWriteTimeUtc(path, DateTime.UtcNow); return ready; }
                catch (DirectFabException) { File.Delete(path); }
            }
            var window = new List<EpicManifestChunk> { _manifest.Chunks[id] };
            var reserved = checked(window[0].DownloadSize + 4);
            if (_positions.TryGetValue(id, out var position))
            {
                foreach (var next in _order.Skip(position + 1).Take(31))
                {
                    if (_local.Contains(next)) continue;
                    var chunk = _manifest.Chunks[next];
                    if (checked(reserved + chunk.DownloadSize + 4) > _plan.ChunkCacheLimitBytes) break;
                    reserved += chunk.DownloadSize + 4; window.Add(chunk);
                }
            }
            var keep = window.Select(chunk => chunk.ChunkId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Evict before downloading, leaving room for both existing and in-flight chunks.
            TrimPatchChunkCache(_cache, Math.Max(0, _plan.ChunkCacheLimitBytes - reserved), keep);
            if (_owner.PatchFreeSpace(_cache) < reserved)
                throw new IOException($"Espace insuffisant pour la fenêtre de chunks du patch : {EpicEnginePatchPlan.Size(reserved)} requis. L’installation n’a pas été remplacée.");
            var report = new PatchProgress(value => _progress?.Report(new(Fraction, value.Message,
                Downloaded + value.DownloadedBytes - value.CachedBytes, Math.Max(_plan.EstimatedDownloadBytes, Downloaded + value.DownloadedBytes - value.CachedBytes)) { PatchPlan = _plan }));
            var (bytes, cached) = await _owner.DownloadChunksAsync(window, _cache, window.Sum(chunk => chunk.DownloadSize), 1, report, token).ConfigureAwait(false);
            Downloaded += bytes - cached; Cached += cached;
            return EpicManifestParser.ParseChunk(await File.ReadAllBytesAsync(path, token).ConfigureAwait(false), _manifest.Chunks[id]);
        }
    }

    private sealed class PatchProgress(Action<EpicBinaryEngineDownloadProgress> report) : IProgress<EpicBinaryEngineDownloadProgress>
    { public void Report(EpicBinaryEngineDownloadProgress value) => report(value); }

    private static FileInfo[] PatchCacheFiles(string cache) => Directory.Exists(cache)
        ? new DirectoryInfo(cache).EnumerateFiles("*.chunk", SearchOption.TopDirectoryOnly)
            .Where(file => file.Name.Length == 38 && file.Name[..32].All(Uri.IsHexDigit)).ToArray() : [];

    private static void TrimPatchChunkCache(string cache, long bytes, ISet<string>? keep = null)
    {
        var files = PatchCacheFiles(cache).Where(file => keep?.Contains(file.Name[..32]) != true).OrderBy(file => file.LastWriteTimeUtc).ToArray();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (total <= bytes) break;
            RejectUpdateLinks(file.FullName);
            var length = file.Length; file.Delete(); total -= length;
        }
    }
}
