using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Extensions;
using CyEngineHub.Fab.Conversion;

partial class Program
{
    static async Task TestSourceConversionUi(Window window, MainWindowViewModel vm)
    {
        if (!SourceConversionFixture.StartsWith(Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Only explicit fixture folders under artifacts are accepted");
        const string id = "cyenginehub.marketplace-blendkit";
        var root = Path.Combine(AppContext.BaseDirectory, "Extensions", id);
        var manifest = await new HubExtensionManifestParser().ParseAsync(root);
        var settings = (ISettingsStore)typeof(MainWindowViewModel).GetField("_settings", Private)!.GetValue(vm)!;
        var library = Path.Combine(Output, "ConvertedLibrary");
        await new MarketplaceConnectorSettingsStore(settings).SaveAsync(id, new(true, Path.Combine(Output, "Downloads"), "ZipFastest", [new(library)]));
        await (Task)typeof(MainWindowViewModel).GetMethod("RebuildMarketplaceConnectorsAsync", Private)!.Invoke(vm,
            new object[] { new HubExtensionSnapshot[] { new(manifest, root, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null) } })!;
        var original = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(SourceConversionFixture, "Fixture.blend"))));
        await (Task)typeof(MainWindowViewModel).GetMethod("RegisterConnectorDownloadAsync", Private)!.Invoke(vm, new object[] { id, SourceConversionFixture })!;
        vm.MarketplaceConnectors.Single().OpenLibraryCommand.Execute(null);
        await Wait(() => vm.SelectedConnectorLibrary is { IsBusy: false } current && current.Assets.Count == 1);
        var local = vm.SelectedConnectorLibrary!;
        var source = local.Assets.Single(); source.OpenCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 4);
        await ImGuiDiagnostics.ClickAsync(window, "command/PrepareInstallationCommand");
        await Wait(() => vm.PackGroups.Count == 1); await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(vm.PackGroups[0].Items[0].Steps.Select(step => step.ScriptId).SequenceEqual(new[] { "convert.blender-interchange", "unreal.raw-assets" }),
            "Fiche detects a Blender source and preselects conversion then Unreal RAW installation, independent of market name");
        await ImGuiDiagnostics.ClickAsync(window, "pack-groups/close");
        await ImGuiDiagnostics.ClickAsync(window, "command/RequestConversionCommand");
        await Wait(() => vm.IsSourceConversionVisible); await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(vm.SourceConversionConverterId == BlenderSourceConverter.ConverterId && vm.SourceConversionOptions["allowLossy"] == "false",
            "The local-derivative dialog selects Blender and does not silently consent to lossy conversion");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "source-conversion-options.png"));
        await ImGuiDiagnostics.ClickAsync(window, "source-conversion/run");
        await Wait(() => !vm.IsSourceConversionBusy && local.Assets.Count == 2 && !vm.IsSourceConversionVisible);
        var derivative = local.Assets.Single(item => item.Asset.Id != source.Asset.Id);
        Check(derivative.Name.Contains("GLB") && derivative.Asset.AssetKind == "Assets RAW", "Real Blender conversion from the UI publishes a separately named RAW derivative");
        var extracted = Path.Combine(Output, "ReadDerivative");
        await CyEngineHub.Fab.Packaging.DerivedLibraryPackageService.MaterializeAsync(derivative.Asset.FilePath, extracted);
        Check(Directory.GetFiles(extracted, "*.glb", SearchOption.AllDirectories).Length > 0, "Published derivative contains converted GLB geometry, not a renamed Blender file");
        using var package = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(derivative.Asset.FilePath, "cyenginehub-package.json")));
        Check(package.RootElement.GetProperty("derivation").GetProperty("sourceArtifactId").GetString() == source.Asset.Id,
            "The local derivative retains its source catalogue identity in the package manifest");
        Check(original == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(SourceConversionFixture, "Fixture.blend")))) &&
            !File.Exists(Path.Combine(SourceConversionFixture, "AUTOEXEC_RAN")), "UI conversion preserves original bytes and disables embedded autoexec");
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(!ImGuiDiagnostics.Elements(window).Contains("source-conversion/run"), "Completed conversion closes the modal and reveals the derivative fiche");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "source-converted-fiche.png"));
        Check(!Directory.EnumerateDirectories(Path.Combine(Output, "SourceConversionInputs")).Any(), "Successful UI conversion cleans its temporary source/export workspaces");
        local.BackCommand.Execute(null);

        static async Task Wait(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(40);
            while (!condition() && DateTime.UtcNow < until) await Task.Delay(30);
            if (!condition()) throw new InvalidOperationException("Source conversion UI timed out");
        }
    }

    sealed class SourceConversionUiRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default)
        {
            if (!Path.GetFileName(request.FileName).Equals("blender.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("UI fixture may only invoke Blender");
            var temp = Path.Combine(Output, "BlenderTemp"); Directory.CreateDirectory(temp);
            return new CyEngineHub.Infrastructure.Processes.SystemProcessRunner().RunAsync(request with
                { EnvironmentVariables = new Dictionary<string, string?> { ["TEMP"] = temp, ["TMP"] = temp } }, output, cancellationToken);
        }
    }
}
