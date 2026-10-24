using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private string? _remoteRowsSortValue;
    private void DrawRemoteAssetRows(MainWindowViewModel vm, string binding)
    {
        if (!Gui.BeginTable("library-assets", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerH,
            new(0, System.Math.Max(120, Gui.GetContentRegionAvail().Y - 4)))) return;
        Gui.TableSetupColumn("Nom", ImGuiTableColumnFlags.WidthStretch, 3);
        Gui.TableSetupColumn("Catégorie", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        Gui.TableSetupColumn("Versions", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        Gui.TableSetupColumn("Acquisition", ImGuiTableColumnFlags.WidthFixed, 140 * UiScale);
        Gui.TableSetupColumn("Taille locale", ImGuiTableColumnFlags.WidthFixed, 110 * UiScale);
        Gui.TableSetupColumn("Mise à jour", ImGuiTableColumnFlags.WidthStretch, 1.8f);
        Gui.TableSetupScrollFreeze(0, 1);
        if (_remoteRowsSortValue != vm.SelectedLibrarySort) _tableSorts.Remove("library-assets");
        var oldSort = _tableSorts.GetValueOrDefault("library-assets", (-1, false));
        var sort = SortableHeaders("library-assets", ["Nom", "Catégorie", "Versions", "Acquisition", "Taille locale", "Mise à jour"], unsortable: 5);
        if (sort != oldSort)
        {
            // The VM sorts the complete filtered catalog asynchronously before pagination.
            vm.SelectedLibrarySort = sort.Column switch
            {
                0 => sort.Descending ? "Nom · Z → A" : "Nom · A → Z",
                1 => sort.Descending ? "Catégorie · Z → A" : "Catégorie",
                2 => sort.Descending ? "Versions moteur" : "Versions moteur · croissant",
                4 => sort.Descending ? "Taille locale · décroissant" : "Taille locale · croissant",
                _ => sort.Descending ? "Acquisition · récent" : "Acquisition · ancien",
            };
        }
        _remoteRowsSortValue = vm.SelectedLibrarySort;
        var height = vm.IsLibraryRowImages ? 48 * UiScale + Gui.GetStyle().CellPadding.Y * 2 : System.Math.Max(30 * UiScale, Gui.GetTextLineHeight() + Gui.GetStyle().CellPadding.Y * 2);
        Clipped(_catalogSlots.Length, height, index =>
        {
            var slot = _catalogSlots[index];
            slot.Row.Realize(false);
            if (_seen.Add(slot.Row)) VisibleRows[binding] = VisibleRows.GetValueOrDefault(binding) + 1;
            _realized.Add(slot.Row);
            var card = slot.Row.Cards[slot.Index];
            if (vm.IsLibraryRowImages) card.EnsureThumbnailLoaded();
            Gui.PushID(card.Asset.AssetId); Gui.TableNextRow(ImGuiTableRowFlags.None, height); Gui.TableNextColumn();
            LibraryRowName(card, card.Name, "asset/" + card.Asset.AssetId, "OpenDetailsCommand", height,
                vm.IsLibraryRowImages ? card.Thumbnail : null, vm.IsLibraryRowImages, card.Source);
            Record("library-row/" + card.Asset.AssetId);
            Gui.TableNextColumn(); Gui.TextUnformatted(Elide(card.CategoryLabel, Gui.GetContentRegionAvail().X));
            Gui.TableNextColumn(); Gui.TextUnformatted(Elide(card.AllEngineVersions, Gui.GetContentRegionAvail().X));
            if (Gui.IsItemHovered()) Gui.SetTooltip(card.AllEngineVersions);
            Gui.TableNextColumn(); Gui.TextDisabled(card.Asset.AcquiredAt?.ToLocalTime().ToString("d", System.Globalization.CultureInfo.CurrentCulture) ?? "Inconnue");
            Gui.TableNextColumn(); Gui.TextDisabled(vm.ResourceSizeBytes(card) is not null ? vm.ResourceSizeLabel(card) : "—");
            if (Gui.IsItemHovered()) Gui.SetTooltip(vm.ResourceSizeEvidence(card));
            Gui.TableNextColumn();
            Gui.TextColored(Color(card, "{Binding UpdateForeground}", ImGuiTheme.Muted), Elide(card.UpdateLabel, Gui.GetContentRegionAvail().X));
            if (Gui.IsItemHovered()) Gui.SetTooltip(card.UpdateLabel);
            Gui.PopID();
        });
        if (Gui.GetScrollMaxY() - Gui.GetScrollY() < 500) _actions.Enqueue(vm.LoadMoreLibraryItems);
        Gui.EndTable(); Record("library-rows-pane");
    }
    private void LibraryRowName(object item, string name, string id, string command, float height,
        Avalonia.Media.Imaging.Bitmap? bitmap, bool image, string placeholder)
    {
        var position = Gui.GetCursorScreenPos();
        PushRowSelection();
        if (Gui.Selectable("###library-row", IsItemSelected(item), ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick,
            new(0, height - Gui.GetStyle().CellPadding.Y * 2)))
        {
            SelectItem(item);
            if (Gui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Execute(item, "{Binding " + command + "}");
        }
        if (Gui.IsItemClicked(ImGuiMouseButton.Right)) RequestItemContext(item, command);
        if (IsItemSelected(item))
        {
            Gui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, Gui.ColorConvertFloat4ToU32(ImGuiTheme.Rgb(0x815022)));
            Record("selection/row/" + id);
            Record("selection/item/" + MainWindowViewModel.ResourceIdentity(item));
        }
        var rect = new System.Numerics.Vector4(Gui.GetItemRectMin().X, Gui.GetItemRectMin().Y, Gui.GetItemRectMax().X, Gui.GetItemRectMax().Y);
        Gui.PopStyleColor(3);
        Gui.SetCursorScreenPos(position);
        if (image)
        {
            Gui.BeginGroup();
            Gui.PushStyleVar(ImGuiStyleVar.WindowPadding, System.Numerics.Vector2.Zero);
            Gui.BeginChild("row-thumbnail", new(72 * UiScale, 48 * UiScale), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoInputs);
            CardImage(bitmap, 48 * UiScale, placeholder.ToUpperInvariant());
            Gui.EndChild(); Gui.PopStyleVar(); Gui.EndGroup();
            Record("library-row-image/" + id);
            Gui.SameLine();
        }
        var favorite = _root is MainWindowViewModel favoriteVm && favoriteVm.IsResourceFavorite(item);
        if (favorite && !image) Gui.SetCursorScreenPos(Gui.GetCursorScreenPos() + new System.Numerics.Vector2(22 * UiScale, 0));
        Gui.TextUnformatted(Elide(name, Gui.GetContentRegionAvail().X));
        if (Gui.IsItemHovered()) Gui.SetTooltip(name);
        if (favorite) DrawFavoriteMarker(position + new System.Numerics.Vector2(8, 9) * UiScale);
        Elements["library-select/" + id] = rect;
    }
    private void DrawLocalLibraryRows(object[] items, string binding, bool images)
    {
        var id = "library-local/" + binding;
        if (!Gui.BeginTable(id, 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
            new(0, System.Math.Max(120, Gui.GetContentRegionAvail().Y - 4)))) return;
        Gui.TableSetupColumn("Nom", ImGuiTableColumnFlags.WidthStretch, 3);
        Gui.TableSetupColumn("Catégorie / format", ImGuiTableColumnFlags.WidthStretch, 2);
        Gui.TableSetupScrollFreeze(0, 1); SortableHeaders(id, ["Nom", "Catégorie / format"]);
        string Category(object item) => item is ConnectorAssetViewModel connector ? connector.Details : Value(item, "Category");
        if (_root is MainWindowViewModel favorites)
        {
            items = items.Where(item => !favorites.FavoritesOnly || favorites.IsResourceFavorite(item)).ToArray();
            if (favorites.FavoritesFirst) items = items.OrderByDescending(favorites.IsResourceFavorite).ToArray();
        }
        items = SortTableItems(id, items, (item, column) => column == 0 ? Value(item, "Name") : Category(item));
        if (_root is MainWindowViewModel firstVm && firstVm.FavoritesFirst) items = items.OrderByDescending(firstVm.IsResourceFavorite).ToArray();
        SynchronizeItemSelection(LibrarySelectionScope(binding), items);
        var height = images ? 48 * UiScale + Gui.GetStyle().CellPadding.Y * 2 : System.Math.Max(30 * UiScale, Gui.GetTextLineHeight() + Gui.GetStyle().CellPadding.Y * 2);
        Clipped(items.Length, height, index =>
        {
            var item = items[index];
            if (images && item is QuixelCollectionCardViewModel collection)
            { collection.EnsureThumbnailLoaded(); _collections.Add(collection); _seenCollections.Add(collection); }
            Gui.PushID(index); Gui.TableNextRow(ImGuiTableRowFlags.None, height); Gui.TableNextColumn();
            var stableId = MainWindowViewModel.ResourceIdentity(item);
            LibraryRowName(item, Value(item, "Name"), binding + "/" + stableId,
                item is QuixelCollectionCardViewModel or ConnectorAssetViewModel ? "OpenCommand" : item is FabLocalContentPackCardViewModel ? "UseForImportCommand" : "UseCommand", height,
                images ? UiBinding.Read(item, "{Binding Thumbnail}") as Avalonia.Media.Imaging.Bitmap : null, images, "PACK");
            Record("library-local-row/" + Value(item, "Name"));
            Gui.TableNextColumn(); Gui.TextUnformatted(Elide(Category(item) + " · " + (_root as MainWindowViewModel)?.ResourceSizeLabel(item), Gui.GetContentRegionAvail().X)); Gui.PopID();
            VisibleRows[binding] = VisibleRows.GetValueOrDefault(binding) + 1;
        });
        Gui.EndTable();
    }
}
