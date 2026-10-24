using System.Numerics;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private void OperationProgress(float fraction, Vector2 size, string? status = null, bool indeterminate = false, string? key = null)
    {
        var tone = OperationProgressPolicy.Resolve(status, fraction, indeterminate);
        var color = ImGuiTheme.ProgressColor(tone);
        Gui.PushStyleColor(ImGuiCol.PlotHistogram, color);
        Gui.ProgressBar(indeterminate ? -(float)Gui.GetTime() : Math.Clamp(fraction, 0, 1), size, "");
        Gui.PopStyleColor();
        if (key is not null) Record("progress/" + key + "/" + tone);
    }
}
