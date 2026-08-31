using CyEngineHub.Core.Processes;
using CyEngineHub.Git.Execution;
using CyEngineHub.Git.Models;
using CyEngineHub.Git.Parsing;

namespace CyEngineHub.Git.Analysis;

public sealed class GitRepositoryInspector
{
    private readonly IProcessRunner _processRunner;
    private readonly GitLfsStorageAnalyzer _lfsStorageAnalyzer;

    public GitRepositoryInspector(
        IProcessRunner processRunner,
        GitLfsStorageAnalyzer? lfsStorageAnalyzer = null)
    {
        _processRunner = processRunner;
        _lfsStorageAnalyzer = lfsStorageAnalyzer ?? new GitLfsStorageAnalyzer();
    }

    public async Task<string?> FindRepositoryRootAsync(
        string candidatePath,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(candidatePath))
        {
            return null;
        }

        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                    GitCommandFactory.CreateRead(
                        candidatePath,
                        "rev-parse",
                        "--path-format=absolute",
                        "--show-toplevel"),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ProcessOperationException exception)
        {
            throw new GitOperationException(
                "Git est introuvable ou n’a pas pu être démarré.",
                "rev-parse",
                candidatePath,
                innerException: exception);
        }

        if (!result.Succeeded)
        {
            return null;
        }

        var root = result.CapturedOutput
            .LastOrDefault(line => line.Channel == ProcessOutputChannel.StandardOutput)
            ?.Text.Trim();
        return string.IsNullOrWhiteSpace(root)
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }

    public async Task<GitRepositorySnapshot> InspectAsync(
        string repositoryRoot,
        IReadOnlyList<GitRepositoryOwner> owners,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var parser = new GitStatusParser();
        var statusProgress = new InlineProgress<ProcessOutputLine>(line =>
        {
            if (line.Channel == ProcessOutputChannel.StandardOutput)
            {
                parser.Accept(line.Text);
            }
        });
        var statusResult = await _processRunner.RunAsync(
                GitCommandFactory.CreateRead(
                    repositoryRoot,
                    "status",
                    "--porcelain=v2",
                    "--branch",
                    "--untracked-files=normal"),
                statusProgress,
                cancellationToken)
            .ConfigureAwait(false);
        if (!statusResult.Succeeded)
        {
            throw CreateFailure("status", repositoryRoot, statusResult);
        }

        var parsed = parser.Build();
        var remoteResult = await _processRunner.RunAsync(
                GitCommandFactory.CreateRead(repositoryRoot, "remote", "-v"),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var remotes = remoteResult.Succeeded
            ? GitRemoteParser.Parse(remoteResult.CapturedOutput
                .Where(line => line.Channel == ProcessOutputChannel.StandardOutput)
                .Select(line => line.Text))
            : [];

        var lfs = await InspectLfsAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        return new GitRepositorySnapshot(
            repositoryRoot,
            owners,
            parsed.Branch,
            parsed.HeadCommit,
            parsed.Upstream,
            parsed.Ahead,
            parsed.Behind,
            parsed.WorkingTree,
            remotes,
            lfs,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow - startedAt);
    }

    private async Task<GitLfsSnapshot> InspectLfsAsync(
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var trackResult = await _processRunner.RunAsync(
                GitCommandFactory.CreateRead(repositoryRoot, "lfs", "track"),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!trackResult.Succeeded)
        {
            return new GitLfsSnapshot(false, 0, 0, [], false);
        }

        var patterns = ParseLfsPatterns(trackResult.CapturedOutput
            .Where(line => line.Channel == ProcessOutputChannel.StandardOutput)
            .Select(line => line.Text));
        var commonDirectoryResult = await _processRunner.RunAsync(
                GitCommandFactory.CreateRead(
                    repositoryRoot,
                    "rev-parse",
                    "--path-format=absolute",
                    "--git-common-dir"),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!commonDirectoryResult.Succeeded)
        {
            return new GitLfsSnapshot(
                true,
                0,
                0,
                patterns,
                false,
                "Le dossier Git commun n’a pas pu être résolu.");
        }

        var commonDirectory = commonDirectoryResult.CapturedOutput
            .LastOrDefault(line => line.Channel == ProcessOutputChannel.StandardOutput)
            ?.Text.Trim();
        if (string.IsNullOrWhiteSpace(commonDirectory))
        {
            return new GitLfsSnapshot(true, 0, 0, patterns, false);
        }

        return _lfsStorageAnalyzer.Analyze(
            Path.GetFullPath(commonDirectory, repositoryRoot),
            isInstalled: true,
            patterns,
            cancellationToken);
    }

    private static List<string> ParseLfsPatterns(IEnumerable<string> lines)
    {
        var results = new List<string>();
        var readingPatterns = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("Listing tracked patterns", StringComparison.Ordinal))
            {
                readingPatterns = true;
                continue;
            }

            if (line.StartsWith("Listing excluded patterns", StringComparison.Ordinal))
            {
                break;
            }

            if (!readingPatterns)
            {
                continue;
            }

            var value = line.Trim();
            var metadata = value.LastIndexOf(" (", StringComparison.Ordinal);
            if (metadata > 0)
            {
                value = value[..metadata];
            }

            if (value.Length > 0 && results.Count < 200)
            {
                results.Add(value);
            }
        }

        return results;
    }

    private static GitOperationException CreateFailure(
        string operation,
        string repositoryRoot,
        ProcessResult result)
    {
        var message = result.CapturedOutput
            .LastOrDefault(line => line.Channel == ProcessOutputChannel.StandardError)
            ?.Text;
        return new GitOperationException(
            message ?? $"git {operation} a échoué avec le code {result.ExitCode}.",
            operation,
            repositoryRoot,
            result.ExitCode);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
