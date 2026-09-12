using System.Text.Json;
using System.Text.Json.Serialization;
using CyEngineHub.Core.Processes;
using CyEngineHub.Engines;
using CyEngineHub.Extensions;

namespace CyEngineHub.Extensions.Tests;

public sealed class HubExtensionServicesTests
{
    [Theory]
    [InlineData("cyenginehub.marketplace-cosmos")]
    [InlineData("cyenginehub.marketplace-megascans-fab")]
    [InlineData("cyenginehub.unreal-relocated-install")]
    [InlineData("cyenginehub.unreal-version-bridge")]
    public async Task BuiltInWebExtensionsHaveValidManifests(string id)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "BuiltInManifests", id);
        var manifest = await new HubExtensionManifestParser().ParseAsync(root);
        Assert.Equal(id, manifest.Id);
        Assert.Equal(HubExtensionExecutionKind.WebLink, manifest.ExecutionKind);
        if (manifest.Kind == HubExtensionKind.Marketplace)
        {
            Assert.Equal(id == "cyenginehub.marketplace-megascans-fab"
                ? "https://www.fab.com/sellers/Quixel%20Megascans"
                : "https://cosmos.leartesstudios.com/inventory", manifest.Marketplace!.InventoryUrl);
            Assert.True(manifest.Capabilities.HasFlag(HubExtensionCapability.OwnedInventory));
        }
    }

    [Theory]
    [InlineData("http://example.com/inventory")]
    [InlineData("https://another.example/inventory")]
    [InlineData("https://example.com/inventory?token=secret")]
    [InlineData("https://user:password@example.com/inventory")]
    [InlineData("https://example.com/inventory#token")]
    public async Task MarketplaceRejectsUnsafeInventoryLinks(string url)
    {
        using var temporary = new TemporaryDirectory();
        WriteManifest(temporary.Path, new HubExtensionManifest
        {
            SchemaVersion = 1, Id = "test.marketplace", DisplayName = "Test", Version = "1.0.0",
            Publisher = "Test", Description = "Owned assets", Kind = HubExtensionKind.Marketplace,
            ExecutionKind = HubExtensionExecutionKind.WebLink,
            Capabilities = HubExtensionCapability.OwnedInventory,
            SupportedEngines = [EngineFamily.Unreal], Website = "https://example.com/inventory",
            Marketplace = new HubMarketplaceDescriptor { InventoryUrl = url },
        });
        await Assert.ThrowsAsync<HubExtensionException>(() => new HubExtensionManifestParser().ParseAsync(temporary.Path));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task ParserAcceptsAContainedExternalTool()
    {
        using var temporary = new TemporaryDirectory();
        var root = CreateExternalPackage(temporary.Path, "test.asset-tool", "1.0.0", "tool-v1");

        var manifest = await new HubExtensionManifestParser().ParseAsync(root);

        Assert.Equal("test.asset-tool", manifest.Id);
        Assert.Equal(HubExtensionExecutionKind.ExternalProcess, manifest.ExecutionKind);
        Assert.Contains(EngineFamily.Unity, manifest.SupportedEngines);
    }

    [Fact]
    public async Task ParserAcceptsBoundedUiContributions()
    {
        using var temporary = new TemporaryDirectory();
        var root = Directory.CreateDirectory(Path.Combine(temporary.Path, "Package")).FullName;
        var tools = Directory.CreateDirectory(Path.Combine(root, "tools")).FullName;
        await File.WriteAllTextAsync(Path.Combine(tools, "tool.exe"), "tool");
        WriteManifest(root, CreateManifest(
            "test.ui-tool",
            "1.0.0",
            "tools/tool.exe",
            [new HubUiContribution
            {
                Id = "project-action",
                Label = "Convertir avec le studio",
                Placement = HubUiPlacement.ProjectsToolbar,
                Action = HubUiAction.PrepareExecution,
                Order = 20,
            }]));

        var manifest = await new HubExtensionManifestParser().ParseAsync(root);

        var contribution = Assert.Single(manifest.UiContributions);
        Assert.Equal(HubUiPlacement.ProjectsToolbar, contribution.Placement);
    }

    [Fact]
    public async Task ParserRejectsDuplicateUiContributionIds()
    {
        using var temporary = new TemporaryDirectory();
        var root = Directory.CreateDirectory(Path.Combine(temporary.Path, "Package")).FullName;
        var tools = Directory.CreateDirectory(Path.Combine(root, "tools")).FullName;
        await File.WriteAllTextAsync(Path.Combine(tools, "tool.exe"), "tool");
        WriteManifest(root, CreateManifest(
            "test.bad-ui",
            "1.0.0",
            "tools/tool.exe",
            [
                new HubUiContribution { Id = "same", Label = "Premier", Action = HubUiAction.OpenExtension },
                new HubUiContribution { Id = "same", Label = "Second", Action = HubUiAction.OpenExtension },
            ]));

        await Assert.ThrowsAsync<HubExtensionException>(
            () => new HubExtensionManifestParser().ParseAsync(root));
    }

    [Fact]
    public async Task ParserRejectsAnEntryPointOutsideTheExtension()
    {
        using var temporary = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(temporary.Path, "outside.exe"), "outside");
        var root = Directory.CreateDirectory(Path.Combine(temporary.Path, "Package")).FullName;
        WriteManifest(root, CreateManifest(
            "test.traversal",
            "1.0.0",
            "../outside.exe"));

        var exception = await Assert.ThrowsAsync<HubExtensionException>(
            () => new HubExtensionManifestParser().ParseAsync(root));

        Assert.Equal("ValidatePath", exception.Operation);
    }

    [Fact]
    public async Task InstallationRetainsThePreviousVersionAsBackup()
    {
        using var temporary = new TemporaryDirectory();
        var package = CreateExternalPackage(temporary.Path, "test.install", "1.0.0", "tool-v1");
        var userRoot = Path.Combine(temporary.Path, "Installed");
        var parser = new HubExtensionManifestParser();
        var service = new HubExtensionInstallationService(parser);
        var first = await service.ExecuteAsync(await service.PrepareAsync(package, userRoot));
        await File.WriteAllTextAsync(Path.Combine(package, "tools", "tool.exe"), "tool-v2");
        WriteManifest(package, CreateManifest("test.install", "2.0.0", "tools/tool.exe"));

        var second = await service.ExecuteAsync(await service.PrepareAsync(package, userRoot));

        Assert.Null(first.BackupDirectory);
        Assert.NotNull(second.BackupDirectory);
        Assert.True(Directory.Exists(second.BackupDirectory));
        Assert.Equal("2.0.0", second.Extension.Manifest.Version);
        Assert.Equal(
            "tool-v2",
            await File.ReadAllTextAsync(Path.Combine(second.Extension.RootDirectory, "tools", "tool.exe")));
    }

    [Fact]
    public async Task CatalogKeepsBuiltInExtensionAheadOfDuplicateUserId()
    {
        using var temporary = new TemporaryDirectory();
        var builtInRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "BuiltIn")).FullName;
        var userRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "User")).FullName;
        CreateWebPackage(builtInRoot, "duplicate.integration");
        CreateWebPackage(userRoot, "duplicate.integration");
        var service = new HubExtensionCatalogService(new HubExtensionManifestParser());

        var snapshots = await service.DiscoverAsync(builtInRoot, userRoot);

        Assert.Equal(2, snapshots.Count);
        Assert.Contains(snapshots, snapshot =>
            snapshot.Origin == HubExtensionOrigin.BuiltIn &&
            snapshot.Health == HubExtensionHealth.Ready);
        Assert.Contains(snapshots, snapshot =>
            snapshot.Origin == HubExtensionOrigin.User &&
            snapshot.Health == HubExtensionHealth.Invalid);
    }

    [Fact]
    public async Task AssetBridgePlanExpandsPathsAndRunsThroughTheIsolatedRunner()
    {
        using var temporary = new TemporaryDirectory();
        var root = CreateExternalPackage(
            temporary.Path,
            "cyenginehub.asset-bridge",
            "1.0.0",
            "trusted-tool");
        var parser = new HubExtensionManifestParser();
        var manifest = await parser.ParseAsync(root);
        var snapshot = new HubExtensionSnapshot(
            manifest,
            root,
            HubExtensionOrigin.BuiltIn,
            HubExtensionHealth.Ready,
            null);
        var input = Directory.CreateDirectory(Path.Combine(temporary.Path, "Input")).FullName;
        await File.WriteAllTextAsync(Path.Combine(input, "mesh.fbx"), "portable-mesh");
        await File.WriteAllTextAsync(Path.Combine(input, "native.uasset"), "native-asset");
        await File.WriteAllTextAsync(Path.Combine(input, "notes.txt"), "unsupported");
        var output = CreateUnityProject(temporary.Path);
        var runner = new CapturingProcessRunner();
        var service = new HubExtensionExecutionService(runner);

        var plan = await service.PrepareAsync(
            snapshot,
            input,
            output,
            EngineFamily.Unreal,
            EngineFamily.Unity);
        var tamperedPlan = plan with { Arguments = ["--input", "unexpected"] };
        var tamperedException = await Assert.ThrowsAsync<HubExtensionException>(
            () => service.ExecuteAsync(tamperedPlan));
        var result = await service.ExecuteAsync(plan);

        Assert.Equal("ExecuteExtension", tamperedException.Operation);
        Assert.Contains("arguments", tamperedException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.ProcessResult.Succeeded);
        Assert.NotNull(runner.Request);
        Assert.Equal(1, plan.InputSummary.TransferableFileCount);
        Assert.Equal(new FileInfo(Path.Combine(input, "mesh.fbx")).Length, plan.InputSummary.TransferableBytes);
        Assert.Equal(1, plan.InputSummary.EngineNativeFileCount);
        Assert.Equal(1, plan.InputSummary.UnsupportedFileCount);
        Assert.StartsWith(
            Path.Combine(output, "Assets", "CyEngineHubImports"),
            plan.OutputPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(input, runner.Request.Arguments);
        Assert.Contains(plan.OutputPath, runner.Request.Arguments);
        Assert.Contains(nameof(EngineFamily.Unreal), runner.Request.Arguments);
        Assert.Contains(nameof(EngineFamily.Unity), runner.Request.Arguments);
    }

    [Fact]
    public async Task AssetBridgePlansUnrealTargetInsideProjectStaging()
    {
        using var temporary = new TemporaryDirectory();
        var root = CreateExternalPackage(
            temporary.Path,
            "cyenginehub.asset-bridge",
            "1.0.0",
            "trusted-tool");
        var manifest = await new HubExtensionManifestParser().ParseAsync(root);
        var snapshot = new HubExtensionSnapshot(
            manifest,
            root,
            HubExtensionOrigin.BuiltIn,
            HubExtensionHealth.Ready,
            null);
        var input = Directory.CreateDirectory(Path.Combine(temporary.Path, "UnityExports")).FullName;
        await File.WriteAllTextAsync(Path.Combine(input, "scene.glb"), "portable-scene");
        var unrealProject = Directory.CreateDirectory(Path.Combine(temporary.Path, "UnrealProject")).FullName;
        var projectFile = Path.Combine(unrealProject, "Target.uproject");
        await File.WriteAllTextAsync(projectFile, "{}");

        var plan = await new HubExtensionExecutionService(new CapturingProcessRunner()).PrepareAsync(
            snapshot,
            input,
            projectFile,
            EngineFamily.Unity,
            EngineFamily.Unreal);

        Assert.StartsWith(
            Path.Combine(unrealProject, "Saved", "CyEngineHubAssetBridge"),
            plan.OutputPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, plan.InputSummary.TransferableFileCount);
    }

    [Fact]
    public async Task AssetBridgePlansGodotTargetInsideProjectImportRoot()
    {
        using var temporary = new TemporaryDirectory();
        var root = CreateExternalPackage(
            temporary.Path,
            "cyenginehub.asset-bridge",
            "1.0.0",
            "trusted-tool");
        var manifest = await new HubExtensionManifestParser().ParseAsync(root);
        var snapshot = new HubExtensionSnapshot(
            manifest,
            root,
            HubExtensionOrigin.BuiltIn,
            HubExtensionHealth.Ready,
            null);
        var input = Directory.CreateDirectory(Path.Combine(temporary.Path, "UnityExports")).FullName;
        await File.WriteAllTextAsync(Path.Combine(input, "environment.glb"), "portable-scene");
        await File.WriteAllTextAsync(Path.Combine(input, "native.tscn"), "native-scene");
        var godotProject = Directory.CreateDirectory(Path.Combine(temporary.Path, "GodotProject")).FullName;
        var projectFile = Path.Combine(godotProject, "project.godot");
        await File.WriteAllTextAsync(projectFile, "[application]");

        var plan = await new HubExtensionExecutionService(new CapturingProcessRunner()).PrepareAsync(
            snapshot,
            input,
            projectFile,
            EngineFamily.Unity,
            EngineFamily.Godot);

        Assert.StartsWith(
            Path.Combine(godotProject, "CyEngineHubImports"),
            plan.OutputPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, plan.InputSummary.TransferableFileCount);
        Assert.Equal(1, plan.InputSummary.EngineNativeFileCount);
    }

    [Fact]
    public async Task AssetBridgeAcceptsGodotAsSourceForUnityTarget()
    {
        using var temporary = new TemporaryDirectory();
        var root = CreateExternalPackage(
            temporary.Path,
            "cyenginehub.asset-bridge",
            "1.0.0",
            "trusted-tool");
        var manifest = await new HubExtensionManifestParser().ParseAsync(root);
        var snapshot = new HubExtensionSnapshot(
            manifest,
            root,
            HubExtensionOrigin.BuiltIn,
            HubExtensionHealth.Ready,
            null);
        var input = Directory.CreateDirectory(Path.Combine(temporary.Path, "GodotExports")).FullName;
        await File.WriteAllTextAsync(Path.Combine(input, "character.gltf"), "portable-scene");

        var plan = await new HubExtensionExecutionService(new CapturingProcessRunner()).PrepareAsync(
            snapshot,
            input,
            CreateUnityProject(temporary.Path),
            EngineFamily.Godot,
            EngineFamily.Unity);

        Assert.Equal(EngineFamily.Godot, plan.SourceEngine);
        Assert.Equal(EngineFamily.Unity, plan.TargetEngine);
        Assert.Equal(1, plan.InputSummary.TransferableFileCount);
    }

    [Fact]
    public async Task AssetBridgePlansCryEngineTargetInsideDeclaredAssetsImportRoot()
    {
        using var temporary = new TemporaryDirectory();
        var root = CreateExternalPackage(
            temporary.Path,
            "cyenginehub.asset-bridge",
            "1.0.0",
            "trusted-tool");
        var manifest = await new HubExtensionManifestParser().ParseAsync(root);
        var snapshot = new HubExtensionSnapshot(
            manifest,
            root,
            HubExtensionOrigin.BuiltIn,
            HubExtensionHealth.Ready,
            null);
        var input = Directory.CreateDirectory(Path.Combine(temporary.Path, "GodotExports")).FullName;
        await File.WriteAllTextAsync(Path.Combine(input, "environment.glb"), "portable-scene");
        var cryProject = Directory.CreateDirectory(Path.Combine(temporary.Path, "CryProject")).FullName;
        Directory.CreateDirectory(Path.Combine(cryProject, "GameAssets"));
        var projectFile = Path.Combine(cryProject, "Game.cryproject");
        await File.WriteAllTextAsync(
            projectFile,
            "{ \"content\": { \"assets\": [ \"GameAssets\" ] }, \"info\": { \"name\": \"Bridge Target\" } }");

        var plan = await new HubExtensionExecutionService(new CapturingProcessRunner()).PrepareAsync(
            snapshot,
            input,
            projectFile,
            EngineFamily.Godot,
            EngineFamily.CryEngine);

        Assert.StartsWith(
            Path.Combine(cryProject, "GameAssets", "CyEngineHubImports"),
            plan.OutputPath,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(EngineFamily.CryEngine, plan.TargetEngine);
    }

    [Fact]
    public async Task AssetBridgeRejectsSameEngineDirection()
    {
        using var temporary = new TemporaryDirectory();
        var root = CreateExternalPackage(
            temporary.Path,
            "cyenginehub.asset-bridge",
            "1.0.0",
            "trusted-tool");
        var manifest = await new HubExtensionManifestParser().ParseAsync(root);
        var snapshot = new HubExtensionSnapshot(
            manifest,
            root,
            HubExtensionOrigin.BuiltIn,
            HubExtensionHealth.Ready,
            null);
        var input = Directory.CreateDirectory(Path.Combine(temporary.Path, "Input")).FullName;
        await File.WriteAllTextAsync(Path.Combine(input, "mesh.obj"), "mesh");

        var exception = await Assert.ThrowsAsync<HubExtensionException>(() =>
            new HubExtensionExecutionService(new CapturingProcessRunner()).PrepareAsync(
                snapshot,
                input,
                CreateUnityProject(temporary.Path),
                EngineFamily.Unity,
                EngineFamily.Unity));

        Assert.Equal("PrepareExecution", exception.Operation);
    }

    [Fact]
    public async Task AssetBridgeRejectsSourceContainingOnlyNativeAssets()
    {
        using var temporary = new TemporaryDirectory();
        var root = CreateExternalPackage(
            temporary.Path,
            "cyenginehub.asset-bridge",
            "1.0.0",
            "trusted-tool");
        var manifest = await new HubExtensionManifestParser().ParseAsync(root);
        var snapshot = new HubExtensionSnapshot(
            manifest,
            root,
            HubExtensionOrigin.BuiltIn,
            HubExtensionHealth.Ready,
            null);
        var input = Directory.CreateDirectory(Path.Combine(temporary.Path, "Input")).FullName;
        await File.WriteAllTextAsync(Path.Combine(input, "map.umap"), "native-map");

        var exception = await Assert.ThrowsAsync<HubExtensionException>(() =>
            new HubExtensionExecutionService(new CapturingProcessRunner()).PrepareAsync(
                snapshot,
                input,
                CreateUnityProject(temporary.Path),
                EngineFamily.Unreal,
                EngineFamily.Unity));

        Assert.Equal("PrepareExecution", exception.Operation);
        Assert.Contains("Aucun format", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UserInstalledExecutableCannotBeRunEvenWithTheTrustedId()
    {
        using var temporary = new TemporaryDirectory();
        var root = CreateExternalPackage(
            temporary.Path,
            "cyenginehub.asset-bridge",
            "1.0.0",
            "untrusted-tool");
        var manifest = await new HubExtensionManifestParser().ParseAsync(root);
        var snapshot = new HubExtensionSnapshot(
            manifest,
            root,
            HubExtensionOrigin.User,
            HubExtensionHealth.Ready,
            null);
        var input = Directory.CreateDirectory(Path.Combine(temporary.Path, "Input")).FullName;

        var exception = await Assert.ThrowsAsync<HubExtensionException>(() =>
            new HubExtensionExecutionService(new CapturingProcessRunner()).PrepareAsync(
                snapshot,
                input,
                Path.Combine(temporary.Path, "Output"),
                EngineFamily.Unreal,
                EngineFamily.Unity));

        Assert.Equal("PrepareExecution", exception.Operation);
    }

    private static string CreateExternalPackage(
        string parent,
        string id,
        string version,
        string executableContent)
    {
        var root = Directory.CreateDirectory(Path.Combine(parent, $"Package-{Guid.NewGuid():N}")).FullName;
        var tools = Directory.CreateDirectory(Path.Combine(root, "tools"));
        var executableName = string.Equals(
            id,
            "cyenginehub.asset-bridge",
            StringComparison.Ordinal)
            ? "CyEngineHub.AssetBridge.Tool.exe"
            : "tool.exe";
        File.WriteAllText(Path.Combine(tools.FullName, executableName), executableContent);
        WriteManifest(root, CreateManifest(id, version, $"tools/{executableName}"));
        return root;
    }

    private static void CreateWebPackage(string parent, string id)
    {
        var root = Directory.CreateDirectory(Path.Combine(parent, id)).FullName;
        WriteManifest(root, new HubExtensionManifest
        {
            SchemaVersion = 1,
            Id = id,
            DisplayName = "Web integration",
            Version = "1.0.0",
            Publisher = "Tests",
            Description = "Integration HTTPS de test",
            Kind = HubExtensionKind.EngineIntegration,
            ExecutionKind = HubExtensionExecutionKind.WebLink,
            Capabilities = HubExtensionCapability.ProjectTool,
            SupportedEngines = [EngineFamily.Unreal],
            Website = "https://example.com/integration",
        });
    }

    private static string CreateUnityProject(string parent)
    {
        var root = Directory.CreateDirectory(Path.Combine(parent, $"UnityProject-{Guid.NewGuid():N}"));
        Directory.CreateDirectory(Path.Combine(root.FullName, "Assets"));
        var projectSettings = Directory.CreateDirectory(Path.Combine(root.FullName, "ProjectSettings"));
        File.WriteAllText(
            Path.Combine(projectSettings.FullName, "ProjectVersion.txt"),
            "m_EditorVersion: 6000.0.0f1");
        return root.FullName;
    }

    private static HubExtensionManifest CreateManifest(
        string id,
        string version,
        string entryPoint,
        IReadOnlyList<HubUiContribution>? uiContributions = null) =>
        new()
        {
            SchemaVersion = 1,
            Id = id,
            DisplayName = "Asset tool",
            Version = version,
            Publisher = "Tests",
            Description = "Outil externe de test",
            Kind = HubExtensionKind.AssetProcessor,
            ExecutionKind = HubExtensionExecutionKind.ExternalProcess,
            Capabilities = HubExtensionCapability.AssetConvert,
            SupportedEngines = [EngineFamily.Unreal, EngineFamily.Unity, EngineFamily.Godot, EngineFamily.CryEngine],
            SupportedFileExtensions = [".fbx"],
            EntryPoint = entryPoint,
            Arguments = ["--input", "{Input}", "--output", "{Output}", "{SourceEngine}", "{TargetEngine}"],
            WritesToOutput = true,
            UiContributions = uiContributions ?? [],
        };

    private static void WriteManifest(string root, HubExtensionManifest manifest) =>
        File.WriteAllText(
            Path.Combine(root, HubExtensionManifestParser.ManifestFileName),
            JsonSerializer.Serialize(manifest, JsonOptions));

    private sealed class CapturingProcessRunner : IProcessRunner
    {
        public ProcessRequest? Request { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            IProgress<ProcessOutputLine>? output = null,
            CancellationToken cancellationToken = default)
        {
            Request = request;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProcessResult(42, 0, now, now, []));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CyEngineHub-ExtensionsTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
