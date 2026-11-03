using System.IO.Compression;
using Avalonia.Controls;
using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Desktop.Views;
using CyEngineHub.Extensions;

partial class Program
{
    static async Task TestNewMarketplaces(Window window, MainWindowViewModel vm)
    {
        string[] slugs = ["itchio", "blendkit", "artstation", "gumroad", "cgtrader"];
        var settings = (ISettingsStore)typeof(MainWindowViewModel).GetField("_settings", Private)!.GetValue(vm)!;
        var store = new MarketplaceConnectorSettingsStore(settings);
        var parser = new HubExtensionManifestParser();
        var extensions = new List<HubExtensionSnapshot>();
        foreach (var slug in slugs.Concat(new[] { "cosmos", "kitbash3d", "megascans-fab" }))
        {
            var id = "cyenginehub.marketplace-" + slug;
            // Read the DISTRIBUTED manifests, not the source checkout.
            var root = Path.Combine(AppContext.BaseDirectory, "Extensions", id);
            var manifest = await parser.ParseAsync(root);
            extensions.Add(new(manifest, root, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null));
            await store.SaveAsync(id, new(true, Path.Combine(Output, slug, "Downloads"), "ZipFastest", [new(Path.Combine(Output, slug, "Library"))]));
        }
        await (Task)typeof(MainWindowViewModel).GetMethod("RebuildMarketplaceConnectorsAsync", Private)!.Invoke(vm, new object[] { extensions.ToArray() })!;
        Check(vm.MarketplaceConnectors.Count == 8 && vm.MarketplaceConnectors.Count(item => item.Name.Contains("Blendkit")) == 1,
            "Five packaged plugins are discovered beside Cosmos/Cargo/Megascans, with one BlenderKit/Blendkit service");
        var nativeBrowser = (EventHandler<MarketplaceBrowserRequestEventArgs>)typeof(MainWindow).GetMethod("OnMarketplaceBrowserRequested", Private)!
            .CreateDelegate(typeof(EventHandler<MarketplaceBrowserRequestEventArgs>), window);
        var requests = new Dictionary<string, MarketplaceBrowserRequestEventArgs>();
        EventHandler<MarketplaceBrowserRequestEventArgs> record = (_, request) => requests[request.Id] = request;
        vm.MarketplaceBrowserRequested -= nativeBrowser; // No live accounts/network in this test.
        vm.MarketplaceBrowserRequested += record;
        try
        {
            foreach (var slug in slugs)
            {
                var id = "cyenginehub.marketplace-" + slug;
                var connector = vm.MarketplaceConnectors.Single(item => item.Id == id);
                vm.ShowSettingsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
                await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/marketplaces");
                await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/marketplaces/" + id);
                var elements = ImGuiDiagnostics.Elements(window);
                Check(elements.Contains("combo/SelectedCompressionOption") && elements.Contains("field/DownloadDirectory") && elements.Contains("field/Path"), slug + " exposes the unified compression, downloads and ordered-library settings");
                Check(!elements.Contains("field/FabDownloadConnections") && !elements.Contains("cosmos-import/project"), slug + " has no fake Fab concurrency or unrelated Cosmos settings");
                connector.Compression = "ZipStore"; await connector.PendingWrite;
                Check((await store.LoadAsync(id, Output)).Compression == "ZipStore", slug + " persists its own compression setting");
                Check(vm.MarketplaceConnectors.Where(item => item.Id != id && !slugs.TakeWhile(other => other != slug).Contains(item.Id.Replace("cyenginehub.marketplace-", "")))
                    .All(item => item.Compression == "ZipFastest"), slug + " changes do not spill into other marketplace forms");
                await ImGuiDiagnostics.ClickAsync(window, "connector-enabled/" + id); await connector.PendingWrite;
                Check(!connector.OpenInventoryCommand.CanExecute(null), slug + " disablement blocks new website operations");
                await ImGuiDiagnostics.ClickAsync(window, "connector-enabled/" + id); await connector.PendingWrite;
                await ImGuiDiagnostics.ClickAsync(window, "marketplace-settings/open-inventory");
                await Wait(() => requests.ContainsKey(id));
                var request = requests[id];
                Check(request.InventoryUri.AbsoluteUri == connector.InventoryUrl && request.ProfileDirectory.EndsWith(id) &&
                    request.GetDownloadDirectory() == connector.DownloadDirectory, slug + " uses its official entry point, isolated session and configured download directory");
                connector.OpenLibraryCommand.Execute(null);
                await Wait(() => vm.LibrarySource == "Connector:" + id && vm.SelectedConnectorLibrary is { IsBusy: false });
                var local = vm.SelectedConnectorLibrary!;
                Check(local.SupportsDirectoryImport && local.SupportsInstallation && local.InventoryButtonLabel == connector.AcquisitionLabel,
                    slug + " obtains its acquisition label and local features from the plugin manifest");
                Check(local.WorkflowNotice.Contains("Alpha") && !string.IsNullOrWhiteSpace(local.WorkflowNotice), slug + " shows its actual workflow and API limitations");
                var zip = Path.Combine(Output, slug + ".zip");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                { using var writer = new StreamWriter(archive.CreateEntry("Content/" + slug + "/Model.uasset").Open()); writer.Write(slug + " fixture"); }
                var original = await File.ReadAllBytesAsync(zip);
                await request.RegisterDownload(zip); await request.RegisterDownload(zip);
                await local.LoadAsync();
                var asset = local.Assets.Single();
                Check(asset.Asset.MarketplaceId == id && asset.Asset.FilePath.StartsWith(Path.Combine(Output, slug, "Library")) &&
                    (await File.ReadAllBytesAsync(zip)).SequenceEqual(original), slug + " registers exactly one local package and preserves its original download");
                asset.OpenCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
                await ImGuiDiagnostics.ClickAsync(window, "command/PrepareInstallationCommand");
                await Wait(() => vm.PackGroups.Any(group => group.Id == "connector-install-" + asset.Asset.Id));
                await ImGuiDiagnostics.FramesAsync(window, 4);
                var plan = vm.PackGroups.Single(group => group.Id == "connector-install-" + asset.Asset.Id);
                Check(plan.Items.Single().Source == id && plan.Items[0].Path == asset.Asset.FilePath, slug + " prepares the exact local variant without downloading another version");
                await ImGuiDiagnostics.ClickAsync(window, "pack-groups/install");
                var installed = Path.Combine(vm.Projects[0].Project.RootPath, "Content", slug, "Model.uasset");
                await Wait(() => File.Exists(installed) && !vm.IsPackGroupBusy);
                Check(await File.ReadAllTextAsync(installed) == slug + " fixture", slug + " confirms and completes the actual transactional import on an isolated project");
                await ImGuiDiagnostics.ClickAsync(window, "pack-groups/close");
                local.BackCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
                Check(local.IsListVisible, slug + " back returns to its own asset browser");
            }
            Check(requests.Values.Select(item => item.ProfileDirectory).Distinct().Count() == 5, "All five browser sessions are isolated");
            vm.ShowSettingsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
            await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/marketplaces");
            foreach (var width in new[] { 1280, 1024 })
            {
                window.Width = width; await ImGuiDiagnostics.FramesAsync(window, 5);
                foreach (var slug in slugs)
                {
                    var bounds = ImGuiDiagnostics.ElementBounds(window, "settings-subtab/marketplaces/cyenginehub.marketplace-" + slug);
                    Check(bounds.Z < window.Width && bounds.W < window.Height, slug + " settings tab is reachable at width " + width);
                }
                await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "marketplaces-settings-" + width + ".png"));
            }
        }
        finally { vm.MarketplaceBrowserRequested -= record; vm.MarketplaceBrowserRequested += nativeBrowser; }

        static async Task Wait(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(20);
            while (!condition() && DateTime.UtcNow < until) await Task.Delay(25);
            if (!condition()) throw new InvalidOperationException("Marketplace fixture timed out");
        }
    }
}
