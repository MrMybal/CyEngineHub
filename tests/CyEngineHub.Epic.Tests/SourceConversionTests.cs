using System.Text.Json;
using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Conversion;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Epic.Tests;

#pragma warning disable CA1861 // Literal manifest arrays keep the external SDK fixture self-contained.

public sealed class SourceConversionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cyhub-converter-test-").FullName;
    private FabInstallationScriptContext Context => new(new("asset", "local", "Pack", "", "cyenginehub.marketplace-blendkit", "ASSET_PACK", null, null, [], []),
        new("source", [], [], []), LibraryPackageContentKind.Unknown, _root, new("target", "Target", FabInstallationTargetKind.Project, _root, "5.5", null, null), new Dictionary<string, string>());

    [Theory]
    [InlineData(".blend", "convert.blender-interchange")]
    [InlineData(".usd", "convert.blender-interchange")]
    [InlineData(".fbx", "unreal.raw-assets")]
    [InlineData(".png", "unreal.raw-assets")]
    [InlineData(".wav", "unreal.raw-assets")]
    [InlineData(".uasset", "unreal.content-pack")]
    public void PlansByRealFileFormatRatherThanMarketplace(string extension, string script)
    {
        var plan = AssetSourceConversionPlanner.Suggest(new HashSet<string>([extension], StringComparer.OrdinalIgnoreCase));
        Assert.Equal(script, plan[0]);
    }

    [Theory]
    [InlineData(".max")]
    [InlineData(".pdf")]
    [InlineData(".exe")]
    public void DoesNotInventConvertersForUnsupportedOrNonAssetFormats(string extension) =>
        Assert.Empty(AssetSourceConversionPlanner.Suggest(new HashSet<string>([extension, ".png"], StringComparer.OrdinalIgnoreCase).Where(value => extension == ".max" || value != ".png").ToHashSet(StringComparer.OrdinalIgnoreCase)));

    [Fact]
    public async Task ReadsActualEngineVersionOfPublishedNativeDerivatives()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, LibraryPackageService.ManifestFileName), "{\"fileExtensions\":{\".uasset\":6},\"engineVersions\":[\"5.5.4\"]}");
        Assert.Equal("5.5.4", Assert.Single(AssetSourceConversionPlanner.ReadPackageEngineVersions(_root)));
        Assert.Contains(".uasset", AssetSourceConversionPlanner.ReadPackageExtensions(_root));
    }

    [Fact]
    public async Task PreparationContractProjectsTheConvertedKindIntoTheNextStep()
    {
        var output = Directory.CreateDirectory(Path.Combine(_root, "converted")).FullName;
        var preparation = new Preparation(output); var install = new Install();
        var pipeline = new FabInstallationPipeline(new FabInstallationScriptRegistry([preparation, install], new Runner(_ => throw new InvalidOperationException())));
        FabInstallationStep[] steps = [new("convert", new Dictionary<string, string>()), new("install", new Dictionary<string, string>())];
        await pipeline.ExecuteAsync(Context, steps);
        Assert.True(preparation.Ran); Assert.Equal(output, install.Seen!.SourceDirectory); Assert.Equal(LibraryPackageContentKind.RawAsset, install.Seen.ContentKind);
        Assert.Equal("false", install.Seen.Configuration["prepareOnly"]);
        Assert.Throws<InvalidOperationException>(() => pipeline.Validate(Context, [steps[0]]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlenderArgumentsDisableAutoexecAndRejectEscapedOutput(bool escaped)
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source with spaces")).FullName;
        var executable = Path.Combine(_root, "blender.exe"); await File.WriteAllTextAsync(executable, "fixture");
        var script = Path.Combine(_root, "convert.py"); await File.WriteAllTextAsync(script, "fixture");
        var runner = new Runner(request =>
        {
            Assert.Null(request.RawArguments); Assert.Contains("--factory-startup", request.Arguments); Assert.Contains("--disable-autoexec", request.Arguments);
            using var config = JsonDocument.Parse(File.ReadAllText(request.Arguments[^1]));
            var destination = config.RootElement.GetProperty("destination").GetString()!;
            File.WriteAllText(Path.Combine(destination, "mesh.glb"), "fixture");
            File.WriteAllText(config.RootElement.GetProperty("report").GetString()!, JsonSerializer.Serialize(new { success = true, outputs = new[] { escaped ? "../outside.glb" : "mesh.glb" } }));
            return Success();
        });
        var converter = new BlenderSourceConverter(runner, script);
        var request = new AssetSourceConversionRequest(source, Path.Combine(_root, "jobs"), new Dictionary<string, string> { ["blenderExecutable"] = executable });
        if (escaped)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => converter.ConvertAsync(request));
            Assert.Empty(Directory.EnumerateFileSystemEntries(request.WorkRoot));
        }
        else Assert.Equal(LibraryPackageContentKind.RawAsset, (await converter.ConvertAsync(request)).ContentKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalConversionSdkUsesFreshOutputAndReportsItsPreparedKind(bool escaped)
    {
        var scriptRoot = Directory.CreateDirectory(Path.Combine(_root, "Scripts", "custom")).FullName;
        await File.WriteAllTextAsync(Path.Combine(scriptRoot, "converter.exe"), "fixture");
        await File.WriteAllTextAsync(Path.Combine(scriptRoot, "installation-script.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, id = "custom.convert", displayName = "Custom converter", description = "QA", publisher = "QA", executable = "converter.exe",
            arguments = new[] { "{OutputDirectory}", "{ResultFile}" }, supportedContentKinds = new[] { "Unknown" }, supportedTargetKinds = new[] { "Project" }, preparedContentKind = "RawAsset"
        }));
        var registry = new FabInstallationScriptRegistry([], new Runner(request =>
        {
            File.WriteAllText(Path.Combine(request.Arguments[0], "mesh.fbx"), "fixture");
            File.WriteAllText(request.Arguments[1], JsonSerializer.Serialize(new { variant = escaped ? _root : "Content" })); return Success();
        }));
        Assert.Equal(1, await registry.RefreshExternalAsync(Path.Combine(_root, "Scripts")));
        var script = Assert.IsAssignableFrom<IFabPreparationScript>(registry.Find("custom.convert"));
        var context = Context with { SourceDirectory = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName };
        if (escaped) { await Assert.ThrowsAsync<InvalidDataException>(() => script.InstallAsync(context)); return; }
        var result = await script.InstallAsync(context);
        Assert.Equal(LibraryPackageContentKind.RawAsset, script.OutputContentKind);
        Assert.True(File.Exists(Path.Combine(result.Outputs["variant"], "mesh.fbx")));
    }

    private static ProcessResult Success() => new(1, 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
    private sealed class Runner(Func<ProcessRequest, ProcessResult> run) : IProcessRunner
    { public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default) => Task.FromResult(run(request)); }
    private sealed class Preparation(string output) : IFabPreparationScript
    {
        public bool Ran { get; private set; }
        public LibraryPackageContentKind OutputContentKind => LibraryPackageContentKind.RawAsset;
        public FabInstallationScriptDescriptor Descriptor { get; } = new("convert", "Convert", "QA", "QA", [LibraryPackageContentKind.Unknown], [FabInstallationTargetKind.Project], [], true);
        public Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context, IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
        { Ran = true; Assert.Equal("true", context.Configuration["prepareOnly"]); return Task.FromResult(new FabInstallationScriptResult("convert", "done", new Dictionary<string, string> { ["variant"] = output })); }
    }
    private sealed class Install : IFabInstallationScript
    {
        public FabInstallationScriptContext? Seen { get; private set; }
        public FabInstallationScriptDescriptor Descriptor { get; } = new("install", "Install", "QA", "QA", [LibraryPackageContentKind.RawAsset], [FabInstallationTargetKind.Project], [], true);
        public Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context, IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
        { Seen = context; return Task.FromResult(new FabInstallationScriptResult("install", "done", new Dictionary<string, string>())); }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
