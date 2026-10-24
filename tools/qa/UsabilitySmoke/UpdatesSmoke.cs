using System.Text.Json;
using Avalonia.Controls;
using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Remote;

static partial class Program
{
    static async Task TestEngineAndFabUpdates(Window window, MainWindowViewModel vm)
    {
        Call(vm, "ApplyDirectFabStatus", new DirectFabStatus(DirectFabConnectionState.Connected, "Fixture"));
        var engine = vm.Engines.Single();
        engine.ApplyOfficialUpdate("Mise à jour : 5.5.4", true);
        engine.OpenDetailsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(vm.CanUpdateSelectedOfficialEngine, "Official engine update is enabled for a newer detected build");
        await ImGuiDiagnostics.ClickAsync(window, "action/RequestOfficialEngineUpdateCommand");
        Check(vm.IsEngineUpdateConfirmationVisible && vm.EngineUpdateConfirmationText.Contains(engine.Path), "Engine update requires confirmation of the actual existing directory");
        Check(EngineBackend.UpdateCalls == 0, "Opening update confirmation does not start any download");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "engine-update-confirmation.png"));
        await ImGuiDiagnostics.ClickAsync(window, "action/CancelOfficialEngineUpdateCommand");
        Check(!vm.IsEngineUpdateConfirmationVisible && EngineBackend.UpdateCalls == 0, "Cancel leaves the engine untouched");
        await ImGuiDiagnostics.ClickAsync(window, "action/RequestOfficialEngineUpdateCommand");
        await ImGuiDiagnostics.ClickAsync(window, "action/ConfirmOfficialEngineUpdateCommand");
        for (var n = 0; n < 30 && EngineBackend.UpdateCalls == 0; n++) await Task.Delay(50);
        Check(EngineBackend.UpdateCalls == 1, "Confirmation invokes the update backend, not a second engine download");
        for (var n = 0; n < 30 && !vm.StatusMessage.StartsWith("Échec", StringComparison.Ordinal); n++) await Task.Delay(50);
        Check(vm.StatusMessage.Contains("Fixture update blocked"), "Update failures are shown without pretending the engine was updated");
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(ImGuiDiagnostics.Elements(window).Contains("engine-patch-plan"), "The engine sheet keeps separate download, temporary-space and final-growth figures from the actual patch plan");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "engine-patch-space-plan.png"));
        vm.ShowEnginesCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 4);
        var row = "row/" + engine.DisplayName;
        await ImGuiDiagnostics.ClickAsync(window, row, rightButton: true);
        Check(ImGuiDiagnostics.Elements(window).Contains("engine-context/apply-update"), "Engine right-click exposes the same update action");
        await ImGuiDiagnostics.ClickAsync(window, "engine-context/apply-update");
        Check(vm.IsEngineUpdateConfirmationVisible, "Right-click routes to the confirmation on the engine sheet");
        vm.CancelOfficialEngineUpdateCommand.Execute(null);

        var fab = (FixtureFab)typeof(MainWindowViewModel).GetField("_directFabClient", Private)!.GetValue(vm)!;
        var version = new RemoteFabProjectVersion("fixture-artifact", [], ["5.5"], ["Windows"]);
        var asset = new RemoteFabAsset("updates-fixture", "fixture", "Downloaded Fab fixture", "Fixture", "Fab", "assetPack", null, null, ["Simulation"], [version]);
        fab.Library = [asset];
        var pack = Path.Combine(Output, "DownloadedFab"); Directory.CreateDirectory(pack);
        var settings = (ISettingsStore)typeof(MainWindowViewModel).GetField("_settings", Private)!.GetValue(vm)!;
        await settings.SetAsync(MainWindowViewModel.FabStoredDownloadsSetting, JsonSerializer.Serialize(new[] { new
        {
            AssetId = asset.AssetId, Version = "UE : 5.5", ArtifactId = version.ArtifactId, Path = pack,
            Bytes = 100, DownloadedAt = DateTimeOffset.UtcNow, Source = "Fab",
            SourceManifest = new DirectFabSourceManifest("EpicBuildPatchManifest", "fixture", "old", 1, 1, 100, "OLD-CONTENT")
        }}));
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadLibrarySettingsAsync", Private)!.Invoke(vm, null)!;
        Call(vm, "ApplyDirectFabStatus", new DirectFabStatus(DirectFabConnectionState.Connected, "Fixture"));
        Call(vm, "ReplaceRemoteAssets", (Func<RemoteFabAsset, bool>)(_ => true), fab.Library);
        vm.ShowLibraryFabCommand.Execute(null);
        Call(vm, "ShowCachedRemoteFabPreview");
        await ImGuiDiagnostics.FramesAsync(window, 6);
        Check(ImGuiDiagnostics.Elements(window).Contains("fab/check-updates"), "Fab update check sits beside refresh and size analysis");
        await ImGuiDiagnostics.ClickAsync(window, "fab/check-updates");
        for (var n = 0; n < 100 && (fab.RevisionChecks == 0 || vm.IsCheckingFabUpdates || vm.IsFabLibraryRefreshing); n++) await Task.Delay(50);
        Check(fab.RevisionChecks == 1 && vm.FabUpdateSummary.Contains("1 pack(s)"), "Refresh checks only the downloaded supplier artifact");
        await ImGuiDiagnostics.FramesAsync(window, 8);
        var cards = vm.PagedRemoteFabRows.SelectMany(item => item.Cards).ToArray();
        var card = cards.Single(item => item.Asset.AssetId == asset.AssetId);
        Check(card.HasSupplierUpdate && card.UpdateLabel.Contains("Mise à jour"), "Changed Fab content is visible on its card");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-update-cards.png"));
        window.Width = 900; vm.ShowCompactLibraryCardsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 4);
        var pane = ImGuiDiagnostics.ElementBounds(window, "pane/library/content");
        var updateAction = ImGuiDiagnostics.ElementBounds(window, "fab/check-updates");
        Check(updateAction.X >= pane.X && updateAction.Z <= pane.Z, "Fab update check wraps inside a narrow catalog header");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-update-narrow.png"));
        window.Width = 1280;
        await ImGuiDiagnostics.ClickAsync(window, "library-view/rows"); await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(ImGuiDiagnostics.Elements(window).Contains("table-header/library-assets/5"), "Library rows expose a separate supplier update column");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-update-rows.png"));
        card.OpenDetailsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 5);
        card = vm.SelectedRemoteFabAssetCard!;
        Check(vm.SelectedFabStoredDownloads.Single().UpdateLabel.Contains("Mise à jour fournisseur"), "The downloaded version shows its supplier update status");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "fab-update-product.png"));
        fab.SourceRevision = null;
        vm.CheckFabAssetUpdates(card);
        for (var n = 0; n < 100 && vm.IsCheckingFabUpdates; n++) await Task.Delay(50);
        Check(!card.HasSupplierUpdate && card.UpdateLabel.Contains("non vérifiable"), "Unavailable manifests never leave a stale current or update claim");
        fab.SourceRevision = new("EpicBuildPatchManifest", "fixture", "old", 1, 1, 100, "OLD-CONTENT");
        vm.CheckFabAssetUpdates(card);
        for (var n = 0; n < 100 && vm.IsCheckingFabUpdates; n++) await Task.Delay(50);
        Check(!card.HasSupplierUpdate && card.UpdateLabel.Contains("à jour") && card.UpdateForeground == "#65CCA9", "Identical content is shown as current in green");
        Check(vm.SelectedFabStoredDownloads.Single().UpdateLabel.StartsWith("À jour", StringComparison.Ordinal), "Rechecking refreshes the version status in an already open sheet");
        Check(Directory.EnumerateFileSystemEntries(pack).Count() == 0, "Fab verification never downloads or changes local asset files");
    }
}
