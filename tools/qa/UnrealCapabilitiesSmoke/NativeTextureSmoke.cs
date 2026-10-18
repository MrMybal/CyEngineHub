using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Projects.Creation;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

internal static class NativeTextureSmoke
{
    public static async Task<int> RunAsync(string root, UnrealEngineInstallation sourceEngine, UnrealEngineInstallation targetEngine, IsolatedRunner runner, bool graph = false,
        string? ownedProject = null, string? ownedInventory = null)
    {
        Console.WriteLine("NATIVE_QA_ROOT " + root);
        var creator = new ProjectCreationService();
        var passes = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); passes.Add(message); Console.WriteLine("PASS " + message); }
        try
        {
            Check(sourceEngine.Version.Major == 5 && targetEngine.Version.Major == 5 &&
                (sourceEngine.Version.Minor, targetEngine.Version.Minor) is (6, 5) or (7, 5) or (7, 3), $"Native UE {sourceEngine.Version} -> {targetEngine.Version}");
            var source = await Project("NativeSource", sourceEngine);
            var target = await Project("NativeTarget", targetEngine);
            var expectedTextures = 3;
            var ownedHash = ownedProject is null ? null : Fingerprint(Path.Combine(ownedProject, "Content"));
            if (ownedProject is not null)
            {
                using var inventory = JsonDocument.Parse(await File.ReadAllTextAsync(ownedInventory!));
                var textures = inventory.RootElement.GetProperty("assets").EnumerateArray()
                    .Where(e => e.GetProperty("assetType").GetString() == "Texture2D").Select(e => e.GetProperty("path").GetString()!).ToArray();
                Check(textures.Length > 0, "Owned texture subset explicitly selected from inspected inventory (not the whole pack)");
                expectedTextures = textures.Length;
                foreach (var path in textures)
                {
                    if (!path.StartsWith("/Game/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid owned inventory path");
                    var relative = path.Split('.')[0][6..].Replace('/', Path.DirectorySeparatorChar) + ".uasset";
                    var destination = Path.Combine(source.ProjectRoot, "Content", relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(Path.Combine(ownedProject, "Content", relative), destination);
                }
            }
            else
            {
                var image = Path.Combine(root, "T_Native.tga");
                var bytes = new byte[18 + 8 * 8 * 4]; bytes[2] = 2; bytes[12] = 8; bytes[14] = 8; bytes[16] = 32; bytes[17] = 8;
                for (var i = 18; i < bytes.Length; i++) bytes[i] = (byte)((i * 37) % 256);
                await File.WriteAllBytesAsync(image, bytes);
                var grayImage = Path.Combine(root, "T_Gray.tga");
                var grayBytes = new byte[18 + 8 * 8]; grayBytes[2] = 3; grayBytes[12] = 8; grayBytes[14] = 8; grayBytes[16] = 8;
                for (var i = 18; i < grayBytes.Length; i++) grayBytes[i] = (byte)(i * 3);
                await File.WriteAllBytesAsync(grayImage, grayBytes);
                var fixture = await Run(source, sourceEngine, Path.Combine(AppContext.BaseDirectory, "scripts", "native-fixture.py"),
                    new Dictionary<string, string?> { ["CYHUB_NATIVE_IMAGE"] = image, ["CYHUB_NATIVE_GRAY"] = grayImage, ["CYHUB_NATIVE_GRAPH"] = graph ? "true" : "false" }, "fixture");
                Check(fixture.Succeeded, "Real RGBA fixture created with non-default texture properties");
            }
            var originalHash = Fingerprint(Path.Combine(source.ProjectRoot, "Content"));
            var store = new SqliteCatalogStore(Path.Combine(root, "native.db")); await store.InitializeAsync();
            await ((IEngineCatalog)store).UpsertAsync(sourceEngine); await ((IEngineCatalog)store).UpsertAsync(targetEngine);
            var context = new FabInstallationScriptContext(new RemoteFabAsset("qa-native", "ue", "QA native texture", "", "Fab", "ASSET_PACK", null, null, [], []),
                new RemoteFabProjectVersion("qa-native", [], [$"{sourceEngine.Version.Major}.{sourceEngine.Version.Minor}"], ["Windows"]), LibraryPackageContentKind.ContentPack, source.ProjectRoot,
                new("qa", "QA", FabInstallationTargetKind.Project, target.ProjectRoot, targetEngine.Version.ToString(),
                    new UnrealProject("qa", "QA", target.ProjectMarker, target.ProjectRoot, targetEngine.AssociationId, null, null, [], [], ProjectHealth.Healthy, [], DateTimeOffset.UtcNow), targetEngine),
                new Dictionary<string, string> { ["conversionMode"] = "native", ["keepStaging"] = "true", ["validateOutput"] = "false" });
            var work = Path.Combine(root, "NativeWork");
            var result = await new UnrealVersionBridgeInstallationScript(store, store, runner, new FabProjectContentImportService(), work, NativeRoot()).InstallAsync(context);
            Check(File.Exists(result.Outputs["manifest"]), "Native variant published");
            Check(originalHash == Fingerprint(Path.Combine(source.ProjectRoot, "Content")), "All original source bytes unchanged");
            var exchange = Directory.GetDirectories(work, "Exchange", SearchOption.AllDirectories).Single();
            using var nativeManifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(exchange, "native-textures.json")));
            var entries = nativeManifest.RootElement.GetProperty("textures");
            Check(entries.GetArrayLength() == expectedTextures && (ownedProject is not null || entries.EnumerateArray().Any(e => e.GetProperty("format").GetString() == "G8")),
                ownedProject is null ? "RGBA, normal-map and G8 textures transferred natively" : $"All {expectedTextures} owned Texture2D assets transferred natively");
            if (ownedProject is not null) Check(ownedHash == Fingerprint(Path.Combine(ownedProject, "Content")), "Entire prepared owned source pack unchanged");
            Check(Directory.GetFiles(exchange).All(f => Path.GetExtension(f) is ".json" or ".bin"), "No image/FBX exporter or importer in native exchange");
            if (graph) Check(File.Exists(Path.Combine(exchange, "native-materials.json")), "Native material graph and instance transferred alongside texture dependencies");
            Check(Directory.GetFiles(work, "native-reopen-report.json", SearchOption.AllDirectories).Length >= 1, "Fresh target verification is mandatory even with legacy validation disabled");
            var config = Path.Combine(root, "verify-installed.json");
            var report = Path.Combine(root, "verify-installed-report.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { exchangeRoot = exchange, reportPath = report, operation = "verify" }));
            var verified = await Run(target, targetEngine, Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "scripts", "native-textures.py"),
                new Dictionary<string, string?> { ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"] = config }, "verify-installed");
            Check(verified.Succeeded, $"Installed asset reopened in {targetEngine.Version}: raw pixels and supported properties match source");
            if (graph)
            {
                var links = await Run(target, targetEngine, Path.Combine(AppContext.BaseDirectory, "scripts", "native-graph-verify.py"), [], "verify-native-graph-links");
                Check(links.Succeeded, "Independent target check: material texture reference, graph input and instance parent/overrides preserved");
            }
            var payload = Directory.GetFiles(exchange, "*.bin").Order(StringComparer.Ordinal).First();
            var corrupted = await File.ReadAllBytesAsync(payload); corrupted[0] ^= 0xff; await File.WriteAllBytesAsync(payload, corrupted);
            var negativeConfig = Path.Combine(root, "verify-tampered.json");
            await File.WriteAllTextAsync(negativeConfig, JsonSerializer.Serialize(new { exchangeRoot = exchange, reportPath = Path.Combine(root, "verify-tampered-report.json"), operation = "verify" }));
            var rejected = await Run(target, targetEngine, Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "scripts", "native-textures.py"),
                new Dictionary<string, string?> { ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"] = negativeConfig }, "reject-tampered-pixels");
            Check(!rejected.Succeeded, "Pixel mismatch rejected rather than silently validated");
            corrupted[0] ^= 0xff; await File.WriteAllBytesAsync(payload, corrupted);
            if (graph)
            {
                var materialManifest = Path.Combine(exchange, "native-materials.json");
                var original = await File.ReadAllTextAsync(materialManifest);
                var changed = JsonNode.Parse(original)!;
                var materialEntry = changed["assets"]!.AsArray().Single(e => e!["class"]!.GetValue<string>() == "/Script/Engine.Material")!;
                materialEntry["properties"]!["TwoSided"]!["value"] = "False";
                await File.WriteAllTextAsync(materialManifest, changed.ToJsonString());
                var wrongGraph = await Run(target, targetEngine, Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "scripts", "native-textures.py"),
                    new Dictionary<string, string?> { ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"] = negativeConfig }, "reject-tampered-material");
                Check(!wrongGraph.Succeeded, "Material setting mismatch rejected after a fresh target reload");
                await File.WriteAllTextAsync(materialManifest, original);
            }
            if (targetEngine.Version.Minor == 3 && ownedProject is null)
            {
                var textureManifest = Path.Combine(exchange, "native-textures.json");
                var original = await File.ReadAllTextAsync(textureManifest);
                var changed = JsonNode.Parse(original)!;
                changed["textures"]![0]!["properties"]!["bPadWithBorderColor"] = "True";
                await File.WriteAllTextAsync(textureManifest, changed.ToJsonString());
                var rejectedTarget = await Project("UnsupportedSettingTarget", targetEngine);
                var rejectedReport = Path.Combine(root, "rejected-nondefault-setting.json");
                var rejectedConfig = Path.Combine(root, "rejected-nondefault-setting-config.json");
                await File.WriteAllTextAsync(rejectedConfig, JsonSerializer.Serialize(new { exchangeRoot = exchange, reportPath = rejectedReport, operation = "import" }));
                var nondefault = await Run(rejectedTarget, targetEngine, Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "scripts", "native-textures.py"),
                    new Dictionary<string, string?> { ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"] = rejectedConfig }, "reject-unsupported-nondefault-setting");
                Check(!nondefault.Succeeded && (await File.ReadAllTextAsync(rejectedReport)).Contains("Property cannot be adapted: bPadWithBorderColor", StringComparison.Ordinal),
                    "5.3 import rejects a non-default setting absent from the target, rather than dropping it");
                Check(!Directory.Exists(Path.Combine(rejectedTarget.ProjectRoot, "Content")) || !Directory.GetFiles(Path.Combine(rejectedTarget.ProjectRoot, "Content"), "*.uasset", SearchOption.AllDirectories).Any(),
                    "Rejected native setting publishes no partial asset");
                await File.WriteAllTextAsync(textureManifest, original);
            }
            await File.WriteAllTextAsync(Path.Combine(root, "native-summary.json"), JsonSerializer.Serialize(new { passes, runner.PeakBytes, diskBytes = runner.Measure() }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(root, "native-error.txt"), error.ToString()); Console.WriteLine(error); return 1; }

        async Task<ProjectCreationResult> Project(string name, UnrealEngineInstallation engine)
        {
            var project = await creator.CreateAsync(new(creator.Presets.Single(p => p.Id == "unreal-blueprint"), name, root, engine.AssociationId,
                ["PythonScriptPlugin", "EditorScriptingUtilities", "CyHubNativeBridge"], creator.ConfigurationProfiles.Single(p => p.Id == "unreal-ue5-lite")));
            var plugin = Path.Combine(NativeRoot(), $"UE_{engine.Version.Major}.{engine.Version.Minor}");
            foreach (var relative in new[] { "CyHubNativeBridge.uplugin", "Binaries/Win64/UnrealEditor-CyHubNativeBridge.dll", "Binaries/Win64/UnrealEditor.modules" })
            {
                var file = Path.Combine(plugin, relative);
                var destination = Path.Combine(project.ProjectRoot, "Plugins", "CyHubNativeBridge", relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
            }
            return project;
        }
        Task<ProcessResult> Run(ProjectCreationResult project, UnrealEngineInstallation engine, string script, Dictionary<string, string?> env, string label) =>
            runner.RunAsync(new ProcessRequest { FileName = engine.EditorPath, WorkingDirectory = project.ProjectRoot,
                Arguments = [project.ProjectMarker, "-run=pythonscript", "-script=" + script, "-Unattended", "-NullRHI", "-NoP4", "-abslog=" + Path.Combine(root, label + ".log")], EnvironmentVariables = env });
        static string Fingerprint(string path) => string.Join("|", Directory.GetFiles(path, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(f => Path.GetRelativePath(path, f) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f)))));
    }
    private static string NativeRoot() => Environment.GetEnvironmentVariable("CYHUB_NATIVE_ADAPTER_ROOT") ??
        Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "native-binaries");
}
