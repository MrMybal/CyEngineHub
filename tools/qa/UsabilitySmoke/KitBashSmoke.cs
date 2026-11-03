using System.IO.Compression;
using Avalonia.Controls;
using Avalonia.Input;
using CyEngineHub.Core.Marketplaces;
using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Extensions;
using CyEngineHub.Fab.Packaging;

partial class Program
{
    static async Task TestKitBash(Window window, MainWindowViewModel vm)
    {
        const string id = "cyenginehub.marketplace-kitbash3d";
        var extensionRoot = Path.GetFullPath("extensions/builtin/" + id);
        var manifest = await new HubExtensionManifestParser().ParseAsync(extensionRoot);
        var downloads = Path.Combine(Output, "KitBashDownloads");
        var library = Path.Combine(Output, "KitBashLibrary");
        var settings = (ISettingsStore)typeof(MainWindowViewModel).GetField("_settings", Private)!.GetValue(vm)!;
        await new MarketplaceConnectorSettingsStore(settings).SaveAsync(id, new(true, downloads, "ZipFastest", [new(library)]));
        await (Task)typeof(MainWindowViewModel).GetMethod("RebuildMarketplaceConnectorsAsync", Private)!.Invoke(vm,
            new object[] { new HubExtensionSnapshot[] { new(manifest, extensionRoot, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null) } })!;
        var connector = vm.MarketplaceConnectors.Single(item => item.Id == id);
        Check(connector.HasPackageStorage && connector.SettingsHeading.Contains("ALPHA"), "KitBash built-in descriptor creates an Alpha connector with real package storage");
        vm.ShowSettingsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/marketplaces");
        await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/marketplaces/" + id);
        var elements = ImGuiDiagnostics.Elements(window);
        Check(elements.Contains("combo/SelectedCompressionOption") && elements.Contains("field/DownloadDirectory") && elements.Contains("field/Path"),
            "KitBash shares the compression, download path and ordered library UI with Cosmos");
        Check(!elements.Contains("cosmos-import/project") && !elements.Contains("field/FabDownloadConnections"), "KitBash exposes neither unrelated Cosmos project settings nor fictitious download concurrency");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "kitbash-settings.png"));
        await ImGuiDiagnostics.ClickAsync(window, "connector-enabled/" + id); await connector.PendingWrite;
        Check(!connector.OpenInventoryCommand.CanExecute(null), "Disabling the connector blocks new website operations without claiming a logged-in account");
        await ImGuiDiagnostics.ClickAsync(window, "connector-enabled/" + id); await connector.PendingWrite;
        connector.OpenLibraryCommand.Execute(null);
        await Wait(() => vm.SelectedConnectorLibrary is not null && !vm.SelectedConnectorLibrary.IsBusy);
        await ImGuiDiagnostics.FramesAsync(window, 5);
        var local = vm.SelectedConnectorLibrary!;
        Check(local.SupportsDirectoryImport && local.SupportsInstallation, "KitBash native library exposes local kit import and installation configuration");
        var kit = Path.Combine(Output, "Original Kit");
        Directory.CreateDirectory(Path.Combine(kit, "Models")); Directory.CreateDirectory(Path.Combine(kit, "Textures"));
        await File.WriteAllTextAsync(Path.Combine(kit, "Models", "Tower.fbx"), "fixture model");
        await File.WriteAllTextAsync(Path.Combine(kit, "Textures", "Wall.png"), "fixture texture");
        await ImGuiDiagnostics.ClickAsync(window, "field/ImportDirectoryPath");
        await ImGuiDiagnostics.TextAsync(window, kit);
        await ImGuiDiagnostics.KeyAsync(window, Key.Tab);
        await ImGuiDiagnostics.ClickAsync(window, "command/ImportDirectoryPathCommand");
        await Wait(() => local.Assets.Count == 1 && !local.IsBusy);
        var raw = local.Assets.Single();
        Check(raw.Asset.MarketplaceId == id && raw.Asset.AssetKind == "Assets RAW" && raw.Asset.FilePath.StartsWith(library), "Manual directory field runs actual JSON packaging and library placement, with no download");
        Check(await File.ReadAllTextAsync(Path.Combine(kit, "Models", "Tower.fbx")) == "fixture model", "Original Cargo/KitBash folder is preserved byte-for-byte");
        var pickerRequests = 0;
        EventHandler<MarketplaceImportRequestEventArgs> pick = (_, request) =>
        { pickerRequests++; Check(request.SelectDirectory, "Browse uses the folder picker rather than a file picker"); request.Completion.TrySetResult([]); };
        var nativePicker = (EventHandler<MarketplaceImportRequestEventArgs>)typeof(CyEngineHub.Desktop.Views.MainWindow)
            .GetMethod("OnMarketplaceImportRequested", Private)!.CreateDelegate(typeof(EventHandler<MarketplaceImportRequestEventArgs>), window);
        vm.MarketplaceImportRequested -= nativePicker; // No real Windows dialog or user interaction in the fixture.
        vm.MarketplaceImportRequested += pick;
        try
        {
            await ImGuiDiagnostics.ClickAsync(window, "command/ImportDirectoryCommand");
            await Wait(() => pickerRequests == 1);
        }
        finally { vm.MarketplaceImportRequested -= pick; vm.MarketplaceImportRequested += nativePicker; }
        // Use the actual registration callback used by completed official browser downloads.
        var zip = Path.Combine(Output, "Owned Unreal Kit.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        { using var writer = new StreamWriter(archive.CreateEntry("Content/KitBash/Tower.uasset").Open()); writer.Write("native unreal fixture"); }
        await (Task)typeof(MainWindowViewModel).GetMethod("RegisterConnectorDownloadAsync", Private)!.Invoke(vm, new object[] { id, zip })!;
        await local.LoadAsync();
        var native = local.Assets.Single(asset => asset.Asset.AssetKind == "Pack Unreal");
        native.OpenCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "kitbash-local-fiche.png"));
        await ImGuiDiagnostics.ClickAsync(window, "command/PrepareInstallationCommand");
        await Wait(() => vm.PackGroups.Count == 1);
        await ImGuiDiagnostics.FramesAsync(window, 6);
        Check(vm.PackGroups[0].Items.Single().Path == native.Asset.FilePath && vm.PackGroups[0].Items[0].Source == id,
            "Fiche installation opens the exact local KitBash variant and editable scripts, without redownload");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "kitbash-install-options.png"));
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/install");
        var installed = Path.Combine(vm.Projects[0].Project.RootPath, "Content", "KitBash", "Tower.uasset");
        await Wait(() => File.Exists(installed) && !vm.IsPackGroupBusy);
        Check(await File.ReadAllTextAsync(installed) == "native unreal fixture" && File.Exists(zip), "Confirmed KitBash installation uses the real transactional importer and keeps the original ZIP");
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/close");
        local.BackCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "kitbash-library.png"));
        Check(local.IsListVisible && local.Assets.Count == 2, "Back returns to the KitBash assets browser with both local variants");

        static async Task Wait(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!condition() && DateTime.UtcNow < until) await Task.Delay(30);
            Check(condition(), "KitBash async operation completes within fixture timeout");
        }
    }
}
