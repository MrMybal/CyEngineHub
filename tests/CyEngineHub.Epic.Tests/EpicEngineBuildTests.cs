using CyEngineHub.Epic.Acquisition;

namespace CyEngineHub.Epic.Tests;

public sealed class EpicEngineBuildTests
{
    [Theory]
    [InlineData("5.8.3-44230000+++UE5+Release-5.8", "5.8.2", 44000000L, true)]
    [InlineData("5.8.3-44230000+++UE5+Release-5.8", "5.8.3", 44000000L, true)]
    [InlineData("5.8.3-44230000+++UE5+Release-5.8", "5.8.3", 44230000L, false)]
    [InlineData("5.8.3-44230000+++UE5+Release-5.8", "5.8.4", 44230000L, false)]
    [InlineData("5.9.0-99999999+++UE5+Release-5.9", "5.8.3", 44230000L, null)]
    [InlineData("5.8.3-44230000+++UE5+Release-5.8", "5.8.3", null, null)]
    [InlineData("unknown", "5.8.3", 44230000L, null)]
    [InlineData("5.8", "5.8.3", 44230000L, null)]
    public void UpdatesAreComparedWithinTheirBranchWithoutGuessing(string build, string installed, long? cl, bool? expected)
        => Assert.Equal(expected, EpicEngineBuild.Parse(build).IsNewerThan(Version.Parse(installed), cl));
}
