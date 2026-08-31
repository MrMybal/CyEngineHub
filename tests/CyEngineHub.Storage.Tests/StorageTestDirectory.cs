namespace CyEngineHub.Storage.Tests;

internal sealed class StorageTestDirectory : IDisposable
{
    public StorageTestDirectory()
    {
        Root = Path.Combine(Path.GetTempPath(), $"CyEngineHub Storage {Guid.NewGuid():N}");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string CreateDirectory(params string[] segments)
    {
        var path = Combine(segments);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string relativePath, int size)
    {
        var path = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, true);
        }
    }

    private string Combine(IReadOnlyList<string> segments)
    {
        var path = Root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }
}
