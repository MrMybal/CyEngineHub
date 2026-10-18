using System.IO.Compression;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Settings;
using CyEngineHub.Core.Marketplaces;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;
using Xunit;
#pragma warning disable CA1861 // Small expected sequences describe pipeline order beside assertions.

namespace CyEngineHub.Epic.Tests;

public sealed class PackGroupsAndCosmosTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("cyhub-groups-test-").FullName;
    private FabInstallationScriptContext Context => new(new("asset", "ue", "Pack", "", "Fab", "ASSET_PACK", null, null, [], []),
        new("variant57", [], ["5.7"], []), LibraryPackageContentKind.ContentPack, _root,
        new("project", "Project", FabInstallationTargetKind.Project, _root, "5.5", null, null), new Dictionary<string, string>());
    private static FabInstallationScriptRegistry Registry(params IFabInstallationScript[] scripts) => new(scripts, new NeverRunner());
    private static FabInstallationStep Step(string id) => new(id, new Dictionary<string, string>());

    [Fact]
    public async Task OrderedPipelinePreflightsAllStepsAndStopsOnFailure()
    {
        List<string> calls = [];
        var registry = Registry(new Script("first", calls), new Script("second", calls, fail: true), new Script("third", calls));
        var pipeline = new FabInstallationPipeline(registry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ExecuteAsync(Context, [Step("first"), Step("absent")]));
        Assert.Empty(calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => pipeline.ExecuteAsync(Context, [Step("first"), Step("second"), Step("third")]));
        Assert.Equal(new[] { "first", "second" }, calls);
    }

    [Fact]
    public async Task PreparedConversionFeedsItsOutputAndVersionToNextStep()
    {
        List<string> calls = [];
        var converted = Directory.CreateDirectory(Path.Combine(_root, "converted")).FullName;
        var convert = new Script(UnrealVersionBridgeInstallationScript.ScriptId, calls,
            result: new Dictionary<string, string> { ["variant"] = converted, ["sourceVersion"] = "5.5" });
        var install = new Script("install", calls);
        await new FabInstallationPipeline(Registry(convert, install)).ExecuteAsync(Context, [Step(convert.Descriptor.Id), Step("install")]);
        Assert.Equal("true", convert.Last!.Configuration["prepareOnly"]);
        Assert.Equal(converted, install.Last!.SourceDirectory);
        Assert.Equal("5.5", Assert.Single(install.Last.Version.EngineVersions));
        Assert.Equal("false", install.Last.Configuration["prepareOnly"]);
    }

    [Fact]
    public async Task PipelineCancellationDoesNotStartAnotherScript()
    {
        List<string> calls = []; using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FabInstallationPipeline(Registry(new Script("first", calls)))
            .ExecuteAsync(Context, [Step("first")], cancellationToken: stop.Token));
        Assert.Empty(calls);
    }

    [Fact]
    public async Task GroupsPersistExactVariantAndOrderedPerPackConfiguration()
    {
        var settings = new Settings(); var store = new LibraryPackGroupStore(settings);
        var item = new LibraryPackGroupItem("asset", "variant57", "Pack", "Fab", _root, LibraryPackageContentKind.ContentPack, ["5.7"],
            [new("convert", new Dictionary<string, string> { ["sourceVersion"] = "5.7" }), Step("move"), Step("install")]);
        await store.SaveAsync([new("group", "Environment", [item])]);
        var loaded = Assert.Single((await store.LoadAsync())[0].Items);
        Assert.Equal(item.Path, loaded.Path); Assert.Equal("variant57", loaded.ArtifactId);
        Assert.Equal(new[] { "convert", "move", "install" }, loaded.Steps.Select(step => step.ScriptId));
        Assert.Equal("5.7", loaded.Steps[0].Configuration["sourceVersion"]);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync([new("group", "Broken", [item with { Steps = [] }])]));
        Assert.Equal("Environment", (await store.LoadAsync())[0].Name);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/Engine/Assets")]
    [InlineData("Assets//Cosmos")]
    [InlineData("C:/outside")]
    public void ProjectDestinationRejectsUnsafePaths(string value) => Assert.Throws<ArgumentException>(() => CosmosProjectSettingsStore.Normalize(value));

    [Fact]
    public async Task CosmosDestinationIsPerProject()
    {
        var store = new CosmosProjectSettingsStore(new Settings());
        await store.SaveAsync(_root, "/Game/Assets/Cosmos");
        Assert.Equal("Assets/Cosmos", await store.LoadAsync(_root));
        Assert.Equal("Cosmos", await store.LoadAsync(Path.Combine(_root, "another")));
    }

    [Theory]
    [InlineData(LibraryPackageStorageMode.Uncompressed)]
    [InlineData(LibraryPackageStorageMode.ZipFastest)]
    public async Task CosmosUsesPackageJsonCompressionAndOrderedStorage(LibraryPackageStorageMode mode)
    {
        var source = Path.Combine(_root, "Original.zip");
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Create))
        { using var writer = new StreamWriter(zip.CreateEntry("Content/Props/Mesh.uasset").Open()); writer.Write("fixture"); }
        var before = File.ReadAllBytes(source);
        var first = Path.Combine(_root, "full"); var second = Path.Combine(_root, "available");
        const string marketplace = "cyenginehub.marketplace-cosmos";
        var result = await ConnectorLibraryPackageService.ImportAsync(source, marketplace, mode,
            new(marketplace, _root, _root, [new(first, 1), new(second)]), Path.Combine(_root, "staging"));
        Assert.StartsWith(second, result.DestinationDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result.ManifestPath)); Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(mode.ToString(), result.StorageMode);
        Assert.Equal(result.DestinationDirectory, Assert.Single(ConnectorLibraryPackageService.DiscoverPackages(marketplace, [second])));
        Assert.Empty(ConnectorLibraryPackageService.DiscoverPackages("another-marketplace", [second]));
        var catalog = new MarketplaceDownloadCatalog(Path.Combine(_root, "catalog.json"));
        var record = await catalog.RegisterAsync("cosmos", result.DestinationDirectory);
        Assert.Equal(result.StoredBytes, record.SizeBytes);
        var extracted = Path.Combine(_root, "extracted");
        await DerivedLibraryPackageService.MaterializeAsync(result.DestinationDirectory, extracted, LibraryPackageContentKind.ContentPack);
        Assert.Equal("fixture", File.ReadAllText(Path.Combine(extracted, "Content", "Props", "Mesh.uasset")));
    }

    [Fact]
    public async Task CosmosRejectsZipTraversalBeforeWritingFiles()
    {
        var zipPath = Path.Combine(_root, "bad.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        { zip.CreateEntry("good.txt"); zip.CreateEntry("../outside.txt"); }
        var destination = Path.Combine(_root, "extract");
        await Assert.ThrowsAsync<InvalidDataException>(() => ConnectorLibraryPackageService.MaterializeFileAsync(zipPath, destination));
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
    }

    private sealed class Settings : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = [];
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) { _values[key] = value; return Task.CompletedTask; }
    }
    private sealed class NeverRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
    private sealed class Script(string id, List<string> calls, bool fail = false, IReadOnlyDictionary<string, string>? result = null) : IFabInstallationScript
    {
        public FabInstallationScriptDescriptor Descriptor { get; } = new(id, id, "test", "QA", [LibraryPackageContentKind.ContentPack], [FabInstallationTargetKind.Project], [], true);
        public FabInstallationScriptContext? Last { get; private set; }
        public Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context, IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
        { Last = context; calls.Add(id); if (fail) throw new InvalidOperationException("fixture failure"); return Task.FromResult(new FabInstallationScriptResult(id, "done", result ?? new Dictionary<string, string>())); }
    }
    public void Dispose() => Directory.Delete(_root, true);
}
