using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private string _extensionQuery = "";
    private string _pluginQuery = "";
    private PluginCardViewModel? _selectedPlugin;

    private void DrawExtensions(MainWindowViewModel vm, UiNode page)
    {
        Gui.SetNextItemWidth(Math.Max(180, Gui.GetContentRegionAvail().X - 160 * UiScale));
        Gui.InputTextWithHint("###extensions-search", "Rechercher une extension, un éditeur ou un type…", ref _extensionQuery, 1024);
        Record("extensions-search"); Gui.SameLine(); ActionButton(vm, "Actualiser", "RefreshHubExtensionsCommand");
        if (Gui.CollapsingHeader("Ajouter une extension depuis un dossier"))
        {
            DrawNode(page.Descendants().Single(node => UiBinding.Path(node["Text"]) == "ManualHubExtensionPackage"), vm);
            ActionButton(vm, "Préparer l’installation", "PrepareHubExtensionInstallCommand");
        }
        DrawNode(page.Children.Single(node => node["Grid.Row"] == "1"), vm);
        var items = vm.HubExtensions.Where(item => string.IsNullOrWhiteSpace(_extensionQuery) ||
            (item.Name + " " + item.Publisher + " " + item.Kind).Contains(_extensionQuery.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (vm.SelectedHubExtension is { } hidden && !items.Contains(hidden)) Set(vm, "{Binding SelectedHubExtension}", null);
        Gui.TextDisabled(items.Length + " extension(s) · sélectionnez une ligne pour ses détails et actions");
        if (!Gui.BeginTable("extensions-split", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp)) return;
        Gui.TableSetupColumn("Liste", ImGuiTableColumnFlags.WidthStretch, 3);
        Gui.TableSetupColumn("Inspecteur", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        Gui.TableNextColumn();
        if (Gui.BeginTable("extensions-list", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new(0, Math.Max(120, Gui.GetContentRegionAvail().Y - 5))))
        {
            Gui.TableSetupColumn("Extension", ImGuiTableColumnFlags.WidthStretch, 3);
            Gui.TableSetupColumn("Version", ImGuiTableColumnFlags.WidthStretch, 1);
            Gui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthStretch, 2);
            Gui.TableSetupColumn("État", ImGuiTableColumnFlags.WidthStretch, 1);
            Gui.TableSetupScrollFreeze(0, 1); SortableHeaders("extensions-list", ["Extension", "Version", "Type", "État"]);
            items = SortTableItems("extensions-list", items, (item, column) => column switch { 0 => item.Name, 1 => item.Version, 2 => item.Kind, _ => item.Health });
            foreach (var item in items)
            {
                Gui.PushID(item.Id); Gui.TableNextRow(); Gui.TableNextColumn();
                if (Gui.Selectable(Elide(item.Name, Gui.GetContentRegionAvail().X) + "###extension", ReferenceEquals(item, vm.SelectedHubExtension),
                    ImGuiSelectableFlags.SpanAllColumns, new(0, Gui.GetFrameHeight()))) Set(vm, "{Binding SelectedHubExtension}", item);
                Record("extension-row/" + item.Id);
                Gui.TableNextColumn(); Gui.TextUnformatted(item.Version);
                Gui.TableNextColumn(); Gui.TextUnformatted(Elide(item.Kind, Gui.GetContentRegionAvail().X));
                Gui.TableNextColumn(); Gui.TextColored(item.HasError ? ImGuiTheme.Warning : ImGuiTheme.Success, item.Health);
                Gui.PopID();
            }
            Gui.EndTable();
        }
        Gui.TableNextColumn(); Gui.BeginChild("extension-inspector", new(0, 0), ImGuiChildFlags.Borders);
        Gui.TextDisabled("INSPECTEUR · EXTENSION"); Gui.Separator();
        if (vm.SelectedHubExtension is { } selected)
        {
            Gui.TextWrapped(selected.Name); Gui.TextColored(ImGuiTheme.Accent, selected.Version + " · " + selected.Origin);
            Gui.TextDisabled(selected.Publisher); Gui.TextWrapped(selected.Description);
            Gui.Spacing(); Gui.Separator();
            Gui.TextWrapped("Moteurs : " + selected.Engines); Gui.TextWrapped("Capacités : " + selected.Capabilities);
            Gui.TextWrapped("Formats : " + selected.Formats); Gui.TextWrapped(selected.ExecutionPolicy);
            if (selected.HasError) Gui.TextWrapped(selected.Error);
            if (vm.CanOpenSelectedHubExtensionWindow) ActionButton(vm, "Ouvrir l’interface", "OpenHubExtensionWindowCommand", true);
            if (vm.CanOpenSelectedHubExtensionWebsite) ActionButton(vm, "Page officielle", "OpenHubExtensionWebsiteCommand");
            if (Gui.CollapsingHeader("Configuration / préparation de l’outil"))
                DrawNode(page.Children.Single(node => node["Grid.Row"] == "2"), vm);
            DrawNode(page.Children.Single(node => node["Grid.Row"] == "3"), vm);
        }
        else Gui.TextWrapped("Sélectionnez une extension dans la liste.");
        Gui.EndChild(); Record("pane/extensions/inspector"); Gui.EndTable();
    }

    private void DrawPlugins(MainWindowViewModel vm, UiNode page)
    {
        foreach (var family in vm.EngineTabs)
        {
            var familySelected = ReferenceEquals(family, vm.SelectedPluginTab);
            if (familySelected) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            if (Gui.Button(family.Label)) Set(vm, "{Binding SelectedPluginTab}", family);
            if (familySelected) Gui.PopStyleColor();
            Gui.SameLine();
        }
        Gui.NewLine();
        if (!vm.IsUnrealPluginTabSelected)
        {
            DrawNode(page.Children.Single(node => node["Grid.Row"] == "2"), vm);
            return;
        }
        Gui.SetNextItemWidth(Math.Max(180, Gui.GetContentRegionAvail().X - 180 * UiScale));
        Gui.InputTextWithHint("###plugin-search", "Rechercher un plugin, une version ou une source…", ref _pluginQuery, 1024);
        Record("plugins-search"); Gui.SameLine(); ActionButton(vm, "Analyser les plugins", "RefreshPluginsCommand");
        if (Gui.CollapsingHeader("Ajouter une bibliothèque de plugins"))
        {
            DrawNode(page.Descendants().Single(node => UiBinding.Path(node["Text"]) == "ManualPluginLibraryRoot"), vm);
            ActionButton(vm, "Ajouter la bibliothèque", "AddPluginLibraryRootCommand");
        }
        DrawNode(page.Children.Single(node => node["Grid.Row"] == "1"), vm);
        var items = vm.VisiblePlugins.Where(item => string.IsNullOrWhiteSpace(_pluginQuery) ||
            (item.Name + " " + item.VersionLabel + " " + item.SourceLabel).Contains(_pluginQuery.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (_selectedPlugin is not null && !items.Contains(_selectedPlugin)) _selectedPlugin = null;
        Gui.TextDisabled(items.Length + " plugin(s) · " + vm.PluginStatusSummary);
        if (!Gui.BeginTable("plugins-split", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.SizingStretchProp)) return;
        Gui.TableSetupColumn("Liste", ImGuiTableColumnFlags.WidthStretch, 3);
        Gui.TableSetupColumn("Inspecteur", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        Gui.TableNextColumn();
        if (Gui.BeginTable("plugins-list", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable,
            new(0, Math.Max(120, Gui.GetContentRegionAvail().Y - 5))))
        {
            foreach (var title in new[] { "Plugin", "Version", "Moteur", "Source" }) Gui.TableSetupColumn(title);
            Gui.TableSetupScrollFreeze(0, 1); SortableHeaders("plugins-list", ["Plugin", "Version", "Moteur", "Source"]);
            items = SortTableItems("plugins-list", items, (item, column) => column switch { 0 => item.Name, 1 => item.VersionLabel, 2 => item.EngineVersionLabel, _ => item.SourceLabel });
            var height = Gui.GetFrameHeight() + Gui.GetStyle().CellPadding.Y * 2;
            Clipped(items.Length, height, index =>
            {
                var item = items[index]; Gui.PushID(index); Gui.TableNextRow(ImGuiTableRowFlags.None, height); Gui.TableNextColumn();
                if (Gui.Selectable(Elide(item.Name, Gui.GetContentRegionAvail().X) + "###plugin", ReferenceEquals(item, _selectedPlugin),
                    ImGuiSelectableFlags.SpanAllColumns, new(0, Gui.GetFrameHeight()))) _selectedPlugin = item;
                Record("plugin-row/" + item.InternalName);
                Gui.TableNextColumn(); Gui.TextUnformatted(item.VersionLabel);
                Gui.TableNextColumn(); Gui.TextUnformatted(item.EngineVersionLabel);
                Gui.TableNextColumn(); Gui.TextUnformatted(Elide(item.SourceLabel, Gui.GetContentRegionAvail().X)); Gui.PopID();
            });
            Gui.EndTable();
        }
        Gui.TableNextColumn(); Gui.BeginChild("plugin-inspector", new(0, 0), ImGuiChildFlags.Borders);
        Gui.TextDisabled("INSPECTEUR · PLUGIN UNREAL"); Gui.Separator();
        Gui.TextUnformatted("Cible d’installation / build");
        DrawNode(page.Descendants().Single(node => UiBinding.Path(node["SelectedItem"]) == "SelectedPluginTarget"), vm);
        if (_selectedPlugin is { } selected)
        {
            Gui.TextWrapped(selected.Name); Gui.TextColored(ImGuiTheme.Accent, selected.VersionLabel);
            Gui.TextWrapped(selected.Path); Gui.TextWrapped(selected.ModuleSummary); Gui.TextWrapped(selected.PlatformSummary);
            Gui.TextWrapped(selected.DependencySummary); Gui.TextWrapped(selected.IssueSummary);
            ActionButton(selected, "Dossier", "OpenFolderCommand");
            ActionButton(selected, "Préparer l’installation", "PrepareInstallCommand", true);
            ActionButton(selected, "BuildPlugin", "BuildPluginCommand");
        }
        else Gui.TextWrapped("Sélectionnez un plugin dans la liste.");
        Gui.EndChild(); Gui.EndTable();
    }
}
