using System.Security.Cryptography;
using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Conversion;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Infrastructure.Processes;
using CyEngineHub.Unreal.Models;

if (args.Length is not (1 or 2)) throw new ArgumentException("Usage: SourceConversionSmoke <blender.exe> [UnrealRoot]");
var blender = Path.GetFullPath(args[0]);
var root = Path.GetFullPath("artifacts/source-conversion-qa-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Environment.ProcessId);
Directory.CreateDirectory(root);
var runner = new FixtureRunner(root, blender);
var source = Path.Combine(root, "Source");
var fixture = await runner.RunAsync(new() { FileName = blender, WorkingDirectory = root,
    Arguments = ["--background", "--factory-startup", "--disable-autoexec", "--python-exit-code", "17", "--python", Path.Combine(AppContext.BaseDirectory, "fixture.py"), "--", source] });
Check(fixture.Succeeded, "Real Blender creates a textured/animated/morph fixture with packed image and an inert autoexec guard");
var original = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(source, "Fixture.blend"))));
var converter = new BlenderSourceConverter(runner, Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.source-converters", "scripts", "blender-convert.py"));
var options = new Dictionary<string, string> { ["blenderExecutable"] = blender, ["outputFormat"] = "GLB", ["animations"] = "true", ["allowLossy"] = "false" };
var converted = await converter.ConvertAsync(new(source, Path.Combine(root, "Converted"), options), new PrintProgress());
using (var report = JsonDocument.Parse(await File.ReadAllTextAsync(converted.ReportPath)))
{
    Check(report.RootElement.GetProperty("success").GetBoolean() && report.RootElement.GetProperty("validation")[0].GetProperty("reopened").GetBoolean(), "Real GLB export reopens successfully in Blender");
    Check(report.RootElement.GetProperty("validation")[0].GetProperty("vertices").GetInt32() > 0 && report.RootElement.GetProperty("validation")[0].GetProperty("images").GetInt32() > 0, "Geometry and packed texture survive conversion and reopen");
    Check(report.RootElement.GetProperty("validation")[0].GetProperty("materials").GetInt32() > 0 && report.RootElement.GetProperty("validation")[0].GetProperty("actions").GetInt32() > 0, "PBR material and animation survive GLB conversion");
}
Check(!File.Exists(Path.Combine(source, "AUTOEXEC_RAN")), "Asset-provided autoexec Python never ran");
Check(original == Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(Path.Combine(source, "Fixture.blend")))), "Original Blender file stays byte-identical");
var fbx = await converter.ConvertAsync(new(source, Path.Combine(root, "Fbx"), new Dictionary<string, string>(options) { ["outputFormat"] = "FBX", ["allowLossy"] = "true" }), new PrintProgress());
Check(Directory.GetFiles(fbx.Directory, "*.fbx", SearchOption.AllDirectories).Length == 1, "Real FBX conversion is available with explicit material-loss consent");
var usd = await converter.ConvertAsync(new(Path.Combine(root, "UsdSource"), Path.Combine(root, "UsdConverted"), options), new PrintProgress());
Check(Directory.GetFiles(usd.Directory, "*.glb", SearchOption.AllDirectories).Length == 1, "Real USD source converts to a reopened GLB without using a market-specific assumption");
try
{
    await converter.ConvertAsync(new(source, Path.Combine(root, "StrictFbx"), new Dictionary<string, string>(options) { ["outputFormat"] = "FBX" }));
    throw new Exception("Lossy FBX must require consent");
}
catch (InvalidOperationException) { Check(!Directory.EnumerateFileSystemEntries(Path.Combine(root, "StrictFbx")).Any(), "Strict FBX refuses unaccepted material loss and cleans its workspace"); }
const string id = "cyenginehub.marketplace-blendkit";
var package = await DerivedLibraryPackageService.CreateAsync(new(new("fixture", "local", "Fixture", "", id, "ASSET_PACK", null, null, [], []),
    new("source-fixture", [], [], []), converted.Directory, Path.Combine(root, "Packages"), "GLB", "Real conversion fixture", LibraryPackageContentKind.RawAsset, LibraryPackageStorageMode.ZipFastest));
using (var json = JsonDocument.Parse(await File.ReadAllTextAsync(package.ManifestPath!)))
    Check(json.RootElement.GetProperty("derivation").GetProperty("sourceArtifactId").GetString() == "source-fixture", "Converted local package retains its source-variant lineage");
if (args.Length == 2)
{
    var engineRoot = Path.GetFullPath(args[1]);
    using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(engineRoot, "Engine", "Build", "Build.version")));
    var version = new EngineVersion(json.RootElement.GetProperty("MajorVersion").GetInt32(), json.RootElement.GetProperty("MinorVersion").GetInt32(), json.RootElement.GetProperty("PatchVersion").GetInt32());
    var editor = Path.Combine(engineRoot, "Engine", "Binaries", "Win64", "UnrealEditor-Cmd.exe");
    runner.Editor = editor;
    var engine = new UnrealEngineInstallation("qa", "qa", "QA", engineRoot, editor, version, EngineInstallationType.EpicBinary, null, null, null, "QA", EngineHealth.Healthy, [], DateTimeOffset.UtcNow);
    var native = await new UnrealRawSourceConverter(runner).ConvertAsync(new(converted.Directory, Path.Combine(root, "UnrealConverted"),
        new Dictionary<string, string> { ["allowLossy"] = "true" }, engine), new PrintProgress());
    Check(Directory.GetFiles(native.Directory, "*.uasset", SearchOption.AllDirectories).Length > 0, "Real target Unreal imports converted GLB into a serialized UAsset variant");
    var verifyProject = Path.Combine(root, "VerifyUnreal");
    await DerivedLibraryPackageService.MaterializeAsync(native.Directory, verifyProject, LibraryPackageContentKind.ContentPack);
    var marker = Path.Combine(verifyProject, "Verify.uproject");
    await File.WriteAllTextAsync(marker, "{\"FileVersion\":3,\"Plugins\":[{\"Name\":\"PythonScriptPlugin\",\"Enabled\":true},{\"Name\":\"EditorScriptingUtilities\",\"Enabled\":true}]}");
    var verification = Path.Combine(root, "fresh-unreal-reopen.json");
    var reopened = await runner.RunAsync(new() { FileName = editor, WorkingDirectory = verifyProject,
        Arguments = [marker, "-run=pythonscript", "-script=" + Path.Combine(AppContext.BaseDirectory, "unreal-verify.py"), "-Unattended", "-NullRHI", "-NoP4", "-abslog=" + Path.Combine(root, "fresh-unreal-reopen.log")],
        EnvironmentVariables = new Dictionary<string, string?> { ["CYHUB_VERIFY_REPORT"] = verification } });
    Check(reopened.Succeeded && File.Exists(verification), "Fresh Unreal process reloads meshes, animation and PBR material references to the imported texture");
    Console.WriteLine("UNREAL_VARIANT " + native.Directory);
}
Console.WriteLine("OUTPUT " + root);
return 0;

static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
sealed class PrintProgress : IProgress<FabInstallationScriptProgress> { public void Report(FabInstallationScriptProgress value) => Console.WriteLine(value.Message); }
sealed class FixtureRunner(string root, string blender) : IProcessRunner
{
    public string? Editor { get; set; }
    public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default)
    {
        if (request.FileName != blender && request.FileName != Editor) throw new InvalidOperationException("Only explicitly selected executables may run");
        if (request.WorkingDirectory is not { } work || !Path.GetFullPath(work).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Work must remain in the fixture");
        var temp = Path.Combine(root, "Temp"); Directory.CreateDirectory(temp);
        var environment = new Dictionary<string, string?>(request.EnvironmentVariables) { ["TEMP"] = temp, ["TMP"] = temp,
            ["UE-SharedDataCachePath"] = "None", ["UE-LocalDataCachePath"] = Path.Combine(root, "DDC") };
        var arguments = request.Arguments;
        if (request.FileName == Editor) arguments = arguments.Concat(new[] { "-NoSplash", "-NoShaderCompile", "-UserDir=" + Path.Combine(root, "User") }).ToArray();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMinutes(4));
        return await new SystemProcessRunner().RunAsync(request with { Arguments = arguments, EnvironmentVariables = environment }, output, timeout.Token);
    }
}
