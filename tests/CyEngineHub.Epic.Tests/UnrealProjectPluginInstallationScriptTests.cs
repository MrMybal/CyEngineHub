using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Plugins.Compatibility;
using CyEngineHub.Plugins.Discovery;
using CyEngineHub.Plugins.Installation;
using CyEngineHub.Plugins.Parsing;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Epic.Tests;

public sealed class UnrealProjectPluginInstallationScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyHub-project-plugin-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("Engine/Plugins/Marketplace/Example")]
    [InlineData("Plugins/Example")]
    [InlineData("Example")]
    public async Task InstallsOnlyPluginRootFromEngineLayoutIntoProject(string layout)
    {
        var source = Path.Combine(_root, "Source");
        var pluginRoot = Path.Combine(source, layout);
        Directory.CreateDirectory(Path.Combine(pluginRoot, "Content"));
        var descriptor = Path.Combine(pluginRoot, "Example.uplugin");
        const string descriptorJson = """{"FileVersion":3,"EngineVersion":"5.5.0","CanContainContent":true,"Installed":true}""";
        await File.WriteAllTextAsync(descriptor, descriptorJson);
        await File.WriteAllTextAsync(Path.Combine(pluginRoot, "Content", "Sample.uasset"), "asset");
        await File.WriteAllTextAsync(Path.Combine(source, "unrelated.txt"), "not a plugin");
        var context = CreateContext(source);
        Directory.CreateDirectory(context.Target.RootPath);
        await File.WriteAllTextAsync(context.Target.Project!.FilePath, """{"FileVersion":3,"EngineAssociation":"5.5"}""");
        var script = CreateScript();

        var result = await script.InstallAsync(context);

        var installed = Path.Combine(context.Target.RootPath, "Plugins", "Example");
        Assert.Equal(UnrealProjectPluginInstallationScript.ScriptId, result.ScriptId);
        Assert.Equal(installed, result.Outputs["destination"]);
        Assert.True(File.Exists(Path.Combine(installed, "Example.uplugin")));
        Assert.True(File.Exists(Path.Combine(installed, "Content", "Sample.uasset")));
        Assert.False(Directory.Exists(Path.Combine(context.Target.RootPath, "Engine")));
        Assert.False(File.Exists(Path.Combine(installed, "unrelated.txt")));
        Assert.False(Directory.Exists(context.Target.Engine!.RootPath));
        Assert.Equal(descriptorJson, await File.ReadAllTextAsync(descriptor));
    }

    [Fact]
    public async Task RejectsEngineTargetAndPackWithoutPlugin()
    {
        var source = Path.Combine(_root, "NoPlugin");
        Directory.CreateDirectory(source);
        var script = CreateScript();
        var context = CreateContext(source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => script.InstallAsync(context));
        await Assert.ThrowsAsync<InvalidOperationException>(() => script.InstallAsync(
            context with { Target = context.Target with { Kind = FabInstallationTargetKind.Engine } }));
        Assert.False(Directory.Exists(context.Target.RootPath));
    }

    private static UnrealProjectPluginInstallationScript CreateScript()
    {
        var parser = new UpluginDescriptorParser();
        return new(new PluginDiscoveryService(parser), new PluginInstallationService(new PluginCompatibilityService(), parser));
    }

    private FabInstallationScriptContext CreateContext(string source)
    {
        var projectRoot = Path.Combine(_root, "Target");
        var project = new UnrealProject("project", "Target", Path.Combine(projectRoot, "Target.uproject"),
            projectRoot, "5.5", null, null, [], [], ProjectHealth.Healthy, [], DateTimeOffset.UtcNow);
        var engineRoot = Path.Combine(_root, "EngineNeverModified");
        var engine = new UnrealEngineInstallation("engine", "5.5", "UE 5.5", engineRoot, "",
            new EngineVersion(5, 5, 4), EngineInstallationType.EpicBinary, null, null, null,
            "Tests", EngineHealth.Healthy, [], DateTimeOffset.UtcNow);
        return new(new RemoteFabAsset("asset", "ue", "Example", "", "Fab", "CODE_PLUGIN", null, null, [], []),
            new RemoteFabProjectVersion("build", [], ["5.5"], ["Windows"]), LibraryPackageContentKind.CodePlugin,
            source, new FabInstallationScriptTarget("project", "Target", FabInstallationTargetKind.Project,
                projectRoot, "5.5.4", project, engine), new Dictionary<string, string>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
