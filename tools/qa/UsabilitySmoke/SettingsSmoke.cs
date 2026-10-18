using Avalonia.Controls;
using Avalonia.Input;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;

partial class Program
{
    static async Task TestOrganizedSettings(Window window, MainWindowViewModel vm)
    {
        vm.ShowSettingsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(!vm.SettingsCategories.Any(c => c.Id == "storage"), "Storage settings duplicate removed; main Storage page retained");
        Check(ImGuiDiagnostics.Elements(window).Contains("connection/Git/disconnected"), "Enabled Git remains visibly unverified, not connected because a repository exists");
        await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/connections/Git");
        Check(vm.IsGitConnectionPageSelected && !ImGuiDiagnostics.Elements(window).Contains("field/QuixelAuthorizationCode"), "Each connection has a selected subtab and only its own panel");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "settings-connections-git.png"));
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/appearance");
        Check(ImGuiDiagnostics.Elements(window).Contains("field/InterfaceTheme"), "Appearance has a discoverable theme selector");
        vm.InterfaceTheme = "Sombre"; await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "settings-theme-dark.png"));
        await ImGuiDiagnostics.ClickAsync(window, "action/SaveAppearanceSettingsCommand"); await Task.Delay(100);
        vm.InterfaceTheme = "Gris";
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadInterfaceAsync", Private)!.Invoke(vm, null)!;
        Check(vm.InterfaceTheme == "Sombre", "Theme is restored from saved settings");
        vm.InterfaceTheme = "Gris";
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/marketplaces");
        Check(ImGuiDiagnostics.Elements(window).Contains("field/FabDownloadConnections") && !ImGuiDiagnostics.Elements(window).Contains("field/QuixelDownloadConnections") && !ImGuiDiagnostics.Elements(window).Contains("field/UnityCacheDirectory"), "Fab marketplace panel excludes other marketplaces");
        vm.FabDownloadConnections = 20;
        var settings = (CyEngineHub.Core.Settings.ISettingsStore)typeof(MainWindowViewModel).GetField("_settings", Private)!.GetValue(vm)!;
        await settings.SetAsync(MainWindowViewModel.QuixelPackageCompressionSetting, "untouched-other-market");
        await ImGuiDiagnostics.ClickAsync(window, "action/SaveFabMarketplacePanelCommand"); await Task.Delay(100);
        Check((await settings.GetAsync(MainWindowViewModel.QuixelPackageCompressionSetting)) == "untouched-other-market" && vm.StatusMessage.StartsWith("Paramètres Fab", StringComparison.Ordinal),
            "Saving one marketplace does not overwrite another marketplace's configuration");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "settings-marketplace-fab.png"));
        await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/marketplaces/Megascans");
        Check(ImGuiDiagnostics.Elements(window).Contains("field/QuixelDownloadConnections") && !ImGuiDiagnostics.Elements(window).Contains("field/FabCacheDirectory"), "Megascans has its own configuration and transfer controls");
        vm.QuixelDownloadConnections = 7; vm.ResumeDownloadsOnStartup = false;
        await ImGuiDiagnostics.ClickAsync(window, "action/SaveDownloadQueueSettingsCommand"); await Task.Delay(100);
        Check(((FixtureFab)typeof(MainWindowViewModel).GetField("_directFabClient", Private)!.GetValue(vm)!).DownloadConnections == 20 &&
              ((FixtureQuixel)typeof(MainWindowViewModel).GetField("_quixelClient", Private)!.GetValue(vm)!).DownloadConnections == 7,
            "Independent Fab and Megascans connection values reach the actual transfer clients");
        vm.AssetDownloadConnections = 1;
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadDownloadQueueAsync", Private)!.Invoke(vm, null)!;
        Check(vm.FabDownloadConnections == 20 && vm.QuixelDownloadConnections == 7, "Independent connection settings survive reload, old shared settings remain migratable");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/project-profiles");
        vm.SelectedManagedProjectProfile = vm.ProjectCreationConfigurations.First(c => c.Profile.EngineSettings.Count + c.Profile.IniSettings.Count > 0);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(ImGuiDiagnostics.Elements(window).Contains("settings/profile-preview"), "Unreal profiles expose their full plugin and INI configuration for viewing");
        await ImGuiDiagnostics.ClickAsync(window, "command/EditManagedProjectProfileCommand");
        Check(vm.IsProjectProfileEditorVisible && ImGuiDiagnostics.Elements(window).Contains("field/ProjectProfileName"), "Profile customization opens the editable profile dialog through a real pointer click");
        await ImGuiDiagnostics.ClickAsync(window, "field/ProjectProfileName");
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control); await ImGuiDiagnostics.TextAsync(window, "Profil QA éditable");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "settings-profile-editor.png"));
        await ImGuiDiagnostics.ClickAsync(window, "command/SaveProjectProfileCommand"); await Task.Delay(200);
        Check(!vm.IsProjectProfileEditorVisible && vm.ProjectCreationConfigurations.Any(p => p.Name == "Profil QA éditable" && !p.IsBuiltIn), "Editing an integrated profile saves a custom copy without changing the template");
        await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/unreal/downloads");
        Check(ImGuiDiagnostics.Elements(window).Contains("field/EngineChunkConcurrency") && !ImGuiDiagnostics.Elements(window).Contains("field/FabDownloadConnections"), "Unreal-only download options are grouped under Unreal Engine");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/downloads");
        Check(!ImGuiDiagnostics.Elements(window).Contains("field/EngineChunkConcurrency") && ImGuiDiagnostics.Elements(window).Contains("field/DownloadConcurrency"), "Global queue settings do not mix Unreal-only options or marketplace connection settings");
        var script = new UnrealVersionBridgeInstallationScript(null!, null!, null!, null!, Output);
        var target = new FabInstallTargetOptionViewModel("qa", "Projet QA", "Projet", Output, "5.5", true, false, LibraryPackageContentKind.ContentPack, [script], script.Descriptor.Id);
        Call(vm, "OpenFabInstallationScriptConfiguration", target);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(ImGuiDiagnostics.Elements(window).Contains("script-option/registerDerivedVariant"), "Downgrade script exposes a real checkbox for adding its derived version to the asset sheet");
        await ImGuiDiagnostics.ClickAsync(window, "script-option/registerDerivedVariant");
        Check(target.GetInstallationConfiguration()["registerDerivedVariant"].Equals("false", StringComparison.OrdinalIgnoreCase), "Derivative checkbox updates the configuration sent to the script");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "settings-downgrade-options.png"));
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseFabInstallationScriptConfigurationCommand");
    }
}
