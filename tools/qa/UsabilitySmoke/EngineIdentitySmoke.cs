using Avalonia.Controls;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Unreal.Models;
using CyEngineHub.Unreal.Registration;
using CyEngineHub.Git.Analysis;
using CyEngineHub.Git.Models;
using CyEngineHub.Git.Parsing;
using CyEngineHub.Core.Settings;

static partial class Program
{
    static async Task TestEngineIdentity(Window window, MainWindowViewModel vm)
    {
        var engine = vm.Engines.Single().Engine with { Type = EngineInstallationType.SourceFork, Branch = "compilation-metadata-not-git" };
        var registration = (IUnrealEngineRegistrationService)typeof(MainWindowViewModel).GetField("_engineRegistration", Private)!.GetValue(vm)!;
        registration.SetAssociation(engine.RootPath, "Original-QA");
        Call(vm, "ReplaceCatalog", new[] { engine }, vm.Projects.Select(item => item.Project).ToArray());
        var card = vm.Engines.Single(); card.OpenDetailsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        await ImGuiDiagnostics.ClickAsync(window, "action/StartEngineRenameCommand");
        await ImGuiDiagnostics.ClickAsync(window, "field/EngineNameDraft");
        await ImGuiDiagnostics.TextAsync(window, "Oculus source · 5.5 QA");
        await ImGuiDiagnostics.ClickAsync(window, "action/SaveEngineNameCommand"); await Task.Delay(100);
        Check(card.DisplayName == "Oculus source · 5.5 QA" && !vm.IsEngineNameEditing, "Engine name edited and saved through the actual detail controls");
        var workspace = (IWorkspacePreferencesService)typeof(MainWindowViewModel).GetField("_workspacePreferencesService", Private)!.GetValue(vm)!;
        Check((await workspace.LoadAsync()).Items.Any(item => item.Key == engine.RootPath && item.Alias == card.DisplayName), "Engine name survives a preferences reload");
        Check(registration.FindByRoot(engine.RootPath)!.Identifier == "Original-QA", "Display rename does not modify the registry association");
        await ImGuiDiagnostics.ClickAsync(window, "action/StartEngineRenameCommand");
        vm.EngineNameDraft = "Discarded name";
        await ImGuiDiagnostics.ClickAsync(window, "action/CancelEngineRenameCommand");
        Check(card.DisplayName == "Oculus source · 5.5 QA", "Cancel does not rename the engine");
        await ImGuiDiagnostics.ClickAsync(window, "action/StartEngineAssociationCommand");
        vm.EngineAssociationDraft = "Oculus-5.5-QA";
        await ImGuiDiagnostics.ClickAsync(window, "action/PrepareEngineAssociationCommand");
        Check(vm.EngineAssociationConfirm && registration.FindByRoot(engine.RootPath)!.Identifier == "Original-QA", "Association requires a second explicit confirmation before any registry write");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "engine-association-confirmation.png"));
        vm.EngineAssociationDraft = "Oculus-5.5-QA-final";
        Check(!vm.EngineAssociationConfirm, "Editing a prepared association invalidates confirmation");
        await ImGuiDiagnostics.ClickAsync(window, "action/PrepareEngineAssociationCommand");
        await ImGuiDiagnostics.ClickAsync(window, "action/ApplyEngineAssociationCommand"); await Task.Delay(100);
        Check(card.Registration!.Identifier == "Oculus-5.5-QA-final" && !vm.IsEngineAssociationEditing &&
            registration.FindByRoot(engine.RootPath)!.Identifier == card.Registration.Identifier, "Confirmed association updates the isolated registry and detail card");
        Check(vm.ProjectCreationEngines.Any(item => item.Association == "Oculus-5.5-QA-final"), "Project creation sees the edited association immediately");
        Check(await File.ReadAllTextAsync(vm.Projects.Single().Project.FilePath) == "{}", "Existing project descriptor remains untouched");
        while (vm.EngineGitBusy) await Task.Delay(30);
        Check(card.EngineGitStatus is null && card.EngineGitMessage.StartsWith("Aucun dépôt Git", StringComparison.Ordinal), "Non-Git engine is not confused with the enclosing source repository");
        card.EngineGitStatus = new(engine.RootPath, new GitParsedStatus("oculus-source-5.5", "0123456789abcdef", "origin/oculus-source-5.5", 1, 3,
            new GitWorkingTreeSummary(0, 1, 0, 0)), true, DateTimeOffset.Now);
        card.EngineGitMessage = "";
        foreach (var width in new[] { 1280, 960 })
        {
            window.Width = width; await ImGuiDiagnostics.FramesAsync(window, 4);
            Check(ImGuiDiagnostics.Elements(window).Contains("engine-details/git"), "Engine Git state is visible in the General tab at " + width);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "engine-identity-" + width + ".png"));
        }
        Check(card.Engine.Branch == "compilation-metadata-not-git", "Git branch display never overwrites build metadata");
    }
}
