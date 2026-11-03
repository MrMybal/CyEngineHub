using System.Text;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Remote;
using Gui = Hexa.NET.ImGui.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed unsafe partial class ImGuiWorkspace
{
    private nint _cyUiDescription;
    private MarketplaceDescriptionDocument? _descriptionDocument;

    private void DrawProductDescription(MainWindowViewModel vm)
    {
        if (vm.SelectedRemoteFabAssetCard?.DescriptionDocument is not { } document) return;
        if (_cyUiDescription == 0) _cyUiDescription = CyUiNative.CreateDocument();
        if (!ReferenceEquals(document, _descriptionDocument))
        {
            _descriptionDocument = document;
            // Fab supplies HTML as well as Markdown. The application's existing safe
            // normalization remains the data boundary; CyUI owns layout/links/code rendering.
            CyUiNative.SetDocument(_cyUiDescription, DescriptionMarkdown(document));
        }
        // This is a bounded viewport, not the entire fiche: downloaded variants and
        // their actions below the summary must remain reachable in the outer scroll.
        var link = CyUiNative.DrawDocument(_cyUiDescription, "product-description", new(0, CyUiNative.Dp(220)));
        if (!string.IsNullOrWhiteSpace(link) && Uri.TryCreate(link, UriKind.Absolute, out var uri))
            _actions.Enqueue(() => _ = vm.OpenDescriptionLinkAsync(uri));
        Record("product-description");
    }

    private static string DescriptionMarkdown(MarketplaceDescriptionDocument document)
    {
        var result = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            if (block.Separator) { result.AppendLine("---").AppendLine(); continue; }
            if (block.Code) result.AppendLine("```");
            if (block.Heading > 0) result.Append('#', Math.Min(block.Heading, 6)).Append(' ');
            if (block.Quote) result.Append("> ");
            if (block.Marker is not null) result.Append(' ', Math.Max(0, block.Indent) * 2).Append("- ");
            foreach (var run in block.Runs)
            {
                var text = run.Text;
                if (run.Link is not null) result.Append('[').Append(text.Replace("]", "\\]", StringComparison.Ordinal)).Append("](").Append(run.Link.AbsoluteUri).Append(')');
                else if (run.Code && !block.Code) result.Append('`').Append(text).Append('`');
                else if (run.Bold) result.Append("**").Append(text).Append("**");
                else if (run.Italic) result.Append('*').Append(text).Append('*');
                else result.Append(text);
            }
            result.AppendLine();
            if (block.Code) result.AppendLine("```");
            result.AppendLine();
        }
        return result.ToString();
    }
}
