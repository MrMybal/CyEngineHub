using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private void DrawCosmosImportOptions(MainWindowViewModel vm)
    {
        Gui.Separator(); Gui.TextUnformatted("DESTINATION DES IMPORTS COSMOS");
        Gui.TextWrapped("Réglage propre à Cosmos, par projet Unreal. Aucun import existant n’est déplacé.");
        Gui.SetNextItemWidth(-1);
        var open = Gui.BeginCombo("###cosmos-import-project", vm.SelectedCosmosImportProject?.Name ?? "Choisir un projet Unreal…");
        Record("cosmos-import/project");
        if (open)
        {
            foreach (var project in vm.Projects)
            {
                Gui.PushID(project.Project.FilePath);
                if (Gui.Selectable(project.Name, ReferenceEquals(project, vm.SelectedCosmosImportProject))) Set(vm, "{Binding SelectedCosmosImportProject}", project);
                if (Gui.IsItemHovered()) Gui.SetTooltip(project.Path);
                Gui.PopID();
            }
            Gui.EndCombo();
        }
        if (vm.SelectedCosmosImportProject is null) return;
        Gui.TextWrapped(vm.SelectedCosmosImportProject.Path);
        Gui.TextUnformatted("Sous-dossier de /Game");
        var path = vm.CosmosProjectDestination; Gui.SetNextItemWidth(-1);
        if (Gui.InputText("###cosmos-import-folder", ref path, 1024)) Set(vm, "{Binding CosmosProjectDestination}", path);
        Record("cosmos-import/folder");
        ActionButton(vm, "Enregistrer la destination Cosmos", "SaveCosmosProjectSettingsCommand", true);
    }
}
