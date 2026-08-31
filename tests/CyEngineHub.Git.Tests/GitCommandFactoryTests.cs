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
    }
}
