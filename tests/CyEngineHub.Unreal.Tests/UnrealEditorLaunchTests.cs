using CyEngineHub.Unreal.Integration;

namespace CyEngineHub.Unreal.Tests;

public sealed class UnrealEditorLaunchTests
{
    [Fact]
    public void EngineLaunchOpensProjectBrowserWithoutRestoringRecentProject()
    {
        Assert.Equal(["-norecentproject"], UnrealEditorLaunch.ProjectBrowserArguments);
        Assert.DoesNotContain(UnrealEditorLaunch.ProjectBrowserArguments, argument => argument.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase));
    }
}
