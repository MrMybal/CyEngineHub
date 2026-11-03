using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using CyEngineHub.Desktop.ViewModels;
using Gui = Hexa.NET.ImGui.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed unsafe partial class ImGuiWorkspace
{
    private sealed class PropertyView
    {
        internal nint Handle = CyUiNative.CreateInspector();
        internal string Value = string.Empty;
        internal ulong Revision = 1;
    }
    private sealed class LogView
    {
        internal nint Handle = CyUiNative.CreateLogs();
        internal int Count;
        internal object? First, Last;
    }
    private readonly Dictionary<string, PropertyView> _cyUiProperties = [];
    private readonly Dictionary<string, LogView> _cyUiLogs = [];

    private bool DrawCyUiInput(UiNode node, object? context, string value)
    {
        var binding = UiBinding.Path(node["Text"]) ?? node.Id;
        // Inspector has a two-column property layout. Keep compact, already-labelled
        // creation fields and live filters on themed Core inputs instead of squeezing them.
        var profileProperty = binding is "ProjectProfileName" or "ProjectProfileDescription";
        if (!profileProperty && (binding.StartsWith("NewProject", StringComparison.Ordinal) || Gui.GetContentRegionAvail().X < CyUiNative.Dp(420))) return false;
        // Live filters retain their application-side search/paging behavior.
        if (binding.Contains("Search", StringComparison.Ordinal) || binding.Contains("Filter", StringComparison.Ordinal) || binding.EndsWith("Query", StringComparison.Ordinal)) return false;
        var key = node.Id + "/" + binding + "/" + (context is null ? 0 : RuntimeHelpers.GetHashCode(context));
        if (!_cyUiProperties.TryGetValue(key, out var view)) _cyUiProperties[key] = view = new();
        if (view.Value != value) { view.Value = value; view.Revision++; }
        var label = Text(context, node["PlaceholderText"]);
        if (profileProperty) label = binding == "ProjectProfileName" ? "Nom" : "Description";
        if (string.IsNullOrWhiteSpace(label)) label = binding;
        var flags = UiBinding.Boolean(context, node["IsReadOnly"], false) ? 1 : 0;
        if (UiBinding.Boolean(context, node["AcceptsReturn"], false)) flags |= 4;
        if (_focusRequests.Remove(binding)) Gui.SetKeyboardFocusHere();
        var committed = CyUiNative.Inspector(view.Handle, "field/" + key, 1, label, Text(context, node["ToolTip.Tip"]), value, flags, view.Revision);
        // CyUI owns drafts/escape/commit. The application owns validation and persistence.
        if (committed is not null && committed != value) Set(context, node["Text"], committed);
        Record("field/" + binding);
        return true;
    }

    private void DrawCyUiLogs(string binding, IList values, UiNode node)
    {
        if (!_cyUiLogs.TryGetValue(binding, out var state)) _cyUiLogs[binding] = state = new();
        var first = values.Count == 0 ? null : values[0];
        if (values.Count < state.Count || !ReferenceEquals(first, state.First) ||
            state.Count > 0 && !ReferenceEquals(values[state.Count - 1], state.Last))
        { CyUiNative.ClearLogs(state.Handle); state.Count = 0; }
        for (var index = state.Count; index < values.Count; index++)
        {
            var item = values[index];
            var message = item is RawLogEntryViewModel raw ? raw.DisplayText : Value(item, "Text");
            if (string.IsNullOrEmpty(message)) message = Label(item);
            var severity = Value(item, "Severity");
            var channel = Value(item, "Channel");
            var level = channel.Equals("ERR", StringComparison.OrdinalIgnoreCase) || severity.Equals("Error", StringComparison.OrdinalIgnoreCase) || message.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ? 3 :
                severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) || message.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) ? 2 :
                message.Contains("[DEBUG]", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            var source = Value(item, "Timestamp");
            if (!string.IsNullOrEmpty(channel)) source += " · " + channel;
            if (!string.IsNullOrEmpty(severity)) source += " · " + severity;
            CyUiNative.AppendLog(state.Handle, level, source, message);
        }
        state.Count = values.Count; state.First = first; state.Last = values.Count == 0 ? null : values[^1];
        var height = node.Number("MaxHeight", node.Number("Height", 0));
        if (height <= 0) height = Math.Max(120, Gui.GetContentRegionAvail().Y - Gui.GetStyle().ItemSpacing.Y);
        CyUiNative.DrawLogs(state.Handle, "logs/" + binding, new(0, height));
        VisibleRows[binding] = Math.Min(values.Count, Math.Max(1, (int)(height / Gui.GetTextLineHeightWithSpacing())));
        Record("logs/" + binding);
    }

    private void DisposeCyUiViews()
    {
        foreach (var view in _cyUiProperties.Values) CyUiNative.DestroyInspector(view.Handle);
        _cyUiProperties.Clear();
        foreach (var view in _cyUiLogs.Values) CyUiNative.DestroyLogs(view.Handle);
        _cyUiLogs.Clear();
        if (_cyUiGallery != 0) CyUiNative.DestroyImage(_cyUiGallery);
        _cyUiGallery = 0;
    }
}
