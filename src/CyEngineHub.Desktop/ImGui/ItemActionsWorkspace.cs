using System.Numerics;
using System.Windows.Input;
using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private object? _contextItem;
    private string _contextOpenCommand = "OpenDetailsCommand";
    private bool _itemContextRequested;
    private object[] _contextSelectionItems = [];
    private void RequestItemContext(object item, string command = "OpenDetailsCommand")
    { SelectItem(item, rightClick: true); _contextSelectionItems = SelectedContextItems(); _contextItem = item; _contextOpenCommand = command; _itemContextRequested = true; }

    private void DrawCardSelection(Vector2 min, Vector2 max)
    {
        var draw = Gui.GetWindowDrawList();
        var accent = Gui.ColorConvertFloat4ToU32(ImGuiTheme.Accent);
        draw.AddRectFilled(min + new Vector2(2), max - new Vector2(2), Gui.ColorConvertFloat4ToU32(new Vector4(ImGuiTheme.Accent.X, ImGuiTheme.Accent.Y, ImGuiTheme.Accent.Z, .16f)), 3);
        draw.AddRect(min + new Vector2(2), max - new Vector2(2), accent, 3, ImDrawFlags.None, 3 * UiScale);
        var badge = new Vector2(max.X - 23 * UiScale, min.Y + 7 * UiScale);
        draw.AddRectFilled(badge, badge + new Vector2(16 * UiScale), accent, 3);
        draw.AddLine(badge + new Vector2(3, 8) * UiScale, badge + new Vector2(6, 11) * UiScale, 0xFFFFFFFF, 2);
        draw.AddLine(badge + new Vector2(6, 11) * UiScale, badge + new Vector2(13, 4) * UiScale, 0xFFFFFFFF, 2);
        Elements["selection/card"] = new(min.X, min.Y, max.X, max.Y);
    }
    private static void DrawFavoriteMarker(Vector2 center)
    {
        var draw = Gui.GetWindowDrawList();
        draw.AddCircleFilled(center, 9 * UiScale, Gui.ColorConvertFloat4ToU32(ImGuiTheme.Input));
        for (var index = 0; index < 10; index++)
        {
            Vector2 Point(int vertex) { var angle = vertex * MathF.PI / 5 - MathF.PI / 2; return center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (vertex % 2 == 0 ? 7 : 3.2f) * UiScale; }
            draw.AddLine(Point(index), Point((index + 1) % 10), Gui.ColorConvertFloat4ToU32(ImGuiTheme.Warning), 1.4f * UiScale);
        }
    }
    private static void PushRowSelection()
    {
        Gui.PushStyleColor(ImGuiCol.Header, ImGuiTheme.Rgb(0x815022));
        Gui.PushStyleColor(ImGuiCol.HeaderHovered, ImGuiTheme.Rgb(0x976031));
        Gui.PushStyleColor(ImGuiCol.HeaderActive, ImGuiTheme.Rgb(0xAA6A30));
    }
    private void ItemMenuCommand(string id, string label, object item, string command)
    {
        var action = UiBinding.Read(item, "{Binding " + command + "}") as ICommand;
        if (action is null) return;
        if (Gui.MenuItem(label, "", false, action.CanExecute(null))) Execute(item, "{Binding " + command + "}");
        Record("item-context/" + id);
    }
    private void DrawItemContextMenus(MainWindowViewModel vm)
    {
        if (_itemContextRequested) { Gui.OpenPopup("item-context"); _itemContextRequested = false; }
        if (!Gui.BeginPopup("item-context")) return;
        if (_contextItem is { } item)
        {
            var selected = _contextSelectionItems;
            if (selected.Length == 0) selected = [item];
            Gui.TextDisabled(selected.Length > 1 ? selected.Length + " éléments sélectionnés" : Elide(ResourceName(item), 280 * UiScale));
            Record("item-context/count/" + selected.Length); Gui.Separator();
            Gui.BeginDisabled(selected.Length > 1);
            ItemMenuCommand("open", "Ouvrir la fiche", item, _contextOpenCommand);
            ItemMenuCommand("launch", "Lancer / ouvrir le projet", item, "LaunchCommand");
            ItemMenuCommand("download", "Télécharger les versions…", item, "DownloadCommand");
            ItemMenuCommand("install", "Installer…", item, item is FabStoredDownloadViewModel ? "InstallCommand" : item is FabLocalContentPackCardViewModel ? "UseForImportCommand" : "OpenInstallCommand");
            ItemMenuCommand("collections", "Collections…", item, item is FabLocalContentPackCardViewModel ? "EditCollectionsCommand" : "OpenCollectionsCommand");
            ItemMenuCommand("web", "Page officielle", item, "OpenListingCommand");
            ItemMenuCommand("folder", "Ouvrir le dossier", item, "OpenFolderCommand");
            Gui.EndDisabled();
            Gui.Separator();
            if (item is not ProjectCardViewModel and not LocalEngineProjectCardViewModel)
            { if (Gui.MenuItem("Groupes de packs…")) OpenPackGroups(vm, selected); Record("item-context/pack-groups"); }
            var addFavorite = !selected.All(vm.IsResourceFavorite);
            if (Gui.MenuItem(addFavorite ? "Ajouter aux favoris" : "Retirer des favoris"))
                _actions.Enqueue(() => vm.SetResourceFavorites(selected, addFavorite));
            Record("item-context/favorite");
            Gui.BeginDisabled(vm.IsResourceSizeBusy);
            if (Gui.MenuItem("Analyser la taille")) _actions.Enqueue(() => _ = vm.AnalyzeResourceSizesAsync(item is ProjectCardViewModel or LocalEngineProjectCardViewModel ? "projects" : "packs", true, selected));
            Record("item-context/size"); Gui.EndDisabled();
        }
        Gui.EndPopup();
    }
    private void DrawSizeAnalysisMenu(MainWindowViewModel vm, string scope)
    {
        Gui.PushID("size-" + scope);
        Gui.BeginDisabled(vm.IsResourceSizeBusy);
        if (Gui.Button("Analyser les tailles")) Gui.OpenPopup("size-options");
        Record("size-analysis/" + scope);
        if (Gui.BeginPopup("size-options"))
        {
            if (Gui.MenuItem("Compléter les tailles manquantes")) _actions.Enqueue(() => _ = vm.AnalyzeResourceSizesAsync(scope, false));
            Record("size-analysis/missing/" + scope);
            if (Gui.MenuItem("Tout revérifier sur disque")) _actions.Enqueue(() => _ = vm.AnalyzeResourceSizesAsync(scope, true));
            Record("size-analysis/full/" + scope);
            Gui.TextDisabled("Annulable dans Actions · aucun fichier modifié"); Gui.EndPopup();
        }
        Gui.EndDisabled(); Gui.PopID();
    }
}
