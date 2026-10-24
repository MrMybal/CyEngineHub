using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

// Preserve the existing browser event handlers but render their chrome with ImGui.
// WebView2 remains a native child surface: a website is not an ImGui widget.
internal static class ImGuiBrowserChrome
{
    public static void Convert(Grid grid, int browserRow)
    {
        foreach (var control in grid.Children.ToArray())
        {
            var row = Grid.GetRow(control);
            if (row == browserRow) continue;
            var surface = Create(control, control is WrapPanel ? 100 : control is Border ? 180 : control is TextBlock ? 44 : 88);
            Grid.SetRow(surface, row);
            grid.Children.Remove(control);
            grid.Children.Add(surface);
        }
    }

    public static ImGuiHost Create(Control source, double height = 100)
    {
        var host = new ImGuiHost("Browser") { Height = height, IsVisible = source.IsVisible };
        var surface = host.Surface;
        surface.NativeSource = source;
        surface.CustomDraw = () => { surface.DrawnBrowserText.Clear(); surface.NativeElements.Clear(); Draw(source, surface); };
        source.PropertyChanged += (_, args) =>
        {
            if (args.Property == Visual.IsVisibleProperty) host.IsVisible = source.IsVisible;
        };
        return host;
    }

    private static void Draw(Control control, ImGuiSurface surface)
    {
        if (!control.IsVisible) return;
        Gui.PushID(control.GetHashCode());
        switch (control)
        {
            case Button button:
                Gui.BeginDisabled(!button.IsEnabled);
                if (Gui.Button(ImGuiWorkspace.NormalizeLabel(button.Content?.ToString() ?? "")))
                    Dispatcher.UIThread.Post(() => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                var min = Gui.GetItemRectMin(); var max = Gui.GetItemRectMax();
                if (Gui.IsItemVisible()) surface.NativeElements["native/" + button.Content] = new(min.X, min.Y, max.X, max.Y);
                Gui.EndDisabled();
                break;
            case TextBlock text:
                Gui.PushTextWrapPos(0);
                Gui.TextUnformatted(ImGuiWorkspace.NormalizeLabel(text.Text ?? ""));
                surface.DrawnBrowserText.Add(text.Text ?? "");
                Gui.PopTextWrapPos();
                break;
            case ItemsControl items:
                if (items.ItemsSource is IEnumerable sequence)
                    foreach (var item in sequence.OfType<BrowserDownloadViewModel>())
                    {
                        Gui.TextUnformatted(item.Name);
                        Gui.TextUnformatted(item.Summary);
                        Gui.TextUnformatted(item.Timing);
                        surface.DrawnBrowserText.Add(item.Name); surface.DrawnBrowserText.Add(item.Summary); surface.DrawnBrowserText.Add(item.Timing);
                        Gui.PushStyleColor(ImGuiCol.PlotHistogram, ImGuiTheme.ProgressColor(OperationProgressPolicy.Resolve(item.Status, (float)item.Percent / 100, item.IsIndeterminate)));
                        Gui.ProgressBar(item.IsIndeterminate ? -(float)Gui.GetTime() : (float)item.Percent / 100, new(-1, 4), "");
                        Gui.PopStyleColor();
                        Gui.Separator();
                    }
                break;
            case ScrollViewer scroll:
                if (scroll.Content is Control content) Draw(content, surface);
                break;
            case Decorator decorator:
                if (decorator.Child is { } decoratedChild) Draw(decoratedChild, surface);
                break;
            case Grid grid:
                if (grid.ColumnDefinitions.Count <= 1) { foreach (var child in grid.Children.OrderBy(Grid.GetRow)) Draw(child, surface); break; }
                if (Gui.BeginTable("grid", grid.ColumnDefinitions.Count, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.NoSavedSettings))
                {
                    for (var index = 0; index < grid.ColumnDefinitions.Count; index++)
                    {
                        var column = grid.ColumnDefinitions[index];
                        var width = grid.Children.Where(child => child.IsVisible && Grid.GetColumn(child) == index)
                            .Select(child => Gui.CalcTextSize(ImGuiWorkspace.NormalizeLabel((child as Button)?.Content?.ToString() ?? "")).X + 28).DefaultIfEmpty(28).Max();
                        Gui.TableSetupColumn("column" + index, column.Width.IsAuto ? ImGuiTableColumnFlags.WidthFixed : ImGuiTableColumnFlags.WidthStretch, column.Width.IsAuto ? width : 1);
                    }
                    foreach (var row in grid.Children.Where(child => child.IsVisible).GroupBy(Grid.GetRow).OrderBy(group => group.Key))
                    {
                        Gui.TableNextRow();
                        foreach (var child in row) { Gui.TableSetColumnIndex(Grid.GetColumn(child)); Draw(child, surface); }
                    }
                    Gui.EndTable();
                }
                break;
            case Panel panel:
                var first = true;
                foreach (var panelChild in panel.Children)
                {
                    if (!panelChild.IsVisible) continue;
                    if (panel is WrapPanel && !first && Gui.GetContentRegionAvail().X > Gui.CalcTextSize((panelChild as Button)?.Content?.ToString() ?? "").X + 24) Gui.SameLine();
                    Draw(panelChild, surface);
                    first = false;
                }
                break;
        }
        Gui.PopID();
    }
}
