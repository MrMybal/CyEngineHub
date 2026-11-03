using System.Collections;
using System.Collections.Concurrent;
using System.Text.Json;
using Avalonia.Controls;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;

static partial class Program
{
    sealed class ParallelLocalScript(string suffix) : IFabInstallationScript
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<FabInstallationScriptContext> Seen { get; } = new();
        public FabInstallationScriptDescriptor Descriptor { get; } = new("qa.local-parallel-" + suffix, "QA · import local parallèle", "QA", "QA",
            [LibraryPackageContentKind.ContentPack], [FabInstallationTargetKind.Project], [], true);
        public async Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context,
            IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            Seen.Enqueue(context);
            await Release.Task.WaitAsync(cancellationToken);
            return await new UnrealContentPackInstallationScript(new FabProjectContentImportService()).InstallAsync(context, progress, cancellationToken);
        }
    }

    static async Task TestLocalInstallReuse(Window window, MainWindowViewModel vm)
    {
        async Task Wait(Func<bool> predicate)
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (!predicate()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("Local QA: " + vm.FabInstallActionError); await ImGuiDiagnostics.FramesAsync(window, 2); }
        }
        var originalProject = vm.Projects.Single().Project;
        var projects = Enumerable.Range(1, 2).Select(index =>
        {
            var root = Path.Combine(Output, "LocalProject" + index); Directory.CreateDirectory(root);
            var file = Path.Combine(root, "LocalProject" + index + ".uproject"); File.WriteAllText(file, "{\"FileVersion\":3}");
            return originalProject with { Id = "local-project-" + index, Name = "LocalProject" + index, RootPath = root, FilePath = file };
        }).ToArray();
        Call(vm, "ReplaceCatalog", vm.Engines.Select(engine => engine.Engine).ToArray(), projects);
        var version = new RemoteFabProjectVersion("local-exact", [], ["5.5"], ["Windows"]);
        var asset = new RemoteFabAsset("local-test", "fab", "Pack local QA", "QA", "Fab", "assetPack", null, null, [],
            [new("remote-other", [], ["5.7"], ["Windows"]), version]);
        var client = (FixtureFab)typeof(MainWindowViewModel).GetField("_directFabClient", Private)!.GetValue(vm)!;
        client.FailProductDetails = true; client.FailNextDownload = true;
        var registry = (IFabInstallationScriptRegistry)typeof(MainWindowViewModel).GetField("_fabInstallationScripts", Private)!.GetValue(vm)!;
        var records = (IList)typeof(MainWindowViewModel).GetField("_fabStoredDownloads", Private)!.GetValue(vm)!;
        var recordType = typeof(MainWindowViewModel).GetNestedType("FabStoredDownloadRecord", System.Reflection.BindingFlags.NonPublic)!;
        foreach (var compressed in new[] { false, true })
        {
            records.Clear();
            var source = Path.Combine(Output, compressed ? "LocalZip" : "LocalRaw");
            var content = Path.Combine(source, "Content", compressed ? "ZipAsset" : "RawAsset"); Directory.CreateDirectory(content);
            var payload = Path.Combine(content, "Fixture.uasset"); await File.WriteAllTextAsync(payload, "local original fixture");
            var local = await LibraryPackageService.PackageAsync(asset, version,
                new(asset.AssetId, asset.Title, source, 1, new FileInfo(payload).Length, DateTimeOffset.UtcNow),
                compressed ? LibraryPackageStorageMode.ZipFastest : LibraryPackageStorageMode.Uncompressed);
            var json = JsonSerializer.Serialize(new { AssetId = asset.AssetId, Version = "UE 5.5", ArtifactId = version.ArtifactId,
                Path = source, Bytes = local.StoredBytes, DownloadedAt = DateTimeOffset.UtcNow, Source = "Fab", PackageKind = "ContentPack",
                StorageMode = local.StorageMode, ManifestPath = local.ManifestPath, EngineVersions = new[] { "5.5" } });
            records.Add(JsonSerializer.Deserialize(json, recordType)!);
            var bytesBefore = Directory.GetFiles(source, "*", SearchOption.AllDirectories).ToDictionary(file => file, File.ReadAllBytes);
            var script = new ParallelLocalScript(compressed ? "zip" : "raw"); registry.Register(script);
            using var card = (RemoteFabAssetCardViewModel)typeof(MainWindowViewModel).GetMethod("CreateRemoteAssetCard", Private)!.Invoke(vm, [asset])!;
            card.OpenDetailsCommand.Execute(null); vm.OpenFabInstallOptionsCommand.Execute(null);
            Check(vm.FabInstallVersions.Single(option => option.IsSelected).Version.ArtifactId == version.ArtifactId,
                "Initial selection prefers the exact local platform variant, not a newer remote one");
            Check(vm.FabInstallVersions.Single(option => option.Version.ArtifactId == version.ArtifactId).LocalSourceNotice.Contains("aucun téléchargement", StringComparison.Ordinal),
                "Local-source availability is explicit in the variant choices");
            var targets = vm.FabInstallTargets.ToArray();
            foreach (var target in targets)
            {
                target.IsSelected = false;
                target.SelectedInstallationScript = target.AvailableInstallationScripts.Single(option => option.Id == script.Descriptor.Id);
            }
            targets[0].IsSelected = true;
            await ImGuiDiagnostics.FramesAsync(window, 3);
            await ImGuiDiagnostics.ClickAsync(window, "command/InstallSelectedFabVersionsCommand");
            await Wait(() => script.Seen.Count == 1);
            vm.OpenFabInstallOptionsCommand.Execute(null); targets[0].IsSelected = false; targets[1].IsSelected = true;
            Check(vm.InstallSelectedFabVersionsCommand.CanExecute(null) && !vm.BypassFabInstallChecks,
                "A second independent project can be installed without enabling the bypass option");
            await ImGuiDiagnostics.FramesAsync(window, 3);
            await ImGuiDiagnostics.ClickAsync(window, "command/InstallSelectedFabVersionsCommand");
            await Wait(() => script.Seen.Count == 2);
            vm.ShowActionsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 4);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, compressed ? "parallel-local-zip.png" : "parallel-local-raw.png"));
            Check(client.DownloadCalls == 0 && client.FailNextDownload, "Two real queued script invocations reuse the same local pack with zero network-client calls");
            Check(script.Seen.Select(context => context.SourceDirectory).Distinct().Count() == 2, "Each script has its own materialized source; originals are not shared mutable workspaces");
            script.Release.TrySetResult(); await Wait(() => !vm.IsFabTargetInstallationActive);
            Check(string.IsNullOrEmpty(vm.FabInstallActionError), "Both parallel local imports complete: " + vm.FabInstallActionError);
            foreach (var project in projects) Check(File.Exists(Path.Combine(project.RootPath, "Content", compressed ? "ZipAsset" : "RawAsset", "Fixture.uasset")), "Actual imported local asset exists in " + project.Name);
            Check(bytesBefore.All(entry => File.Exists(entry.Key) && File.ReadAllBytes(entry.Key).SequenceEqual(entry.Value)), "Original local package and manifest remain byte-identical");
            Check(vm.FabDownloadStatus.Contains("0 téléchargée(s)", StringComparison.Ordinal), "Completion reports local reuse instead of claiming a download");
        }
        using var remoteCard = (RemoteFabAssetCardViewModel)typeof(MainWindowViewModel).GetMethod("CreateRemoteAssetCard", Private)!.Invoke(vm, [asset])!;
        remoteCard.OpenDetailsCommand.Execute(null); vm.OpenFabInstallOptionsCommand.Execute(null);
        foreach (var option in vm.FabInstallVersions) option.IsSelected = option.Version.ArtifactId == "remote-other";
        foreach (var target in vm.FabInstallTargets) target.IsSelected = false;
        vm.FabInstallTargets[0].IsSelected = true;
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "command/InstallSelectedFabVersionsCommand");
        await Wait(() => !vm.IsFabTargetInstallationActive);
        Check(client.DownloadCalls == 1 && !client.FailNextDownload && vm.FabInstallActionError.Contains("Échec", StringComparison.Ordinal),
            "A different explicitly selected artifact is not silently replaced by the local version");
    }
}
