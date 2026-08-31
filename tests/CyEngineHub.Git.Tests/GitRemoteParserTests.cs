using CyEngineHub.Git.Parsing;

namespace CyEngineHub.Git.Tests;

public sealed class GitRemoteParserTests
{
    [Fact]
    public void KeepsFetchRemotesAndRemovesEmbeddedCredentials()
    {
        var remotes = GitRemoteParser.Parse([
            "origin\thttps://token:secret@example.com/team/repo.git (fetch)",
            "origin\thttps://token:secret@example.com/team/repo.git (push)",
            "upstream\tgit@github.com:EpicGames/UnrealEngine.git (fetch)",
            "mirror\thttps://example.net/repo.git?access_token=hidden (fetch)",
        ]);

        Assert.Equal(3, remotes.Count);
        var origin = Assert.Single(remotes, remote => remote.Name == "origin");
        Assert.DoesNotContain("token", origin.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", origin.Url, StringComparison.Ordinal);
        Assert.Contains("example.com/team/repo.git", origin.Url, StringComparison.Ordinal);
        Assert.Contains(remotes, remote =>
            remote.Name == "upstream" &&
            remote.Url == "git@github.com:EpicGames/UnrealEngine.git");
        var mirror = Assert.Single(remotes, remote => remote.Name == "mirror");
        Assert.DoesNotContain("access_token", mirror.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden", mirror.Url, StringComparison.Ordinal);
    }
}
