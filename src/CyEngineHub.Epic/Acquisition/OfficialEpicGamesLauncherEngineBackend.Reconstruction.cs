using System.Diagnostics;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Acquisition;

public sealed partial class OfficialEpicGamesLauncherEngineBackend
{
    private static DecodedChunkCache CreateReconstructionCache(string cache, EpicDownloadManifest manifest) =>
        new(128L * 1024 * 1024, (id, token) =>
            EpicChunkTransfer.ReadDecodedChunkAsync(GetSafeDestination(cache, id + ".chunk"), manifest.Chunks[id], token));

    internal static Task ReconstructFileAsync(EpicManifestFile file, string destination, DecodedChunkCache cache,
        Action invalidate, CancellationToken cancellationToken) =>
        EpicFileReconstruction.WriteVerifiedAsync(file, destination, cache, invalidate, cancellationToken);

    private static bool ReportReconstructionProgress(ref long last, int index, int count)
    {
        if (index != 0 && index != count - 1 && Stopwatch.GetElapsedTime(last).TotalMilliseconds < 250) return false;
        last = Stopwatch.GetTimestamp(); return true;
    }
}
