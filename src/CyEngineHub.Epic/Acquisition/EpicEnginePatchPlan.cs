using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Acquisition;

public sealed record EpicEnginePatchPlan(int ChangedFiles, long EstimatedDownloadBytes, long ReusableBytes,
    long AdditionalSpaceBytes, long FinalSizeDeltaBytes, long ChunkCacheLimitBytes)
{
    public string Summary => $"Patch · {ChangedFiles:N0} fichier(s) · téléchargement estimé : {Size(EstimatedDownloadBytes)} · " +
        $"espace temporaire supplémentaire : {Size(AdditionalSpaceBytes)} · variation finale : {(FinalSizeDeltaBytes >= 0 ? "+" : "−")}{Size(Math.Abs(FinalSizeDeltaBytes))} · données locales réutilisables : {Size(ReusableBytes)}";
    public string DisplayText => $"Patch · {ChangedFiles:N0} fichier(s) modifié(s)\nTéléchargement estimé : {Size(EstimatedDownloadBytes)}\n" +
        $"Espace temporaire supplémentaire : {Size(AdditionalSpaceBytes)}\nVariation finale : {(FinalSizeDeltaBytes >= 0 ? "+" : "−")}{Size(Math.Abs(FinalSizeDeltaBytes))} · données locales réutilisables : {Size(ReusableBytes)}";
    internal static string Size(long value) => value >= 1L << 30 ? $"{value / (double)(1L << 30):N2} Gio" : $"{value / (double)(1L << 20):N1} Mio";
    internal static long RequiredSpace(long remainingFiles, long peakCache, long existingCache)
    {
        var extra = checked(remainingFiles + Math.Max(0, peakCache - existingCache));
        return extra == 0 ? 0 : checked(extra + Math.Clamp(extra / 33, 16L << 20, 256L << 20));
    }
}

public sealed partial class OfficialEpicGamesLauncherEngineBackend
{
    internal long PatchChunkCacheBytes { get; set; } = 64L << 20;
    internal Func<string, long> PatchFreeSpace { get; set; } = target => new DriveInfo(Path.GetPathRoot(target)!).AvailableFreeSpace;

    internal static EpicManifestFile[] SelectPatchFiles(string target, EpicDownloadManifest manifest, EpicDownloadManifest? installed)
    {
        // Epic's explicit tags are authoritative. A core Linux helper or Android header
        // is NOT evidence that the optional Linux/Android SDK was installed.
        var profile = installed ?? manifest;
        var tags = profile.Files.Where(file => file.InstallTags.Count == 1)
            .GroupBy(file => file.InstallTags[0], StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Any(file => File.Exists(GetSafeDestination(target, file.RelativePath))))
            .Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tagged = manifest.Files.Any(file => file.InstallTags.Count > 0);
        string[] legacyOptional = ["source", "metahuman", "symbols", "android", "ios", "linux", "mac"];
        return manifest.Files.Where(file => File.Exists(GetSafeDestination(target, file.RelativePath)) ||
            (tagged ? file.InstallTags.Count == 0 || file.InstallTags.Any(tags.Contains) :
                !legacyOptional.Any(component => MatchesComponent(file, component)))).ToArray();
    }

    private static async Task<EpicDownloadManifest?> ReadInstalledPatchManifestAsync(string target, CancellationToken token)
    {
        var own = Path.Combine(target, ".cyenginehub-install.manifest");
        var store = Path.Combine(target, ".egstore");
        RejectUpdateLinks(own); RejectUpdateLinks(store);
        var candidates = (File.Exists(own) ? new[] { own } : Array.Empty<string>()).Concat(Directory.Exists(store)
            ? Directory.EnumerateFiles(store, "*.manifest", SearchOption.TopDirectoryOnly).Take(16).OrderByDescending(path => new FileInfo(path).Length)
            : []);
        foreach (var path in candidates)
        {
            token.ThrowIfCancellationRequested();
            RejectUpdateLinks(path);
            if (new FileInfo(path).Length is < 2 or > MaximumManifestBytes) continue;
            try
            {
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (input.Length is < 2 or > MaximumManifestBytes) continue;
                var content = new byte[checked((int)input.Length)];
                await input.ReadExactlyAsync(content, token).ConfigureAwait(false);
                var manifest = EpicManifestParser.Parse(content, new Uri("https://local-manifest.invalid/"));
                var build = manifest.Files.FirstOrDefault(file => file.RelativePath.Equals("Engine/Build/Build.version", StringComparison.OrdinalIgnoreCase));
                if (build is not null && await MatchesPatchFileAsync(GetSafeDestination(target, build.RelativePath), build, token).ConfigureAwait(false))
                    return manifest;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectFabException or ArgumentException or InvalidOperationException)
            { /* Optional local metadata is never trusted instead of the target manifest. */ }
        }
        return null;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "Epic distribution integrity protocol, not credentials.")]
    internal static async Task<bool> MatchesPatchFileAsync(string path, EpicManifestFile file, CancellationToken token)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != file.Size || file.Sha1.Length != 20) return false;
        RejectUpdateLinks(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await System.Security.Cryptography.SHA1.HashDataAsync(stream, token).ConfigureAwait(false);
        return hash.AsSpan().SequenceEqual(file.Sha1);
    }

}
