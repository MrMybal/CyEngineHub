using System.Buffers.Binary;
using System.IO.Compression;

namespace CyEngineHub.Core.Ai;

public sealed record HubPackFile(string Path, long Bytes);
public sealed record HubPackInspection(IReadOnlyList<HubPackFile> Files, int Matched, int Scanned, bool Truncated, string[] Warnings);

public static class HubPackInspector
{
    public static HubPackInspection Search(IEnumerable<string> registeredPaths, string query, int offset, int limit, int scanLimit = 50_000, int registeredPathsLimit = 100, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        ArgumentOutOfRangeException.ThrowIfNegative(scanLimit);
        ArgumentOutOfRangeException.ThrowIfNegative(registeredPathsLimit);
        scanLimit = HubMcpOptions.Bound(scanLimit);
        var files = new List<HubPackFile>();
        var warnings = new List<string>();
        var scanned = 0;
        var matched = 0;
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        void Add(string name, long bytes)
        {
            if (terms.All(term => name.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                if (matched >= offset && files.Count < limit) files.Add(new(name, bytes));
                matched++;
            }
        }
        foreach (var registered in registeredPaths.Distinct(StringComparer.OrdinalIgnoreCase).Take(HubMcpOptions.Bound(registeredPathsLimit)))
        {
            token.ThrowIfCancellationRequested();
            var root = Path.GetFullPath(registered);
            try
            {
                EnsureNoLinks(root);
                var pending = new Stack<string>();
                pending.Push(root);
                while (pending.Count > 0 && scanned < scanLimit)
                {
                    token.ThrowIfCancellationRequested();
                    var path = pending.Pop();
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                    if (Directory.Exists(path))
                    {
                        foreach (var child in Directory.EnumerateFileSystemEntries(path))
                        {
                            token.ThrowIfCancellationRequested();
                            if (++scanned >= scanLimit) break;
                            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                        }
                    }
                    else if (File.Exists(path))
                    {
                        var relative = File.Exists(root) ? Path.GetFileName(root) : Path.GetRelativePath(root, path);
                        if (Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            using var stream = File.OpenRead(path);
                            ValidateZipDirectorySize(stream);
                            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                            foreach (var entry in archive.Entries)
                            {
                                token.ThrowIfCancellationRequested();
                                if (++scanned >= scanLimit) break;
                                var name = entry.FullName.Replace('\\', '/');
                                if (name.EndsWith('/') || name.StartsWith('/') || name.Contains(':') || name.Split('/').Contains("..")) continue;
                                Add(relative + "!/" + name, entry.Length);
                            }
                        }
                        else Add(relative, new FileInfo(path).Length);
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                warnings.Add("A registered pack could not be inspected (missing, linked, damaged or ZIP directory exceeds safe limits).");
            }
            if (scanned >= scanLimit) break;
        }
        return new(files, matched, scanned, scanned >= scanLimit, warnings.Distinct().ToArray());
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Linked paths are not inspected.");
    }

    private static void ValidateZipDirectorySize(FileStream stream)
    {
        // Bound allocations BEFORE ZipArchive materializes the central directory. No decompression.
        var tail = new byte[(int)Math.Min(stream.Length, 65_557)];
        stream.Position = stream.Length - tail.Length;
        stream.ReadExactly(tail);
        for (var i = tail.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) != 0x06054b50) continue;
            if (i + 22 + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) != tail.Length) continue;
            var count = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 10));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i + 12));
            if (count == ushort.MaxValue || size > 16 * 1024 * 1024) throw new IOException("ZIP directory too large or ZIP64 directory unsupported.");
            stream.Position = 0;
            return;
        }
        throw new IOException("Invalid ZIP directory.");
    }
}
