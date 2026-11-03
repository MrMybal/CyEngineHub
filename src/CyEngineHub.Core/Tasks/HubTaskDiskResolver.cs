namespace CyEngineHub.Core.Tasks;

public sealed record HubTaskDisk(string Id, string Label, int MaximumConcurrency);

public interface IHubTaskDiskResolver
{
    IReadOnlyList<HubTaskDisk> Resolve(string path);
}

/// <summary>Physical-disk I/O budget where Windows exposes it; conservative volume/share fallback.</summary>
public sealed class HubTaskDiskResolver : IHubTaskDiskResolver
{
    public IReadOnlyList<HubTaskDisk> Resolve(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path))!;
        var network = root.StartsWith(@"\\", StringComparison.Ordinal);
        if (!network && OperatingSystem.IsWindows())
        {
            var physical = WindowsTaskDisks.Resolve(path);
            if (physical.Count > 0) return physical;
        }
        return [new(root.ToUpperInvariant(), root, network ? 1 : 2)];
    }
}
