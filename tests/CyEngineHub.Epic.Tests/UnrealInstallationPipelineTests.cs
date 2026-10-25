using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Settings;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;
#pragma warning disable CA1861 // Small JSON fixture arrays kept beside their simulated report for readability.

namespace CyEngineHub.Epic.Tests;

public sealed class UnrealInstallationPipelineTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cyhub-install-pipeline-").FullName;

    [Fact]
    public async Task RelocationLoadsOriginalPathsBeforePublishingAndInstalling()
    {
        var context = Context(); var runner = new FixtureRunner();
        var script = new UnrealRelocatedContentInstallationScript(runner, new FabProjectContentImportService(), Path.Combine(_root, "Work"));
        var result = await script.InstallAsync(context);
        Assert.True(runner.SawOriginalPaths);
        Assert.Equal("fixture", await File.ReadAllTextAsync(Path.Combine(context.SourceDirectory, "Content", "Original", "Mesh.uasset")));
        Assert.True(File.Exists(Path.Combine(context.Target.RootPath, "Content", "QA", "Original", "Mesh.uasset")));
        Assert.False(File.Exists(Path.Combine(context.Target.RootPath, "Content", "Original", "Mesh.uasset")));
        Assert.True(File.Exists(result.Outputs["receipt"]));
    }

    [Theory]
    [InlineData("report")]
    [InlineData("log")]
    [InlineData("exit")]
    public async Task RelocationRejectsValidationFailuresBeforeWritingTarget(string failure)
    {
        var context = Context(); var runner = new FixtureRunner { Failure = failure };
        var script = new UnrealRelocatedContentInstallationScript(runner, new FabProjectContentImportService(), Path.Combine(_root, "Work"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => script.InstallAsync(context));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
        Assert.True(File.Exists(context.Target.Project!.FilePath));
    }

    [Fact]
    public async Task RelocationCancellationPreservesSourceAndTarget()
    {
        var context = Context(); var runner = new FixtureRunner();
        using var stop = new CancellationTokenSource(); stop.Cancel();
        var script = new UnrealRelocatedContentInstallationScript(runner, new FabProjectContentImportService(), Path.Combine(_root, "Work"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => script.InstallAsync(context, cancellationToken: stop.Token));
        Assert.Equal(0, runner.Calls);
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
    }

    [Theory]
    [InlineData("../Outside")]
    [InlineData("/Engine/Forbidden")]
    public async Task RelocationRejectsUnsafeDestination(string destination)
    {
        var context = Context() with { Configuration = new Dictionary<string, string> { ["destinationFolder"] = destination } };
        var runner = new FixtureRunner();
        var script = new UnrealRelocatedContentInstallationScript(runner, new FabProjectContentImportService(), Path.Combine(_root, "Work"));
        await Assert.ThrowsAnyAsync<Exception>(() => script.InstallAsync(context));
        Assert.Equal(0, runner.Calls);
    }

    [Theory]
    [InlineData("source-error")]
    [InlineData("target-error")]
    public async Task ConversionMustNotInstallWhenUnrealReportsErrors(string failure)
    {
        var context = Context(); var engines = new EngineStore([context.Target.Engine!, Engine(6)]);
        var runner = new FixtureRunner { Failure = failure };
        var script = new UnrealVersionBridgeInstallationScript(engines, new Settings(), runner, new FabProjectContentImportService(), Path.Combine(_root, "Converted"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => script.InstallAsync(context));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
    }

    [Fact]
    public async Task ConversionPublishesAndInstallsOnlyAfterBothEnginesSucceed()
    {
        var context = Context(); var runner = new FixtureRunner();
        var script = new UnrealVersionBridgeInstallationScript(new EngineStore([context.Target.Engine!, Engine(6)]), new Settings(), runner,
            new FabProjectContentImportService(), Path.Combine(_root, "Converted"));
        var result = await script.InstallAsync(context);
        Assert.Equal(3, runner.Calls);
        Assert.All(runner.Requests, request =>
        {
            Assert.Contains("-AllowCommandletAudio", request.Arguments);
            Assert.DoesNotContain("-NoSound", request.Arguments);
        });
        Assert.True(File.Exists(result.Outputs["manifest"]));
        Assert.True(File.Exists(result.Outputs["receipt"]));
        Assert.True(File.Exists(Path.Combine(context.Target.RootPath, "Content", "Converted.uasset")));
    }

    [Theory]
    [InlineData("native", true)]
    [InlineData("portable", false)]
    public async Task ConversionBlocksMissingAdapterOrLossConsentBeforeExport(string mode, bool allowLosses)
    {
        var context = Context() with { Configuration = new Dictionary<string, string>
        { ["conversionMode"] = mode, ["allowLossyConversion"] = allowLosses.ToString() } };
        var runner = new FixtureRunner();
        var script = new UnrealVersionBridgeInstallationScript(new EngineStore([context.Target.Engine!, Engine(6)]), new Settings(), runner,
            new FabProjectContentImportService(), Path.Combine(_root, "Converted"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => script.InstallAsync(context));
        Assert.Contains("asset-conversion-plan.json", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, runner.Calls);
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    public async Task DowngradeExposesAndReturnsExplicitLibraryDerivativeChoice(string register)
    {
        var original = Context();
        var config = new Dictionary<string, string>(original.Configuration) { ["registerDerivedVariant"] = register };
        var runner = new FixtureRunner();
        var script = new UnrealVersionBridgeInstallationScript(new EngineStore([original.Target.Engine!, Engine(6)]), new Settings(), runner,
            new FabProjectContentImportService(), Path.Combine(_root, "Converted"));
        Assert.Contains(script.Descriptor.Options, option => option.Id == "registerDerivedVariant" && option.Kind == FabInstallationOptionKind.Boolean);
        var result = await script.InstallAsync(original with { Configuration = config });
        Assert.Equal(register, result.Outputs["registerDerivedVariant"]);
        Assert.True(File.Exists(result.Outputs["manifest"]));
        Assert.True(File.Exists(Path.Combine(original.Target.RootPath, "Content", "Converted.uasset")));
        Assert.Equal("fixture", await File.ReadAllTextAsync(Path.Combine(original.SourceDirectory, "Content", "Original", "Mesh.uasset")));
    }

    [Fact]
    public async Task AnalyzeOnlyRetainsPlanWithoutExportPublicationOrInstallation()
    {
        var context = Context() with { Configuration = new Dictionary<string, string> { ["analyzeOnly"] = "true" } };
        var runner = new FixtureRunner();
        var script = new UnrealVersionBridgeInstallationScript(new EngineStore([context.Target.Engine!, Engine(6)]), new Settings(), runner,
            new FabProjectContentImportService(), Path.Combine(_root, "Converted"));
        var result = await script.InstallAsync(context);
        Assert.Equal(1, runner.Calls);
        Assert.True(File.Exists(result.Outputs["report"]));
        Assert.False(result.Outputs.ContainsKey("variant"));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
    }

    [Fact]
    public async Task MapOnlySourceIsInspectedAndUnsupportedExportNeverPublishesPartialPack()
    {
        var context = Context();
        File.Move(Path.Combine(context.SourceDirectory, "Content", "Original", "Mesh.uasset"),
            Path.Combine(context.SourceDirectory, "Content", "Original", "Map.umap"));
        var runner = new FixtureRunner { Failure = "unsupported" };
        var script = new UnrealVersionBridgeInstallationScript(new EngineStore([context.Target.Engine!, Engine(6)]), new Settings(), runner,
            new FabProjectContentImportService(), Path.Combine(_root, "Converted"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => script.InstallAsync(context));
        Assert.Equal(2, runner.Calls);
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
    }

    [Fact]
    public async Task StrictPackInstallRefusesConflictsAndPreservesExistingData()
    {
        var context = Context();
        var target = Path.Combine(context.Target.RootPath, "Content", "Original", "Mesh.uasset");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); await File.WriteAllTextAsync(target, "keep target");
        context = context with { Configuration = new Dictionary<string, string> { ["conflictPolicy"] = "fail" } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new UnrealContentPackInstallationScript(new FabProjectContentImportService()).InstallAsync(context));
        Assert.Equal("keep target", await File.ReadAllTextAsync(target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("verify-error")]
    [InlineData("verify-count")]
    [InlineData("missing-license")]
    [InlineData("missing-notice")]
    public async Task NativeTexturesMustReopenAndMatchBeforeInstallation(string failure)
    {
        var context = Context() with { Configuration = new Dictionary<string, string> { ["conversionMode"] = "native", ["validateOutput"] = "false", ["keepStaging"] = "true" } };
        var nativeRoot = Path.Combine(_root, "NativeAdapters");
        foreach (var version in new[] { "5.6", "5.5" })
        {
            var plugin = Path.Combine(nativeRoot, "UE_" + version);
            var binaries = Directory.CreateDirectory(Path.Combine(plugin, "Binaries", "Win64")).FullName;
            await File.WriteAllTextAsync(Path.Combine(plugin, "CyHubNativeBridge.uplugin"), "{}");
            await File.WriteAllTextAsync(Path.Combine(plugin, "LICENSE"), "fixture MIT license");
            await File.WriteAllTextAsync(Path.Combine(plugin, "NOTICE.md"), "fixture native adapter notice");
            await File.WriteAllTextAsync(Path.Combine(binaries, "UnrealEditor-CyHubNativeBridge.dll"), "not executed");
            await File.WriteAllTextAsync(Path.Combine(binaries, "UnrealEditor.modules"), "{}");
            await File.WriteAllTextAsync(Path.Combine(binaries, "private.pdb"), "must not be staged");
        }
        var runner = new FixtureRunner { Native = true, Failure = failure };
        var script = new UnrealVersionBridgeInstallationScript(new EngineStore([context.Target.Engine!, Engine(6)]), new Settings(), runner,
            new FabProjectContentImportService(), Path.Combine(_root, "Converted"), nativeRoot);
        if (failure.StartsWith("missing-", StringComparison.Ordinal))
        {
            File.Delete(Path.Combine(nativeRoot, "UE_5.6", failure == "missing-license" ? "LICENSE" : "NOTICE.md"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => script.InstallAsync(context));
            Assert.Equal(1, runner.Calls);
            Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => script.InstallAsync(context));
            return;
        }
        if (failure.Length > 0)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => script.InstallAsync(context));
            Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Content")));
        }
        else
        {
            var result = await script.InstallAsync(context);
            Assert.True(File.Exists(Path.Combine(result.Outputs["variant"], "native-reopen-report.json")));
            Assert.True(File.Exists(Path.Combine(context.Target.RootPath, "Content", "Native.uasset")));
            Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Plugins")));
        }
        Assert.Equal(4, runner.Calls);
        foreach (var request in runner.Requests.Where(r => r.Arguments.Any(a => a.Contains("native-textures.py", StringComparison.Ordinal))))
        {
            var conversionIni = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(request.Arguments[0])!, "Config", "DefaultEngine.ini"));
            Assert.Contains("r.Nanite.ProjectEnabled=True", conversionIni, StringComparison.Ordinal);
            Assert.Contains("D3D12TargetedShaderFormats=PCD3D_SM6", conversionIni, StringComparison.Ordinal);
            var pluginFiles = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(request.Arguments[0])!, "Plugins", "CyHubNativeBridge"), "*", SearchOption.AllDirectories);
            Assert.Equal(5, pluginFiles.Length);
            var pluginRoot = Path.Combine(Path.GetDirectoryName(request.Arguments[0])!, "Plugins", "CyHubNativeBridge");
            Assert.Equal("fixture MIT license", await File.ReadAllTextAsync(Path.Combine(pluginRoot, "LICENSE")));
            Assert.Equal("fixture native adapter notice", await File.ReadAllTextAsync(Path.Combine(pluginRoot, "NOTICE.md")));
            Assert.DoesNotContain(pluginFiles, f => f.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase));
        }
        Assert.False(File.Exists(Path.Combine(context.Target.RootPath, "Config", "DefaultEngine.ini")));
        Assert.DoesNotContain(runner.Requests, r => r.Arguments.Any(a => a.Contains("source-export.py", StringComparison.Ordinal) || a.Contains("target-import.py", StringComparison.Ordinal)));
    }

    private FabInstallationScriptContext Context()
    {
        var source = Path.Combine(_root, "Source"); Directory.CreateDirectory(Path.Combine(source, "Content", "Original"));
        File.WriteAllText(Path.Combine(source, "Content", "Original", "Mesh.uasset"), "fixture");
        var target = Path.Combine(_root, "Target"); Directory.CreateDirectory(target);
        var projectFile = Path.Combine(target, "Qa.uproject"); File.WriteAllText(projectFile, "{\"FileVersion\":3}");
        var project = new UnrealProject("qa", "Qa", projectFile, target, "5.5", null, null, [], [], ProjectHealth.Healthy, [], DateTimeOffset.UtcNow);
        return new(new RemoteFabAsset("qa", "ue", "QA", "", "Fab", "ASSET_PACK", null, null, [], []),
            new RemoteFabProjectVersion("qa-artifact", [], ["5.6"], ["Windows"]), LibraryPackageContentKind.ContentPack, source,
            new("qa", "Qa", FabInstallationTargetKind.Project, target, "5.5", project, Engine(5)),
            new Dictionary<string, string> { ["destinationFolder"] = "QA", ["conversionMode"] = "portable", ["allowLossyConversion"] = "true" });
    }
    private UnrealEngineInstallation Engine(int minor)
    {
        var root = Path.Combine(_root, "Engine" + minor); var editor = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor-Cmd.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(editor)!); File.WriteAllText(editor, "never executed");
        return new("engine" + minor, "5." + minor, "Fixture", root, editor, new(5, minor, 0), EngineInstallationType.EpicBinary, null, null, null, "QA", EngineHealth.Healthy, [], DateTimeOffset.UtcNow);
    }
    private sealed class EngineStore(IReadOnlyList<UnrealEngineInstallation> engines) : IEngineCatalog
    {
        public Task<IReadOnlyList<UnrealEngineInstallation>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult(engines);
        public Task UpsertAsync(UnrealEngineInstallation engine, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Settings : ISettingsStore
    {
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class FixtureRunner : IProcessRunner
    {
        public bool Native { get; init; }
        public string Failure { get; init; } = "";
        public int Calls { get; private set; }
        public List<ProcessRequest> Requests { get; } = [];
        public bool SawOriginalPaths { get; private set; }
        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); Calls++; Requests.Add(request);
            var project = Path.GetDirectoryName(request.Arguments[0])!;
            var relocation = request.EnvironmentVariables.TryGetValue("CYENGINEHUB_RELOCATED_IMPORT_CONFIG", out var configPath);
            configPath ??= request.EnvironmentVariables["CYENGINEHUB_VERSION_BRIDGE_CONFIG"]!;
            using var config = JsonDocument.Parse(await File.ReadAllTextAsync(configPath!, cancellationToken));
            var report = config.RootElement.GetProperty("reportPath").GetString()!;
            var log = request.Arguments.Single(a => a.StartsWith("-abslog=", StringComparison.Ordinal))[8..];
            await File.WriteAllTextAsync(log, Failure == "log" ? "LogStreaming: Warning: Can't find file '/Game/Missing'" : "QA OK", cancellationToken);
            if (relocation)
            {
                SawOriginalPaths = File.Exists(Path.Combine(project, "Content", "Original", "Mesh.uasset"));
                var dest = Path.Combine(project, "Content", "QA", "Original"); Directory.CreateDirectory(dest);
                File.Copy(Path.Combine(project, "Content", "Original", "Mesh.uasset"), Path.Combine(dest, "Mesh.uasset"));
                await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { originalAssetCount = 1, movedAssetCount = 1, errors = Failure == "report" ? new[] { "Broken dependency" } : [] }), cancellationToken);
            }
            else if (request.Arguments.Any(a => a.Contains("source-inspect.py", StringComparison.Ordinal)))
            {
                await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { assets = new[] {
                    new { path = "/Game/Texture.Texture", assetType = "Texture2D", dependencies = Array.Empty<string>(), features = Native ? new[] { "native-texture-candidate" } : [], inspectionErrors = Array.Empty<string>() }
                }, errors = Array.Empty<string>() }), cancellationToken);
            }
            else if (request.Arguments.Any(a => a.Contains("native-textures.py", StringComparison.Ordinal)))
            {
                var operation = config.RootElement.GetProperty("operation").GetString();
                if (operation == "export")
                {
                    var exchange = config.RootElement.GetProperty("exchangeRoot").GetString()!;
                    await File.WriteAllTextAsync(Path.Combine(exchange, "native-textures.json"), "{}", cancellationToken);
                    await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { exported = new[] { "texture" }, unsupported = Array.Empty<string>(), errors = Array.Empty<string>() }), cancellationToken);
                }
                else
                {
                    if (operation == "import")
                    {
                        Directory.CreateDirectory(Path.Combine(project, "Content"));
                        await File.WriteAllTextAsync(Path.Combine(project, "Content", "Native.uasset"), "native fixture", cancellationToken);
                    }
                    await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new {
                        imported = operation == "verify" && Failure == "verify-count" ? Array.Empty<string>() : new[] { "/Game/Native" },
                        errors = operation == "verify" && Failure == "verify-error" ? new[] { "Pixel mismatch" } : [] }), cancellationToken);
                }
            }
            else if (request.Arguments.Any(a => a.Contains("source-export.py", StringComparison.Ordinal)))
            {
                var exchange = config.RootElement.GetProperty("exchangeRoot").GetString()!;
                await File.WriteAllTextAsync(Path.Combine(exchange, "Texture.tga"), "fixture", cancellationToken);
                await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { exported = new[] { "texture" }, unsupported = Failure == "unsupported" ? new[] { "World" } : [], errors = Failure == "source-error" ? new[] { "Export failed" } : [] }), cancellationToken);
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(project, "Content"));
                await File.WriteAllTextAsync(Path.Combine(project, "Content", "Converted.uasset"), "fixture", cancellationToken);
                await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { imported = new[] { "/Game/Converted" }, errors = Failure == "target-error" ? new[] { "Import dependency failed" } : [] }), cancellationToken);
            }
            return new(42, Failure == "exit" ? 1 : 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
