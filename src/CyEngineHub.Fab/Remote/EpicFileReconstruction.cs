using System.Buffers;
using System.Security.Cryptography;

namespace CyEngineHub.Fab.Remote;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "Epic distribution file-integrity protocol.")]
internal static class EpicFileReconstruction
{
    private const int BlockSize = 1024 * 1024;
    private const int HashBufferSize = 256 * 1024;

    internal static async Task WriteVerifiedAsync(EpicManifestFile file, string destination, DecodedChunkCache cache,
        Action invalidate, CancellationToken token, Action<DirectFabDownloadPhase, long>? progress = null)
    {
        token.ThrowIfCancellationRequested();
        RejectReparsePoint(destination);
        var existing = new FileInfo(destination);
        if (existing.Exists && existing.Length == file.Size &&
            await VerifyExistingAsync(file, destination, progress, token).ConfigureAwait(false)) return;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".partial";
        RejectReparsePoint(temporary);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            long written = 0;
            progress?.Invoke(DirectFabDownloadPhase.Reconstruction, 0);
            await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                foreach (var part in file.Parts)
                {
                    var data = await cache.GetAsync(part.ChunkGuid, token).ConfigureAwait(false);
                    if ((long)part.Offset + part.Size > data.LongLength)
                        throw new InvalidOperationException($"Le fragment {part.ChunkGuid} dépasse son chunk.");
                    var remaining = data.AsMemory(checked((int)part.Offset), checked((int)part.Size));
                    while (!remaining.IsEmpty)
                    {
                        var block = remaining[..Math.Min(remaining.Length, BlockSize)];
                        await output.WriteAsync(block, token).ConfigureAwait(false);
                        hash.AppendData(block.Span);
                        written = checked(written + block.Length);
                        progress?.Invoke(DirectFabDownloadPhase.Reconstruction, written);
                        remaining = remaining[block.Length..];
                    }
                }
            }
            token.ThrowIfCancellationRequested();
            if (written != file.Size || !hash.GetHashAndReset().AsSpan().SequenceEqual(file.Sha1))
            {
                invalidate();
                throw new InvalidOperationException($"L’intégrité du fichier {file.RelativePath} est invalide après reconstruction.");
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<bool> VerifyExistingAsync(EpicManifestFile file, string path,
        Action<DirectFabDownloadPhase, long>? progress, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1,
            FileOptions.SequentialScan | FileOptions.Asynchronous);
        progress?.Invoke(DirectFabDownloadPhase.Verification, 0);
        if (progress is null)
            return (await SHA1.HashDataAsync(stream, token).ConfigureAwait(false)).AsSpan().SequenceEqual(file.Sha1);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        var buffer = ArrayPool<byte>.Shared.Rent(HashBufferSize);
        try
        {
            long verified = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, HashBufferSize), token).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                verified += read;
                progress(DirectFabDownloadPhase.Verification, verified);
            }
            return verified == file.Size && hash.GetHashAndReset().AsSpan().SequenceEqual(file.Sha1);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("La reconstruction ne peut pas utiliser un lien.");
    }
}
