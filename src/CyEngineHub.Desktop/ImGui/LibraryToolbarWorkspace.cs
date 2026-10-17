using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private static float ButtonWidth(string label) => Gui.CalcTextSize(label).X + Gui.GetStyle().FramePadding.X * 2;
    private static void ToolbarItem(ref bool first, float right, float width)
    {
        if (!first && Gui.GetItemRectMax().X + Gui.GetStyle().ItemSpacing.X + width <= right) Gui.SameLine();
        first = false;
    }
    private void LibraryToggle(MainWindowViewModel vm, string label, string property)
    {
        var value = Flag(vm, property);
        if (value)
        {
            Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            Gui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiTheme.Selected);
            Gui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiTheme.Primary);
        }
        if (Gui.Button(label + "###" + property)) Set(vm, "{Binding " + property + "}", !value);
        Record("library-filter/" + property);
        if (value) { Record("library-filter-selected/" + property); Gui.PopStyleColor(3); }
        Record("check/" + property);
    }
    private void ToolbarGroup(string id, ref bool first, float right, float width, Action draw, bool alignRight = false)
    {
        ToolbarItem(ref first, right, width);
        width = Math.Min(width, Math.Max(1, right - Gui.GetCursorScreenPos().X));
        if (alignRight) Gui.SetCursorScreenPos(new(right - width, Gui.GetCursorScreenPos().Y));
        Gui.BeginChild(id, new(width, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY, ImGuiWindowFlags.NoScrollbar);
        draw();
        Gui.EndChild();
        Record("toolbar-group/" + id);
    }
    private void DrawFavoriteGroup(MainWindowViewModel vm, ref bool first, float right)
    {
        var padding = Gui.GetStyle().WindowPadding.X * 2 + 8 * UiScale;
        ToolbarGroup("library-favorites", ref first, right, ViewModeButtonSize() + padding, () =>
        {
            var selected = vm.FavoritesOnly;
            if (selected) Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            if (Gui.Button("###favorite-filter", new(ViewModeButtonSize(), Gui.GetFrameHeight())))
                Set(vm, "{Binding FavoritesOnly}", !selected);
            if (selected) Gui.PopStyleColor();
            Record("favorites/only"); Record("icon/favorites");
            if (selected) Record("favorites/selected");
            if (Gui.IsItemHovered()) Gui.SetTooltip(selected ? "Favoris uniquement · cliquer pour tout afficher" : "Afficher uniquement les favoris");
            var center = (Gui.GetItemRectMin() + Gui.GetItemRectMax()) / 2;
            var radius = Gui.GetFontSize() * .45f;
            var draw = Gui.GetWindowDrawList();
            var color = selected ? Gui.ColorConvertFloat4ToU32(ImGuiTheme.Warning) : Gui.GetColorU32(ImGuiCol.Text);
            System.Numerics.Vector2 Point(int vertex)
            {
                var angle = vertex * MathF.PI / 5 - MathF.PI / 2;
                return center + new System.Numerics.Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * (vertex % 2 == 0 ? 1 : .43f);
            }
            for (var index = 0; index < 10; index++)
            {
                if (selected) draw.AddTriangleFilled(center, Point(index), Point((index + 1) % 10), color);
                else draw.AddLine(Point(index), Point((index + 1) % 10), color, 1.1f * UiScale);
            }
        });
    }
    private void DrawLibraryCatalogHeader(MainWindowViewModel vm, UiNode node)
    {
        var heading = node.Children.Single(child => child["Grid.Column"] is null);
        var actions = node.Children.Where(child => child["Grid.Column"] == "1" && Visible(child, vm)).ToArray();
        var spacing = Gui.GetStyle().ItemSpacing.X;
        var width = ButtonWidth("Analyser les tailles") + actions.Sum(action => NaturalWidth(action, vm) + spacing) + 8 * UiScale;
        var wide = Gui.GetContentRegionAvail().X >= width + 135 * UiScale;
        if (wide && Gui.BeginTable("library-catalog-header", 2, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
        {
            Gui.TableSetupColumn("Compte", ImGuiTableColumnFlags.WidthStretch);
            Gui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, width);
            Gui.TableNextColumn(); DrawNode(heading, vm); Gui.TableNextColumn();
            DrawHeaderActions(); Gui.EndTable();
        }
        else
        {
            DrawNode(heading, vm);
            if (width < Gui.GetContentRegionAvail().X) Gui.SetCursorPosX(Gui.GetCursorPosX() + Gui.GetContentRegionAvail().X - width);
            DrawHeaderActions();
        }
        void DrawHeaderActions()
        {
            var first = true;
            var right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
            foreach (var action in actions)
            {
                ToolbarItem(ref first, right, NaturalWidth(action, vm)); DrawNode(action, vm);
            }
            ToolbarItem(ref first, right, ButtonWidth("Analyser les tailles")); DrawSizeAnalysisMenu(vm, "packs");
        }
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
        var right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
        ToolbarGroup("library-display", ref first, right, ViewModeWidth() + Gui.GetStyle().WindowPadding.X * 2 + 8 * UiScale, () =>
        {
            var innerFirst = true;
            DrawLibraryViewModes(vm, ref innerFirst, Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X);
        });
        DrawFavoriteGroup(vm, ref first, right);
    }
    private static float ViewModeWidth() => ViewModeButtonSize() * 4 + Gui.GetStyle().ItemSpacing.X * 3;
    private void DrawLibraryViewModes(MainWindowViewModel vm, ref bool first, float right)
    {
        foreach (var mode in new[] { (Id: "large", Label: "Cartes", Command: "ShowLargeLibraryCardsCommand", Selected: !vm.IsLibraryRowsMode && !vm.IsLibraryImageOnlyCards),
            (Id: "images", Label: "Image + nom", Command: "ShowImageOnlyLibraryCardsCommand", Selected: vm.IsLibraryImageOnlyCards && !vm.IsLibraryRowsMode),
            (Id: "rows", Label: "Lignes", Command: "ShowLibraryRowsCommand", Selected: vm.IsLibraryRowsMode && !vm.IsLibraryRowImages),
            (Id: "thumbs", Label: "Lignes + image", Command: "ShowLibraryThumbnailRowsCommand", Selected: vm.IsLibraryRowsMode && vm.IsLibraryRowImages) })
        {
            ToolbarItem(ref first, right, ViewModeButtonSize());
            var command = UiBinding.Read(vm, "{Binding " + mode.Command + "}") as System.Windows.Input.ICommand;
            ViewModeIconButton("library/" + mode.Id, mode.Id switch { "large" => 0, "images" => 1, "rows" => 2, _ => 3 },
                mode.Selected, () => Execute(vm, "{Binding " + mode.Command + "}"), command?.CanExecute(null) == true);
            Record("action/" + mode.Command);
            Record("library-view/" + mode.Id);
            if (mode.Selected) Record("library-view-selected/" + mode.Id);
        }
    }
    private void DrawLibraryDisplayToolbar(MainWindowViewModel vm)
    {
        var right = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
        Gui.TextDisabled(vm.LibraryResultsSummary);
        var first = true;
        var padding = Gui.GetStyle().WindowPadding.X * 2 + 8 * UiScale;
        var spacing = Gui.GetStyle().ItemSpacing.X;
        ToolbarGroup("library-filters", ref first, right,
            ButtonWidth("Téléchargés") + ButtonWidth("Non téléchargés") + ButtonWidth("Masqués") + spacing * 2 + padding, () =>
        {
            var innerFirst = true;
            var innerRight = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
            foreach (var filter in new[] { (Label: "Téléchargés", Property: "ShowDownloadedLibraryAssets"),
                (Label: "Non téléchargés", Property: "ShowUndownloadedLibraryAssets"), (Label: "Masqués", Property: "ShowHiddenFabAssets") })
            {
                ToolbarItem(ref innerFirst, innerRight, ButtonWidth(filter.Label));
                LibraryToggle(vm, filter.Label, filter.Property);
            }
        });
        ToolbarGroup("library-display", ref first, right, ViewModeWidth() + padding, () =>
        {
            var innerFirst = true;
            DrawLibraryViewModes(vm, ref innerFirst, Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X);
        });
        DrawFavoriteGroup(vm, ref first, right);
        var modeLabel = vm.IsLibraryInfiniteMode ? "Pages" : "Infini";
        var pagerWidth = ButtonWidth(modeLabel) + (vm.IsLibraryPagedMode ? ButtonWidth("<") + Gui.CalcTextSize(vm.LibraryPageSummary).X + ButtonWidth(">") + spacing * 3 : 0);
        ToolbarGroup("library-pagination", ref first, right, pagerWidth + padding, () =>
        {
            ActionButton(vm, modeLabel, vm.IsLibraryInfiniteMode ? "ShowPagedLibraryCommand" : "ShowInfiniteLibraryCommand");
            if (vm.IsLibraryPagedMode)
            {
                Gui.SameLine(); ActionButton(vm, "<", "PreviousLibraryPageCommand"); Gui.SameLine();
                Gui.TextDisabled(vm.LibraryPageSummary); Gui.SameLine();
                ActionButton(vm, ">", "NextLibraryPageCommand");
            }
        }, alignRight: true);
    }
}
