using CyEngineHub.Core.Processes;
using CyEngineHub.Git.Execution;
using CyEngineHub.Git.Parsing;

namespace CyEngineHub.Git.Analysis;

public sealed record EngineGitStatus(string RepositoryRoot, GitParsedStatus Status, bool RemoteChecked, DateTimeOffset CheckedAt);

/// <summary>Inspects an engine's own repository, never its parent's. Fetch does not merge or check out files.</summary>
public sealed class EngineGitStatusService(IProcessRunner runner)
{
    public async Task<EngineGitStatus?> InspectAsync(string engineRoot, bool fetch, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var inspector = new GitRepositoryInspector(runner);
        var root = await inspector.FindRepositoryRootAsync(engineRoot, token).ConfigureAwait(false);
        if (root is null || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(engineRoot)).Equals(root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;
        var status = await ReadStatus(root, token).ConfigureAwait(false);
        var checkedRemote = false;
        if (fetch && status.Branch != "(detached)" && !string.IsNullOrWhiteSpace(status.Upstream))
        {
            var remoteResult = await runner.RunAsync(GitCommandFactory.CreateRead(root, "for-each-ref",
                "--format=%(upstream:remotename)", "refs/heads/" + status.Branch), cancellationToken: token).ConfigureAwait(false);
            var remote = remoteResult.CapturedOutput.FirstOrDefault(line => line.Channel == ProcessOutputChannel.StandardOutput)?.Text.Trim();
            if (!remoteResult.Succeeded || string.IsNullOrEmpty(remote) || remote == ".")
                throw new InvalidOperationException("La branche n’a pas de suivi distant vérifiable.");
            var result = await runner.RunAsync(GitCommandFactory.CreateStatusFetch(root, remote), cancellationToken: token).ConfigureAwait(false);
            if (!result.Succeeded)
                throw new InvalidOperationException("Vérification distante impossible (réseau ou authentification Git). Les sources sont inchangées ; l’état local ne prouve pas que le moteur est à jour.");
            checkedRemote = true;
            status = await ReadStatus(root, token).ConfigureAwait(false);
        }
        return new(root, status, checkedRemote, DateTimeOffset.Now);
    }

    private async Task<GitParsedStatus> ReadStatus(string root, CancellationToken token)
    {
        var parser = new GitStatusParser();
        var progress = new StatusProgress(parser);
        var result = await runner.RunAsync(GitCommandFactory.CreateRead(root, "status", "--porcelain=v2", "--branch", "--untracked-files=no"),
            progress, token).ConfigureAwait(false);
        if (!result.Succeeded) throw new InvalidOperationException("Impossible de lire l’état Git du moteur (droits ou dépôt non approuvé par Git).");
        return parser.Build();
    }
    private sealed class StatusProgress(GitStatusParser parser) : IProgress<ProcessOutputLine>
    {
        public void Report(ProcessOutputLine line) { if (line.Channel == ProcessOutputChannel.StandardOutput) parser.Accept(line.Text); }
    }
}
