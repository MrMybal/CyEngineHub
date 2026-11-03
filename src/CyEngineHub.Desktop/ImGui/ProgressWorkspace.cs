using System.Numerics;
using Hexa.NET.ImGui;
using Gui = Hexa.NET.ImGui.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed unsafe partial class ImGuiWorkspace
{
    private void OperationProgress(float fraction, Vector2 size, string? status = null, bool indeterminate = false, string? key = null)
    {
        var tone = OperationProgressPolicy.Resolve(status, fraction, indeterminate);
        CyUiNative.Progress(indeterminate ? -1 : Math.Clamp(fraction, 0, 1), size, tone switch
        { OperationProgressTone.Completed => CyUiTone.Success, OperationProgressTone.Failed => CyUiTone.Error,
          OperationProgressTone.Canceled => CyUiTone.Warning, _ => CyUiTone.Info });
        if (key is not null) Record("progress/" + key + "/" + tone);
    }
}
