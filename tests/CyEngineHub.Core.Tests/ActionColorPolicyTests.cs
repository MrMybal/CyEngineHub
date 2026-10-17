using CyEngineHub.Desktop.ImGui;

namespace CyEngineHub.Core.Tests;

public sealed class ActionColorPolicyTests
{
    [Theory]
    [InlineData("LaunchCommand", "Start")]
    [InlineData("ConfirmUtilityLaunchCommand", "Start")]
    [InlineData("RunCrossCompileCommand", "Start")]
    [InlineData("ToggleEngineImporterCommand", "Start")]
    [InlineData("RequestRemoveSelectedEngineCommand", "Danger")]
    [InlineData("ConfirmUninstallSelectedEngineCommand", "Danger")]
    [InlineData("RequestUnregisterCommand", "Danger")]
    [InlineData("RequestDeleteAllFabDownloadsCommand", "Danger")]
    [InlineData("CancelDeleteCommand", "Neutral")]
    [InlineData("CancelUtilityLaunchCommand", "Neutral")]
    [InlineData("OpenFolderCommand", "Neutral")]
    [InlineData("RefreshToolchainsCommand", "Neutral")]
    public void SemanticCommandsOverrideThePrimaryAccent(string command, string expected)
    {
        Assert.Equal(expected, ActionColorPolicy.Resolve(command).ToString());
        if (expected != "Neutral") Assert.Equal(expected, ActionColorPolicy.Resolve(command, primary: true).ToString());
    }

    [Fact]
    public void DangerClassWinsOverStartAndSelection()
    {
        Assert.Equal(ActionTone.Danger, ActionColorPolicy.Resolve("LaunchCommand", primary: true, danger: true));
        Assert.Equal(ActionTone.Primary, ActionColorPolicy.Resolve("DownloadCommand", primary: true));
    }
}
