using Avalonia.Controls;
using Avalonia.Input;
using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Extensions;
using CyEngineHub.Fab.Packaging;

partial class Program
{
    static async Task TestCosmosSettings(Window window, MainWindowViewModel vm)
    {
        const string id = "cyenginehub.marketplace-cosmos";
        var settings = (ISettingsStore)typeof(MainWindowViewModel).GetField("_settings", Private)!.GetValue(vm)!;
        var store = new MarketplaceConnectorSettingsStore(settings);
        var previous = vm.MarketplaceConnectors.ToArray();
        var root = Path.Combine(Output, "CosmosSettings");
        var firstLibrary = Path.Combine(root, "FirstLibrary");
        var secondLibrary = Path.Combine(root, "SecondLibrary");
        Directory.CreateDirectory(firstLibrary);
        var original = Path.Combine(firstLibrary, "original-fixture.txt");
        await File.WriteAllTextAsync(original, "original fixture bytes");
        var opened = 0;
        var errors = new List<Exception>();
        var connector = new MarketplaceConnectorViewModel(new HubExtensionSnapshot(new HubExtensionManifest
        {
            Id = id, DisplayName = "Cosmos · mes assets",
            Description = "Alpha : inventaire web officiel et bibliothèque locale. Packages JSON, compression et bibliothèques ordonnées avec quotas ; dossier d’assets configurable par projet Unreal. Les fichiers originaux sont conservés. Aucun inventaire ni image du site extrait automatiquement.",
            Marketplace = new() { InventoryUrl = "https://example.invalid/inventory" },
        }, root, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null), true, Path.Combine(root, "Downloads"),
            _ => { opened++; return Task.CompletedTask; }, () => Task.CompletedTask, errors.Add,
            snapshot => store.SaveAsync(id, snapshot),
            new MarketplaceConnectorSettings(true, Path.Combine(root, "Downloads"), "ZipUltra", [new(firstLibrary)]));
        var other = new MarketplaceConnectorViewModel(new HubExtensionSnapshot(new HubExtensionManifest
        {
            Id = "cyenginehub.marketplace-megascans-fab", DisplayName = "Megascans via Fab",
            Marketplace = new() { InventoryUrl = "https://example.invalid/other" },
        }, root, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null), true, Path.Combine(root, "OtherDownloads"),
            _ => Task.CompletedTask, () => Task.CompletedTask, errors.Add, _ => Task.CompletedTask);
        var fabCompression = vm.SelectedFabCompressionOption;
        var fabConnections = vm.FabDownloadConnections;
        var quixelConnections = vm.QuixelDownloadConnections;
        try
        {
            vm.MarketplaceConnectors.Clear(); vm.MarketplaceConnectors.Add(connector); vm.MarketplaceConnectors.Add(other);
            vm.ShowSettingsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
            await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/marketplaces");
            await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/marketplaces/Fab");
            var fabCombo = ImGuiDiagnostics.ElementBounds(window, "combo/SelectedFabCompressionOption");
            var fabPath = ImGuiDiagnostics.ElementBounds(window, "field/FabDownloadDirectory");
            await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/marketplaces/" + id);
            Check(connector.SelectedCompressionOption.Label == "Ultra" && connector.Compression == "ZipUltra",
                "Cosmos maps saved technical compression values to the shared marketplace labels");
            Check(connector.CompressionOptions.Where(option => option.StorageMode != "Uncompressed")
                .Select(option => (option.StorageMode, option.Label, option.Description))
                .SequenceEqual(vm.MarketplaceCompressionOptions.Select(option => (option.StorageMode.ToString(), option.Label, option.Description))),
                "Cosmos and Fab reuse exactly the same ZIP labels and descriptions");
            Check(connector.CompressionOptions.Any(option => option.StorageMode == "Uncompressed"),
                "The existing uncompressed Cosmos storage mode remains available");
            var cosmosCombo = ImGuiDiagnostics.ElementBounds(window, "combo/SelectedCompressionOption");
            var cosmosPath = ImGuiDiagnostics.ElementBounds(window, "field/DownloadDirectory");
            var libraryPath = ImGuiDiagnostics.ElementBounds(window, "field/Path");
            Check(Math.Abs(fabCombo.X - cosmosCombo.X) < 2 && Math.Abs(fabCombo.Z - cosmosCombo.Z) < 2 && Math.Abs(fabPath.X - cosmosPath.X) < 2,
                "Cosmos compression and download fields use the same column alignment as Fab");
            Check(cosmosCombo.Y < cosmosPath.Y && cosmosPath.Y < libraryPath.Y && libraryPath.W < ImGuiDiagnostics.ElementBounds(window, "marketplace-settings/save").Y,
                "Cosmos uses compression, download path, ordered libraries and an outside-card save footer in that order");
            Check(!ImGuiDiagnostics.Elements(window).Contains("field/FabDownloadConnections") && !ImGuiDiagnostics.Elements(window).Contains("field/QuixelDownloadConnections"),
                "Browser-based connectors expose no fake Fab or Megascans connection settings");
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "cosmos-settings-unified.png"));
            await ImGuiDiagnostics.ClickAsync(window, "combo/SelectedCompressionOption");
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "cosmos-settings-compression.png"));
            await ImGuiDiagnostics.ClickAsync(window, "combo-option/SelectedCompressionOption/ZipFastest");
            await connector.PendingWrite;
            Check(connector.SelectedCompressionOption.Label == "Rapide" && (await store.LoadAsync(id, root)).Compression == "ZipFastest",
                "Selecting a friendly compression label saves the original compatible storage value");
            await ImGuiDiagnostics.ClickAsync(window, "command/AddLibraryCommand");
            Check(connector.LibraryLocations.Count == 2, "The shared-style add-folder button adds a Cosmos library row");
            await Edit("field/Path", secondLibrary);
            await Edit("field/MaximumGigabytes", "0,5");
            await connector.PendingWrite;
            Check((await store.LoadAsync(id, root)).Libraries![1].MaximumBytes == 536870912,
                "Cosmos automatically saves edited paths and French decimal library quotas");
            var second = connector.LibraryLocations[1];
            await ImGuiDiagnostics.ClickAsync(window, "command/MoveUpCommand"); await connector.PendingWrite;
            Check(ReferenceEquals(connector.LibraryLocations[0], second) && connector.LibraryLocations[0].PriorityLabel == "#1" &&
                  connector.LibraryLocations[1].PriorityLabel == "#2" && (await store.LoadAsync(id, root)).Libraries![0].Path == secondLibrary,
                "Moving a Cosmos library refreshes its visible priority and persists its new order");
            await ImGuiDiagnostics.ClickAsync(window, "connector-enabled/" + id); await connector.PendingWrite;
            Check(!(await store.LoadAsync(id, root)).Enabled && !connector.OpenInventoryCommand.CanExecute(null),
                "The Cosmos header toggle persists enablement and disables opening its inventory");
            await ImGuiDiagnostics.ClickAsync(window, "connector-enabled/" + id); await connector.PendingWrite;
            await ImGuiDiagnostics.ClickAsync(window, "marketplace-settings/save"); await connector.PendingWrite;
            await ImGuiDiagnostics.ClickAsync(window, "marketplace-settings/open-inventory");
            Check(opened == 1 && errors.Count == 0, "The aligned footer saves and opens only the selected connector through its real commands");
            var goodSnapshot = await store.LoadAsync(id, root);
            await Edit("field/MaximumGigabytes", "invalid");
            Check(connector.SaveStatus.StartsWith("Non enregistré", StringComparison.Ordinal) &&
                  (await store.LoadAsync(id, root)).Libraries!.SequenceEqual(goodSnapshot.Libraries!),
                "Invalid Cosmos quota remains visible and cannot overwrite the last valid saved settings");
            await Edit("field/MaximumGigabytes", "0,5"); await connector.PendingWrite;
            await ImGuiDiagnostics.ClickAsync(window, "command/RemoveCommand"); await connector.PendingWrite;
            Check(connector.LibraryLocations.Count == 1 && connector.LibraryLocations[0].PriorityLabel == "#1" &&
                  (await store.LoadAsync(id, root)).Libraries!.Count == 1 && File.Exists(original),
                "Removing a Cosmos library only removes its configuration and keeps its original files intact");
            foreach (var width in new[] { 1280, 1024 })
            {
                window.Width = width; await ImGuiDiagnostics.FramesAsync(window, 5);
                var save = ImGuiDiagnostics.ElementBounds(window, "marketplace-settings/save");
                var open = ImGuiDiagnostics.ElementBounds(window, "marketplace-settings/open-inventory");
                var download = ImGuiDiagnostics.ElementBounds(window, "field/DownloadDirectory");
                Check(Math.Abs(save.Y - open.Y) < 2 && open.Z < window.Width && download.Z < window.Width && save.W < window.Height,
                    "Cosmos footer and download fields remain aligned and inside the window at width " + width);
                await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "cosmos-settings-" + width + ".png"));
            }
            await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/marketplaces/" + other.Id);
            Check(!ImGuiDiagnostics.Elements(window).Contains("combo/SelectedCompressionOption") &&
                  ImGuiDiagnostics.Elements(window).Contains("marketplace-settings/save"),
                "Other connectors keep their shared footer without unsupported Cosmos storage sections");
            Check(vm.SelectedFabCompressionOption == fabCompression && vm.FabDownloadConnections == fabConnections && vm.QuixelDownloadConnections == quixelConnections &&
                  await File.ReadAllTextAsync(original) == "original fixture bytes" && !Directory.Exists(secondLibrary),
                "Editing Cosmos neither changes other marketplaces nor moves or materializes stored assets");
        }
        finally
        {
            window.Width = 1280; vm.MarketplaceConnectors.Clear();
            foreach (var item in previous) vm.MarketplaceConnectors.Add(item);
            await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/marketplaces/Fab");
        }

        async Task Edit(string element, string value)
        {
            await ImGuiDiagnostics.ClickAsync(window, element);
            await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
            await ImGuiDiagnostics.TextAsync(window, value);
        }
    }
}
