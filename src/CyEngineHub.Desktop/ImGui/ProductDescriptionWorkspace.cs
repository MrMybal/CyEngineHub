using System.Numerics;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Remote;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private MarketplaceDescriptionDocument? _descriptionDocument;
    private readonly Dictionary<MarketplaceTextRun, string[]> _descriptionWords = [];

    private void DrawProductDescription(MainWindowViewModel vm)
    {
        if (vm.SelectedRemoteFabAssetCard?.DescriptionDocument is not { } document) return;
        if (!ReferenceEquals(document, _descriptionDocument)) { _descriptionWords.Clear(); _descriptionDocument = document; }
        var origin = Gui.GetCursorScreenPos(); var width = Math.Max(50, Gui.GetContentRegionAvail().X);
        var y = origin.Y; var normalSize = Gui.GetFontSize(); var linkIndex = 0;
        var draw = Gui.GetWindowDrawList(); var linkColor = ImGuiTheme.Rgb(0x77BAFF);
        Gui.PushStyleVar(ImGuiStyleVar.FramePadding, Vector2.Zero);
        foreach (var block in document.Blocks)
        {
            if (block.Separator)
            {
                y += normalSize * .4f;
                draw.AddLine(new(origin.X, y), new(origin.X + width, y), Gui.GetColorU32(ImGuiCol.Separator));
                y += normalSize * .9f; continue;
            }
            var scale = block.Heading switch { 1 => 1.35f, 2 => 1.23f, 3 => 1.14f, > 3 => 1.06f, _ => 1 };
            Gui.SetWindowFontScale(scale);
            var lineHeight = normalSize * scale * 1.35f;
            var left = origin.X + block.Indent * normalSize + (block.Marker is not null || block.Quote ? normalSize * 1.4f : 0);
            var right = origin.X + width; var x = left; var blockY = y;
            if (block.Marker is { } marker) draw.AddText(new(left - normalSize * 1.4f, y), Gui.GetColorU32(ImGuiCol.Text), marker);
            foreach (var run in block.Runs)
            {
                var fontIndex = block.Code || run.Code ? 4 : (run.Bold || block.Heading > 0 ? 1 : 0) + (run.Italic ? 2 : 0);
                var fonts = Gui.GetIO().Fonts.Fonts;
                Gui.PushFont(fonts[Math.Min(fontIndex, fonts.Size - 1)]);
                var size = Gui.GetFontSize();
                var color = run.Link is not null ? linkColor : block.Heading > 0 || run.Bold ? ImGuiTheme.Rgb(0xEAEFF5) :
                    run.Code || block.Code ? ImGuiTheme.Rgb(0xEBC488) : ImGuiTheme.Rgb(0xBDC8D6);
                if (!_descriptionWords.TryGetValue(run, out var words)) _descriptionWords[run] = words = DescriptionWords(run.Text);
                var target = run.Link; var currentLink = target is null ? -1 : linkIndex++; var partIndex = 0;
                foreach (var word in words)
                {
                    if (word == "\n") { x = left; y += lineHeight; continue; }
                    if (string.IsNullOrWhiteSpace(word)) { if (x > left) x += Gui.CalcTextSize(" ").X; continue; }
                    var remainder = word;
                    while (remainder.Length > 0)
                    {
                        var measured = Gui.CalcTextSize(remainder).X;
                        if (x > left && x + measured > right) { x = left; y += lineHeight; }
                        var count = FitDescriptionText(remainder, Math.Max(1, right - x));
                        var piece = remainder[..count]; var pieceSize = Gui.CalcTextSize(piece);
                        if (run.Code || block.Code) draw.AddRectFilled(new(x - 2, y), new(x + pieceSize.X + 2, y + size + 2), Gui.GetColorU32(ImGuiCol.FrameBg), 2);
                        if (target is not null)
                        {
                            Gui.SetCursorScreenPos(new(x, y)); Gui.PushID(currentLink); Gui.PushID(partIndex++);
                            Gui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
                            Gui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(linkColor.X, linkColor.Y, linkColor.Z, .14f));
                            Gui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(linkColor.X, linkColor.Y, linkColor.Z, .25f));
                            Gui.PushStyleColor(ImGuiCol.Text, color);
                            if (Gui.Button(piece + "###description-link", new(pieceSize.X, size + 2))) _actions.Enqueue(() => _ = vm.OpenDescriptionLinkAsync(target));
                            Record("product-description-link/" + currentLink);
                            if (Gui.IsItemHovered()) { Gui.SetMouseCursor(ImGuiMouseCursor.Hand); Gui.SetTooltip(target.AbsoluteUri); }
                            draw.AddLine(new(x, y + size + 1), new(x + pieceSize.X, y + size + 1), Gui.GetColorU32(color));
                            if (Gui.BeginPopupContextItem("link-menu"))
                            {
                                if (Gui.MenuItem("Copier l’adresse du lien")) Gui.SetClipboardText(target.AbsoluteUri);
                                Gui.EndPopup();
                            }
                            Gui.PopStyleColor(4); Gui.PopID(); Gui.PopID();
                        }
                        else draw.AddText(new(x, y), Gui.GetColorU32(color), piece);
                        x += pieceSize.X; remainder = remainder[count..];
                        if (remainder.Length > 0) { x = left; y += lineHeight; }
                    }
                }
                Gui.PopFont();
            }
            if (block.Quote) draw.AddLine(new(origin.X + 3, blockY), new(origin.X + 3, y + lineHeight), Gui.GetColorU32(ImGuiTheme.Rgb(0x6684A4)), 2);
            y += lineHeight + normalSize * (block.Marker is null ? .65f : .2f);
            Gui.SetWindowFontScale(1);
        }
        Gui.PopStyleVar(); Gui.SetCursorScreenPos(origin);
        Gui.Dummy(new(width, Math.Max(normalSize, y - origin.Y)));
        Record("product-description");
    }
    private static int FitDescriptionText(string text, float width)
    {
        if (Gui.CalcTextSize(text).X <= width) return text.Length;
        var low = 1; var high = text.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (Gui.CalcTextSize(text[..middle]).X <= width) low = middle; else high = middle - 1;
        }
        if (low < text.Length && char.IsHighSurrogate(text[low - 1]) && char.IsLowSurrogate(text[low])) low = low == 1 ? 2 : low - 1;
        return low;
    }
    private static string[] DescriptionWords(string text)
    {
        var words = new List<string>();
        for (var index = 0; index < text.Length;)
        {
            if (text[index] == '\n') { words.Add("\n"); index++; continue; }
            var start = index++; var whitespace = char.IsWhiteSpace(text[start]);
            while (index < text.Length && text[index] != '\n' && char.IsWhiteSpace(text[index]) == whitespace) index++;
            words.Add(text[start..index]);
        }
        return words.ToArray();
    }
}
