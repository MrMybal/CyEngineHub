using System.Numerics;
using Avalonia.Media.Imaging;
using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private const float UiScale = UiCardGrid.InterfaceScale;
    private RemoteFabAssetRowViewModel[] _catalogRows = [];
    private (RemoteFabAssetRowViewModel Row, int Index)[] _catalogSlots = [];

    private void CardInteraction(object item, string id, Vector4 actions, Action select, bool selected, string command = "OpenDetailsCommand")
    {
        var min = Gui.GetWindowPos();
        var max = min + Gui.GetWindowSize();
        if (Gui.IsRectVisible(min, max)) Elements["card/body/" + id] = new(min.X, min.Y, max.X, max.Y);
        var mouse = Gui.GetMousePos();
        var onActions = mouse.X >= actions.X && mouse.X < actions.Z && mouse.Y >= actions.Y && mouse.Y < actions.W;
        var hovered = !onActions && Gui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && mouse.X >= min.X && mouse.X < max.X && mouse.Y >= min.Y && mouse.Y < max.Y;
        if (hovered)
        {
            Gui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (Gui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                select();
                if (Gui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) Execute(item, "{Binding " + command + "}");
            }
        }
        if (hovered && Gui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            if (item is EngineCardViewModel or LocalEngineInstallationCardViewModel or EngineDownloadCardViewModel) RequestEngineContext(item);
            else RequestItemContext(item, command);
        }
        selected = item is EngineCardViewModel or LocalEngineInstallationCardViewModel or EngineDownloadCardViewModel
            ? IsResourceSelected(item, true) : IsItemSelected(item);
        if (selected) DrawCardSelection(min, max);
        else if (hovered) Gui.GetWindowDrawList().AddRect(min + new Vector2(2), max - new Vector2(2), Gui.ColorConvertFloat4ToU32(ImGuiTheme.Muted), 3);
        if (_root is MainWindowViewModel favorites && favorites.IsResourceFavorite(item)) DrawFavoriteMarker(min + new Vector2(13, 13) * UiScale);
        if (selected) Record("selection/item/" + MainWindowViewModel.ResourceIdentity(item));
        if (hovered && _root is MainWindowViewModel sizes && sizes.ResourceSizeBytes(item) is not null)
            Gui.SetTooltip(ResourceName(item) + "\n" + sizes.ResourceSizeLabel(item) + "\n" + sizes.ResourceSizeEvidence(item));
    }

    private void DrawFixedCards(string id, int count, float width, float height, Action<int> draw, string? catalogBinding = null)
    {
        Gui.BeginChild(id, new(0, 0), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        if (catalogBinding is not null && _scrollRequests.Remove(catalogBinding)) Gui.SetScrollY(Gui.GetScrollMaxY());
        var layout = UiCardGrid.Create(Gui.GetContentRegionAvail().X, width, height, 10 * UiScale);
        var origin = Gui.GetCursorPos();
        var range = layout.VisibleRows(count, Gui.GetScrollY(), Gui.GetWindowSize().Y);
        for (var row = range.Start; row < range.End; row++)
        {
            for (var column = 0; column < layout.Columns; column++)
            {
                var index = row * layout.Columns + column;
                if (index >= count) break;
                Gui.SetCursorPos(origin + new Vector2(column * (width + layout.Gap), row * layout.RowStride));
                Gui.PushID(index);
                draw(index);
                Gui.PopID();
            }
        }
        Gui.SetCursorPos(origin + new Vector2(0, Math.Max(0, layout.Rows(count) * layout.RowStride - layout.Gap)));
        Gui.Dummy(new(Math.Max(1, Math.Min(count, layout.Columns) * (width + layout.Gap) - layout.Gap), 1));
        if (catalogBinding == "PagedRemoteFabRows" && Gui.GetScrollMaxY() - Gui.GetScrollY() < 500 && _root is MainWindowViewModel vm)
            _actions.Enqueue(vm.LoadMoreLibraryItems);
        Gui.EndChild();
    }

    private void DrawAssetCatalog(RemoteFabAssetRowViewModel[] rows, string binding)
    {
        // Reflow independently of the legacy VM's row grouping. Only visible rows load thumbnails.
        if (!_catalogRows.SequenceEqual(rows))
        {
            _catalogRows = rows;
            _catalogSlots = rows.SelectMany(row => Enumerable.Range(0, row.AssetCount).Select(index => (Row: row, Index: index))).ToArray();
        }
        SynchronizeItemSelection(LibrarySelectionScope(binding),
            _catalogSlots.Select(slot => (object)slot.Row.AssetAt(slot.Index)));
        if (_root is MainWindowViewModel { IsLibraryRowsMode: true } rowVm) { DrawRemoteAssetRows(rowVm, binding); return; }
        var small = _root is MainWindowViewModel { IsLibraryCompactCards: true };
        var minimal = _root is MainWindowViewModel { IsLibraryImageOnlyCards: true };
        var width = (minimal ? 180 : small ? 230 : 310) * UiScale;
        var height = (minimal ? 150 : small ? 310 : 350) * UiScale;
        DrawFixedCards("asset-grid", _catalogSlots.Length, width, height, index =>
        {
            var slot = _catalogSlots[index];
            slot.Row.Realize();
            if (_seen.Add(slot.Row)) VisibleRows[binding] = VisibleRows.GetValueOrDefault(binding) + 1;
            _realized.Add(slot.Row);
            var asset = slot.Row.Cards[slot.Index];
            asset.EnsureThumbnailLoaded();
            if (minimal) DrawMinimalLibraryCard(asset, asset.Name, asset.Thumbnail, width, height, "asset/" + asset.Asset.AssetId, asset.Source, "OpenDetailsCommand");
            else DrawAssetCard(asset, width, height, small);
        }, binding);
    }

    private void DrawMinimalLibraryCard(object item, string name, Bitmap? thumbnail, float width, float height, string id, string placeholder, string command)
    {
        Gui.BeginChild("minimal-asset", new(width, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        CardImage(thumbnail, 95 * UiScale, placeholder.ToUpperInvariant());
        Record("minimal/image/" + id);
        CardTitle(name, 2);
        CardInteraction(item, id, new(float.PositiveInfinity), () => SelectItem(item), IsItemSelected(item), command);
        if (IsItemSelected(item)) Record("minimal/selected/" + id);
        Gui.EndChild();
        Record("minimal/card/" + id);
        if (item is RemoteFabAssetCardViewModel asset) Record("asset/card/" + asset.Asset.AssetId);
    }

    private void DrawMinimalLibraryCatalog(object[] items, string binding)
    {
        if (_root is MainWindowViewModel favorites)
        {
            items = items.Where(item => !favorites.FavoritesOnly || favorites.IsResourceFavorite(item)).ToArray();
            if (favorites.FavoritesFirst) items = items.OrderByDescending(favorites.IsResourceFavorite).ToArray();
        }
        SynchronizeItemSelection(LibrarySelectionScope(binding), items);
        DrawFixedCards("minimal-library-" + binding, items.Length, 180 * UiScale, 150 * UiScale, index =>
        {
            var item = items[index];
            if (item is QuixelCollectionCardViewModel collection)
            {
                collection.EnsureThumbnailLoaded(); _seenCollections.Add(collection); _collections.Add(collection);
            }
            var command = item is QuixelCollectionCardViewModel or ConnectorAssetViewModel ? "OpenCommand" : item is FabLocalContentPackCardViewModel ? "UseForImportCommand" : "UseCommand";
            var id = binding + "/" + MainWindowViewModel.ResourceIdentity(item);
            DrawMinimalLibraryCard(item, Value(item, "Name"), UiBinding.Read(item, "{Binding Thumbnail}") as Bitmap,
                180 * UiScale, 150 * UiScale, id, item is ConnectorAssetViewModel ? "PACK" : "MEGASCANS", command);
            VisibleRows[binding] = VisibleRows.GetValueOrDefault(binding) + 1;
        }, binding);
    }

    private void DrawAssetCard(RemoteFabAssetCardViewModel asset, float width, float height, bool small)
    {
        Gui.BeginChild("asset", new(width, height), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        CardImage(asset.Thumbnail, (small ? 105 : 150) * UiScale, asset.Source.ToUpperInvariant());
        Record("asset/image/" + asset.Asset.AssetId);
        CardTitle(asset.Name, 2);
        Record("asset/details/" + asset.Asset.AssetId);
        Gui.TextColored(Color(asset, "{Binding LocalStatusForeground}", ImGuiTheme.Muted), Elide(asset.LocalStatusLabel, Gui.GetContentRegionAvail().X));
        if (!string.IsNullOrWhiteSpace(asset.UpdateLabel))
        {
            Gui.TextColored(Color(asset, "{Binding UpdateForeground}", ImGuiTheme.Muted), Elide(asset.UpdateLabel, Gui.GetContentRegionAvail().X));
            if (Gui.IsItemHovered()) Gui.SetTooltip(asset.UpdateLabel);
        }
        Gui.TextColored(ImGuiTheme.Accent, Elide(asset.CategoryLabel, Gui.GetContentRegionAvail().X));
        Gui.TextDisabled(Elide(asset.AcquisitionLabel, Gui.GetContentRegionAvail().X));
        var footerY = height - Gui.GetStyle().WindowPadding.Y - Gui.GetFrameHeight() - 1;
        Gui.BeginChild("versions", new(0, Gui.GetTextLineHeightWithSpacing() * 2), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        var versionRight = Gui.GetCursorScreenPos().X + Gui.GetContentRegionAvail().X;
        var line = 0; var remaining = 0;
        for (var index = 0; index < asset.EngineVersionBadges.Count; index++)
        {
            var badge = asset.EngineVersionBadges[index];
            var label = "UE " + badge.Version;
            var badgeWidth = Gui.CalcTextSize(label).X;
            var reserve = Gui.CalcTextSize("+99").X + Gui.GetStyle().ItemSpacing.X;
            if (index > 0)
            {
                var nextX = Gui.GetItemRectMax().X + Gui.GetStyle().ItemSpacing.X;
                if (nextX + badgeWidth + reserve > versionRight)
                {
                    if (++line >= 2) { remaining = asset.EngineVersionBadges.Count - index; break; }
                }
                else Gui.SameLine();
            }
            Gui.TextColored(Color(badge, "{Binding Foreground}", ImGuiTheme.Muted), label);
            if (Gui.IsItemHovered()) Gui.SetTooltip(badge.ToolTip);
        }
        if (remaining > 0) { Gui.SameLine(); Gui.TextDisabled("+" + remaining); }
        Gui.EndChild();
        if (Gui.IsItemHovered()) Gui.SetTooltip("Versions disponibles : " + asset.AllEngineVersions + "\n" + asset.CollectionSummary);
        if (Gui.GetCursorPosY() + Gui.GetTextLineHeight() < footerY - 3)
            Gui.TextDisabled(Elide((_root as MainWindowViewModel)?.ResourceSizeBytes(asset) is not null
                ? (_root as MainWindowViewModel)!.ResourceSizeLabel(asset) + " · " + asset.CollectionSummary : asset.CollectionSummary, Gui.GetContentRegionAvail().X));
        Gui.SetCursorPosY(footerY);
        var actionsMin = Gui.GetCursorScreenPos();
        ActionButton(asset, "Fiche", "OpenDetailsCommand"); Gui.SameLine();
        ActionButton(asset, "Web", "OpenListingCommand"); Gui.SameLine();
        ActionButton(asset, "DL", "DownloadCommand");
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) Gui.SetTooltip("Choisir les versions à télécharger");
        Gui.SameLine(); ActionButton(asset, "Installer", "OpenInstallCommand");
        Gui.SameLine(); ActionButton(asset, "+", "OpenCollectionsCommand");
        var actionsMax = Gui.GetItemRectMax();
        CardInteraction(asset, "asset/" + asset.Asset.AssetId, new(actionsMin.X, actionsMin.Y, actionsMax.X, actionsMax.Y), () => SelectItem(asset), IsItemSelected(asset));
        if (IsItemSelected(asset)) Elements["card/selected/asset/" + asset.Asset.AssetId] = Elements.GetValueOrDefault("card/body/asset/" + asset.Asset.AssetId);
        Gui.EndChild();
        Record("asset/card/" + asset.Asset.AssetId);
    }

    private void CardImage(Bitmap? bitmap, float height, string placeholder)
    {
        var position = Gui.GetCursorScreenPos();
        var size = new Vector2(Gui.GetContentRegionAvail().X, height);
        var draw = Gui.GetWindowDrawList();
        draw.AddRectFilled(position, position + size, Gui.ColorConvertFloat4ToU32(ImGuiTheme.Input), 3);
        if (bitmap is not null)
        {
            var scale = Math.Min(size.X / Math.Max(1, bitmap.PixelSize.Width), size.Y / Math.Max(1, bitmap.PixelSize.Height));
            var imageSize = new Vector2(bitmap.PixelSize.Width, bitmap.PixelSize.Height) * scale;
            var min = position + (size - imageSize) / 2;
            var texture = _image(bitmap);
            if (texture != 0) draw.AddImage(texture, min, min + imageSize);
        }
        else draw.AddText(position + (size - Gui.CalcTextSize(placeholder)) / 2,
            Gui.ColorConvertFloat4ToU32(ImGuiTheme.Muted), placeholder);
        // The card owns pointer handling; a nested button would capture the second click.
        Gui.Dummy(size);
    }

    private static void CardTitle(string title, int lines)
    {
        var position = Gui.GetCursorScreenPos();
        var size = new Vector2(Gui.GetContentRegionAvail().X, Gui.GetTextLineHeight() * lines);
        Gui.Dummy(size);
        var display = title;
        if (Gui.CalcTextSize(display, false, size.X).Y > size.Y)
        {
            var min = 0; var max = title.Length;
            while (min < max)
            {
                var mid = (min + max + 1) / 2;
                if (Gui.CalcTextSize(title[..mid] + "…", false, size.X).Y <= size.Y) min = mid;
                else max = mid - 1;
            }
            display = title[..min] + "…";
        }
        var draw = Gui.GetWindowDrawList();
        draw.PushClipRect(position, position + size, true);
        draw.AddText(Gui.GetFont(), Gui.GetFontSize(), position, Gui.GetColorU32(ImGuiCol.Text), display, size.X);
        draw.PopClipRect();
        if (Gui.IsItemHovered()) Gui.SetTooltip(title);
    }

    private void DrawResourceRowImage(object item, string label, float width, float height)
    {
        var start = Gui.GetItemRectMin();
        var size = Math.Min(42 * UiScale, height - Gui.GetStyle().CellPadding.Y * 2);
        var draw = Gui.GetWindowDrawList();
        var end = start + new Vector2(width, height);
        draw.PushClipRect(start, end, true);
        draw.AddRectFilled(start, start + new Vector2(size), Gui.ColorConvertFloat4ToU32(ImGuiTheme.Input), 3);
        if (UiBinding.Read(item, "{Binding Thumbnail}") is Bitmap bitmap)
        {
            var factor = size / Math.Max(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            var imageSize = new Vector2(bitmap.PixelSize.Width, bitmap.PixelSize.Height) * factor;
            var min = start + (new Vector2(size) - imageSize) / 2;
            var texture = _image(bitmap);
            if (texture != 0) draw.AddImage(texture, min, min + imageSize);
        }
        draw.AddText(start + new Vector2(size + 8 * UiScale, Math.Max(0, (size - Gui.GetTextLineHeight()) / 2)),
            Gui.GetColorU32(ImGuiCol.Text), Elide(label, Math.Max(1, width - size - 8 * UiScale)));
        draw.PopClipRect();
        Record("resource-row/image/" + ResourceName(item));
    }

    private void DrawMinimalResourceCards(object[] items, bool engines)
    {
        DrawFixedCards("resource-minimal-cards", items.Length, 180 * UiScale, 150 * UiScale, index =>
        {
            var item = items[index];
            var selected = IsResourceSelected(item, engines);
            var official = engines && Flag(item, "IsOfficialEpic");
            if (official) Gui.PushStyleColor(ImGuiCol.ChildBg, Flag(item, "IsInstallationAvailable") ? ImGuiTheme.OfficialRow : ImGuiTheme.Input);
            Gui.BeginChild("minimal-resource", new(180 * UiScale, 150 * UiScale), ImGuiChildFlags.Borders,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            CardImage(UiBinding.Read(item, "{Binding Thumbnail}") as Bitmap, 95 * UiScale, engines ? "MOTEUR" : "PROJET");
            CardTitle(ResourceName(item), 2);
            CardInteraction(item, "resource/" + ResourceName(item), new(float.PositiveInfinity), () => SelectResource(item, items, engines), selected);
            if (selected) Elements["card/selected/resource/" + ResourceName(item)] = Elements.GetValueOrDefault("card/body/resource/" + ResourceName(item));
            if (engines && Gui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && Gui.IsMouseClicked(ImGuiMouseButton.Right)) RequestEngineContext(item);
            if (engines && selected) Elements["engine/selected/" + Value(item, "Path")] = Elements.GetValueOrDefault("card/body/resource/" + ResourceName(item));
            if (Gui.IsWindowHovered()) TextTooltip(item is EngineDownloadCardViewModel pending ? pending.StatusLabel + "\n" + pending.Message : ResourceVersion(item) + "\n" + Value(item, "Path"));
            if (official) Record("official-engine/" + Value(item, "Path"));
            Gui.EndChild();
            Record("resource/card/" + ResourceName(item));
            Record("resource/minimal/" + ResourceName(item));
            if (official) Gui.PopStyleColor();
        });
        if (items.Length == 0) Gui.TextDisabled("Aucun élément pour cette recherche.");
    }

    private void DrawResourceCards(object[] items, bool engines)
    {
        DrawFixedCards("resource-cards", items.Length, 270 * UiScale, 285 * UiScale, index =>
        {
            var item = items[index];
            var selected = IsResourceSelected(item, engines);
            var official = engines && Flag(item, "IsOfficialEpic");
            if (official) Gui.PushStyleColor(ImGuiCol.ChildBg, Flag(item, "IsInstallationAvailable") ? ImGuiTheme.OfficialRow : ImGuiTheme.Input);
            if (selected) Gui.PushStyleColor(ImGuiCol.Border, ImGuiTheme.Accent);
            Gui.BeginChild("resource", new(270 * UiScale, 285 * UiScale), ImGuiChildFlags.Borders,
                ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            CardImage(UiBinding.Read(item, "{Binding Thumbnail}") as Bitmap, (item is EngineDownloadCardViewModel ? 85 : 110) * UiScale, engines ? "UNREAL ENGINE" : "PROJET");
            CardTitle(ResourceName(item), 2);
            if (official) Record("official-engine/" + Value(item, "Path"));
            Record("row/" + ResourceName(item));
            Gui.TextColored(official ? ImGuiTheme.Success : ImGuiTheme.Accent, (official ? "OFFICIEL EPIC · " : "") + ResourceVersion(item) + (engines ? "" : " · " + ResourceType(item)));
            Gui.TextDisabled(Elide(Value(item, "Path"), Gui.GetContentRegionAvail().X));
            if (Gui.IsItemHovered()) Gui.SetTooltip(Value(item, "Path"));
            if (item is EngineDownloadCardViewModel pending)
            {
                Gui.TextColored(ImGuiTheme.Warning, pending.StatusLabel);
                OperationProgress((float)pending.ProgressPercent / 100, new(-1, 8), pending.StatusLabel);
                MutedText(pending.ProgressPercent.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " %");
                foreach (var line in pending.Message.Split('\n').Skip(1)) MutedText(Elide(line, Gui.GetContentRegionAvail().X));
                if (Gui.IsWindowHovered()) TextTooltip(pending.Message);
            }
            else Gui.TextDisabled(engines ? Value(item, "EffectiveSizeLabel") : (_root as MainWindowViewModel)?.ResourceSizeLabel(item) ?? Value(item, "HealthLabel"));
            if (official && item is not EngineDownloadCardViewModel) Gui.TextColored(Flag(item, "OfficialUpdateAvailable") ? ImGuiTheme.Warning : ImGuiTheme.Muted, Value(item, "OfficialUpdateLabel"));
            var footerY = 285 * UiScale - Gui.GetStyle().WindowPadding.Y - Gui.GetFrameHeight() - 1;
            Gui.SetCursorPosY(footerY);
            var actionsMin = Gui.GetCursorScreenPos();
            if (item is EngineDownloadCardViewModel transfer)
            {
                ActionButton(item, "Suivi", "OpenDetailsCommand");
                if (transfer.CanCancel) { Gui.SameLine(); ActionButton(item, transfer.IsCancellationRequested ? "Annulation…" : "Annuler", "CancelCommand"); Record("engine-download/cancel/" + transfer.TaskId); }
                if (transfer.CanRetry) { Gui.SameLine(); ActionButton(item, "Reprendre", "RetryCommand", true); Record("engine-download/retry/" + transfer.TaskId); }
            }
            else
            {
                ActionButton(item, engines ? "Lancer" : "Ouvrir", "LaunchCommand", true); Gui.SameLine();
                ActionButton(item, "Fiche", "OpenDetailsCommand"); Gui.SameLine();
                ActionButton(item, "Dossier", "OpenFolderCommand");
            }
            var actionsMax = Gui.GetItemRectMax();
            CardInteraction(item, "resource/" + ResourceName(item), new(actionsMin.X, actionsMin.Y, actionsMax.X, actionsMax.Y), () => SelectResource(item, items, engines), selected);
            if (IsResourceSelected(item, engines)) Elements["card/selected/resource/" + ResourceName(item)] = Elements.GetValueOrDefault("card/body/resource/" + ResourceName(item));
            if (engines && Gui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows) && Gui.IsMouseClicked(ImGuiMouseButton.Right)) RequestEngineContext(item);
            if (engines && IsResourceSelected(item, true)) Elements["engine/selected/" + Value(item, "Path")] = Elements.GetValueOrDefault("card/body/resource/" + ResourceName(item));
            Gui.EndChild();
            Record("resource/card/" + ResourceName(item));
            if (selected) Gui.PopStyleColor();
            if (official) Gui.PopStyleColor();
        });
        if (items.Length == 0) Gui.TextDisabled("Aucun élément pour cette recherche.");
    }
}
