using Hexa.NET.ImGui;
using Gui = Hexa.NET.ImGui.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed unsafe partial class ImGuiWorkspace
{
    private void DrawFabOptionsDialog(UiNode border)
    {
        var panel = border.Children.First().Children.FirstOrDefault(node => node.Kind == "Grid" && Visible(node, _root));
        if (panel is null) return;
        var rows = panel.Children.Where(node => !node.Kind.Contains('.', StringComparison.Ordinal)).ToArray();
        var lastRow = rows.Max(node => (int)node.Number("Grid.Row", 0));
        var footer = rows.Single(node => (int)node.Number("Grid.Row", 0) == lastRow);
        foreach (var header in rows.Where(node => node.Number("Grid.Row", 0) == 0)) DrawNode(header, _root);
        Gui.Separator();

        // Reserve the actual wrapped footer height, independent of the scrollable choices.
        var width = Gui.GetContentRegionAvail().X;
        var buttonsWidth = footer.Descendants().Where(node => node.Kind == "Button" && Visible(node, _root))
            .Sum(node => Gui.CalcTextSize(ButtonLabel(node, _root)).X + Gui.GetStyle().FramePadding.X * 2 + Gui.GetStyle().ItemSpacing.X);
        var footerTexts = footer.Descendants().Where(node => node.Kind == "TextBlock")
            .Select(node => Text(_root, node["Text"])).Where(value => !string.IsNullOrEmpty(value)).ToArray();
        var supportHeight = footerTexts.Length == 0 ? 0 : Gui.CalcTextSize(footerTexts[0], false, Math.Max(80, width - buttonsWidth - 16)).Y;
        var footerHeight = Math.Max(Gui.GetFrameHeight(), supportHeight) +
            footerTexts.Skip(1).Sum(value => Gui.CalcTextSize(value, false, Math.Max(80, width)).Y) +
            footer.Descendants().Count(node => node.Kind == "CheckBox" && Visible(node, _root)) * Gui.GetFrameHeightWithSpacing() +
            Gui.GetStyle().ItemSpacing.Y * (footerTexts.Length + 4) + 2;
        Gui.BeginChild("fab-options-choices", new(0, -footerHeight), ImGuiChildFlags.None);
        foreach (var row in rows.Where(node => node.Number("Grid.Row", 0) > 0 && node.Number("Grid.Row", 0) < lastRow)
                     .OrderBy(node => node.Number("Grid.Row", 0))) DrawNode(row, _root);
        Gui.EndChild();
        Record("fab-options/body");
        Gui.Separator();
        Gui.BeginGroup(); DrawNode(footer, _root); Gui.EndGroup();
        Record("fab-options/footer");
    }
}
