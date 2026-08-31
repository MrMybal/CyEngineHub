namespace CyEngineHub.Fab.Library;

public enum FabLocalContentPackHealth
{
    Ready,
    Warning,
    Partial,
}

public enum FabLocalContentKind
{
    Unreal,
    Unity,
    Godot,
    CryEngine,
    Generic,
    EpicVault,
}

public sealed record FabLocalContentPack(
    string Id,
    string DisplayName,
    string PackageRoot,
    string ContentDirectory,
    string? ProjectPath,
    string? EngineAssociation,
    int AssetCount,
    int MapCount,
    int SupportedFileCount,
    long SupportedBytes,
    DateTimeOffset LastModifiedAt,
    FabLocalContentPackHealth Health,
    IReadOnlyList<string> Issues,
    DateTimeOffset DiscoveredAt,
    FabLocalContentKind Kind = FabLocalContentKind.Unreal,
    int BlockedFileCount = 0);

public sealed record FabLocalLibraryRequest(
    IReadOnlyList<string> Roots,
    int MaximumDepth = 6,
    int MaximumDirectoriesPerRoot = 50_000,
    int MaximumPacks = 25_000,
    int MaximumFilesPerPack = 100_000,
    long MaximumBytesPerPack = 100L * 1024 * 1024 * 1024);

public sealed record FabLocalLibraryProgress(int Completed, int Total, string Message);

public sealed class FabLocalLibraryException : Exception
{
    public FabLocalLibraryException(string message, string? path = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Path = path;
    }

    public string? Path { get; }
}
