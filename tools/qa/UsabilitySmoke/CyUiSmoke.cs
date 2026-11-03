using Avalonia.Controls;
using Avalonia.Input;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;

static partial class Program
{
    static bool CyUiOnly;

    static async Task TestCyUiMigration(Window window, MainWindowViewModel vm)
    {
        vm.ShowSettingsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/appearance");
        foreach (var theme in new[] { "mid-grey", "light-grey", "amber-night", "violet", "navy" })
            foreach (var compact in new[] { true, false })
            {
                vm.InterfaceTheme = theme; vm.UseCompactInterface = compact;
                await ImGuiDiagnostics.FramesAsync(window, 3);
                Check(ImGuiDiagnostics.FontSize(window) > 10 && ImGuiDiagnostics.Elements(window).Contains("field/InterfaceTheme"),
                    $"CyUI theme {theme}, density {(compact ? "compact" : "comfortable")} renders on the GPU");
                await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, $"cyui-{theme}-{compact}.png"));
            }
        vm.InterfaceTheme = "light-grey"; vm.UseCompactInterface = true;
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/project-profiles");
        vm.SelectedManagedProjectProfile = vm.ProjectCreationConfigurations.First(c => c.IsBuiltIn);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "command/EditManagedProjectProfileCommand");
        var original = vm.ProjectProfileName;
        await ImGuiDiagnostics.ClickAsync(window, "field/ProjectProfileName", horizontal: .8f);
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
        await ImGuiDiagnostics.TextAsync(window, "Draft not saved");
        Check(vm.ProjectProfileName == original, "Inspector drafts do not mutate application data before commit");
        await ImGuiDiagnostics.EscapeAsync(window);
        Check(vm.ProjectProfileName == original && vm.IsProjectProfileEditorVisible, "Escape cancels an Inspector edit without closing its parent dialog");
        await ImGuiDiagnostics.ClickAsync(window, "field/ProjectProfileName", horizontal: .8f);
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
        await ImGuiDiagnostics.TextAsync(window, "Committed profile");
        await ImGuiDiagnostics.KeyAsync(window, Key.Z, KeyModifiers.Control);
        await ImGuiDiagnostics.KeyAsync(window, Key.Y, KeyModifiers.Control);
        await ImGuiDiagnostics.KeyAsync(window, Key.Tab);
        Check(vm.ProjectProfileName == "Committed profile", "Inspector keeps text Undo/Redo and commits once on keyboard deactivation");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "cyui-inspector-light.png"));
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseProjectProfileEditorCommand");
        vm.InterfaceLanguage = "Français";
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/appearance");
        await ImGuiDiagnostics.ClickAsync(window, "action/SaveAppearanceSettingsCommand");
        await Task.Delay(100);
        vm.InterfaceLanguage = "English"; vm.InterfaceTheme = "navy";
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadInterfaceAsync", Private)!.Invoke(vm, null)!;
        Check(vm.InterfaceLanguage == "Français" && vm.InterfaceTheme == "light-grey", "CyUI theme and component locale survive settings reload");
        await TestActivityLog(window, vm);
        Check(ImGuiDiagnostics.Elements(window).Contains("logs/FilteredRawLogEntries"), "Raw journals use the native CyUI Diagnostics view");
    }
}
