using System.Numerics;
using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private RemoteFabAssetCardViewModel? _galleryUiCard;
    private int _galleryUiIndex = -1;
    private bool _galleryViewerWasVisible;
    private float _galleryZoom = 1;
    private Vector2 _galleryPan;
    private int _galleryStripIndex = -1;
    private int _galleryViewerStripIndex = -1;

    private void DrawFabGallery(MainWindowViewModel vm, bool enlarged)
    {
        if (vm.SelectedRemoteFabAssetCard is not { } card) return;
        if (!ReferenceEquals(_galleryUiCard, card) || _galleryUiIndex != card.SelectedGalleryIndex || enlarged && !_galleryViewerWasVisible)
        {
            if (!ReferenceEquals(_galleryUiCard, card)) { _galleryStripIndex = -1; _galleryViewerStripIndex = -1; }
            _galleryZoom = 1; _galleryPan = Vector2.Zero;
            _galleryUiCard = card; _galleryUiIndex = card.SelectedGalleryIndex;
        }
        if (enlarged) _galleryViewerWasVisible = true;
        else if (!vm.IsFabImagePreviewVisible) _galleryViewerWasVisible = false;

        if (enlarged)
        {
            Gui.TextUnformatted(card.Name);
            if (Gui.Button("Fermer (Échap)")) Execute(vm, "{Binding CloseFabImagePreviewCommand}");
            Record("gallery-close"); Gui.SameLine();
            GalleryNavigation(card);
            Gui.SameLine();
            Gui.BeginDisabled(_galleryZoom <= 1);
            if (Gui.Button("−###zoom-out")) ChangeGalleryZoom(_galleryZoom / 1.25f);
            Record("gallery-zoom-out"); Gui.EndDisabled(); Gui.SameLine();
            if (Gui.Button("+###zoom-in")) ChangeGalleryZoom(_galleryZoom * 1.25f);
            Record("gallery-zoom-in"); Gui.SameLine();
            if (Gui.Button("Ajuster")) { _galleryZoom = 1; _galleryPan = Vector2.Zero; }
            Record("gallery-fit"); Gui.SameLine(); Gui.TextUnformatted($"{_galleryZoom * 100:0} %");
            Record("gallery-zoom/" + (int)MathF.Round(_galleryZoom * 100));
            if (Gui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !Gui.GetIO().WantTextInput)
            {
                if (Gui.IsKeyPressed(ImGuiKey.LeftArrow)) _actions.Enqueue(() => card.MoveGalleryImage(-1));
                if (Gui.IsKeyPressed(ImGuiKey.RightArrow)) _actions.Enqueue(() => card.MoveGalleryImage(1));
                if (Gui.IsKeyPressed(ImGuiKey.Home)) _actions.Enqueue(() => card.SelectGalleryImage(0));
                if (Gui.IsKeyPressed(ImGuiKey.End)) _actions.Enqueue(() => card.SelectGalleryImage(card.GalleryImages.Count - 1));
            }
        }

        var canvasHeight = enlarged ? Math.Max(80, Gui.GetContentRegionAvail().Y - 157) : 185 * UiCardGrid.InterfaceScale;
        Gui.BeginChild("gallery-canvas", new(0, canvasHeight), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        var origin = Gui.GetCursorScreenPos();
        var area = Vector2.Max(new(1), Gui.GetContentRegionAvail());
        Gui.InvisibleButton("gallery-image", area);
        Record(enlarged ? "gallery-canvas" : "gallery-open");
        var hovered = Gui.IsItemHovered();
        if (enlarged && hovered)
        {
            if (Gui.GetIO().MouseWheel != 0) ChangeGalleryZoom(_galleryZoom * MathF.Pow(1.2f, Gui.GetIO().MouseWheel));
            if (_galleryZoom > 1)
            {
                Gui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
                if (Gui.IsItemActive() && Gui.IsMouseDragging(ImGuiMouseButton.Left)) _galleryPan += Gui.GetIO().MouseDelta;
            }
            if (Gui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) { _galleryZoom = 1; _galleryPan = Vector2.Zero; }
        }
        else if (!enlarged && Gui.IsItemClicked()) Execute(vm, "{Binding OpenFabImagePreviewCommand}");
        var draw = Gui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + area, Gui.GetColorU32(ImGuiCol.FrameBg), 4);
        draw.PushClipRect(origin, origin + area, true);
        if (card.PreviewImage is { } bitmap)
        {
            var dimensions = new Vector2(bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            var fit = Math.Min(area.X / Math.Max(1, dimensions.X), area.Y / Math.Max(1, dimensions.Y));
            var imageSize = dimensions * fit * (enlarged ? _galleryZoom : 1);
            var bounds = Vector2.Max(Vector2.Zero, (imageSize - area) / 2);
            if (enlarged) _galleryPan = Vector2.Clamp(_galleryPan, -bounds, bounds);
            var min = origin + (area - imageSize) / 2 + (enlarged ? _galleryPan : Vector2.Zero);
            var texture = _image(bitmap);
            if (texture != 0) draw.AddImage(texture, min, min + imageSize);
        }
        else draw.AddText(origin + new Vector2(12), Gui.GetColorU32(ImGuiCol.TextDisabled), card.IsGalleryLoading ? "Chargement…" : "Image indisponible");
        if (!enlarged)
        {
            draw.AddRectFilled(origin + new Vector2(0, Math.Max(0, area.Y - 25)), origin + area, 0xB0000000);
            draw.AddText(origin + new Vector2(8, Math.Max(0, area.Y - 21)), 0xFFFFFFFF, "Agrandir · " + card.GalleryCounter);
            if (hovered) { Gui.SetMouseCursor(ImGuiMouseCursor.Hand); Gui.SetTooltip("Afficher la galerie en grand"); }
        }
        draw.PopClipRect(); Gui.EndChild();
        if (enlarged) Record($"gallery-pan/{MathF.Round(_galleryPan.X)}/{MathF.Round(_galleryPan.Y)}");

        if (!enlarged) GalleryNavigation(card);
        DrawGalleryThumbnails(card, enlarged);
        Gui.TextWrapped(card.GalleryStatus);
        Gui.BeginDisabled(vm.IsRemoteFabProductLoading);
        if (Gui.SmallButton(vm.IsRemoteFabProductLoading ? "Actualisation…" : "Réessayer la galerie")) _actions.Enqueue(() => _ = vm.RetryFabGalleryAsync());
        Record("gallery-retry"); Gui.EndDisabled();
        if (enlarged) { Gui.SameLine(); Gui.TextDisabled("Molette : zoom · Glisser : déplacer · Flèches : images"); }
    }

    private void GalleryNavigation(RemoteFabAssetCardViewModel card)
    {
        Gui.BeginDisabled(card.GalleryImages.Count < 2);
        if (Gui.Button("<###previous-image")) _actions.Enqueue(() => card.MoveGalleryImage(-1));
        Record("gallery-previous"); Gui.SameLine();
        Gui.TextUnformatted(card.GalleryCounter); Record("gallery-counter/" + card.GalleryCounter);
        Gui.SameLine();
        if (Gui.Button(">###next-image")) _actions.Enqueue(() => card.MoveGalleryImage(1));
        Record("gallery-next"); Gui.EndDisabled();
    }

    private void DrawGalleryThumbnails(RemoteFabAssetCardViewModel card, bool enlarged)
    {
        var thumbnailSize = new Vector2(78, 52) * UiCardGrid.InterfaceScale;
        Gui.BeginChild("gallery-thumbnails", new(0, thumbnailSize.Y + 25), ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);
        var selectedChanged = (enlarged ? _galleryViewerStripIndex : _galleryStripIndex) != card.SelectedGalleryIndex;
        for (var index = 0; index < card.GalleryImages.Count; index++)
        {
            if (index > 0) Gui.SameLine();
            Gui.PushID(index);
            var min = Gui.GetCursorScreenPos();
            if (Gui.InvisibleButton("thumbnail", thumbnailSize)) { var selected = index; _actions.Enqueue(() => card.SelectGalleryImage(selected)); }
            Record("gallery-thumbnail/" + index);
            if (selectedChanged && index == card.SelectedGalleryIndex) Gui.SetScrollHereX();
            if (Gui.IsItemVisible())
            {
                var visibleIndex = index;
                _actions.Enqueue(() => card.EnsureGalleryThumbnailLoaded(visibleIndex));
                var draw = Gui.GetWindowDrawList();
                draw.AddRectFilled(min, min + thumbnailSize, Gui.GetColorU32(ImGuiCol.FrameBg), 3);
                if (card.GalleryImages[index].Image is { } image)
                {
                    var pixels = new Vector2(image.PixelSize.Width, image.PixelSize.Height);
                    var fitted = pixels * Math.Min((thumbnailSize.X - 6) / Math.Max(1, pixels.X), (thumbnailSize.Y - 6) / Math.Max(1, pixels.Y));
                    var start = min + (thumbnailSize - fitted) / 2;
                    var texture = _image(image);
                    if (texture != 0) draw.AddImage(texture, start, start + fitted);
                }
                else draw.AddText(min + new Vector2(8, 15), Gui.GetColorU32(ImGuiCol.TextDisabled), (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                draw.AddRect(min, min + thumbnailSize, Gui.GetColorU32(index == card.SelectedGalleryIndex ? ImGuiCol.CheckMark : ImGuiCol.Border), 3, ImDrawFlags.None, index == card.SelectedGalleryIndex ? 2 : 1);
            }
            if (Gui.IsItemHovered()) { Gui.SetMouseCursor(ImGuiMouseCursor.Hand); Gui.SetTooltip($"Image {index + 1} / {card.GalleryImages.Count}"); }
            Gui.PopID();
        }
        if (enlarged) _galleryViewerStripIndex = card.SelectedGalleryIndex; else _galleryStripIndex = card.SelectedGalleryIndex;
        Gui.EndChild();
    }

    private void ChangeGalleryZoom(float value)
    {
        var next = Math.Clamp(value, 1, 8);
        _galleryPan *= next / _galleryZoom;
        _galleryZoom = next;
        if (next == 1) _galleryPan = Vector2.Zero;
    }
}
