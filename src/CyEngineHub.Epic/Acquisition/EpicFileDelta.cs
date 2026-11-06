using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Acquisition;

// A reversible byte-range patch, not a second copy of each changed binary.
// All target bytes are SHA-1 verified before any installed byte is changed.
[System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350", Justification = "Epic distribution file integrity, not credentials.")]
internal static class EpicFileDelta
{
    private const int BlockSize = 4096;
    private const string Magic = "CYDELTA1";
    internal sealed record Header(string Path, bool Existed, long OriginalLength, long TargetLength,
        string OriginalSha1, string TargetSha1, long OriginalWriteTicks);
    internal sealed record Info(string PatchPath, Header Header, long DataPosition, int Ranges,
        long NewBytes, long OldBytes, long DiskBytes, string Digest)
    {
        public long AllocatedBytes => checked((DiskBytes + 4095) / 4096 * 4096 + 1024);
    }
    private delegate void RangeAction(long offset, ReadOnlySpan<byte> old, ReadOnlySpan<byte> next);

    internal static async Task<Info> CreateAsync(string originalPath, EpicManifestFile file, string patchPath,
        DecodedChunkCache cache, Action<long> ensureCapacity, CancellationToken token)
    {
        OfficialEpicGamesLauncherEngineBackend.RejectPatchSourceLinks(originalPath);
        OfficialEpicGamesLauncherEngineBackend.RejectPatchSourceLinks(patchPath);
        var temporary = patchPath + ".partial";
        OfficialEpicGamesLauncherEngineBackend.RejectPatchSourceLinks(temporary);
        if (File.Exists(temporary)) File.Delete(temporary);
        var existed = File.Exists(originalPath);
        await using var original = existed ? new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan) : null;
        var header = new Header(file.RelativePath, existed, original?.Length ?? 0, file.Size, new string('0', 40),
            Convert.ToHexString(file.Sha1), existed ? File.GetLastWriteTimeUtc(originalPath).Ticks : 0);
        var json = JsonSerializer.SerializeToUtf8Bytes(header);
        long outputBudget = 0;
        void Charge(long count)
        {
            if (outputBudget < count) { var next = Math.Max(1L << 20, count); ensureCapacity(next); outputBudget = next; }
            outputBudget -= count;
        }
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.Asynchronous))
            using (var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true))
            using (var oldHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
            using (var nextHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
            {
                Charge(json.Length + 48); writer.Write(Encoding.ASCII.GetBytes(Magic)); writer.Write(json.Length); writer.Write(json);
                var countPosition = output.Position; writer.Write(0);
                var previous = new byte[BlockSize]; var ranges = 0; long position = 0; long oldRead = 0;
                foreach (var part in file.Parts)
                {
                    token.ThrowIfCancellationRequested();
                    var data = await cache.GetAsync(part.ChunkGuid, token).ConfigureAwait(false);
                    if (part.FileOffset != position || checked((long)part.Offset + part.Size) > data.LongLength)
                        throw new InvalidOperationException("Plages du fichier cible invalides.");
                    var consumed = 0L;
                    while (consumed < part.Size)
                    {
                        token.ThrowIfCancellationRequested();
                        var count = (int)Math.Min(BlockSize, part.Size - consumed);
                        var oldCount = (int)Math.Min(count, header.OriginalLength - oldRead);
                        if (oldCount > 0)
                        {
                            await original!.ReadExactlyAsync(previous.AsMemory(0, oldCount), token).ConfigureAwait(false);
                            oldHash.AppendData(previous.AsSpan(0, oldCount)); oldRead += oldCount;
                        }
                        nextHash.AppendData(data.AsSpan(checked((int)(part.Offset + consumed)), count));
                        WriteRange(position, previous.AsSpan(0, oldCount), data.AsSpan(checked((int)(part.Offset + consumed)), count));
                        consumed += count; position += count;
                    }
                }
                while (oldRead < header.OriginalLength)
                {
                    token.ThrowIfCancellationRequested();
                    var count = (int)Math.Min(BlockSize, header.OriginalLength - oldRead);
                    await original!.ReadExactlyAsync(previous.AsMemory(0, count), token).ConfigureAwait(false);
                    oldHash.AppendData(previous.AsSpan(0, count));
                    WriteRange(oldRead, previous.AsSpan(0, count), []); oldRead += count;
                }
                if (position != file.Size || !nextHash.GetHashAndReset().AsSpan().SequenceEqual(file.Sha1))
                    throw new InvalidOperationException("L’intégrité du fichier cible du delta est invalide.");
                header = header with { OriginalSha1 = Convert.ToHexString(oldHash.GetHashAndReset()) };
                var finalJson = JsonSerializer.SerializeToUtf8Bytes(header);
                if (finalJson.Length != json.Length) throw new InvalidDataException("En-tête du delta invalide.");
                output.Position = Magic.Length + sizeof(int); writer.Write(finalJson); writer.Write(ranges); writer.Flush();
                output.Position = 0;
                var digest = await SHA256.HashDataAsync(output, token).ConfigureAwait(false);
                await output.WriteAsync(digest, token).ConfigureAwait(false);
                await output.FlushAsync(token).ConfigureAwait(false); output.Flush(flushToDisk: true);

                void WriteRange(long offset, ReadOnlySpan<byte> old, ReadOnlySpan<byte> next)
                {
                    var prefix = 0; var common = Math.Min(old.Length, next.Length);
                    while (prefix < common && old[prefix] == next[prefix]) prefix++;
                    old = old[prefix..]; next = next[prefix..]; offset += prefix;
                    var suffix = 0;
                    while (old.Length == next.Length && suffix < old.Length && old[^(suffix + 1)] == next[^(suffix + 1)]) suffix++;
                    if (suffix > 0) { old = old[..^suffix]; next = next[..^suffix]; }
                    if (old.IsEmpty && next.IsEmpty) return;
                    if (old.Length != next.Length) { Emit(offset, old, next); return; }
                    // Do not store unchanged interiors merely because both ends of a block changed.
                    // Merge tiny equal gaps only when cheaper than another 16-byte range header.
                    var start = 0;
                    while (start < old.Length)
                    {
                        while (start < old.Length && old[start] == next[start]) start++;
                        if (start == old.Length) break;
                        var end = start + 1; var cursor = end;
                        while (cursor < old.Length)
                        {
                            if (old[cursor] != next[cursor]) end = cursor + 1;
                            else if (cursor - end >= 8) break;
                            cursor++;
                        }
                        Emit(offset + start, old[start..end], next[start..end]); start = cursor;
                    }
                }

                void Emit(long offset, ReadOnlySpan<byte> old, ReadOnlySpan<byte> next)
                {
                    Charge(16 + old.Length + next.Length);
                    writer.Write(offset); writer.Write(old.Length); writer.Write(next.Length); writer.Write(old); writer.Write(next);
                    ranges = checked(ranges + 1);
                }
            }
            token.ThrowIfCancellationRequested();
            EpicPatchFileSafety.PublishJournal(temporary, patchPath);
            return Read(patchPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static Info Read(string patchPath)
    {
        using var input = OpenPatch(patchPath);
        return ReadInfo(input, patchPath);
    }

    private static FileStream OpenPatch(string path)
    {
        OfficialEpicGamesLauncherEngineBackend.RejectPatchSourceLinks(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024);
    }

    private static Info ReadInfo(FileStream input, string path)
    {
        if (input.Length < 64) throw new InvalidDataException("Delta incomplet.");
        var payloadLength = input.Length - 32;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024]; long remaining = payloadLength;
        while (remaining > 0) { var count = (int)Math.Min(buffer.Length, remaining); input.ReadExactly(buffer.AsSpan(0, count)); hash.AppendData(buffer.AsSpan(0, count)); remaining -= count; }
        var expected = new byte[32]; input.ReadExactly(expected);
        if (!hash.GetHashAndReset().AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Empreinte du delta invalide.");
        input.Position = 0; using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length)) != Magic) throw new InvalidDataException("Format du delta invalide.");
        var headerLength = reader.ReadInt32();
        if (headerLength is < 2 or > 65536) throw new InvalidDataException("En-tête du delta trop volumineux.");
        var header = JsonSerializer.Deserialize<Header>(reader.ReadBytes(headerLength)) ?? throw new InvalidDataException("En-tête absent.");
        if (header.OriginalLength < 0 || header.TargetLength < 0 || string.IsNullOrEmpty(header.Path) ||
            header.OriginalSha1 is not { Length: 40 } || header.TargetSha1 is not { Length: 40 } ||
            !header.OriginalSha1.All(Uri.IsHexDigit) || !header.TargetSha1.All(Uri.IsHexDigit) ||
            header.OriginalWriteTicks < 0 || header.OriginalWriteTicks > DateTime.MaxValue.Ticks)
            throw new InvalidDataException("Métadonnées du delta invalides.");
        var countRanges = reader.ReadInt32(); var dataPosition = input.Position;
        if (countRanges is < 0 or > 10_000_000 || countRanges > (payloadLength - dataPosition) / 16) throw new InvalidDataException("Nombre de plages invalide.");
        long oldBytes = 0; long newBytes = 0; long end = 0;
        for (var i = 0; i < countRanges; i++)
        {
            var offset = reader.ReadInt64(); var oldCount = reader.ReadInt32(); var newCount = reader.ReadInt32();
            if (offset < end || oldCount is < 0 or > BlockSize || newCount is < 0 or > BlockSize || oldCount + newCount == 0 ||
                (oldCount > 0 && offset > header.OriginalLength - oldCount) || (newCount > 0 && offset > header.TargetLength - newCount) ||
                input.Position > payloadLength - oldCount - newCount) throw new InvalidDataException("Plages du delta invalides.");
            end = checked(offset + Math.Max(oldCount, newCount));
            input.Position += oldCount + newCount; oldBytes += oldCount; newBytes += newCount;
        }
        if (input.Position != payloadLength) throw new InvalidDataException("Taille du delta invalide.");
        return new(path, header, dataPosition, countRanges, newBytes, oldBytes, input.Length, Convert.ToHexString(expected));
    }

    internal static bool MatchesOriginal(Info info, FileStream? target)
    {
        if (!info.Header.Existed) return target is null;
        if (target is null || target.Length != info.Header.OriginalLength) return false;
        target.Position = 0;
        return Convert.ToHexString(SHA1.HashData(target)) == info.Header.OriginalSha1;
    }

    internal static void Apply(Info info, FileStream target, CancellationToken token, Action? beforeWrite = null, Action? afterRange = null)
    {
        EpicPatchFileSafety.RequireSingleLink(target);
        using var input = OpenPatch(info.PatchPath);
        var actual = ReadInfo(input, info.PatchPath);
        if (actual.Header != info.Header || actual.Digest != info.Digest) throw new InvalidDataException("Le delta a changé après sa vérification.");
        if (info.Header.Existed ? !MatchesOriginal(info, target) : target.Length != 0)
            throw new IOException("Le fichier d’origine a changé depuis la préparation.");
        // Check undo bytes as well, before the first installed byte is written.
        var undoBuffer = new byte[BlockSize];
        Visit(input, actual, (offset, old, _) =>
        {
            if (old.IsEmpty) return;
            var bytes = undoBuffer.AsSpan(0, old.Length); target.Position = offset; target.ReadExactly(bytes);
            if (!old.SequenceEqual(bytes)) throw new IOException("Les octets d’origine ont changé depuis la préparation.");
        }, token);
        beforeWrite?.Invoke();
        Visit(input, actual, (offset, _, next) =>
        {
            if (!next.IsEmpty) { target.Position = offset; target.Write(next); }
            afterRange?.Invoke();
        }, token);
        target.SetLength(info.Header.TargetLength); target.Flush(flushToDisk: true); target.Position = 0;
        if (Convert.ToHexString(SHA1.HashData(target)) != info.Header.TargetSha1) throw new IOException("Fichier corrigé non conforme au manifest Epic.");
    }

    internal static void Restore(string patchPath, string targetPath, string expectedRelativePath, bool? expectedExisted = null)
    {
        using var input = OpenPatch(patchPath);
        var info = ReadInfo(input, patchPath);
        if (info.Header.Path != expectedRelativePath) throw new InvalidDataException("Le journal et le delta désignent des fichiers différents.");
        if (expectedExisted is { } existed && existed != info.Header.Existed) throw new InvalidDataException("Le journal et le delta ne correspondent pas.");
        OfficialEpicGamesLauncherEngineBackend.RejectPatchSourceLinks(targetPath);
        if (!info.Header.Existed) { if (File.Exists(targetPath)) File.Delete(targetPath); return; }
        using (var target = new FileStream(targetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            EpicPatchFileSafety.RequireSingleLink(target);
            Visit(input, info, (offset, old, _) => { if (!old.IsEmpty) { target.Position = offset; target.Write(old); } }, CancellationToken.None);
            target.SetLength(info.Header.OriginalLength); target.Flush(flushToDisk: true); target.Position = 0;
            if (Convert.ToHexString(SHA1.HashData(target)) != info.Header.OriginalSha1) throw new IOException("Restauration du delta incomplète ; journal conservé.");
        }
        File.SetLastWriteTimeUtc(targetPath, new DateTime(info.Header.OriginalWriteTicks, DateTimeKind.Utc));
    }

    private static void Visit(FileStream input, Info info, RangeAction action, CancellationToken token)
    {
        input.Position = info.DataPosition;
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        var buffer = new byte[BlockSize * 2];
        for (var i = 0; i < info.Ranges; i++)
        {
            token.ThrowIfCancellationRequested();
            var offset = reader.ReadInt64(); var oldCount = reader.ReadInt32(); var newCount = reader.ReadInt32();
            input.ReadExactly(buffer.AsSpan(0, oldCount)); input.ReadExactly(buffer.AsSpan(BlockSize, newCount));
            action(offset, buffer.AsSpan(0, oldCount), buffer.AsSpan(BlockSize, newCount));
        }
    }
}
