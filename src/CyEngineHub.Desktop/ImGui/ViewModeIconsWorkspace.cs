using System.Numerics;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private static float ViewModeButtonSize() => Gui.GetFrameHeight();

    private void ViewModeIconButton(string id, int mode, bool selected, Action activate, bool enabled = true)
    {
        Gui.BeginDisabled(!enabled);
        if (selected)
        {
            Gui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.Selected);
            Gui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiTheme.Selected);
            Gui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiTheme.Primary);
        }
        var size = ViewModeButtonSize();
        if (Gui.Button("###view-mode-" + id, new(size))) _actions.Enqueue(activate);
        Record("view-mode-icon/" + id);
        var min = Gui.GetItemRectMin();
        var max = Gui.GetItemRectMax();
        var draw = Gui.GetWindowDrawList();
        var color = Gui.GetColorU32(ImGuiCol.Text);
        var scale = Gui.GetFontSize();
        var origin = min + (new Vector2(size) - new Vector2(scale)) / 2;
        Vector2 Point(float x, float y) => origin + new Vector2(x, y) * (scale / 20);
        void Line(float x1, float y1, float x2, float y2) => draw.AddLine(Point(x1, y1), Point(x2, y2), color, 1.05f * UiScale);
        void Rect(float x1, float y1, float x2, float y2) => draw.AddRect(Point(x1, y1), Point(x2, y2), color, UiScale, ImDrawFlags.None, 1.05f * UiScale);
        draw.PushClipRect(min, max, true);
        switch (mode)
        {
            case 0: // Detailed cards: image above metadata, inside each card.
                foreach (var x in new[] { 1f, 12f })
                {
                    Rect(x, 1, x + 7, 19);
                    draw.AddRectFilled(Point(x + 1.5f, 3), Point(x + 5.5f, 9), color);
                    Line(x + 1.5f, 12, x + 5.5f, 12);
                    Line(x + 1.5f, 15, x + 4.5f, 15);
                }
                break;
            case 1: // Compact image/name cards, in a denser 2x2 grid.
                for (var row = 0; row < 2; row++)
                    for (var column = 0; column < 2; column++)
                    {
                        var x = 1 + column * 11; var y = 1 + row * 10;
                        Rect(x, y, x + 7, y + 5);
                        Line(x, y + 7, x + 5, y + 7);
                    }
                break;
            case 2: // Plain rows, with no thumbnail.
                for (var row = 0; row < 3; row++) Line(1, 3 + row * 7, 19, 3 + row * 7);
                break;
            case 3: // Rows with a small thumbnail on the left.
                for (var row = 0; row < 3; row++)
                {
                    var y = 1 + row * 7;
                    Rect(1, y, 5, y + 4);
                    Line(8, y + 1, 19, y + 1);
                    Line(8, y + 4, 16, y + 4);
                }
                break;
        }
        if (selected) draw.AddRect(min, max, Gui.ColorConvertFloat4ToU32(ImGuiTheme.Accent), 3 * UiScale);
        draw.PopClipRect();
        if (selected) Gui.PopStyleColor(3);
        Gui.EndDisabled();
        if (Gui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            var label = mode switch { 0 => "Cartes détaillées", 1 => "Image + nom", 2 => "Lignes sans image", _ => "Lignes avec vignette" };
            Gui.SetTooltip(label + (selected ? " · sélectionné" : ""));
            Record("view-mode-tooltip/" + id);
        }
    }
}
