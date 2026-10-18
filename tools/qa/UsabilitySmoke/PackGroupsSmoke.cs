using Avalonia.Controls;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Extensions;

static partial class Program
{
    static async Task TestPackGroups(Window window, MainWindowViewModel vm)
    {
        vm.ShowLibraryLocalCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 4);
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/open");
        await ImGuiDiagnostics.FramesAsync(window, 4);
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/new");
        Check(vm.PackGroups.Count == 1, "Native group manager creates a group without changing files");
        var source = Path.Combine(Output, "GroupSource", "Content", "GroupAsset"); Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "Mesh.uasset"), "isolated fixture");
        var candidate = new LibraryPackGroupItem("fixture", "exact-local-variant", "Fixture local 5.5", "Fab", Path.GetDirectoryName(Path.GetDirectoryName(source))!,
            LibraryPackageContentKind.ContentPack, ["5.5"], [new(UnrealContentPackInstallationScript.ScriptId, new Dictionary<string, string> { ["conflictPolicy"] = "fail" })]);
        vm.PackGroupCandidates.Clear(); vm.PackGroupCandidates.Add(candidate);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/add");
        Check(vm.PackGroups[0].Items.Count == 1, "UI adds exact local variant to the group");
        var second = Path.Combine(Output, "GroupSecond", "Content", "GroupSecond"); Directory.CreateDirectory(second);
        await File.WriteAllTextAsync(Path.Combine(second, "Texture.uasset"), "second isolated fixture");
        vm.PackGroupCandidates.Add(candidate with { AssetId = "second", ArtifactId = "second-variant", Name = "Second pack", Path = Path.GetDirectoryName(Path.GetDirectoryName(second))! });
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/variant");
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/candidate/1");
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/add");
        Check(vm.PackGroups[0].Items.Count == 2, "A group contains several independently configured packs");
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/save");
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(vm.PackGroupStatus.Contains("enregistrés"), "Group manager persists the ordered per-pack script configuration");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-pack-groups.png"));
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/install");
        var installed = Path.Combine(vm.Projects[0].Project.RootPath, "Content", "GroupAsset", "Mesh.uasset");
        var until = DateTime.UtcNow.AddSeconds(10);
        while ((vm.IsPackGroupBusy || !File.Exists(installed)) && DateTime.UtcNow < until) await Task.Delay(50);
        Check(File.Exists(installed) && File.ReadAllText(installed) == "isolated fixture", "One-click group install runs the real transactional content importer on fixture project");
        Check(File.Exists(Path.Combine(vm.Projects[0].Project.RootPath, "Content", "GroupSecond", "Texture.uasset")), "One-click installs both packs, not just the first variant");
        Check(File.Exists(Path.Combine(source, "Mesh.uasset")), "Group installation preserves source package");
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/close");
        vm.Projects[0].OpenDetailsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
        vm.MarketplaceConnectors.Add(new MarketplaceConnectorViewModel(new HubExtensionSnapshot(new HubExtensionManifest
        { Id = "cyenginehub.marketplace-cosmos", DisplayName = "Cosmos", Marketplace = new() { InventoryUrl = "https://example.invalid" } },
            Output, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null), true, Path.Combine(Output, "cosmos-downloads"),
            _ => Task.CompletedTask, () => Task.CompletedTask, _ => { }, _ => Task.CompletedTask));
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(ImGuiDiagnostics.Elements(window).Contains("field/CosmosProjectDestination"), "Cosmos extension contributes its project destination setting");
        vm.CosmosProjectDestination = "Assets/CosmosFixture"; vm.SaveCosmosProjectSettingsCommand.Execute(null);
        await Task.Delay(150);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-cosmos-project-settings.png"));
        Check(vm.StatusMessage.Contains("Dossier Cosmos"), "Per-project Cosmos destination saves independently");
    }
}
