using CyEngineHub.Core.Processes;
using CyEngineHub.Git.Analysis;
using CyEngineHub.Git.Models;
using CyEngineHub.Infrastructure.Processes;

namespace CyEngineHub.Git.Tests;

public sealed class GitRepositoryInspectorIntegrationTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Dépôt Git é {Guid.NewGuid():N}");

    [Fact]
    public async Task InspectorReadsARealTemporaryRepositoryWithoutNetworkAccess()
    {
        Directory.CreateDirectory(_testDirectory);
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("config", "user.name", "CyEngineHub Tests");
        await RunGitAsync("config", "user.email", "tests@cyenginehub.local");
        await RunGitAsync("config", "commit.gpgsign", "false");
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "Tracked.txt"), "initial");
        await RunGitAsync("add", "Tracked.txt");
        await RunGitAsync("commit", "-m", "Initial");
        await RunGitAsync(
            "remote",
            "add",
            "origin",
            "https://token:secret@example.com/team/repo.git");
        await File.AppendAllTextAsync(Path.Combine(_testDirectory, "Tracked.txt"), "\nmodified");
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "Untracked.txt"), "new");
        var nested = Path.Combine(_testDirectory, "Nested Project");
        Directory.CreateDirectory(nested);

        var inspector = new GitRepositoryInspector(new SystemProcessRunner());
        var root = await inspector.FindRepositoryRootAsync(nested);
        var snapshot = await inspector.InspectAsync(
            root!,
            [new GitRepositoryOwner("Test Project", nested, GitRepositoryOwnerKind.Project, "P1")]);

        Assert.Equal(Path.GetFullPath(_testDirectory), root);
        Assert.Equal("main", snapshot.Branch);
        Assert.NotEmpty(snapshot.HeadCommit);
        Assert.Equal(1, snapshot.WorkingTree.Modified);
        Assert.True(snapshot.WorkingTree.Untracked >= 1);
        var origin = Assert.Single(snapshot.Remotes, remote => remote.Name == "origin");
        Assert.DoesNotContain("secret", origin.Url, StringComparison.Ordinal);
        Assert.Null(snapshot.Error);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            foreach (var filePath in Directory.EnumerateFiles(
                         _testDirectory,
                         "*",
                         SearchOption.AllDirectories))
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private async Task RunGitAsync(params string[] arguments)
    {
        var result = await new SystemProcessRunner().RunAsync(new ProcessRequest
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = _testDirectory,
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["LC_ALL"] = "C",
            },
        });
        Assert.True(
            result.Succeeded,
            string.Join(Environment.NewLine, result.CapturedOutput.Select(line => line.Text)));
    }
}
