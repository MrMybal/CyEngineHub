using CyEngineHub.Git.Execution;

namespace CyEngineHub.Git.Tests;

public sealed class GitCommandFactoryTests
{
    [Fact]
    public void FetchUsesStructuredArgumentsAndDisablesPrompts()
    {
        var request = GitCommandFactory.CreateFetch(Path.GetTempPath(), "upstream");

        Assert.Contains("fetch", request.Arguments);
        Assert.Contains("--prune", request.Arguments);
        Assert.Contains("upstream", request.Arguments);
        Assert.Equal("0", request.EnvironmentVariables["GIT_TERMINAL_PROMPT"]);
        Assert.Null(request.RawArguments);
    }

    [Theory]
    [InlineData("--upload-pack=evil")]
    [InlineData("remote with spaces")]
    [InlineData("")]
    public void FetchRejectsUnsafeRemoteNames(string remoteName)
    {
        Assert.Throws<ArgumentException>(() =>
            GitCommandFactory.CreateFetch(Path.GetTempPath(), remoteName));
        Assert.Throws<ArgumentException>(() =>
            GitCommandFactory.CreateStatusFetch(Path.GetTempPath(), remoteName));
    }

    [Fact]
    public void UpdateCheckOnlyFetchesAndDoesNotPruneOrRecurseIntoSubmodules()
    {
        var request = GitCommandFactory.CreateStatusFetch(Path.GetTempPath(), "company");
        Assert.Contains("fetch", request.Arguments); Assert.Contains("--no-recurse-submodules", request.Arguments);
        Assert.DoesNotContain("--prune", request.Arguments); Assert.DoesNotContain("pull", request.Arguments);
        Assert.Equal("company", request.Arguments[^1]); Assert.Equal("Never", request.EnvironmentVariables["GCM_INTERACTIVE"]);
    }
}
