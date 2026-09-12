using System.Security.Cryptography;
using System.Text.Json;
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
    public static async Task<int> RunAsync(string root, UnrealEngineInstallation sourceEngine, UnrealEngineInstallation targetEngine, IsolatedRunner runner)
    {
        Console.WriteLine("NATIVE_QA_ROOT " + root);
        var creator = new ProjectCreationService();
        var passes = new List<string>();
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); passes.Add(message); Console.WriteLine("PASS " + message); }
        try
        {
            Check(sourceEngine.Version.Major == 5 && sourceEngine.Version.Minor == 6 && targetEngine.Version.Major == 5 && targetEngine.Version.Minor == 5, "UE 5.6 -> 5.5");
            var source = await Project("NativeSource", sourceEngine);
            var target = await Project("NativeTarget", targetEngine);
            var image = Path.Combine(root, "T_Native.tga");
            var bytes = new byte[18 + 8 * 8 * 4]; bytes[2] = 2; bytes[12] = 8; bytes[14] = 8; bytes[16] = 32; bytes[17] = 8;
            for (var i = 18; i < bytes.Length; i++) bytes[i] = (byte)((i * 37) % 256);
            await File.WriteAllBytesAsync(image, bytes);
            var grayImage = Path.Combine(root, "T_Gray.tga");
            var grayBytes = new byte[18 + 8 * 8]; grayBytes[2] = 3; grayBytes[12] = 8; grayBytes[14] = 8; grayBytes[16] = 8;
            for (var i = 18; i < grayBytes.Length; i++) grayBytes[i] = (byte)(i * 3);
            await File.WriteAllBytesAsync(grayImage, grayBytes);
            var fixture = await Run(source, sourceEngine, Path.Combine(AppContext.BaseDirectory, "scripts", "native-fixture.py"),
                new Dictionary<string, string?> { ["CYHUB_NATIVE_IMAGE"] = image, ["CYHUB_NATIVE_GRAY"] = grayImage }, "fixture");
            Check(fixture.Succeeded, "Real RGBA fixture created with non-default texture properties");
            var originalHash = Fingerprint(Path.Combine(source.ProjectRoot, "Content"));
            var store = new SqliteCatalogStore(Path.Combine(root, "native.db")); await store.InitializeAsync();
            await ((IEngineCatalog)store).UpsertAsync(sourceEngine); await ((IEngineCatalog)store).UpsertAsync(targetEngine);
            var context = new FabInstallationScriptContext(new RemoteFabAsset("qa-native", "ue", "QA native texture", "", "Fab", "ASSET_PACK", null, null, [], []),
                new RemoteFabProjectVersion("qa-native", [], ["5.6"], ["Windows"]), LibraryPackageContentKind.ContentPack, source.ProjectRoot,
                new("qa", "QA", FabInstallationTargetKind.Project, target.ProjectRoot, "5.5",
                    new UnrealProject("qa", "QA", target.ProjectMarker, target.ProjectRoot, "5.5", null, null, [], [], ProjectHealth.Healthy, [], DateTimeOffset.UtcNow), targetEngine),
                new Dictionary<string, string> { ["conversionMode"] = "native", ["keepStaging"] = "true", ["validateOutput"] = "false" });
            var work = Path.Combine(root, "NativeWork");
            var result = await new UnrealVersionBridgeInstallationScript(store, store, runner, new FabProjectContentImportService(), work).InstallAsync(context);
            Check(File.Exists(result.Outputs["manifest"]), "Native variant published");
            Check(originalHash == Fingerprint(Path.Combine(source.ProjectRoot, "Content")), "All original source bytes unchanged");
            var exchange = Directory.GetDirectories(work, "Exchange", SearchOption.AllDirectories).Single();
            using var nativeManifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(exchange, "native-textures.json")));
            var entries = nativeManifest.RootElement.GetProperty("textures");
            Check(entries.GetArrayLength() == 3 && entries.EnumerateArray().Any(e => e.GetProperty("format").GetString() == "G8"), "RGBA, normal-map and G8 textures transferred natively");
            Check(Directory.GetFiles(exchange).All(f => Path.GetExtension(f) is ".json" or ".bin"), "No image/FBX exporter or importer in native exchange");
            Check(Directory.GetFiles(work, "native-reopen-report.json", SearchOption.AllDirectories).Length >= 1, "Fresh 5.5 verification is mandatory even with legacy validation disabled");
            var config = Path.Combine(root, "verify-installed.json");
            var report = Path.Combine(root, "verify-installed-report.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(new { exchangeRoot = exchange, reportPath = report, operation = "verify" }));
            var verified = await Run(target, targetEngine, Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "scripts", "native-textures.py"),
                new Dictionary<string, string?> { ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"] = config }, "verify-installed");
            Check(verified.Succeeded, "Installed asset reopened in 5.5: raw pixels and supported properties match source");
            var payload = Directory.GetFiles(exchange, "*.bin").Order(StringComparer.Ordinal).First();
            var corrupted = await File.ReadAllBytesAsync(payload); corrupted[0] ^= 0xff; await File.WriteAllBytesAsync(payload, corrupted);
            var negativeConfig = Path.Combine(root, "verify-tampered.json");
            await File.WriteAllTextAsync(negativeConfig, JsonSerializer.Serialize(new { exchangeRoot = exchange, reportPath = Path.Combine(root, "verify-tampered-report.json"), operation = "verify" }));
            var rejected = await Run(target, targetEngine, Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "scripts", "native-textures.py"),
                new Dictionary<string, string?> { ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"] = negativeConfig }, "reject-tampered-pixels");
            Check(!rejected.Succeeded, "Pixel mismatch rejected rather than silently validated");
            corrupted[0] ^= 0xff; await File.WriteAllBytesAsync(payload, corrupted);
            await File.WriteAllTextAsync(Path.Combine(root, "native-summary.json"), JsonSerializer.Serialize(new { passes, runner.PeakBytes, diskBytes = runner.Measure() }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception error) { await File.WriteAllTextAsync(Path.Combine(root, "native-error.txt"), error.ToString()); Console.WriteLine(error); return 1; }

        async Task<ProjectCreationResult> Project(string name, UnrealEngineInstallation engine)
        {
            var project = await creator.CreateAsync(new(creator.Presets.Single(p => p.Id == "unreal-blueprint"), name, root, engine.AssociationId,
                ["PythonScriptPlugin", "EditorScriptingUtilities", "CyHubNativeBridge"], creator.ConfigurationProfiles.Single(p => p.Id == "unreal-ue5-lite")));
            var plugin = Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "native-binaries", $"UE_{engine.Version.Major}.{engine.Version.Minor}");
            foreach (var file in Directory.GetFiles(plugin, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(project.ProjectRoot, "Plugins", "CyHubNativeBridge", Path.GetRelativePath(plugin, file));
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
}
