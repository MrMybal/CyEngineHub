using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Unreal.Models;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private void DetailActions(params (object Model, string Label, string Command, bool Primary)[] actions)
    {
        var right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
        for (var index = 0; index < actions.Length; index++)
        {
            var action = actions[index];
            if (index > 0 && Gui.GetItemRectMax().X + Gui.GetStyle().ItemSpacing.X + Gui.CalcTextSize(action.Label).X + 24 <= right) Gui.SameLine();
            ActionButton(action.Model, action.Label, action.Command, action.Primary);
        }
    }
    private void DrawEngineDetails(MainWindowViewModel vm, UiNode page)
    {
        var unreal = vm.SelectedEngineDetails;
        object? engine = (object?)unreal ?? vm.SelectedLocalEngineDetails;
        if (engine is null) { Gui.TextWrapped("Sélectionnez un moteur dans la liste."); return; }
        if (_focusCompilation) { vm.EngineDetailsTab = 2; _focusCompilation = false; }
        Gui.TextUnformatted(unreal?.DisplayName ?? Value(engine, "Name")); Gui.SameLine();
        Gui.TextColored(ImGuiTheme.Accent, unreal?.VersionLabel ?? Value(engine, "Version"));
        if (unreal is not null)
        {
            Gui.SameLine(); Gui.TextColored(unreal.Engine.Health == EngineHealth.Healthy ? ImGuiTheme.Success : ImGuiTheme.Warning,
                unreal.Engine.Health == EngineHealth.Healthy ? "Disponible" : unreal.HealthLabel);
            if (unreal.IsOfficialEpic) { Gui.SameLine(); Gui.TextColored(ImGuiTheme.Success, "OFFICIEL EPIC"); }
        }
        Gui.TextDisabled(Elide(Value(engine, "Path"), Gui.GetContentRegionAvail().X));
        if (Gui.IsItemHovered()) TextTooltip(Value(engine, "Path"));
        Gui.Spacing();
        var tabs = new[] { "Général", "Options d’installation", "Compilation / cross-compilation" };
        for (var index = 0; index < tabs.Length; index++)
        {
            if (index > 0 && Gui.GetContentRegionAvail().X > Gui.CalcTextSize(tabs[index]).X + 24) Gui.SameLine();
            var selected = vm.EngineDetailsTab == index;
            if (selected) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            var clicked = Gui.Button(tabs[index]);
            Record("engine-detail-tab/" + index);
            if (selected) Gui.PopStyleColor();
            if (clicked) vm.EngineDetailsTab = index;
        }
        Gui.Separator();
        Gui.BeginChild("engine-detail-content", new(0, 0), ImGuiChildFlags.None);
        if (vm.EngineDetailsTab == 0)
        {
            DetailActions((engine, unreal is null ? "Lancer" : "Lancer · accueil Unreal", "LaunchCommand", true),
                (engine, "Ouvrir le dossier", "OpenFolderCommand", false), (vm, "Analyser la taille", "AnalyzeSelectedEngineSizeCommand", false));
            Gui.Spacing(); Gui.Separator();
            if (Gui.BeginTable("engine-general-info", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
            {
                Gui.TableSetupColumn("Info", ImGuiTableColumnFlags.WidthStretch, 1);
                Gui.TableSetupColumn("Valeur", ImGuiTableColumnFlags.WidthStretch, 4);
                void Info(string label, string text)
                { Gui.TableNextRow(); Gui.TableNextColumn(); Gui.TextDisabled(label); Gui.TableNextColumn(); Gui.TextWrapped(text); }
                Info("Type", unreal?.TypeLabel ?? Value(engine, "Family"));
                Info("Branche / source", unreal?.BranchLabel ?? Value(engine, "Source"));
                Info("Taille", Value(engine, "EffectiveSizeLabel"));
                if (unreal is not null) Info("Association", unreal.RegistrationLabel);
                Gui.EndTable();
            }
            if (unreal is not null)
            {
                Gui.Spacing();
                Gui.TextColored(unreal.Engine.Health == EngineHealth.Healthy ? ImGuiTheme.Success : ImGuiTheme.Warning, unreal.IssueSummary);
                if (unreal.IsOfficialEpic)
                {
                    Gui.TextColored(unreal.OfficialUpdateAvailable ? ImGuiTheme.Warning : ImGuiTheme.Muted, unreal.OfficialUpdateLabel);
                    ActionButton(vm, "Vérifier la révision officielle", "CheckOfficialEngineUpdatesCommand");
                }
                DetailActions((vm, "Plugins du moteur", "ShowSelectedEnginePluginsCommand", false),
                    (vm, "Outils / prérequis", "ShowToolchainsCommand", false));
                if (unreal.CanRegister) ActionButton(unreal, "Enregistrer le moteur", "RegisterCommand");
                if (unreal.IsRegistered) ActionButton(unreal, "Désenregistrer…", "RequestUnregisterCommand");
            }
            Gui.Spacing();
            if (unreal is null)
                DetailActions((vm, "Plugins du moteur", "ShowSelectedEnginePluginsCommand", false),
                    (vm, "Outils / prérequis", "ShowToolchainsCommand", false));
            if (Gui.CollapsingHeader("Gestion du catalogue / désinstallation"))
                DetailActions((vm, "Retirer du catalogue…", "RequestRemoveSelectedEngineCommand", false),
                    (vm, "Désinstaller…", "RequestUninstallSelectedEngineCommand", false));
            Record("engine-details/general");
        }
        else if (vm.EngineDetailsTab == 1)
        {
            if (unreal is not null)
            {
                if (unreal.CanSetup) ActionButton(unreal, "Préparer les dépendances (Setup)", "SetupCommand");
                DrawNode(page.Descendants().Single(node => node["Name"] == "EngineInstallationOptions"), vm);
            }
            else
            {
                Gui.TextWrapped("Les composants de cette installation se gèrent avec les outils de son fournisseur. Les prérequis locaux restent accessibles dans Outils.");
                var local = page.Descendants().Single(node => UiBinding.Path(node["IsVisible"]) == "IsLocalEngineDetailsSelected");
                DrawNode(local.Descendants().Single(node => node.Kind == "Border" && node["Grid.Column"] == "1"), vm);
            }
            Record("engine-details/installation");
        }
        else
        {
            if (unreal is not null)
            {
                DetailActions((unreal, "Compiler l’éditeur", "BuildEditorCommand", true),
                    (unreal, "Générer les fichiers de projet", "GenerateProjectFilesCommand", false),
                    (vm, "Voir les opérations", "ShowActionsCommand", false));
                ActionButton(vm, "Actualiser les prérequis", "RefreshToolchainsCommand");
                Gui.TextDisabled("Un outil détecté peut être d’une autre version. Requis indique la version attendue par ce moteur.");
            }
            DrawNode(page.Descendants().Single(node => node["Name"] == "EngineCompilationPanel"), vm);
            Record("engine-details/compilation");
        }
        Gui.EndChild(); Record("pane/engine-details/content");
    }
}
