using CyEngineHub.Build.Execution;

namespace CyEngineHub.Build.Tests;

public sealed class UnrealBuildProgressParserTests
{
    [Theory]
    [InlineData("@progress 'Compiling C++ source code...' 58%", .58)]
    [InlineData("@progress 'Generating project files...' 100%", 1)]
    [InlineData("[5/12] Compile [x64] CyAnimBake.cpp", 5d / 12)]
    [InlineData("[12/12] WriteMetadata GameEditor.target", 1)]
    public void ReadsActualUnrealProgressWithoutInventingPercentages(string text, double expected) =>
        Assert.Equal(expected, UnrealBuildProgressParser.Parse(text));

    [Theory]
    [InlineData("Processus Unreal en cours")]
    [InlineData("[5/0] invalid")]
    [InlineData("[13/12] invalid")]
    [InlineData("@progress 'bad' 101%")]
    [InlineData("Compiled in 25% less time")]
    public void UnquantifiedOrInvalidOutputRemainsIndeterminate(string text) => Assert.Null(UnrealBuildProgressParser.Parse(text));
}
