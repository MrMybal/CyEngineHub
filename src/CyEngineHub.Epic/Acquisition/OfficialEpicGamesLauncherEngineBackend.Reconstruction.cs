using System.Diagnostics;
using System.Security.Cryptography;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Acquisition;

public sealed partial class OfficialEpicGamesLauncherEngineBackend
{
    private static DecodedChunkCache CreateReconstructionCache(string cache, EpicDownloadManifest manifest) => new(128L * 1024 * 1024, async (id, token) =>
    {
        var path = GetSafeDestination(cache, id + ".chunk");
        RejectReparsePoint(path);
        var raw = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
        return EpicManifestParser.ParseChunk(raw, manifest.Chunks[id]);
    });

    // SHA-1 is a distribution integrity field. Compute it as verified parts are written,
    // avoiding a second complete read of every newly reconstructed file.
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "Required Epic file-integrity protocol, not credentials.")]
    internal static async Task ReconstructFileAsync(EpicManifestFile file, string destination, DecodedChunkCache cache,
        Action invalidate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RejectReparsePoint(destination);
        if (EpicManifestParser.MatchesSha1(destination, file.Sha1)) return; // Resume still validates existing output.
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".partial";
        RejectReparsePoint(temporary);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            long written = 0;
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                foreach (var part in file.Parts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var data = await cache.GetAsync(part.ChunkGuid, cancellationToken).ConfigureAwait(false);
                    if (checked((long)part.Offset + part.Size) > data.LongLength)
                        throw new InvalidOperationException($"Le fragment {part.ChunkGuid} dépasse son chunk.");
                    var memory = data.AsMemory(checked((int)part.Offset), checked((int)part.Size));
                    await output.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                    hash.AppendData(memory.Span);
                    written = checked(written + part.Size);
                }
            }
            if (written != file.Size || !hash.GetHashAndReset().AsSpan().SequenceEqual(file.Sha1))
            {
                invalidate();
                throw new InvalidOperationException("L’intégrité du fichier reconstruit est invalide. Réessayez le téléchargement.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool ReportReconstructionProgress(ref long last, int index, int count)
    {
        if (index != 0 && index != count - 1 && Stopwatch.GetElapsedTime(last).TotalMilliseconds < 250) return false;
        last = Stopwatch.GetTimestamp(); return true;
    }
}
