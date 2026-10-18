using CyEngineHub.Core.Processes;
using CyEngineHub.Git.Analysis;
using CyEngineHub.Git.Execution;
using CyEngineHub.Infrastructure.Processes;

namespace CyEngineHub.Git.Tests;

public sealed class EngineGitStatusServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyEngineHub.Tests", "EngineGit-" + Guid.NewGuid().ToString("N"));
    private readonly SystemProcessRunner _runner = new();
    private async Task Git(string directory, params string[] args)
    {
        Directory.CreateDirectory(directory);
        var result = await _runner.RunAsync(GitCommandFactory.CreateRead(directory, args));
        Assert.True(result.Succeeded, string.Join("\n", result.CapturedOutput.Select(line => line.Text)));
    }
    [Fact]
    public async Task FetchDetectsUpdatesAndDivergenceWithoutChangingSources()
    {
        var remote = Path.Combine(_root, "Remote.git"); var engine = Path.Combine(_root, "Engine"); var peer = Path.Combine(_root, "Peer");
        await Git(remote, "init", "--bare", "--initial-branch=source-5.5");
        await Git(_root, "clone", remote, engine);
        await Git(engine, "config", "user.name", "QA"); await Git(engine, "config", "user.email", "qa@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(engine, "Asset.txt"), "original");
        await Git(engine, "add", "."); await Git(engine, "commit", "-m", "initial"); await Git(engine, "push", "-u", "origin", "source-5.5");
        await Git(engine, "remote", "rename", "origin", "company");
        await Git(_root, "clone", remote, peer);
        await Git(peer, "config", "user.name", "QA"); await Git(peer, "config", "user.email", "qa@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(peer, "Remote.txt"), "update");
        await Git(peer, "add", "."); await Git(peer, "commit", "-m", "remote update"); await Git(peer, "push");
        await File.WriteAllTextAsync(Path.Combine(engine, "Asset.txt"), "local edits");
        var service = new EngineGitStatusService(_runner);
        var local = (await service.InspectAsync(engine, false))!;
        Assert.Equal("source-5.5", local.Status.Branch); Assert.False(local.RemoteChecked); Assert.Equal(0, local.Status.Behind);
        var updated = (await service.InspectAsync(engine, true))!;
        Assert.True(updated.RemoteChecked); Assert.Equal(1, updated.Status.Behind); Assert.Equal(local.Status.HeadCommit, updated.Status.HeadCommit);
        Assert.Equal("company/source-5.5", updated.Status.Upstream);
        Assert.True(updated.Status.WorkingTree.IsDirty);
        Assert.Equal("local edits", await File.ReadAllTextAsync(Path.Combine(engine, "Asset.txt")));
        Assert.False(File.Exists(Path.Combine(engine, "Remote.txt")));
        await Git(engine, "add", "."); await Git(engine, "commit", "-m", "local update");
        var diverged = (await service.InspectAsync(engine, true))!;
        Assert.Equal(1, diverged.Status.Behind); Assert.Equal(1, diverged.Status.Ahead);
        await Git(engine, "remote", "set-url", "company", Path.Combine(_root, "Unavailable.git"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.InspectAsync(engine, true));
        Assert.Equal(diverged.Status.HeadCommit, (await service.InspectAsync(engine, false))!.Status.HeadCommit);
    }
    [Fact]
    public async Task ParentRepositoryIsNotMistakenForAnEngineAndDetachedHasNoRemoteStatus()
    {
        await Git(_root, "init", "--initial-branch=source");
        await Git(_root, "config", "user.name", "QA"); await Git(_root, "config", "user.email", "qa@example.invalid");
        await Git(_root, "commit", "--allow-empty", "-m", "initial");
        var service = new EngineGitStatusService(_runner);
        var nested = Path.Combine(_root, "NotARepository"); Directory.CreateDirectory(nested);
        Assert.Null(await service.InspectAsync(nested, false));
        var untracked = (await service.InspectAsync(_root, true))!;
        Assert.Null(untracked.Status.Upstream); Assert.False(untracked.RemoteChecked);
        await Git(_root, "checkout", "--detach");
        var detached = (await service.InspectAsync(_root, true))!;
        Assert.Equal("(detached)", detached.Status.Branch); Assert.False(detached.RemoteChecked);
    }
    public void Dispose()
    {
        if (!Directory.Exists(_root)) return;
        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(_root, true);
    }
}
