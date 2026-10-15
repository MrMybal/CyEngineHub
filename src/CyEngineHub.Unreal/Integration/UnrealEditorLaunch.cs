namespace CyEngineHub.Unreal.Integration;

public static class UnrealEditorLaunch
{
    // No project path means the project browser; don't restore the recent project.
    public static IReadOnlyList<string> ProjectBrowserArguments { get; } =
        Array.AsReadOnly(new[] { "-norecentproject" });
}
