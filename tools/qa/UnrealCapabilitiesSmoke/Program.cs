using System.Text.Json;
using System.Security.Cryptography;
using CyEngineHub.Engines;
using CyEngineHub.Extensions;
using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Infrastructure.Processes;
using CyEngineHub.Plugins.Compatibility;
using CyEngineHub.Plugins.Discovery;
using CyEngineHub.Plugins.Installation;
using CyEngineHub.Plugins.Parsing;
using CyEngineHub.Projects.Creation;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

// Explicitly opt in. Only fresh QA projects are used; engines are never downloaded or installed.
if (args.Length != 3 || args[0] is not ("--live" or "--complex" or "--native-textures")) { Console.WriteLine("Usage: --live|--complex|--native-textures <source-engine-root> <target-engine-root>"); return 2; }
var root = Path.GetFullPath(Path.Combine("artifacts", "unreal-capabilities-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(root);
var passes = new List<string>();
void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); passes.Add(message); Console.WriteLine("PASS " + message); }
var sourceEngine = Engine(args[1]);
var targetEngine = Engine(args[2]);
var runner = new IsolatedRunner(root, [sourceEngine.EditorPath, targetEngine.EditorPath]);
if (args[0] == "--complex") return await ComplexDowngrade.RunAsync(root, sourceEngine, targetEngine, runner);
if (args[0] == "--native-textures") return await NativeTextureSmoke.RunAsync(root, sourceEngine, targetEngine, runner);
var creator = new ProjectCreationService();
var importer = new FabProjectContentImportService();
var asset = new RemoteFabAsset("qa-owned", "ue", "QA generated asset", "", "Fab", "ASSET_PACK", null, null, [], []);
var version = new RemoteFabProjectVersion("qa-source", [], [$"{sourceEngine.Version.Major}.{sourceEngine.Version.Minor}"], ["Windows"]);
Console.WriteLine("QA_ROOT " + root);
try
{
    var source = await Create("QaSource", sourceEngine);
    var normal = await Create("QaNormal", sourceEngine);
    var relocated = await Create("QaRelocated", sourceEngine);
    var converted = await Create("QaConverted", targetEngine);
    var pluginTarget = await Create("QaPluginTarget", sourceEngine);
    var cpp = await creator.CreateAsync(new(creator.Presets.Single(p => p.Id == "unreal-cpp"), "QaCpp", root, sourceEngine.AssociationId, []));
    Check(File.Exists(Path.Combine(cpp.ProjectRoot, "Source", "QaCpp", "QaCpp.Build.cs")), "C++ project skeleton generated (not compiled)");
    var tga = Path.Combine(root, "T_QA.tga");
    var bytes = new byte[18 + 4 * 4 * 3]; bytes[2] = 2; bytes[12] = 4; bytes[14] = 4; bytes[16] = 24;
    Array.Fill<byte>(bytes, 120, 18, bytes.Length - 18); await File.WriteAllBytesAsync(tga, bytes);
    using (var wave = new BinaryWriter(File.Create(Path.Combine(root, "S_QA.wav"))))
    {
        wave.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); wave.Write(36 + 1600);
        wave.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); wave.Write(16); wave.Write((short)1); wave.Write((short)1);
        wave.Write(8000); wave.Write(16000); wave.Write((short)2); wave.Write((short)16);
        wave.Write(System.Text.Encoding.ASCII.GetBytes("data")); wave.Write(1600); wave.Write(new byte[1600]);
    }
    await Verify(source, sourceEngine, "create", tga);
    var derived = await DerivedLibraryPackageService.CreateAsync(new(asset, version, source.ProjectRoot,
        Path.Combine(root, "Derived"), "Qa derived content", "fixture", LibraryPackageContentKind.ContentPack, LibraryPackageStorageMode.Uncompressed));
    Check(!File.Exists(Path.Combine(derived.DestinationDirectory, "QaSource.uproject")), "Project converted to standalone content pack");
    Check(File.Exists(Path.Combine(source.ProjectRoot, "Content", "OriginalPack", "M_Parent.uasset")), "Source project preserved");
    await new UnrealContentPackInstallationScript(importer).InstallAsync(Context(derived.DestinationDirectory, normal, sourceEngine));
    await Verify(normal, sourceEngine, "normal", tga);
    await new UnrealRelocatedContentInstallationScript(runner, importer, Path.Combine(root, "RelocationWork"))
        .InstallAsync(Context(derived.DestinationDirectory, relocated, sourceEngine) with
        { Configuration = new Dictionary<string, string> { ["destinationFolder"] = "QA", ["keepStaging"] = "true" } });
    await Verify(relocated, sourceEngine, "relocated", tga);
    var pluginSource = Path.Combine(root, "PluginSource", "QaPlugin"); Directory.CreateDirectory(Path.Combine(pluginSource, "Content"));
    await File.WriteAllTextAsync(Path.Combine(pluginSource, "QaPlugin.uplugin"), "{\"FileVersion\":3,\"Version\":1,\"FriendlyName\":\"QA Plugin\",\"CanContainContent\":true,\"EnabledByDefault\":true}");
    File.Copy(Path.Combine(source.ProjectRoot, "Content", "OriginalPack", "M_Parent.uasset"), Path.Combine(pluginSource, "Content", "M_Parent.uasset"));
    var parser = new UpluginDescriptorParser();
    await new UnrealProjectPluginInstallationScript(new PluginDiscoveryService(parser), new PluginInstallationService(new PluginCompatibilityService(), parser))
        .InstallAsync(Context(Path.GetDirectoryName(pluginSource)!, pluginTarget, sourceEngine) with { ContentKind = LibraryPackageContentKind.CodePlugin });
    await Verify(pluginTarget, sourceEngine, "plugin", tga);
    var textureOnly = Path.Combine(root, "TextureSource", "Content", "Textures"); Directory.CreateDirectory(textureOnly);
    File.Copy(Path.Combine(source.ProjectRoot, "Content", "Textures", "T_QA.uasset"), Path.Combine(textureOnly, "T_QA.uasset"));
    foreach (var entry in new[] { ("Models", "SM_QA.uasset"), ("Audio", "S_QA.uasset") })
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "TextureSource", "Content", entry.Item1)).FullName;
        File.Copy(Path.Combine(source.ProjectRoot, "Content", entry.Item1, entry.Item2), Path.Combine(folder, entry.Item2));
    }
    var store = new SqliteCatalogStore(Path.Combine(root, "isolated.db")); await store.InitializeAsync();
    await ((IEngineCatalog)store).UpsertAsync(sourceEngine); await ((IEngineCatalog)store).UpsertAsync(targetEngine);
    await new UnrealVersionBridgeInstallationScript(store, store, runner, importer, Path.Combine(root, "ConversionWork"))
        .InstallAsync(Context(Path.Combine(root, "TextureSource"), converted, targetEngine) with
        { Configuration = new Dictionary<string, string> { ["keepStaging"] = "true", ["conversionMode"] = "portable", ["allowLossyConversion"] = "true" } });
    await Verify(converted, targetEngine, "converted", tga);
    var exchange = Directory.EnumerateDirectories(Path.Combine(root, "ConversionWork"), "Exchange", SearchOption.AllDirectories).Single();
    var bridgeRoot = Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.asset-bridge");
    var bridgeManifest = await new HubExtensionManifestParser().ParseAsync(bridgeRoot);
    var snapshot = new HubExtensionSnapshot(bridgeManifest, bridgeRoot, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null);
    var bridge = new HubExtensionExecutionService(new SystemProcessRunner());
    var input = exchange;
    var previous = EngineFamily.Unreal;
    var roundTrip = await Create("QaRoundTrip", targetEngine);
    foreach (var family in new[] { EngineFamily.Unity, EngineFamily.Godot, EngineFamily.CryEngine, EngineFamily.Unreal })
    {
        var target = family == EngineFamily.Unreal ? roundTrip.ProjectRoot : Directory.CreateDirectory(Path.Combine(root, "Bridge" + family)).FullName;
        if (family == EngineFamily.Unity)
        { Directory.CreateDirectory(Path.Combine(target, "Assets")); Directory.CreateDirectory(Path.Combine(target, "ProjectSettings")); await File.WriteAllTextAsync(Path.Combine(target, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.0.0f1"); }
        if (family == EngineFamily.Godot) await File.WriteAllTextAsync(Path.Combine(target, "project.godot"), "config_version=5\n[application]\nconfig/name=\"QA\"");
        if (family == EngineFamily.CryEngine)
        { Directory.CreateDirectory(Path.Combine(target, "Assets")); await File.WriteAllTextAsync(Path.Combine(target, "Qa.cryproject"), "{\"content\":{\"assets\":[\"Assets\"]}}"); }
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var plan = await bridge.PrepareAsync(snapshot, input, target, previous, family, timeout.Token);
        if (!plan.OutputPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Bridge QA target escaped fixture root");
        var result = await bridge.ExecuteAsync(plan, cancellationToken: timeout.Token);
        Check(result.ProcessResult.Succeeded, "External Asset Bridge executed: " + previous + " -> " + family);
        foreach (var file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories))
            Check(SHA256.HashData(await File.ReadAllBytesAsync(file)).SequenceEqual(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(plan.OutputPath, Path.GetRelativePath(input, file))))), "Bridge checksum: " + family + " / " + Path.GetFileName(file));
        input = plan.OutputPath; previous = family;
    }
    await new UnrealVersionBridgeInstallationScript(store, store, runner, importer, Path.Combine(root, "RoundTripConversion"))
        .InstallAsync(Context(input, roundTrip, targetEngine) with { Asset = asset with { AssetId = "qa-roundtrip" }, Configuration = new Dictionary<string, string> { ["keepStaging"] = "true", ["conversionMode"] = "portable", ["allowLossyConversion"] = "true" } });
    await Verify(roundTrip, targetEngine, "roundtrip", tga);
    Check(runner.PeakBytes < 8L * 1024 * 1024 * 1024, "QA storage below 8 GiB safety threshold (requested maximum 30 GB)");
    await File.WriteAllTextAsync(Path.Combine(root, "summary.json"), JsonSerializer.Serialize(new { passes, runner.PeakBytes, diskBytes = runner.Measure(), sourceEngine = sourceEngine.Version, targetEngine = targetEngine.Version }));
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine(ex);
    await File.WriteAllTextAsync(Path.Combine(root, "summary.json"), JsonSerializer.Serialize(new { passes, error = ex.ToString(), runner.PeakBytes, diskBytes = runner.Measure() }));
    return 1;
}

async Task<ProjectCreationResult> Create(string name, UnrealEngineInstallation engine)
{
    var project = await creator.CreateAsync(new(creator.Presets.Single(p => p.Id == "unreal-blueprint"), name, root,
        engine.AssociationId, ["PythonScriptPlugin", "EditorScriptingUtilities"], creator.ConfigurationProfiles.Single(p => p.Id == "unreal-ue5-lite")));
    Check(File.Exists(project.ProjectMarker), "Blueprint project created: " + name); return project;
}
async Task Verify(ProjectCreationResult project, UnrealEngineInstallation engine, string mode, string tga)
{
    var report = Path.Combine(root, mode + "-report.json");
    var result = await runner.RunAsync(new ProcessRequest { FileName = engine.EditorPath, WorkingDirectory = project.ProjectRoot,
        Arguments = [project.ProjectMarker, "-run=pythonscript", "-script=" + Path.Combine(AppContext.BaseDirectory, "scripts", "fixture.py"), "-Unattended", "-NullRHI", "-AllowCommandletAudio", "-NoP4", "-abslog=" + Path.Combine(root, mode + "-editor.log")],
        EnvironmentVariables = new Dictionary<string, string?> { ["CYHUB_QA_MODE"] = mode, ["CYHUB_QA_REPORT"] = report, ["CYHUB_QA_TGA"] = tga } });
    Check(result.Succeeded && File.Exists(report), "Unreal commandlet completed: " + mode);
    using var json = JsonDocument.Parse(await File.ReadAllTextAsync(report));
    Check(json.RootElement.GetProperty("errors").GetArrayLength() == 0, "Real Unreal asset validation: " + mode);
}
FabInstallationScriptContext Context(string source, ProjectCreationResult project, UnrealEngineInstallation engine) => new(asset, version,
    LibraryPackageContentKind.ContentPack, source, new("qa-project", project.ProjectRoot, FabInstallationTargetKind.Project, project.ProjectRoot,
        engine.Version.ToString(), new UnrealProject("qa", Path.GetFileName(project.ProjectRoot), project.ProjectMarker, project.ProjectRoot,
            engine.AssociationId, null, null, [], [], ProjectHealth.Healthy, [], DateTimeOffset.UtcNow), engine), new Dictionary<string, string>());
static UnrealEngineInstallation Engine(string root)
{
    root = Path.GetFullPath(root); using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Engine", "Build", "Build.version")));
    var version = new EngineVersion(json.RootElement.GetProperty("MajorVersion").GetInt32(), json.RootElement.GetProperty("MinorVersion").GetInt32(), json.RootElement.GetProperty("PatchVersion").GetInt32());
    var editor = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor-Cmd.exe");
    if (!File.Exists(editor)) throw new FileNotFoundException(editor);
    return new(root, $"{version.Major}.{version.Minor}", "QA engine", root, editor, version, EngineInstallationType.EpicBinary, null, null, null, "QA explicit", EngineHealth.Healthy, [], DateTimeOffset.UtcNow);
}

sealed class IsolatedRunner(string root, string[] allowedEditors) : IProcessRunner
{
    public long PeakBytes { get; private set; }
    public long Measure() => Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }).Sum(p => { try { return new FileInfo(p).Length; } catch (IOException) { return 0L; } });
    public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default)
    {
        if (!allowedEditors.Contains(request.FileName, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("QA may only launch explicit installed editors");
        var project = Path.GetFullPath(request.Arguments[0]);
        if (!project.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || Path.GetExtension(project) != ".uproject") throw new InvalidOperationException("QA refused non-fixture project");
        var config = Path.Combine(Path.GetDirectoryName(project)!, "Config"); Directory.CreateDirectory(config);
        var cache = Path.Combine(root, "DDC").Replace('\\', '/');
        await File.AppendAllTextAsync(Path.Combine(config, "DefaultEngine.ini"), $"\n[QaIsolated]\nRoot=(Type=FileSystem,ReadOnly=false,Clean=false,Flush=false,DeleteUnused=false,Path=\"{cache}\")\n", cancellationToken);
        var temp = Path.Combine(root, "Temp"); Directory.CreateDirectory(temp);
        var env = new Dictionary<string, string?>(request.EnvironmentVariables) { ["TEMP"] = temp, ["TMP"] = temp, ["UE-SharedDataCachePath"] = "None", ["UE-LocalDataCachePath"] = cache };
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); stop.CancelAfter(TimeSpan.FromMinutes(4));
        var task = new SystemProcessRunner().RunAsync(request with { Arguments = request.Arguments.Concat(["-ddc=QaIsolated", "-NoSplash", "-NoShaderCompile"]).ToArray(), EnvironmentVariables = env }, output, stop.Token);
        while (!task.IsCompleted)
        {
            await Task.WhenAny(task, Task.Delay(2000, CancellationToken.None)); PeakBytes = Math.Max(PeakBytes, Measure());
            if (PeakBytes > 8L * 1024 * 1024 * 1024) { stop.Cancel(); try { await task; } catch (OperationCanceledException) { } throw new IOException("QA disk budget exceeded"); }
        }
        var result = await task;
        if (!result.Succeeded) Console.WriteLine(string.Join('\n', result.CapturedOutput.TakeLast(20).Select(l => l.Text)));
        return result;
    }
}
