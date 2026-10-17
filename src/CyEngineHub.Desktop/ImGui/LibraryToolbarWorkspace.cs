using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private static float ButtonWidth(string label) => Gui.CalcTextSize(label).X + Gui.GetStyle().FramePadding.X * 2;
    private static float CheckWidth(string label) => Gui.CalcTextSize(label).X + Gui.GetFrameHeight() + Gui.GetStyle().ItemInnerSpacing.X;
    private static void ToolbarItem(ref bool first, float right, float width)
    {
        if (!first && Gui.GetItemRectMax().X + Gui.GetStyle().ItemSpacing.X + width <= right) Gui.SameLine();
        first = false;
    }
    private void LibraryCheck(MainWindowViewModel vm, string label, string property)
    {
        var value = Flag(vm, property);
        if (Gui.Checkbox(label + "###" + property, ref value)) Set(vm, "{Binding " + property + "}", value);
        Record("check/" + property);
    }
    private void DrawLibrarySearchToolbar(MainWindowViewModel vm, UiNode node)
    {
        var search = node.Children.Single(child => UiBinding.Path(child["Text"]) == "LibrarySearchQuery");
        var sort = node.Children.Single(child => child.Kind == "ComboBox");
        var advanced = node.Children.Single(child => UiBinding.Path(child["Command"]) == "ToggleAdvancedLibraryFilterCommand");
        var sortWidth = 170 * UiScale;
        var buttonWidth = NaturalWidth(advanced, vm);
        var wide = Gui.GetContentRegionAvail().X > sortWidth + buttonWidth + 180 * UiScale;
        if (wide && Gui.BeginTable("library-search-toolbar", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            Gui.TableSetupColumn("Recherche", ImGuiTableColumnFlags.WidthStretch);
            Gui.TableSetupColumn("Tri", ImGuiTableColumnFlags.WidthFixed, sortWidth);
            Gui.TableSetupColumn("Filtres", ImGuiTableColumnFlags.WidthFixed, buttonWidth);
            Gui.TableNextColumn(); DrawNode(search, vm);
            Gui.TableNextColumn(); DrawNode(sort, vm);
            Gui.TableNextColumn(); DrawNode(advanced, vm);
            Gui.EndTable();
        }
        else
        {
            DrawNode(search, vm);
            var right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
            Gui.SetNextItemWidth(sortWidth);
            // DrawCombo normally fills its cell; supply a bounded child on narrow windows.
            Gui.BeginChild("library-sort", new(sortWidth, Gui.GetFrameHeight()), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar);
            DrawNode(sort, vm); Gui.EndChild();
            if (Gui.GetItemRectMax().X + Gui.GetStyle().ItemSpacing.X + buttonWidth <= right) Gui.SameLine();
            DrawNode(advanced, vm);
        }
        if (vm.HasActiveLibraryFilters)
        {
            Gui.TextDisabled(vm.ActiveLibraryFilterSummary); Gui.SameLine();
            ActionButton(vm, "Réinitialiser", "ClearAdvancedLibraryFiltersCommand");
        }
    }
    private void DrawLibraryViewModes(MainWindowViewModel vm)
    {
        var first = true;
        DrawLibraryViewModes(vm, ref first, Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X);
    }
    private void DrawLibraryViewModes(MainWindowViewModel vm, ref bool first, float right)
    {
        foreach (var mode in new[] { (Id: "large", Label: "Cartes", Command: "ShowLargeLibraryCardsCommand", Selected: !vm.IsLibraryRowsMode && !vm.IsLibraryImageOnlyCards),
            (Id: "images", Label: "Image + nom", Command: "ShowImageOnlyLibraryCardsCommand", Selected: vm.IsLibraryImageOnlyCards && !vm.IsLibraryRowsMode),
            (Id: "rows", Label: "Lignes", Command: "ShowLibraryRowsCommand", Selected: vm.IsLibraryRowsMode && !vm.IsLibraryRowImages),
            (Id: "thumbs", Label: "Lignes + image", Command: "ShowLibraryThumbnailRowsCommand", Selected: vm.IsLibraryRowsMode && vm.IsLibraryRowImages) })
        {
            ToolbarItem(ref first, right, ButtonWidth(mode.Label));
            if (mode.Selected)
            {
                Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
                Gui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiTheme.Selected);
                Gui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiTheme.Primary);
            }
            ActionButton(vm, mode.Label, mode.Command);
            Record("library-view/" + mode.Id);
            if (mode.Selected) { Record("library-view-selected/" + mode.Id); Gui.PopStyleColor(3); }
        }
    }
    private void DrawLibraryDisplayToolbar(MainWindowViewModel vm)
    {
        var start = Gui.GetCursorScreenPos().X;
        var width = Gui.GetContentRegionAvail().X;
        var right = start + width;
        var first = true;
        Gui.TextDisabled(vm.LibraryResultsSummary); first = false;
        ToolbarItem(ref first, right, CheckWidth("Téléchargés"));
        LibraryCheck(vm, "Téléchargés", "ShowDownloadedLibraryAssets");
        ToolbarItem(ref first, right, CheckWidth("Non téléchargés"));
        LibraryCheck(vm, "Non téléchargés", "ShowUndownloadedLibraryAssets");
        var groupWidth = CheckWidth("Afficher les masqués") + ButtonWidth("Cartes") + ButtonWidth("Image + nom") + ButtonWidth("Lignes") + ButtonWidth("Lignes + image") +
            ButtonWidth(vm.IsLibraryInfiniteMode ? "Pages" : "Infini") + Gui.GetStyle().ItemSpacing.X * 5 + UiScale;
        if (vm.IsLibraryPagedMode) groupWidth += ButtonWidth("<") + Gui.CalcTextSize(vm.LibraryPageSummary).X + ButtonWidth(">") + Gui.GetStyle().ItemSpacing.X * 3;
        if (Gui.GetItemRectMax().X + Gui.GetStyle().ItemSpacing.X + groupWidth <= right)
        {
            Gui.SameLine();
            // Absolute positioning avoids applying the enclosing group's indent twice.
            Gui.SetCursorScreenPos(new(right - groupWidth, Gui.GetCursorScreenPos().Y));
        }
        else if (groupWidth <= width) Gui.SetCursorScreenPos(new(right - groupWidth, Gui.GetCursorScreenPos().Y));
        first = true;
        LibraryCheck(vm, "Afficher les masqués", "ShowHiddenFabAssets"); first = false;
        DrawLibraryViewModes(vm, ref first, right);
        ToolbarItem(ref first, right, ButtonWidth(vm.IsLibraryInfiniteMode ? "Pages" : "Infini"));
        ActionButton(vm, vm.IsLibraryInfiniteMode ? "Pages" : "Infini", vm.IsLibraryInfiniteMode ? "ShowPagedLibraryCommand" : "ShowInfiniteLibraryCommand");
        if (vm.IsLibraryPagedMode)
        {
            var pagerWidth = ButtonWidth("<") + Gui.CalcTextSize(vm.LibraryPageSummary).X + ButtonWidth(">") + Gui.GetStyle().ItemSpacing.X * 2;
            ToolbarItem(ref first, right, pagerWidth);
            Gui.BeginGroup();
            ActionButton(vm, "<", "PreviousLibraryPageCommand"); Gui.SameLine();
            Gui.TextDisabled(vm.LibraryPageSummary); Gui.SameLine();
            ActionButton(vm, ">", "NextLibraryPageCommand");
            Gui.EndGroup();
        }
    }
}
