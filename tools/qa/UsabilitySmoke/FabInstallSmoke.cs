using Avalonia.Controls;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Remote;

static partial class Program
{
    static async Task TestAssetInstallChoices(Window window, MainWindowViewModel vm)
    {
        var versions = new[] {
            new RemoteFabProjectVersion("manual-ue57", [], ["UE_5.7"], ["Windows"]),
            new RemoteFabProjectVersion("manual-ue54", [], ["UE_5.4"], ["Windows"]),
        };
        var source = new RemoteFabAsset("manual-fixture", "fab", "Pack QA · choix libre",
            "Pack fictif : versions 5.7 et 5.4, projet cible 5.5. Aucun accès réseau.", "Fab", "assetPack",
            null, null, ["Architecture"], versions);
        var catalog = Enumerable.Range(0, 260).Select(index => source with {
            AssetId = "manual-fixture-" + index, Title = "Pack QA " + index.ToString("D3") }).ToArray();
        var client = (FixtureFab)typeof(MainWindowViewModel).GetField("_directFabClient", Private)!.GetValue(vm)!;
        client.FailProductDetails = true;
        typeof(MainWindowViewModel).GetField("_remoteFabCatalog", Private)!.SetValue(vm, catalog);
        Call(vm, "ApplyDirectFabStatus", new DirectFabStatus(DirectFabConnectionState.Connected, "Fixture catalog"));
        vm.ShowProjectsCommand.Execute(null);
        vm.ShowLibraryFabCommand.Execute(null);
        vm.ShowPagedLibraryCommand.Execute(null);
        vm.LibrarySearchQuery = "Pack QA";
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        var until = DateTime.UtcNow.AddSeconds(15);
        while (!vm.CanShowNextLibraryPage)
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("QA catalog not ready");
            await ImGuiDiagnostics.FramesAsync(window, 2);
        }
        vm.NextLibraryPageCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 5);
        var page = vm.LibraryPageSummary;
        string[] AssetIds() => vm.PagedRemoteFabRows.SelectMany(row => Enumerable.Range(0, row.AssetCount).Select(index => row.AssetAt(index).AssetId)).ToArray();
        var assetIds = AssetIds();
        Check(page.Contains("2 /", StringComparison.Ordinal), "Fixture exercises the actual second catalog page");
        using var card = (RemoteFabAssetCardViewModel)typeof(MainWindowViewModel).GetMethod("CreateRemoteAssetCard", Private)!.Invoke(vm, [catalog[120]])!;
        card.OpenDetailsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 5);
        Check(!vm.ShowOnlyCompatibleFabTargets, "Projects are not silently filtered on product opening");
        Check(vm.NavigationBackLabel == "← Retour aux assets", "Header back describes the local asset destination, not the previous Projects tab");
        vm.OpenFabInstallOptionsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(vm.FabInstallVersions.Count == 2 && ImGuiDiagnostics.Elements(window).Contains("variant-label/manual-ue57"), "Variants are displayed before a target is selected");
        var target = vm.FabInstallTargets.Single();
        target.IsSelected = true;
        target.SelectedInstallationScript = target.AvailableInstallationScripts.Single(script => script.Id == UnrealContentPackInstallationScript.ScriptId);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(vm.HasNoCompatibleFabInstallVersions && vm.FabInstallVersions.Count == 2, "No recommended match is a warning, not an empty list");
        var selectedBefore = vm.FabInstallVersions.Single(option => option.Version.ArtifactId == "manual-ue57");
        Check(selectedBefore.IsSelected, "Choosing a mismatched project or script preserves the preferred/manual variant");
        Check(selectedBefore.CompatibilityNotice.Contains("Choix manuel autorisé", StringComparison.Ordinal), "Version mismatches expose an explicit advisory notice");
        await ImGuiDiagnostics.PointerGestureAsync(window, "fab-options/body", wheel: -6);
        await ImGuiDiagnostics.ClickAsync(window, "variant-label/manual-ue54");
        Check(vm.FabInstallVersions.Single(option => option.Version.ArtifactId == "manual-ue54").IsSelected, "A non-matching variant remains clickable");
        selectedBefore.IsSelected = false;
        target.IsSelected = false; target.IsSelected = true;
        target.SelectedInstallationScript = target.AvailableInstallationScripts.Single(script => script.Id == UnrealVersionBridgeInstallationScript.ScriptId);
        target.SelectedInstallationScript = target.AvailableInstallationScripts.Single(script => script.Id == UnrealContentPackInstallationScript.ScriptId);
        Check(!selectedBefore.IsSelected && vm.FabInstallVersions.Single(option => option.Version.ArtifactId == "manual-ue54").IsSelected, "Target and script changes neither discard manual choices nor silently reselect other variants");
        await ImGuiDiagnostics.FramesAsync(window, 4);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-install-manual-variants.png"));

        // Exercise actual transactional import, but with synthetic files and a fake downloader.
        var fixtureSource = Path.Combine(Output, "ManualImportSource");
        Directory.CreateDirectory(Path.Combine(fixtureSource, "Content", "ManualChoice"));
        await File.WriteAllTextAsync(Path.Combine(fixtureSource, "Content", "ManualChoice", "Fixture.uasset"), "synthetic asset - not executable");
        var downloadCopy = Path.Combine(Output, "ManualDownloadTemp");
        Directory.CreateDirectory(Path.Combine(downloadCopy, "Content", "ManualChoice"));
        File.Copy(Path.Combine(fixtureSource, "Content", "ManualChoice", "Fixture.uasset"), Path.Combine(downloadCopy, "Content", "ManualChoice", "Fixture.uasset"));
        client.EditorFixtureSource = downloadCopy;
        await ImGuiDiagnostics.ClickAsync(window, "command/InstallSelectedFabVersionsCommand");
        var installDeadline = DateTime.UtcNow.AddSeconds(20);
        while (vm.IsFabTargetInstallationActive)
        {
            if (DateTime.UtcNow > installDeadline) throw new TimeoutException("Manual install did not settle");
            await ImGuiDiagnostics.FramesAsync(window, 2);
        }
        Check(File.Exists(Path.Combine(target.Path, "Content", "ManualChoice", "Fixture.uasset")), "Manual version choice reaches the chosen script and performs the isolated import instead of failing a catalog-version gate");
        Check(File.Exists(Path.Combine(fixtureSource, "Content", "ManualChoice", "Fixture.uasset")), "The original synthetic pack stays intact");
        await TestForcedInstallChoice(window, vm, client, fixtureSource);
        client.EditorFixtureSource = null;
        await ImGuiDiagnostics.FramesAsync(window, 5);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-product-contextual-back.png"));
        await ImGuiDiagnostics.ClickAsync(window, "action/NavigateBackCommand");
        Check(vm.IsLibraryVisible && vm.IsRemoteFabCatalogVisible && !vm.IsRemoteFabProductVisible, "The actual header button returns to assets, not to Projects");
        Check(vm.LibrarySearchQuery == "Pack QA" && vm.LibraryPageSummary == page && assetIds.SequenceEqual(AssetIds()), "Returning preserves the search, second page and displayed catalog assets, including after installation refresh");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-catalog-return-page2.png"));
        vm.NavigateBackCommand.Execute(null);
        Check(vm.IsProjectsVisible, "Closing a product does not consume or corrupt the existing section history");
    }
}
