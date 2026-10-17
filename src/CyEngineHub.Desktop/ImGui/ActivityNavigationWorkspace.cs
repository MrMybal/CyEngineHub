using System.Numerics;
using CyEngineHub.Desktop.ViewModels;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private void StudioActivityNav(MainWindowViewModel vm, bool downloads)
    {
        var label = downloads ? "Téléchargements" : "Actions";
        var id = downloads ? "downloads" : "actions";
        var running = downloads ? vm.RunningDownloads : vm.RunningActions;
        var queued = downloads ? vm.QueuedDownloads : vm.QueuedActions;
        if (Gui.Selectable("###activity-" + id, downloads ? vm.IsTasksNavigationActive : vm.IsActionsNavigationActive,
            ImGuiSelectableFlags.None, new(0, 39 * UiScale)))
            Execute(vm, downloads ? "{Binding ShowTasksCommand}" : "{Binding ShowActionsCommand}");
        Record("nav/" + label);
        var min = Gui.GetItemRectMin(); var draw = Gui.GetWindowDrawList();
        var color = Gui.GetColorU32(ImGuiCol.Text);
        var icon = min + new Vector2(8, 11) * UiScale;
        if (downloads)
        {
            draw.AddLine(icon - new Vector2(0, 6) * UiScale, icon + new Vector2(0, 2) * UiScale, color, 1.5f);
            draw.AddLine(icon + new Vector2(-3, -1) * UiScale, icon + new Vector2(0, 2) * UiScale, color, 1.5f);
            draw.AddLine(icon + new Vector2(3, -1) * UiScale, icon + new Vector2(0, 2) * UiScale, color, 1.5f);
            draw.AddLine(icon + new Vector2(-5, 5) * UiScale, icon + new Vector2(5, 5) * UiScale, color, 1.5f);
        }
        else draw.AddTriangleFilled(icon + new Vector2(-3, -5) * UiScale, icon + new Vector2(-3, 5) * UiScale, icon + new Vector2(5, 0) * UiScale, color);
        draw.AddText(min + new Vector2(22 * UiScale, 1), color, label);
        var baseline = min + new Vector2(8, 29) * UiScale;
        draw.AddCircleFilled(baseline, 3 * UiScale, Gui.ColorConvertFloat4ToU32(running > 0 ? ImGuiTheme.Success : ImGuiTheme.Muted));
        draw.AddText(Gui.GetFont(), Gui.GetFontSize() * .8f, baseline + new Vector2(7, -7) * UiScale, color, running + " en cours");
        var wait = baseline + new Vector2(86, 0) * UiScale;
        draw.AddCircle(wait, 4 * UiScale, Gui.ColorConvertFloat4ToU32(queued > 0 ? ImGuiTheme.Warning : ImGuiTheme.Muted));
        draw.AddLine(wait, wait + new Vector2(0, -2) * UiScale, color);
        draw.AddText(Gui.GetFont(), Gui.GetFontSize() * .8f, wait + new Vector2(8, -7) * UiScale, color, queued + " attente");
        Record("nav-count/" + id + "/running/" + running);
        Record("nav-count/" + id + "/queued/" + queued);
        if (Gui.IsItemHovered()) Gui.SetTooltip($"{label} : {running} en cours · {queued} en attente");
    }
}
