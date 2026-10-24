using Avalonia.Controls;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;

static partial class Program
{
    static async Task TestProjectEnginePicker(Window window, MainWindowViewModel vm)
    {
        var first = vm.Engines.Single().Engine;
        var secondRoot = Path.Combine(Output, "SecondEngine");
        var secondEditor = Path.Combine(secondRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(secondEditor)!);
        await File.WriteAllTextAsync(secondEditor, "fixture - not executable");
        var second = first with { Id = "fixture:second-engine", AssociationId = "Second-QA", RootPath = secondRoot, EditorPath = secondEditor };
        Call(vm, "ReplaceCatalog", new[] { first, second }, vm.Projects.Select(item => item.Project).ToArray());
        var selected = vm.ProjectCreationEngines.Single(item => item.Engine.Id == second.Id);
        vm.SelectedProjectCreationEngine = selected;
        var card = vm.Engines.Single(item => item.Engine.Id == first.Id);
        card.OpenDetailsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "action/StartEngineRenameCommand");
        vm.EngineNameDraft = "Oculus source · QA";
        await ImGuiDiagnostics.ClickAsync(window, "action/SaveEngineNameCommand");
        for (var n = 0; n < 60 && vm.IsEngineNameEditing; n++) await Task.Delay(30);
        var renamed = vm.ProjectCreationEngines.Single(item => item.Engine.Id == first.Id);
        Check(renamed.Name == "Oculus source · QA" && renamed.Label.EndsWith("5.5.0", StringComparison.Ordinal), "Project creator immediately uses a just-saved engine alias and the full patch version");
        Check(ReferenceEquals(vm.SelectedProjectCreationEngine, selected), "Renaming an engine does not reset the selected engine or its option instance");
        Check(renamed.Path == first.RootPath && renamed.Association == first.AssociationId, "Engine aliases never change the project target path or association");
        Call(vm, "RebuildProjectCreationEngines");
        Check(vm.ProjectCreationEngines.Single(item => item.Engine.Id == first.Id).Name == "Oculus source · QA", "Rebuilding the picker reloads the saved alias rather than the raw discovery name");

        vm.ShowProjectsCommand.Execute(null); vm.ToggleProjectCreatorCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 6);
        var combo = "combo/SelectedProjectCreationEngine";
        // Zero clicks moves the pointer without pressing either mouse button.
        await ImGuiDiagnostics.ClickAsync(window, combo, clicks: 0);
        Check(ImGuiDiagnostics.Elements(window).Contains("combo-tooltip/SelectedProjectCreationEngine"), "The closed engine selector exposes the selected engine's path tooltip");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "project-engine-selected-tooltip.png"));
        await ImGuiDiagnostics.ClickAsync(window, combo);
        Check(ImGuiDiagnostics.Elements(window).Contains("combo-option/SelectedProjectCreationEngine/1"), "Both same-version engine installations remain selectable");
        await ImGuiDiagnostics.ClickAsync(window, "combo-option/SelectedProjectCreationEngine/0", clicks: 0);
        Check(ImGuiDiagnostics.Elements(window).Contains("combo-option-tooltip/SelectedProjectCreationEngine/0"), "Each dropdown entry exposes its own full engine path tooltip");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "project-engine-option-tooltip.png"));
        await ImGuiDiagnostics.ClickAsync(window, "combo-option/SelectedProjectCreationEngine/1");
        Check(vm.SelectedProjectCreationEngine?.Path == second.RootPath && vm.NewProjectEngineAssociation == second.AssociationId,
            "Selecting the second same-version engine uses its exact installation and association");
        vm.CloseProjectCreatorCommand.Execute(null);
        card.OpenDetailsCommand.Execute(null); vm.StartEngineRenameCommand.Execute(null); vm.EngineNameDraft = ""; vm.SaveEngineNameCommand.Execute(null);
        for (var n = 0; n < 60 && vm.IsEngineNameEditing; n++) await Task.Delay(30);
        Check(vm.ProjectCreationEngines.Single(item => item.Engine.Id == first.Id).Name == first.DisplayName, "Clearing an alias restores the original picker name without a catalog rescan");
    }
}
