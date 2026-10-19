using System.Text.Json;
using System.Text.Json.Nodes;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Settings;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Epic.Tests;

public sealed class UnrealVersionBridgeAgentTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cyhub-agent-").FullName;
    private readonly AgentRunner _runner = new();
    private FabInstallationScriptContext Context(bool execute = true, bool plugin = false)
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "Source")).FullName;
        var content = Directory.CreateDirectory(Path.Combine(source, "Content")).FullName;
        File.WriteAllText(Path.Combine(content, "Example.uasset"), "original asset");
        if (plugin)
        {
            Directory.CreateDirectory(Path.Combine(source, "Source", "Example"));
            File.WriteAllText(Path.Combine(source, "Example.uplugin"), "{\"FileVersion\":3,\"FriendlyName\":\"Example\",\"Version\":1,\"Modules\":[{\"Name\":\"Example\",\"Type\":\"Runtime\"}]}");
            File.WriteAllText(Path.Combine(source, "Source", "Example", "Example.cpp"), "// licensed fixture source");
        }
        var target = Directory.CreateDirectory(Path.Combine(_root, "UserProject")).FullName;
        var projectFile = Path.Combine(target, "QA.uproject"); File.WriteAllText(projectFile, "{\"FileVersion\":3}");
        var project = new UnrealProject("qa", "QA", projectFile, target, "5.5", null, null, [], [], ProjectHealth.Healthy, [], DateTimeOffset.UtcNow);
        return new(new RemoteFabAsset("pack", "ue", "Pack", "", "Fab", "ASSET_PACK", null, null, [], []),
            new RemoteFabProjectVersion("artifact", [], ["5.7"], ["Windows"]),
            plugin ? LibraryPackageContentKind.CodePlugin : LibraryPackageContentKind.ContentPack, source,
            new("project", "QA", FabInstallationTargetKind.Project, target, "5.5", project, Engine(5)),
            new Dictionary<string, string> { ["enableAgentFallback"] = "true", ["allowAgentCodeExecution"] = execute.ToString() });
    }
    private UnrealEngineInstallation Engine(int minor)
    {
        var root = Path.Combine(_root, "Engine" + minor);
        var editor = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor-Cmd.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(editor)!); File.WriteAllText(editor, "fixture, never executed");
        var automation = Path.Combine(root, "Engine", "Binaries", "DotNET", "AutomationTool", "AutomationTool.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(automation)!); File.WriteAllText(automation, "fixture");
        return new("engine" + minor, "5." + minor, "Fixture", root, editor, new(5, minor, 0), EngineInstallationType.EpicBinary,
            null, null, null, "QA", EngineHealth.Healthy, [], DateTimeOffset.UtcNow);
    }
    private UnrealVersionBridgeInstallationScript Script() => new(new Engines([Engine(7), Engine(5)]), new Settings(), _runner,
        new FabProjectContentImportService(), Path.Combine(_root, "Variants"));
    private static JsonElement Args(object value) => JsonSerializer.SerializeToElement(value);
    private async Task<string> PrepareAsync(FabInstallationScriptContext context)
    {
        var result = await Script().InstallAsync(context);
        Assert.Equal("true", result.Outputs["agentPending"]);
        Assert.False(result.Outputs.ContainsKey("variant"));
        Assert.False(result.Outputs.ContainsKey("receipt"));
        Assert.True(File.Exists(result.Outputs["mcpConfig"]));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Plugins")));
        return result.Outputs["agentWorkspace"];
    }
    private static FabInstallationScriptContext Resume(FabInstallationScriptContext context, string workspace) => context with {
        Configuration = new Dictionary<string, string>(context.Configuration) { ["agentWorkspace"] = workspace } };

    [Fact]
    public async Task DisabledFallbackPreservesExistingFailClosedBehavior()
    {
        var context = Context() with { Configuration = new Dictionary<string, string>() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Script().InstallAsync(context));
        Assert.Empty(Directory.GetFiles(Path.Combine(_root, "Variants"), "agent-task.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task NativeBlockCreatesMcpTaskWithoutConvertingInstallingOrEditingOriginals()
    {
        var context = Context(); var workspace = await PrepareAsync(context);
        Assert.Equal("original asset", File.ReadAllText(Path.Combine(context.SourceDirectory, "Content", "Example.uasset")));
        var service = new UnrealVersionBridgeAgentWorkspace(_runner);
        var description = JsonSerializer.Serialize(await service.ExecuteAsync(workspace, "describe", Args(new { }), _root));
        Assert.Contains("/Game/Example", description, StringComparison.Ordinal);
        Assert.Contains("awaiting_agent_repair", description, StringComparison.Ordinal);
        var task = JsonNode.Parse(File.ReadAllText(Path.Combine(workspace, "agent-task.json")))!;
        Assert.True(task["allowCodeExecution"]!.GetValue<bool>());
    }

    [Fact]
    public async Task GeneratedDirectoryNamesInsideContentAreStillRealAssetPackages()
    {
        var context = Context();
        var directory = Directory.CreateDirectory(Path.Combine(context.SourceDirectory, "Content", "Saved")).FullName;
        File.WriteAllText(Path.Combine(directory, "Keep.UASSET"), "original asset in a valid content folder");
        var workspace = await PrepareAsync(context);
        var task = JsonNode.Parse(File.ReadAllText(Path.Combine(workspace, "agent-task.json")))!;
        Assert.Contains(task["expectedPackages"]!.AsArray(), item => item!.GetValue<string>() == "/Game/Saved/Keep");
        Assert.True(File.Exists(Path.Combine(workspace, "SourceProject", "Content", "Saved", "Keep.UASSET")));
    }

    [Theory]
    [InlineData("../outside.py")]
    [InlineData("SourceProject/Content/Example.uasset")]
    [InlineData("agent-task.json")]
    [InlineData("TargetProject/CyVersionBridgeTarget.uproject")]
    [InlineData("Scripts/not-authorized.py")]
    public async Task McpRejectsWritesOutsideExplicitRepairSurface(string path)
    {
        var context = Context(); var workspace = await PrepareAsync(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnrealVersionBridgeAgentWorkspace(_runner)
            .ExecuteAsync(workspace, "write_file", Args(new { path, text = "bad" }), _root));
    }

    [Fact]
    public async Task ReadOnlyConsentCannotWriteRunCompileOrValidate()
    {
        var context = Context(execute: false); var workspace = await PrepareAsync(context);
        var service = new UnrealVersionBridgeAgentWorkspace(_runner);
        await service.ExecuteAsync(workspace, "describe", Args(new { }), _root);
        foreach (var operation in new[] { "write_file", "run_source", "run_target", "build_plugin", "validate" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(workspace, operation,
                Args(new { path = "Scripts/target.py", text = "pass" }), _root));
    }

    [Fact]
    public async Task RepairScriptsRunOnlyAgainstExactCopiedProjectAndSelectedEngine()
    {
        var context = Context(); var workspace = await PrepareAsync(context);
        var service = new UnrealVersionBridgeAgentWorkspace(_runner);
        await service.ExecuteAsync(workspace, "write_file", Args(new { path = "Scripts/target.py", text = "# repair fixture" }), _root);
        await service.ExecuteAsync(workspace, "run_target", Args(new { }), _root);
        var process = _runner.Requests.Last();
        Assert.Contains("Engine5", process.FileName, StringComparison.Ordinal);
        Assert.StartsWith(workspace, process.Arguments[0], StringComparison.Ordinal);
        Assert.DoesNotContain(context.Target.RootPath, process.Arguments[0], StringComparison.Ordinal);
        Assert.Equal(Path.Combine(workspace, "Exchange"), process.EnvironmentVariables["CYHUB_BRIDGE_EXCHANGE"]);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("report-error")]
    [InlineData("editor-error")]
    [InlineData("reopen-error")]
    [InlineData("compiler-log-error")]
    public async Task ResumeNeverPublishesOrInstallsFailedRepair(string failure)
    {
        var context = Context(); var workspace = await PrepareAsync(context);
        // A previous successful-looking report is not sufficient.
        File.WriteAllText(Path.Combine(workspace, "agent-validation.json"), "{\"validated\":true}");
        _runner.Failure = failure;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Script().InstallAsync(Resume(context, workspace)));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
        Assert.False(Directory.Exists(Path.Combine(_root, "Variants", "pack", "artifact")));
    }

    [Fact]
    public async Task ResumeRevalidatesReopensPublishesAndInstallsRepairedContent()
    {
        var context = Context(); var workspace = await PrepareAsync(context);
        File.WriteAllText(Path.Combine(workspace, "TargetProject", "Content", "Example.uasset"), "target serialized fixture");
        var result = await Script().InstallAsync(Resume(context, workspace));
        Assert.Equal(2, _runner.Requests.Count(r => r.Arguments.Any(a => a.Contains("agent-validate.py", StringComparison.Ordinal))));
        Assert.Equal("target serialized fixture", File.ReadAllText(Path.Combine(context.Target.RootPath, "Content", "Example.uasset")));
        Assert.True(File.Exists(result.Outputs["manifest"])); Assert.True(File.Exists(result.Outputs["receipt"]));
        Assert.Equal("original asset", File.ReadAllText(Path.Combine(context.SourceDirectory, "Content", "Example.uasset")));
    }

    [Fact]
    public async Task ResumeChecksPackIdentityAndOriginalInventoryNotMutableAgentClaims()
    {
        var context = Context(); var workspace = await PrepareAsync(context);
        var metadata = Path.Combine(workspace, "agent-task.json");
        var task = JsonNode.Parse(File.ReadAllText(metadata))!;
        task["expectedPackages"] = new JsonArray();
        File.WriteAllText(metadata, task.ToJsonString());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Script().InstallAsync(Resume(context, workspace)));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
    }

    [Fact]
    public async Task PluginTaskPreservesMountPointAllowsSourceEditsAndCompilesForTarget()
    {
        var context = Context(plugin: true); var workspace = await PrepareAsync(context);
        var service = new UnrealVersionBridgeAgentWorkspace(_runner);
        Assert.True(File.Exists(Path.Combine(workspace, "SourceProject", "Plugins", "Example", "Content", "Example.uasset")));
        await service.ExecuteAsync(workspace, "write_file", Args(new { path = "TargetProject/Plugins/Example/Source/Example/Example.cpp", text = "// adapted target API" }), _root);
        await service.ExecuteAsync(workspace, "build_plugin", Args(new { }), _root);
        Assert.Contains(_runner.Requests, r => r.Arguments.Contains("BuildPlugin") && r.Arguments[0].Contains("Engine5", StringComparison.Ordinal));
        Assert.Equal("// licensed fixture source", File.ReadAllText(Path.Combine(context.SourceDirectory, "Source", "Example", "Example.cpp")));
        Assert.Equal("5.5.0", JsonNode.Parse(File.ReadAllText(Path.Combine(workspace, "TargetProject", "Plugins", "Example", "Example.uplugin")))!["EngineVersion"]!.GetValue<string>());
    }

    [Fact]
    public async Task BinaryOnlyPluginCannotPretendToRecompile()
    {
        var context = Context(plugin: true); Directory.Delete(Path.Combine(context.SourceDirectory, "Source"), true);
        var workspace = await PrepareAsync(context);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new UnrealVersionBridgeAgentWorkspace(_runner)
            .ExecuteAsync(workspace, "build_plugin", Args(new { }), _root));
        Assert.Contains("sources", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PluginResumeInstallsItsOwnRootWithoutPollutingProjectContent()
    {
        var context = Context(plugin: true); var workspace = await PrepareAsync(context);
        var result = await Script().InstallAsync(Resume(context, workspace));
        Assert.True(File.Exists(Path.Combine(context.Target.RootPath, "Plugins", "Example", "Example.uplugin")));
        Assert.True(File.Exists(Path.Combine(context.Target.RootPath, "Plugins", "Example", "Content", "Example.uasset")));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
        Assert.True(File.Exists(result.Outputs["manifest"]));
        Assert.Equal("original asset", File.ReadAllText(Path.Combine(context.SourceDirectory, "Content", "Example.uasset")));
    }

    [Fact]
    public async Task PluginCompilerFailureNeverPublishesOrInstalls()
    {
        var context = Context(plugin: true); var workspace = await PrepareAsync(context);
        _runner.Failure = "build-error";
        await Assert.ThrowsAsync<InvalidOperationException>(() => Script().InstallAsync(Resume(context, workspace)));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Plugins")));
    }

    [Fact]
    public async Task RemovingModulesCannotTurnCodePluginIntoFakeSuccessfulContentOnlyConversion()
    {
        var context = Context(plugin: true); var workspace = await PrepareAsync(context);
        File.WriteAllText(Path.Combine(workspace, "TargetProject", "Plugins", "Example", "Example.uplugin"), "{\"FileVersion\":3}");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Script().InstallAsync(Resume(context, workspace)));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Plugins")));
    }

    [Fact]
    public async Task AnalyzeOnlyCannotResumePublishOrInstallAgentOutput()
    {
        var context = Context(); var workspace = await PrepareAsync(context);
        var resumed = Resume(context, workspace);
        resumed = resumed with { Configuration = new Dictionary<string, string>(resumed.Configuration) { ["analyzeOnly"] = "true" } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Script().InstallAsync(resumed));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
    }

    [Fact]
    public async Task PendingAgentStopsInstallationPipelineBeforeNextStep()
    {
        var context = Context(); var later = new NeverInstall();
        var pipeline = new FabInstallationPipeline(new FabInstallationScriptRegistry([Script(), later], _runner));
        var results = await pipeline.ExecuteAsync(context, [new(UnrealVersionBridgeInstallationScript.ScriptId, context.Configuration), new("fixture.install", new Dictionary<string, string>())]);
        Assert.Single(results); Assert.Equal("true", results[0].Outputs["agentPending"]);
        Assert.False(later.Called);
    }

    private sealed class NeverInstall : IFabInstallationScript
    {
        public bool Called { get; private set; }
        public FabInstallationScriptDescriptor Descriptor { get; } = new("fixture.install", "Fixture", "", "QA", [LibraryPackageContentKind.ContentPack], [FabInstallationTargetKind.Project], [], true);
        public Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context, IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
        { Called = true; throw new InvalidOperationException("Must not install"); }
    }
    private sealed class Engines(IReadOnlyList<UnrealEngineInstallation> engines) : IEngineCatalog
    {
        public Task<IReadOnlyList<UnrealEngineInstallation>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(engines);
        public Task UpsertAsync(UnrealEngineInstallation engine, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Settings : ISettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class AgentRunner : IProcessRunner
    {
        public List<ProcessRequest> Requests { get; } = [];
        public string Failure { get; set; } = "";
        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Requests.Add(request);
            if (request.Arguments.Contains("BuildPlugin"))
            {
                if (Failure == "build-error") return new(1, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
                var package = request.Arguments.Single(a => a.StartsWith("-Package=", StringComparison.Ordinal))[9..];
                var binaries = Directory.CreateDirectory(Path.Combine(package, "Binaries", "Win64")).FullName;
                await File.WriteAllTextAsync(Path.Combine(binaries, "UnrealEditor.modules"), "{\"BuildId\":\"fixture\"}", cancellationToken);
                await File.WriteAllTextAsync(Path.Combine(binaries, "UnrealEditor-Example.dll"), "fixture", cancellationToken);
            }
            else if (request.Arguments.Any(a => a.Contains("source-inspect.py", StringComparison.Ordinal)))
            {
                var config = JsonNode.Parse(await File.ReadAllTextAsync(request.EnvironmentVariables["CYENGINEHUB_VERSION_BRIDGE_CONFIG"]!, cancellationToken))!;
                await File.WriteAllTextAsync(config["reportPath"]!.GetValue<string>(), "{\"assets\":[{\"path\":\"/Game/Example.Example\",\"assetType\":\"UnknownType\",\"dependencies\":[],\"features\":[],\"inspectionErrors\":[]}],\"errors\":[]}", cancellationToken);
            }
            else if (request.Arguments.Any(a => a.Contains("agent-validate.py", StringComparison.Ordinal)))
            {
                var log = request.Arguments.Single(a => a.StartsWith("-abslog=", StringComparison.Ordinal))[8..];
                await File.WriteAllTextAsync(log, Failure == "compiler-log-error" ? "LogBlueprint: Error: Broken graph even with stale generated class" : "QA target editor completed", cancellationToken);
                var config = JsonNode.Parse(await File.ReadAllTextAsync(request.EnvironmentVariables["CYENGINEHUB_VERSION_BRIDGE_CONFIG"]!, cancellationToken))!;
                var loaded = Failure == "missing" ? new JsonArray() : config["expectedPackages"]!.DeepClone();
                var errors = Failure == "report-error" ? new JsonArray("broken dependency") : new JsonArray();
                await File.WriteAllTextAsync(config["reportPath"]!.GetValue<string>(), new JsonObject { ["loaded"] = loaded, ["errors"] = errors }.ToJsonString(), cancellationToken);
                if (Failure == "editor-error" || (Failure == "reopen-error" && !config["save"]!.GetValue<bool>()))
                    return new(1, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
            }
            return new(1, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
        }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
