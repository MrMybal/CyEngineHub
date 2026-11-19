using System.Diagnostics;

namespace CyEngineHub.Fab.Remote;

public sealed partial class DirectFabApiClient
{
    internal static async Task ReconstructFilesAsync(EpicDownloadManifest manifest, string stagingFiles, string chunkCache,
        long completedBytes, long cachedBytes, IProgress<DirectFabDownloadProgress>? progress, CancellationToken cancellationToken)
    {
        const long decodedCacheBytes = 64L * 1024 * 1024;
        using var decoded = new DecodedChunkCache(decodedCacheBytes, (id, token) =>
            EpicChunkTransfer.ReadDecodedChunkAsync(GetSafeDestination(chunkCache, id + ".chunk"), manifest.Chunks[id], token));

        var transferTotal = Math.Max(1, manifest.DownloadSize);
        var outputTotal = manifest.Files.Sum(file => file.Size);
        long completedFilesBytes = 0;
        long reportedAt = 0;
        var lastPhase = DirectFabDownloadPhase.Transfer;
        var reportedData = false;
        var reportedVerification = false;
        var reportedReconstruction = false;
        double lastFraction = 0.72;
        for (var index = 0; index < manifest.Files.Count; index++)
        {
            var file = manifest.Files[index];
            var destination = GetSafeDestination(stagingFiles, file.RelativePath);
            try
            {
                await EpicFileReconstruction.WriteVerifiedAsync(file, destination, decoded,
                    () => EpicChunkTransfer.InvalidateFileChunks(chunkCache, file), cancellationToken,
                    progress is null ? null : ReportFileProgress).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception) { throw new DirectFabException(exception.Message, exception); }
            completedFilesBytes += file.Size;

            void ReportFileProgress(DirectFabDownloadPhase phase, long inFile)
            {
                lastPhase = phase;
                var verification = phase == DirectFabDownloadPhase.Verification;
                var phaseAlreadyReported = verification ? reportedVerification : reportedReconstruction;
                if (phaseAlreadyReported && (reportedData || inFile == 0) && Stopwatch.GetElapsedTime(reportedAt).TotalMilliseconds < 250)
                    return;
                var processed = completedFilesBytes + inFile;
                // A damaged resume candidate is rewritten. The phase byte count then starts again for that file.
                var ratio = outputTotal > 0 ? (double)processed / outputTotal : (double)index / manifest.Files.Count;
                lastFraction = Math.Max(lastFraction, 0.72 + 0.27 * Math.Clamp(ratio, 0, 1));
                var label = verification ? "Vérification" : "Reconstruction";
                progress!.Report(new(lastFraction, $"{label} · fichier {index + 1:N0}/{manifest.Files.Count:N0}", completedBytes, transferTotal)
                {
                    Phase = phase, ProcessedBytes = processed, TotalProcessingBytes = outputTotal, CachedBytes = cachedBytes,
                });
                if (verification) reportedVerification = true;
                else reportedReconstruction = true;
                reportedData |= inFile > 0;
                reportedAt = Stopwatch.GetTimestamp();
            }
        }
        progress?.Report(new(0.99, "Reconstruction Fab vérifiée", completedBytes, transferTotal)
        {
            Phase = lastPhase, ProcessedBytes = completedFilesBytes, TotalProcessingBytes = outputTotal, CachedBytes = cachedBytes,
        });
    }
}
