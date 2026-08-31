using CyEngineHub.Git.Parsing;

namespace CyEngineHub.Git.Tests;

public sealed class GitStatusParserTests
{
    [Fact]
    public void ParsesPorcelainV2BranchDivergenceAndCounts()
    {
        var parser = new GitStatusParser();
        parser.Accept("# branch.oid 0123456789abcdef");
        parser.Accept("# branch.head feature/test");
        parser.Accept("# branch.upstream origin/feature/test");
        parser.Accept("# branch.ab +2 -3");
        parser.Accept("1 M. N... 100644 100644 100644 abc abc staged.cpp");
        parser.Accept("1 .M N... 100644 100644 100644 abc abc modified.cpp");
        parser.Accept("1 MM N... 100644 100644 100644 abc abc both.cpp");
        parser.Accept("? Content/New Asset.uasset");
        parser.Accept("u UU N... 100644 100644 100644 100644 a b c conflict.cpp");

        var result = parser.Build();

        Assert.Equal("feature/test", result.Branch);
        Assert.Equal("0123456789abcdef", result.HeadCommit);
        Assert.Equal("origin/feature/test", result.Upstream);
        Assert.Equal(2, result.Ahead);
        Assert.Equal(3, result.Behind);
        Assert.Equal(2, result.WorkingTree.Staged);
        Assert.Equal(2, result.WorkingTree.Modified);
        Assert.Equal(1, result.WorkingTree.Untracked);
        Assert.Equal(1, result.WorkingTree.Conflicted);
        Assert.True(result.WorkingTree.IsDirty);
    }
}
