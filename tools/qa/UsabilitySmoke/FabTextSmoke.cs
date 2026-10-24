using Avalonia.Controls;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Remote;

static partial class Program
{
    static async Task TestFabText(Window window, MainWindowViewModel vm, RecordingLauncher launcher)
    {
        const string html = """
            <p><strong>IMPORTANT: AVE II IS NOW AVAILABLE!</strong></p>
            <p>The next evolution uses a modular framework, enhanced input and new materials.</p>
            <p><a target="_blank" href="https://example.invalid/docs" rel="noopener"><strong>Documentation &amp; FAQ</strong></a></p>
            <h3>Key Features</h3><ul><li>Architecture &amp; construction presentations</li><li>Interactive events</li><li>Pixel&nbsp;Streaming<br>References and dependencies preserved</li></ul>
            <script>neverExecuteThis()</script>
            """;
        var source = new RemoteFabAsset("html-cache-fixture", "fab", "ArchViz Explorer · HTML fixture", html, "Fab", "assetPack",
            new Uri("https://example.invalid/listing"), null, ["Architecture"], []);
        var client = (FixtureFab)typeof(MainWindowViewModel).GetField("_directFabClient", Private)!.GetValue(vm)!;
        client.FailProductDetails = true;
        typeof(MainWindowViewModel).GetField("_remoteFabCatalog", Private)!.SetValue(vm, new[] { source });
        Call(vm, "ApplyDirectFabStatus", new DirectFabStatus(DirectFabConnectionState.Connected, "Fixture catalog"));
        vm.ShowLibraryFabCommand.Execute(null); Call(vm, "ShowCachedRemoteFabPreview");
        using var card = (RemoteFabAssetCardViewModel)typeof(MainWindowViewModel).GetMethod("CreateRemoteAssetCard", Private)!.Invoke(vm, [source])!;
        card.OpenDetailsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 5);
        var selected = vm.SelectedRemoteFabAssetCard!;
        Check(vm.StatusMessage.Contains("résumé du catalogue affiché", StringComparison.Ordinal), "The real offline/error fallback is exercised, not only the online description");
        Check(selected.Description.Contains("IMPORTANT: AVE II", StringComparison.Ordinal) && !selected.Description.Contains("<p>", StringComparison.Ordinal) &&
              !selected.Description.Contains("&nbsp;", StringComparison.Ordinal) && !selected.Description.Contains("neverExecuteThis", StringComparison.Ordinal),
            "Old HTML cache descriptions display readable text without tags, entities or scripts");
        Check(selected.Description.Contains("• Pixel Streaming", StringComparison.Ordinal) && selected.Description.Contains("Documentation & FAQ (https://example.invalid/docs)", StringComparison.Ordinal),
            "Bullets, decoded accents/entities and useful link destinations survive conversion");
        Check(card.Summary == selected.Description && source.Description == html, "Card summaries are normalized once while original catalog data stays unchanged");
        Check(launcher.OpenedUris.Count == 0 && launcher.Launches.Count == 0, "Description conversion launches nothing and follows no external link");
        var namedLink = selected.DescriptionDocument.Blocks.SelectMany(block => block.Runs).Single(run => run.Link is not null);
        Check(namedLink.Text == "Documentation & FAQ" && namedLink.Bold && !namedLink.Text.Contains("https://", StringComparison.Ordinal),
            "Rich HTML keeps a bold clickable label rather than a dumped URL");
        await ImGuiDiagnostics.ClickAsync(window, "product-description-link/0");
        Check(launcher.OpenedUris.Single().AbsoluteUri == "https://example.invalid/docs", "Clicking the rendered description link opens exactly its intended web address");
        foreach (var width in new[] { 1440, 1024 })
        {
            window.Width = width; await ImGuiDiagnostics.FramesAsync(window, 4);
            var description = ImGuiDiagnostics.ElementBounds(window, "product-description");
            Check(description.Z - description.X > 250 && description.Z < width, "The product description keeps a readable width without horizontal overflow at " + width);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-description-offline-" + width + ".png"));
        }
        selected.ApplyProductDetails(new("&lt;p&gt;Version détaillée : &lt;strong&gt;Éclairage&lt;/strong&gt; &amp;amp; matériaux.&lt;/p&gt;", []));
        Check(selected.Description == "Version détaillée : Éclairage & matériaux.", "Encoded online description updates use the same conversion as the cached fallback");
        selected.ApplyProductDetails(new("<p> </p>", []));
        Check(selected.Description == card.Summary, "An empty formatted web response cannot replace a useful cached summary with markup");
        selected.ApplyProductDetails(new("## Markdown details\n\n**Important** and *readable*. [Support](https://example.invalid/support)\n\n- Meshes\n- Materials\n\n`inline code`", []));
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(selected.DescriptionDocument.Blocks[0].Heading == 2 && selected.DescriptionDocument.Blocks.SelectMany(block => block.Runs).Any(run => run.Bold),
            "Actual Markdown is rendered as headings and styled text, not raw punctuation");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-markdown-rich.png"));
        await ImGuiDiagnostics.ClickAsync(window, "product-description-link/0");
        Check(launcher.OpenedUris.Last().AbsoluteUri == "https://example.invalid/support" && launcher.OpenedUris.Count == 2, "Markdown link labels are clickable too");
        selected.ApplyProductDetails(new("<p><a href='javascript:alert(1)'>Unsafe link</a></p><p><a href='file:///C:/private'>Local file</a></p>", []));
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(!ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("product-description-link/", StringComparison.Ordinal)) && launcher.OpenedUris.Count == 2,
            "Executable and local-file addresses never become clickable description links");
    }
}
