using System.Text.Json;
using Avalonia.Controls;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Projects.Creation;

static partial class Program
{
    static async Task TestProjectCreationForm(Window window, MainWindowViewModel vm)
    {
        vm.ShowProjectsCommand.Execute(null); vm.ToggleProjectCreatorCommand.Execute(null);
        vm.NewProjectParentDirectory = Output; vm.NewProjectName = "BlueprintFixture";
        await ImGuiDiagnostics.FramesAsync(window, 5);
        Check(ImGuiDiagnostics.Elements(window).Contains("combo/SelectedProjectCreationFamily") &&
            ImGuiDiagnostics.Elements(window).Contains("combo/SelectedProjectCreationCodeKind"), "Environment and Blueprint/C++ choices are separate, permanently visible controls");
        Check(!ImGuiDiagnostics.Elements(window).Contains("field/NewProjectEngineAssociation"), "Unreal association is not an unexplained editable field");
        Check(ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("command/BrowseNewProjectParentDirectory/", StringComparison.Ordinal)), "The parent folder has an explicit native Browse action");
        var engine = vm.SelectedProjectCreationEngine;
        var profile = vm.SelectedProjectCreationConfiguration;
        var overrides = (Dictionary<string, bool>)typeof(MainWindowViewModel).GetField("_manualProjectPlugins", Private)!.GetValue(vm)!;
        overrides["FixtureOverride"] = false;
        var iniDocument = vm.ProjectCreationIniDocuments.First();
        iniDocument.Content += "\n[Fixture]\nKeepManualSetting=True\n";
        var iniContent = iniDocument.Content;
        await ImGuiDiagnostics.ClickAsync(window, "combo/SelectedProjectCreationCodeKind");
        await ImGuiDiagnostics.ClickAsync(window, "combo-option/SelectedProjectCreationCodeKind/0");
        Check(vm.SelectedProjectCreationPreset?.Preset.IsCodeProject == false, "The real Blueprint control selects the Blueprint creation preset");
        Check(ReferenceEquals(engine, vm.SelectedProjectCreationEngine) && ReferenceEquals(profile, vm.SelectedProjectCreationConfiguration) && overrides.ContainsKey("FixtureOverride"),
            "Changing Blueprint/C++ keeps the selected engine, profile and manual plugin overrides");
        Check(vm.ProjectCreationIniDocuments.Contains(iniDocument) && iniDocument.Content == iniContent,
            "Changing Blueprint/C++ preserves hand-edited INI documents");
        var service = new ProjectCreationService();
        var blueprint = await service.CreateAsync(new(vm.SelectedProjectCreationPreset!.Preset, vm.NewProjectName, vm.NewProjectParentDirectory,
            vm.NewProjectEngineAssociation, [], vm.SelectedProjectCreationConfiguration?.Profile));
        using (var descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(blueprint.ProjectMarker)))
        {
            Check(!Directory.Exists(Path.Combine(blueprint.ProjectRoot, "Source")) && descriptor.RootElement.GetProperty("Modules").GetArrayLength() == 0,
                "Blueprint choice creates an actual project with no C++ modules or Source directory");
        }
        Check(vm.ProjectCreationDestinationPreview.EndsWith(Path.Combine(Output, "BlueprintFixture"), StringComparison.Ordinal), "Destination preview includes the project subfolder, not only its parent");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "project-creation-blueprint.png"));
        await ImGuiDiagnostics.ClickAsync(window, "combo/SelectedProjectCreationCodeKind");
        await ImGuiDiagnostics.ClickAsync(window, "combo-option/SelectedProjectCreationCodeKind/1");
        Check(vm.SelectedProjectCreationPreset?.Preset.IsCodeProject == true, "The real C++ control selects the code creation preset");
        vm.NewProjectName = "CppFixture";
        var cpp = await service.CreateAsync(new(vm.SelectedProjectCreationPreset!.Preset, vm.NewProjectName, vm.NewProjectParentDirectory,
            vm.NewProjectEngineAssociation, [], vm.SelectedProjectCreationConfiguration?.Profile));
        using (var descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(cpp.ProjectMarker)))
        {
            Check(File.Exists(Path.Combine(cpp.ProjectRoot, "Source", "CppFixture", "CppFixture.Build.cs")) &&
                File.Exists(Path.Combine(cpp.ProjectRoot, "Source", "CppFixtureEditor.Target.cs")) && descriptor.RootElement.GetProperty("Modules").GetArrayLength() == 1,
                "C++ choice creates real runtime source, build rules and the Editor target");
        }
        vm.NewProjectName = "../Outside";
        Check(vm.ProjectCreationDestinationPreview.StartsWith("Nom invalide", StringComparison.Ordinal), "An invalid project name cannot produce a misleading destination preview");
        vm.NewProjectName = "CppFixture";
        await ImGuiDiagnostics.ClickAsync(window, "expander/ProjectCreationAdvanced");
        Check(!ImGuiDiagnostics.Elements(window).Contains("field/NewProjectEngineAssociation"), "Advanced Unreal association remains automatic and read-only");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "project-creation-advanced.png"));
        foreach (var width in new[] { 1280, 960 })
        {
            window.Width = width; await ImGuiDiagnostics.FramesAsync(window, 4);
            var name = ImGuiDiagnostics.ElementBounds(window, "field/NewProjectName");
            var folder = ImGuiDiagnostics.ElementBounds(window, "field/NewProjectParentDirectory");
            Check(name.Z <= folder.X && Math.Abs(name.Y - folder.Y) < 2, "Labeled name and parent fields align without overlap at " + width);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "project-creation-form-" + width + ".png"));
        }
        vm.SelectedProjectCreationFamily = "Godot";
        Check(!vm.IsUnrealProjectCreation && vm.NewProjectEngineAssociation.Length == 0, "Switching to another environment does not carry an Unreal association into that project");
        vm.SelectedProjectCreationFamily = "Unreal Engine";
        Check(vm.IsUnrealProjectCreation && vm.NewProjectEngineAssociation == engine!.Association, "Returning to Unreal restores the selected engine association automatically");
    }
}
