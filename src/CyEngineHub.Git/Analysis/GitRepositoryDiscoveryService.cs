using CyEngineHub.Git.Models;

namespace CyEngineHub.Git.Analysis;

public sealed record GitDiscoveryProgress(int Completed, int Total, string Message);

public sealed class GitRepositoryDiscoveryService
{
    private readonly GitRepositoryInspector _inspector;
    private readonly StringComparer _pathComparer;

    public GitRepositoryDiscoveryService(GitRepositoryInspector inspector)
    {
        _inspector = inspector;
        _pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
    }

    public async Task<IReadOnlyList<GitRepositorySnapshot>> RefreshAsync(
        IEnumerable<GitRepositoryCandidate> candidates,
        IProgress<GitDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var candidateArray = candidates.ToArray();
        var ownersByRoot = new Dictionary<string, List<GitRepositoryOwner>>(_pathComparer);
        for (var index = 0; index < candidateArray.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = candidateArray[index];
            progress?.Report(new GitDiscoveryProgress(
                index,
                candidateArray.Length,
                $"Détection · {candidate.DisplayName}"));
            var root = await _inspector.FindRepositoryRootAsync(candidate.RootPath, cancellationToken)
                .ConfigureAwait(false);
            if (root is null)
            {
                continue;
            }

            if (!ownersByRoot.TryGetValue(root, out var owners))
            {
                owners = [];
                ownersByRoot.Add(root, owners);
            }

            if (!owners.Any(owner =>
                    owner.Kind == candidate.Kind &&
                    string.Equals(owner.OwnerId, candidate.OwnerId, StringComparison.Ordinal)))
            {
                owners.Add(new GitRepositoryOwner(
                    candidate.DisplayName,
                    candidate.RootPath,
                    candidate.Kind,
                    candidate.OwnerId));
            }
        }

        var snapshots = new List<GitRepositorySnapshot>();
        var repositoryIndex = 0;
        foreach (var pair in ownersByRoot.OrderBy(item => item.Key, _pathComparer))
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new GitDiscoveryProgress(
                repositoryIndex,
                ownersByRoot.Count,
                $"Status · {Path.GetFileName(pair.Key)}"));
            try
            {
                snapshots.Add(await _inspector.InspectAsync(
                        pair.Key,
                        pair.Value,
                        cancellationToken)
                    .ConfigureAwait(false));
            }
            catch (GitOperationException exception)
            {
                snapshots.Add(new GitRepositorySnapshot(
                    pair.Key,
                    pair.Value,
                    "(erreur)",
                    string.Empty,
                    null,
                    0,
                    0,
                    new GitWorkingTreeSummary(0, 0, 0, 0),
                    [],
                    new GitLfsSnapshot(false, 0, 0, [], false),
                    DateTimeOffset.UtcNow,
                    TimeSpan.Zero,
                    exception.Message));
            }

            repositoryIndex++;
        }

        progress?.Report(new GitDiscoveryProgress(
            ownersByRoot.Count,
            ownersByRoot.Count,
            "Analyse Git terminée"));
        return snapshots;
    }
}
