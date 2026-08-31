namespace CyEngineHub.Git.Models;

public enum GitRepositoryOwnerKind
{
    Engine,
    Project,
    Plugin,
}

public sealed record GitRepositoryCandidate(
    string DisplayName,
    string RootPath,
    GitRepositoryOwnerKind Kind,
    string OwnerId);

public sealed record GitRepositoryOwner(
    string DisplayName,
    string SourcePath,
    GitRepositoryOwnerKind Kind,
    string OwnerId);

public sealed record GitRemote(string Name, string Url);

public sealed record GitWorkingTreeSummary(
    int Staged,
    int Modified,
    int Untracked,
    int Conflicted)
{
    public bool IsDirty => Staged > 0 || Modified > 0 || Untracked > 0 || Conflicted > 0;
}

public sealed record GitLfsSnapshot(
    bool IsInstalled,
    long ObjectCount,
    long StorageBytes,
    IReadOnlyList<string> TrackedPatterns,
    bool IsTruncated,
    string? Error = null);

public sealed record GitRepositorySnapshot(
    string RepositoryRoot,
    IReadOnlyList<GitRepositoryOwner> Owners,
    string Branch,
    string HeadCommit,
    string? Upstream,
    int Ahead,
    int Behind,
    GitWorkingTreeSummary WorkingTree,
    IReadOnlyList<GitRemote> Remotes,
    GitLfsSnapshot Lfs,
    DateTimeOffset RefreshedAt,
    TimeSpan Duration,
    string? Error = null);

public sealed class GitOperationException : Exception
{
    public GitOperationException(
        string message,
        string operation,
        string repositoryRoot,
        int? exitCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        RepositoryRoot = repositoryRoot;
        ExitCode = exitCode;
    }

    public string Operation { get; }

    public string RepositoryRoot { get; }

    public int? ExitCode { get; }
}
