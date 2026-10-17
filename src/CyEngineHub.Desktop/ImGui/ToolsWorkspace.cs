using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Toolchains.Detection;
using CyEngineHub.Toolchains.Models;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private string _toolsQuery = "";
    private ToolchainInstallationCardViewModel? _selectedToolchainInstallation;

    private void DrawTools(MainWindowViewModel vm, UiNode page)
    {
        var labels = new[] { "Unreal", "Toolchains / SDK", "Diagnostic détaillé" };
        for (var index = 0; index < labels.Length; index++)
        {
            if (index > 0) Gui.SameLine();
            if (vm.ToolsTabIndex == index) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            var clicked = Gui.Button(labels[index]);
            Record("tools-tab/" + index);
            if (vm.ToolsTabIndex == index) Gui.PopStyleColor();
            if (clicked) { vm.ToolsTabIndex = index; _toolsQuery = ""; }
        }
        Gui.Spacing();
        DrawPrerequisiteRemediation(vm);
        if (vm.ToolsTabIndex == 2)
        {
            // Preserve the existing per-engine compatibility, device and environment diagnostics.
            DrawNode(page, vm);
            return;
        }
        if (vm.ToolsTabIndex == 0)
        {
            Gui.SetNextItemWidth(Math.Max(180, Gui.GetContentRegionAvail().X - 210 * UiScale));
            if (Gui.BeginCombo("###tools-engine", vm.SelectedToolsEngine is { } current ? current.DisplayName + " · " + current.VersionLabel : "Aucun moteur sélectionné"))
            {
                foreach (var engine in vm.Engines)
                {
                    if (Gui.Selectable(engine.DisplayName + " · " + engine.VersionLabel + "###" + engine.Engine.Id, ReferenceEquals(vm.SelectedToolsEngine, engine)))
                        Set(vm, "{Binding SelectedToolsEngine}", engine);
                    Record("tools-engine/" + engine.Engine.Id);
                    if (Gui.IsItemHovered()) Gui.SetTooltip(engine.Path);
                }
                Gui.EndCombo();
            }
            Record("tools-engine-selector"); Gui.SameLine();
            ActionButton(vm, "Actualiser les utilitaires", "RefreshUnrealToolsCommand");
            Gui.TextWrapped(vm.UnrealToolsStatus);
        }
        else
        {
            ActionButton(vm, "Analyser les toolchains", "RefreshToolchainsCommand", true);
            Gui.TextWrapped(vm.ToolchainStatusSummary);
        }
        if (vm.ToolsTabIndex == 1) Gui.TextWrapped("Choisissez les versions requises par votre moteur (Diagnostic détaillé), pas forcément les plus récentes.");
        Gui.TextDisabled("RESSOURCES OFFICIELLES · ouverture dans le navigateur, sans installation automatique");
        ActionButton(vm, "Prérequis Unreal", "OpenUnrealPrerequisitesGuideCommand"); Gui.SameLine();
        ActionButton(vm, ".NET", "OpenDotNetDownloadsCommand"); Gui.SameLine();
        ActionButton(vm, "Visual Studio / C++", "OpenCppToolsDownloadsCommand"); Gui.SameLine();
        ActionButton(vm, "Windows SDK", "OpenWindowsSdkDownloadsCommand");
        Gui.SetNextItemWidth(-1);
        Gui.InputTextWithHint("###tools-search", "Rechercher un outil, une version ou un emplacement…", ref _toolsQuery, 1024);
        Record("tools-search");
        if (vm.ToolsTabIndex == 0) DrawUnrealUtilities(vm);
        else DrawToolchainInventory(vm);
    }

    private void DrawPrerequisiteRemediation(MainWindowViewModel vm)
    {
        if (vm.PrerequisiteCheck is not { } check || vm.PrerequisiteRemediation is not { } remedy) return;
        Gui.BeginChild("prerequisite-remediation", new(0, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY);
        Gui.TextUnformatted(vm.PrerequisiteEngineName + " · " + check.Label);
        Gui.TextColored(check.Health == ToolchainHealth.Missing ? ImGuiTheme.Rgb(0xE8747C) : ImGuiTheme.Warning,
            check.Health == ToolchainHealth.Missing ? "Composant / version requis manquant" : "Installé ou détecté, mais configuration à vérifier");
        Gui.TextWrapped("Requis : " + check.Required + " · Détecté : " + check.Installed);
        Gui.TextWrapped(check.Diagnostic);
        Gui.TextWrapped(remedy.Instructions);
        Gui.TextDisabled("Le bouton ouvre la source officielle dans votre navigateur. Aucun programme n’est installé ou exécuté automatiquement.");
        if (remedy.DownloadPage is not null)
        {
            Gui.TextWrapped(remedy.DownloadPage.AbsoluteUri);
            ActionButton(vm, "Téléchargements officiels", "OpenPrerequisiteDownloadCommand");
        }
        else ActionButton(vm, "Options / réparation du moteur", "RepairPrerequisiteEngineCommand");
        DetailActions((vm, "Ré-analyser après installation", "RefreshToolchainsCommand", false),
            (vm, "Fermer cette aide", "ClosePrerequisiteCommand", false));
        Gui.EndChild(); Record("tools/prerequisite-remediation"); Gui.Spacing();
    }

    private bool MatchesTools(string text) => string.IsNullOrWhiteSpace(_toolsQuery) || text.Contains(_toolsQuery.Trim(), StringComparison.OrdinalIgnoreCase);
    private void DrawUnrealUtilities(MainWindowViewModel vm)
    {
        var items = vm.UnrealUtilities.Where(item => MatchesTools(item.Name + " " + item.Description + " " + item.ExecutablePath)).ToArray();
        if (vm.SelectedUnrealUtility is { } hidden && !items.Contains(hidden)) vm.SelectedUnrealUtility = null;
        if (!Gui.BeginTable("unreal-tools-split", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp)) return;
        Gui.TableSetupColumn("Liste", ImGuiTableColumnFlags.WidthStretch, 3);
        Gui.TableSetupColumn("Détails", ImGuiTableColumnFlags.WidthStretch, 1.5f);
        Gui.TableNextColumn();
        if (Gui.BeginTable("unreal-tools-list", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new(0, Math.Max(120, Gui.GetContentRegionAvail().Y - 5))))
        {
            Gui.TableSetupColumn("Outil", ImGuiTableColumnFlags.WidthStretch, 2);
            Gui.TableSetupColumn("État", ImGuiTableColumnFlags.WidthStretch, 1.4f);
            Gui.TableSetupColumn("Emplacement", ImGuiTableColumnFlags.WidthStretch, 3);
            Gui.TableSetupScrollFreeze(0, 1); SortableHeaders("unreal-tools-list", ["Outil", "État", "Emplacement"]);
            foreach (var item in SortTableItems("unreal-tools-list", items, (item, column) => column switch { 0 => item.Name, 1 => item.Status, _ => item.ExecutablePath }))
            {
                Gui.PushID(item.Id); Gui.TableNextRow(); Gui.TableNextColumn();
                if (Gui.Selectable(Elide(item.Name, Gui.GetContentRegionAvail().X) + "###utility", ReferenceEquals(item, vm.SelectedUnrealUtility),
                    ImGuiSelectableFlags.SpanAllColumns, new(0, Gui.GetFrameHeight()))) vm.SelectedUnrealUtility = item;
                Record("unreal-tool/" + item.Id);
                Gui.TableNextColumn(); Gui.TextColored(item.IsAvailable ? ImGuiTheme.Success : ImGuiTheme.Warning, item.Status);
                Gui.TableNextColumn(); Gui.TextUnformatted(Elide(item.ExecutablePath ?? "—", Gui.GetContentRegionAvail().X));
                if (Gui.IsItemHovered()) TextTooltip(item.ExecutablePath ?? item.SearchLocation);
                Gui.PopID();
            }
            Gui.EndTable();
        }
        Gui.TableNextColumn(); Gui.BeginChild("unreal-tools-inspector", new(0, 0), ImGuiChildFlags.Borders);
        if (vm.SelectedUnrealUtility is { } selected)
        {
            Gui.TextWrapped(selected.Name); Gui.Separator(); Gui.TextWrapped(selected.Description);
            Gui.Spacing(); Gui.TextColored(selected.IsAvailable ? ImGuiTheme.Success : ImGuiTheme.Warning, selected.Status);
            Gui.TextWrapped(selected.ExecutablePath ?? "L’utilitaire n’a pas été trouvé dans les emplacements connus. Ce n’est pas un diagnostic des prérequis installés sur Windows.");
            Gui.Spacing(); ActionButton(vm, "Dossier", "OpenUnrealUtilityFolderCommand");
            ActionButton(vm, selected.ChangesSystem ? "Préparer le lancement…" : "Lancer…", "PrepareUnrealUtilityCommand", true);
            if (Gui.CollapsingHeader("Emplacements recherchés")) Gui.TextWrapped(selected.SearchLocation);
        }
        else Gui.TextWrapped("Sélectionnez un utilitaire dans la liste.");
        Gui.EndChild(); Record("pane/tools/inspector"); Gui.EndTable();
    }

    private void DrawToolchainInventory(MainWindowViewModel vm)
    {
        var items = vm.ToolchainInstallations.Where(item => MatchesTools(item.Name + " " + item.Kind + " " + item.Version + " " + item.Path)).ToArray();
        if (_selectedToolchainInstallation is not null && !items.Contains(_selectedToolchainInstallation)) _selectedToolchainInstallation = null;
        if (items.Length == 0) Gui.TextWrapped("Aucune installation dans cette vue. Lancez l’analyse ou modifiez la recherche. La compatibilité se consulte dans Diagnostic détaillé.");
        if (!Gui.BeginTable("toolchain-split", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp)) return;
        Gui.TableSetupColumn("Liste", ImGuiTableColumnFlags.WidthStretch, 3);
        Gui.TableSetupColumn("Détails", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        Gui.TableNextColumn();
        if (Gui.BeginTable("toolchain-list", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new(0, Math.Max(120, Gui.GetContentRegionAvail().Y - 5))))
        {
            foreach (var title in new[] { "Composant", "Version", "État", "Emplacement", "Source" }) Gui.TableSetupColumn(title);
            Gui.TableSetupScrollFreeze(0, 1); SortableHeaders("toolchain-list", ["Composant", "Version", "État", "Emplacement", "Source"]);
            items = SortTableItems("toolchain-list", items, (item, column) => column switch { 0 => item.Name, 1 => item.Version, 2 => item.Health, 3 => item.Path, _ => item.Source });
            var height = Gui.GetFrameHeight() + Gui.GetStyle().CellPadding.Y * 2;
            Clipped(items.Length, height, index =>
            {
                var item = items[index]; Gui.PushID(item.Installation.Id); Gui.TableNextRow(ImGuiTableRowFlags.None, height); Gui.TableNextColumn();
                if (Gui.Selectable(Elide(item.Name, Gui.GetContentRegionAvail().X) + "###toolchain", ReferenceEquals(item, _selectedToolchainInstallation),
                    ImGuiSelectableFlags.SpanAllColumns, new(0, Gui.GetFrameHeight()))) _selectedToolchainInstallation = item;
                Record("toolchain-row/" + item.Installation.Id);
                Gui.TableNextColumn(); Gui.TextUnformatted(item.Version);
                Gui.TableNextColumn();
                var ready = item.Installation.Health == ToolchainHealth.Ready;
                Gui.TextColored(ready ? ImGuiTheme.Success : ImGuiTheme.Warning, ready ? "Détecté" : item.Installation.Health == ToolchainHealth.Missing ? "Manquant" : "À vérifier");
                Gui.TableNextColumn(); Gui.TextUnformatted(Elide(item.Path, Gui.GetContentRegionAvail().X));
                if (Gui.IsItemHovered()) TextTooltip(item.Path);
                Gui.TableNextColumn(); Gui.TextUnformatted(Elide(item.Source, Gui.GetContentRegionAvail().X)); Gui.PopID();
            });
            Gui.EndTable();
        }
        Gui.TableNextColumn(); Gui.BeginChild("toolchain-inspector", new(0, 0), ImGuiChildFlags.Borders);
        Gui.TextDisabled("INSTALLATION DÉTECTÉE"); Gui.Separator();
        if (_selectedToolchainInstallation is { } selected)
        {
            Gui.TextWrapped(selected.Name); Gui.TextColored(ImGuiTheme.Accent, selected.Version + " · " + selected.Kind);
            Gui.TextWrapped(selected.Path); Gui.TextWrapped(selected.IssueSummary);
            Gui.Spacing(); ActionButton(selected, "Ouvrir le dossier", "OpenFolderCommand");
            Gui.TextWrapped("Détecté ne signifie pas compatible avec tous les moteurs. Consultez Diagnostic détaillé pour les versions requises.");
        }
        else Gui.TextWrapped("Sélectionnez une installation pour voir son emplacement et son diagnostic.");
        Gui.EndChild(); Record("pane/tools/toolchain-inspector"); Gui.EndTable();
    }
}
