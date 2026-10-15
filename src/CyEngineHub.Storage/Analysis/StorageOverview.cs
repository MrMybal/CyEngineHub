using CyEngineHub.Storage.Models;

namespace CyEngineHub.Storage.Analysis;

public static class StorageOverview
{
    // Parent footprints already contain their descendants. Category totals are
    // independent and must not be added to the disk's used bytes.
    public static long SumDistinctRoots(IEnumerable<StorageEntry> entries)
    {
        var roots = new List<string>();
        long total = 0;
        foreach (var entry in entries.OrderBy(entry => entry.Path.Length))
        {
            var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(entry.Path));
            if (roots.Any(root => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))) continue;
            roots.Add(path);
            total += Math.Max(0, entry.SizeBytes);
        }
        return total;
    }
}
