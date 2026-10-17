using System.Numerics;
using System.Windows.Input;
using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private void DrawQuixelSubTabs(MainWindowViewModel vm)
    {
        foreach (var tab in new[] { (Id: "assets", Label: "Mes assets", Selected: vm.IsQuixelAssetsTabSelected, Command: "ShowQuixelAssetsTabCommand"),
                                    (Id: "collections", Label: "Collections", Selected: vm.IsQuixelCollectionsTabSelected, Command: "ShowQuixelCollectionsTabCommand") })
        {
            if (tab.Selected) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            ActionButton(vm, tab.Label, tab.Command);
            Record("library-subtab/" + tab.Id);
            if (tab.Selected) { Record("library-subtab-selected/" + tab.Id); Gui.PopStyleColor(); }
            Gui.SameLine();
        }
        Gui.TextDisabled("|"); Gui.SameLine();
        ActionButton(vm, "Inventaire Fab (web)", "OpenMegascansFabCommand"); Gui.SameLine();
        ActionButton(vm, "Fichiers téléchargés via Fab", "ShowMegascansFabDownloadsCommand");
    }

    private void SectionTab(string id, string label, string subtitle, bool selected, Action activate)
    {
        var height = Gui.GetTextLineHeightWithSpacing() * (string.IsNullOrEmpty(subtitle) ? 1 : 2) + 12 * UiScale;
        if (Gui.Selectable("###" + id, selected, ImGuiSelectableFlags.None, new(Gui.GetContentRegionAvail().X, height))) _actions.Enqueue(activate);
        Record("section-tab/" + id);
        if (selected) Record("section-selected/" + id);
        var min = Gui.GetItemRectMin(); var max = Gui.GetItemRectMax();
        var draw = Gui.GetWindowDrawList();
        draw.PushClipRect(min, max, true);
        draw.AddRectFilled(min, max, Gui.ColorConvertFloat4ToU32(selected ? ImGuiTheme.Selected : Gui.IsItemHovered() ? ImGuiTheme.Rgb(0x565656) : ImGuiTheme.Rgb(0x414141)), 3);
        if (selected) draw.AddRectFilled(min, min + new Vector2(3 * UiScale, height), Gui.ColorConvertFloat4ToU32(ImGuiTheme.Accent));
        var text = min + new Vector2(9 * UiScale, 5 * UiScale);
        draw.AddText(text, Gui.GetColorU32(ImGuiCol.Text), Elide(label, max.X - text.X - 4));
        if (!string.IsNullOrEmpty(subtitle)) draw.AddText(text + new Vector2(0, Gui.GetTextLineHeightWithSpacing()),
            Gui.ColorConvertFloat4ToU32(ImGuiTheme.Muted), Elide(subtitle, max.X - text.X - 4));
        draw.PopClipRect();
    }

    private void DrawSection(MainWindowViewModel vm, UiNode page, bool settings)
    {
        // Persistent navigation and content have separate scroll regions, both full height.
        Gui.BeginChild(settings ? "settings-navigation" : "library-sources", new((settings ? 220 : 185) * UiScale, 0), ImGuiChildFlags.Borders);
        Gui.TextDisabled(settings ? "PARAMÈTRES" : "SOURCES"); Gui.Separator();
        if (settings)
        {
            foreach (var category in vm.SettingsCategories)
            {
                var current = category;
                SectionTab("settings/" + category.Id, category.Label, category.Description,
                    ReferenceEquals(vm.SelectedSettingsCategory, category), () => vm.SelectedSettingsCategory = current);
            }
        }
        else
        {
            void Source(string id, string label, string subtitle, bool selected, string command, bool available = true)
            {
                if (available) SectionTab("source/" + id, label, "", selected, () =>
                {
                    if (selected && vm.IsRemoteFabProductVisible) vm.CloseRemoteFabProductCommand.Execute(null);
                    if (selected && vm.IsLibraryQuixelSelected && vm.IsQuixelCollectionsTabSelected) vm.ShowQuixelAssetsTabCommand.Execute(null);
                    if (UiBinding.Read(vm, "{Binding " + command + "}") is ICommand action && action.CanExecute(null)) action.Execute(null);
                });
            }
            Source("Fab", "Fab", "My Library", vm.IsLibraryFabSelected, "ShowLibraryFabCommand", vm.IsFabLibrarySourceAvailable);
            Source("Unity", "Unity Store", "Asset Store", vm.IsLibraryUnitySelected, "ShowLibraryUnityCommand", vm.IsUnityLibrarySourceAvailable);
            Source("Quixel", "Megascans", "Compte Epic", vm.IsLibraryQuixelSelected, "ShowLibraryQuixelCommand", vm.IsQuixelLibrarySourceAvailable);
            Source("Local", "Packages locaux", "Fichiers et bibliothèques", vm.IsLibraryLocalSelected, "ShowLibraryLocalCommand");
            foreach (var connector in vm.MarketplaceConnectors.Where(item => item.IsEnabled))
            {
                var current = connector;
                var selected = vm.LibrarySource == "Connector:" + connector.Id ||
                    vm.IsLibraryFabWebSelected && connector.Id == "cyenginehub.marketplace-megascans-fab";
                SectionTab("source/" + connector.Id, connector.Name, "",
                    selected, () =>
                    {
                        if (selected && vm.IsRemoteFabProductVisible) vm.CloseRemoteFabProductCommand.Execute(null);
                        if (current.OpenLibraryCommand.CanExecute(null)) current.OpenLibraryCommand.Execute(null);
                    });
            }
            var noteHeight = Gui.CalcTextSize("Les connexions et dossiers par défaut se configurent dans Paramètres.", false, Gui.GetContentRegionAvail().X).Y;
            Gui.SetCursorPosY(Math.Max(Gui.GetCursorPosY(), Gui.GetCursorPosY() + Gui.GetContentRegionAvail().Y - noteHeight - Gui.GetStyle().ItemSpacing.Y - 1));
            Gui.Separator();
            Gui.TextWrapped("Les connexions et dossiers par défaut se configurent dans Paramètres.");
            Record("library-source-help");
        }
        Gui.EndChild();
        Record(settings ? "pane/settings/navigation" : "pane/library/navigation");
        Gui.SameLine();
        Gui.BeginChild(settings ? "settings-content" : "library-content", new(0, 0), ImGuiChildFlags.None);
        if (settings)
        {
            if (vm.IsSettingsAdvancedSelected)
            {
                Gui.TextUnformatted("AFFICHAGE");
                var density = vm.UseCompactInterface ? 0 : 1;
                Gui.SetNextItemWidth(210 * UiScale);
                if (Gui.Combo("Densité de l’interface", ref density, "Compact\0Confort\0")) Set(vm, "{Binding UseCompactInterface}", density == 0);
                Record("interface-density");
                Gui.Separator();
            }
            var body = page.Children.Single(node => node["Grid.Row"] == "1");
            DrawChildren(body.Children.Single(node => node["Grid.Column"] == "1"), vm);
        }
        else
        {
            var product = vm.IsRemoteFabProductVisible || vm.IsConnectorLibrarySelected && vm.SelectedConnectorLibrary?.IsProductVisible == true;
            if (!product && !(vm.IsRemoteMarketplaceContentVisible && vm.IsSelectedRemoteConnectionReady && vm.IsRemoteFabCatalogVisible))
            { DrawSizeAnalysisMenu(vm, "packs"); DrawLibraryViewModes(vm); }
            if (!string.IsNullOrEmpty(vm.RemoteCatalogNotice) && vm.IsLibraryRemoteSelected)
            {
                Gui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.Warning);
                Gui.TextWrapped(vm.RemoteCatalogNotice);
                Gui.PopStyleColor(); Gui.Separator();
            }
            DrawNode(page.Children.Single(node => node["Grid.Column"] == "1"), vm);
        }
        Gui.EndChild();
        Record(settings ? "pane/settings/content" : "pane/library/content");
    }
}
