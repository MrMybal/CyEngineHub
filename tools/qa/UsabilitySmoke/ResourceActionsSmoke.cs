using System.Collections;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Storage.Models;

static partial class Program
{
    static async Task TestResourceActions(Window window, MainWindowViewModel vm, RecordingLauncher launcher)
    {
        var launches = launcher.Launches.Count;
        vm.ShowProjectsCommand.Execute(null); vm.SetResourcePresentation(false, 0);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        var project = vm.Projects[0];
        await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/" + project.Name, horizontal: .8f, vertical: .7f, rightButton: true);
        Check(new[] { "open", "launch", "folder", "favorite", "size" }.All(id => ImGuiDiagnostics.Elements(window).Contains("item-context/" + id)), "Project right-click exposes actions for the clicked project");
        await ImGuiDiagnostics.ClickAsync(window, "item-context/favorite");
        Check(vm.IsResourceFavorite(project) && launcher.Launches.Count == launches, "Project favorite is changed without launching the editor");
        Check(ImGuiDiagnostics.Elements(window).Contains("selection/card"), "Selected cards have a tinted fill, thick border and check badge");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-project-selection-favorite.png"));
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Lignes");
        await ImGuiDiagnostics.ClickAsync(window, "row/" + project.Name);
        Check(ImGuiDiagnostics.Elements(window).Contains("selection/row/" + project.Name), "Selected project rows have a full-width contrasting selection");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-project-row-selection.png"));
        await vm.AnalyzeResourceSizesAsync("projects", true, project);
        var before = vm.ResourceSizeBytes(project);
        Check(before is > 0, "Project sizes are measured from the project root and displayed in lists");
        var probe = Path.Combine(project.Project.RootPath, "size-probe.bin");
        await File.WriteAllBytesAsync(probe, new byte[1024]);
        await vm.AnalyzeResourceSizesAsync("projects", false, project);
        Check(vm.ResourceSizeBytes(project) == before, "Missing-only analysis preserves completed measurements");
        await vm.AnalyzeResourceSizesAsync("projects", true, project);
        Check(vm.ResourceSizeBytes(project) == before + 1024, "Full recheck updates a changed project footprint");
        var snapshotCache = (CyEngineHub.Storage.Persistence.StorageSnapshotCache)typeof(MainWindowViewModel).GetField("_storageSnapshotCache", Private)!.GetValue(vm)!;
        var persisted = await snapshotCache.LoadAsync();
        Check(persisted!.Entries.Any(entry => entry.Path == project.Project.RootPath && entry.SizeBytes == before + 1024), "Resource measurements are persisted for the next startup");
        var scheduler = (HubTaskScheduler)typeof(MainWindowViewModel).GetField("_taskScheduler", Private)!.GetValue(vm)!;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = scheduler.Enqueue(new HubTaskDefinition("Stockage", "Fixture blocker", async (_, token) => { started.SetResult(); await Task.Delay(Timeout.Infinite, token); }));
        await started.Task;
        var canceledScan = vm.AnalyzeResourceSizesAsync("projects", true, project);
        var queued = scheduler.GetSnapshot().Single(task => task.Title.StartsWith("Revérifier les tailles", StringComparison.Ordinal) && task.Status == HubTaskStatus.Queued);
        scheduler.Cancel(queued.Id);
        await canceledScan;
        Check(vm.ResourceSizeBytes(project) == before + 1024, "Canceling an analysis preserves previous measurements");
        scheduler.Cancel(blocker.Id); await blocker.Completion;
        await ImGuiDiagnostics.ClickAsync(window, "size-analysis/projects");
        Check(ImGuiDiagnostics.Elements(window).Contains("size-analysis/missing/projects") && ImGuiDiagnostics.Elements(window).Contains("size-analysis/full/projects"), "Size analysis button exposes both missing-only and full recheck");
        await ImGuiDiagnostics.EscapeAsync(window);

        vm.ShowLibraryFabCommand.Execute(null); vm.LibrarySearchQuery = ""; vm.SelectedLibrarySort = "Nom · A → Z";
        vm.ShowImageOnlyLibraryCardsCommand.Execute(null);
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        await ImGuiDiagnostics.FramesAsync(window, 4);
        // The legacy smoke scenario ends scrolled to the last item. A changed source resets its viewport.
        vm.ShowLibraryLocalCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 2);
        vm.ShowLibraryFabCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 4);
        var firstCard = ImGuiDiagnostics.Elements(window).First(key => key.StartsWith("card/body/asset/", StringComparison.Ordinal));
        await ImGuiDiagnostics.ClickAsync(window, firstCard, rightButton: true);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-fab-context-selection.png"));
        Check(new[] { "open", "download", "install", "collections", "favorite", "size" }.All(id => ImGuiDiagnostics.Elements(window).Contains("item-context/" + id)), "Fab right-click exposes product actions without opening a different product");
        await ImGuiDiagnostics.ClickAsync(window, "item-context/open");
        var asset = vm.SelectedRemoteFabAssetCard!;
        Check(vm.IsRemoteFabProductVisible, "Product context action opens the clicked product");
        vm.ToggleResourceFavorite(asset);
        await (Task)typeof(MainWindowViewModel).GetField("_favoriteWrite", Private)!.GetValue(vm)!;
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadResourceFavoritesAsync", Private)!.Invoke(vm, null)!;
        Check(vm.IsResourceFavorite(project) && vm.IsResourceFavorite(asset), "Resource favorites survive saving and reloading settings");
        var engine = vm.Engines[0]; vm.ToggleResourceFavorite(engine);
        Check(vm.IsResourceFavorite(engine), "Engine favorites use the same persistent resource mechanism");
        vm.ToggleResourceFavorite(engine);

        var root = Path.Combine(Output, "DownloadedPack"); Directory.CreateDirectory(root);
        var manifest = Path.Combine(root, "cyenginehub-package.json");
        await File.WriteAllTextAsync(manifest, "{\"storedBytes\":12345,\"uncompressedBytes\":32768}");
        await File.WriteAllBytesAsync(Path.Combine(root, "payload.bin"), new byte[128]);
        var records = (IList)typeof(MainWindowViewModel).GetField("_fabStoredDownloads", Private)!.GetValue(vm)!;
        var recordType = typeof(MainWindowViewModel).GetNestedType("FabStoredDownloadRecord", BindingFlags.NonPublic)!;
        var saved = records.Cast<object>().ToArray(); records.Clear();
        foreach (var artifact in new[] { "local-supplier", "duplicate-path" })
        {
            var json = JsonSerializer.Serialize(new { AssetId = asset.Asset.AssetId, Version = "UE 5.5", ArtifactId = artifact,
                Path = root, Bytes = 0, DownloadedAt = DateTimeOffset.UtcNow, Source = asset.Source, PackageKind = "ContentPack", StorageMode = "Uncompressed", ManifestPath = manifest,
                EngineVersions = new[] { "5.5" }, AssetTitle = asset.Name });
            records.Add(JsonSerializer.Deserialize(json, recordType)!);
        }
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshPackFootprintsAsync", Private)!.Invoke(vm, null)!;
        Check(vm.ResourceSizeBytes(asset) == 12345, "Pack manifest sizes appear immediately and duplicate paths are counted once");
        Call(vm, "RebuildSelectedFabStoredDownloads", asset.Asset.AssetId);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-downloaded-supplier-install.png"));
        await ImGuiDiagnostics.ClickAsync(window, "stored-version-action/local-supplier/InstallCommand");
        Check(vm.IsFabInstallOptionsVisible && vm.FabInstallVersions.Count == 1 && vm.FabInstallVersions[0].Version.ArtifactId == "local-supplier", "Supplier downloaded version has an Install button scoped to its exact local artifact");
        Check(launcher.Launches.Count == launches, "Opening local installation options never launches an editor or an installer");
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseFabInstallOptionsCommand");
        await vm.AnalyzeResourceSizesAsync("packs", false, asset);
        Check(vm.ResourceSizeBytes(asset) == 12345, "Missing-only pack analysis reuses known manifest metadata");
        await vm.AnalyzeResourceSizesAsync("packs", true, new object[] { asset.Asset, asset.Asset });
        var measured = Directory.EnumerateFiles(root).Sum(path => new FileInfo(path).Length);
        Check(vm.ResourceSizeBytes(asset) == measured, "Full pack recheck replaces declared payload bytes with actual disk bytes including sidecars");
        Check(vm.StorageEntries.Count(entry => entry.Entry.Path == root && entry.Entry.Role == StorageEntryRole.Footprint) == 1, "Full recheck does not create duplicate pack footprints");
        Check(vm.ResourceSizeBytes(project) == before + 1024, "A pack recheck preserves unrelated project measurements");
        vm.FavoritesFirst = false; vm.SelectedLibrarySort = "Taille locale · décroissant";
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        var sizeSorted = (CyEngineHub.Fab.Remote.RemoteFabAsset[])typeof(MainWindowViewModel).GetField("_visibleRemoteFabCatalog", Private)!.GetValue(vm)!;
        Check(sizeSorted[0].AssetId == asset.Asset.AssetId, "Local-size sorting orders the full catalog before pagination and leaves unknown sizes last");
        vm.FavoritesFirst = true; vm.SelectedLibrarySort = "Nom · A → Z";

        vm.CloseRemoteFabProductCommand.Execute(null);
        vm.FavoritesOnly = true;
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        Check(((Array)typeof(MainWindowViewModel).GetField("_visibleRemoteFabCatalog", Private)!.GetValue(vm)!).Length == 1, "Favorite filtering applies to the entire remote catalog before pagination");
        vm.FavoritesOnly = false; vm.ToggleResourceFavorite(asset); vm.ToggleResourceFavorite(project);
        typeof(MainWindowViewModel).GetField("_activeDerivedInstallArtifactId", Private)!.SetValue(vm, null);
        records.Clear(); foreach (var record in saved) records.Add(record);
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshPackFootprintsAsync", Private)!.Invoke(vm, null)!;
        await ImGuiDiagnostics.FramesAsync(window, 3);
    }
}
