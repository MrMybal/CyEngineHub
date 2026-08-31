using CyEngineHub.CryEngine.Integration;
using CyEngineHub.Engines;
using CyEngineHub.Godot.Integration;
using CyEngineHub.Unity.Integration;

namespace CyEngineHub.Engines.Tests;

public sealed class LocalEnginePackageInventoryTests
{
    [Fact]
    public async Task UnityInventoryCombinesManifestAndResolvedLock()
    {
        using var workspace = new TestWorkspace();
        var packagesRoot = workspace.CreateDirectory("UnityProject", "Packages");
        var embeddedRoot = workspace.CreateDirectory(
            "UnityProject",
            "Packages",
            "com.cyenginehub.tool");
        workspace.Write(
            "UnityProject/Packages/manifest.json",
            """
            {
              "dependencies": {
                "com.cyenginehub.tool": "file:com.cyenginehub.tool"
              }
            }
            """);
        workspace.Write(
            "UnityProject/Packages/packages-lock.json",
            """
            {
              "dependencies": {
                "com.cyenginehub.tool": {
                  "version": "file:com.cyenginehub.tool",
                  "depth": 0,
                  "source": "embedded",
                  "dependencies": { "com.unity.modules.jsonserialize": "1.0.0" }
                },
                "com.unity.modules.jsonserialize": {
                  "version": "1.0.0",
                  "depth": 1,
                  "source": "builtin",
                  "dependencies": {}
                }
              }
            }
            """);
        workspace.Write(
            "UnityProject/Packages/com.cyenginehub.tool/package.json",
            """
            {
              "name": "com.cyenginehub.tool",
              "displayName": "CyEngineHub Tool",
              "version": "2.1.0",
              "description": "Package de test"
            }
            """);
        var projectRoot = Directory.GetParent(packagesRoot)!.FullName;
        var provider = new UnityEngineModule();

        var snapshot = await provider.DiscoverPackagesAsync(
            [CreateProject(EngineFamily.Unity, "unity", projectRoot, "Packages/manifest.json")]);

        Assert.False(snapshot.IsPartial);
        Assert.Equal(2, snapshot.Packages.Count);
        var direct = Assert.Single(snapshot.Packages, item => item.Name == "CyEngineHub Tool");
        Assert.Equal(embeddedRoot, direct.RootPath);
        Assert.Equal("Dépendance directe", direct.State);
        Assert.Equal("UPM · embarqué", direct.Source);
        Assert.Contains("com.unity.modules.jsonserialize", direct.Dependencies);
        Assert.Contains(snapshot.Packages, item =>
            item.Name == "com.unity.modules.jsonserialize" &&
            item.State == "Dépendance transitive");
    }

    [Fact]
    public async Task UnityInventoryReportsMalformedManifestWithoutThrowing()
    {
        using var workspace = new TestWorkspace();
        var projectRoot = workspace.CreateDirectory("UnityProject");
        workspace.CreateDirectory("UnityProject", "Packages");
        workspace.Write("UnityProject/Packages/manifest.json", "{ invalid");
        var provider = new UnityEngineModule();

        var snapshot = await provider.DiscoverPackagesAsync(
            [CreateProject(EngineFamily.Unity, "unity", projectRoot, "Packages/manifest.json")]);

        Assert.True(snapshot.IsPartial);
        Assert.Empty(snapshot.Packages);
        Assert.NotEmpty(snapshot.Warnings);
    }

    [Fact]
    public async Task GodotInventoryReadsEditorPluginStateAndNativeExtension()
    {
        using var workspace = new TestWorkspace();
        var projectRoot = workspace.CreateDirectory("GodotProject");
        workspace.Write(
            "GodotProject/project.godot",
            """
            [application]
            config/name="Inventaire"
            [editor_plugins]
            enabled=PackedStringArray("res://addons/cy_tools/plugin.cfg")
            """);
        workspace.CreateDirectory("GodotProject", "addons", "cy_tools");
        workspace.Write(
            "GodotProject/addons/cy_tools/plugin.cfg",
            """
            [plugin]
            name="Cy Tools"
            description="Outils Godot"
            author="CyberAlien"
            version="1.4.0"
            script="plugin.gd"
            """);
        workspace.Write(
            "GodotProject/addons/cy_tools/plugin.gd",
            "@tool\nextends EditorPlugin\n");
        workspace.CreateDirectory("GodotProject", "addons", "native_fx", "bin");
        workspace.Write(
            "GodotProject/addons/native_fx/bin/native_fx.gdextension",
            "[configuration]\nentry_symbol=\"native_fx_init\"\n");
        var provider = new GodotEngineModule();

        var snapshot = await provider.DiscoverPackagesAsync(
            [CreateProject(EngineFamily.Godot, "godot", projectRoot, "project.godot")]);

        Assert.False(snapshot.IsPartial);
        Assert.Equal(2, snapshot.Packages.Count);
        var editorPlugin = Assert.Single(snapshot.Packages, item => item.Name == "Cy Tools");
        Assert.Equal("Plugin éditeur · activé", editorPlugin.State);
        Assert.Equal(LocalEnginePackageHealth.Ready, editorPlugin.Health);
        Assert.Contains(snapshot.Packages, item =>
            item.Name == "native_fx" && item.Source == "Godot · GDExtension");
    }

    [Fact]
    public async Task GodotInventoryFlagsMissingPluginScript()
    {
        using var workspace = new TestWorkspace();
        var projectRoot = workspace.CreateDirectory("GodotProject", "addons", "broken");
        projectRoot = Directory.GetParent(Directory.GetParent(projectRoot)!.FullName)!.FullName;
        workspace.Write("GodotProject/project.godot", "[application]\nconfig/name=\"Test\"\n");
        workspace.Write(
            "GodotProject/addons/broken/plugin.cfg",
            "[plugin]\nname=\"Broken\"\nversion=\"1.0\"\nscript=\"missing.gd\"\n");
        var provider = new GodotEngineModule();

        var snapshot = await provider.DiscoverPackagesAsync(
            [CreateProject(EngineFamily.Godot, "godot", projectRoot, "project.godot")]);

        var package = Assert.Single(snapshot.Packages);
        Assert.Equal(LocalEnginePackageHealth.Warning, package.Health);
        Assert.Contains(package.Issues, issue => issue.Contains("n’existe plus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CryEngineInventoryReadsDeclaredNativeAndManagedPlugins()
    {
        using var workspace = new TestWorkspace();
        var projectRoot = workspace.CreateDirectory("CryProject");
        var localPlugin = workspace.CreateDirectory("CryProject", "Plugins", "CyNative");
        var marker = workspace.Write(
            "CryProject/Sample.cryproject",
            """
            {
              "info": { "name": "Sample" },
              "require": {
                "engine": "cryengine-5.7",
                "plugins": [
                  { "path": "Plugins/CyNative", "type": "EPluginType::Native", "version": "1.2" },
                  { "path": "CryMonoBridge", "type": "EPluginType::Managed" }
                ]
              }
            }
            """);
        var provider = new CryEngineModule();

        var snapshot = await provider.DiscoverPackagesAsync(
            [CreateProject(EngineFamily.CryEngine, "cryengine", projectRoot, marker)]);

        Assert.False(snapshot.IsPartial);
        Assert.Equal(2, snapshot.Packages.Count);
        var native = Assert.Single(snapshot.Packages, item => item.Name == "CyNative");
        Assert.Equal(localPlugin, native.RootPath);
        Assert.Equal("CRYENGINE · natif C++", native.Source);
        Assert.Contains(snapshot.Packages, item =>
            item.Name == "CryMonoBridge" && item.Source == "CRYENGINE · managé C#");
    }

    [Fact]
    public async Task ProvidersIgnoreProjectsOwnedByAnotherEngine()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.CreateDirectory("OtherProject");
        var project = CreateProject(EngineFamily.Custom, "custom", root, "custom.project");

        var snapshots = await Task.WhenAll(
            new UnityEngineModule().DiscoverPackagesAsync([project]),
            new GodotEngineModule().DiscoverPackagesAsync([project]),
            new CryEngineModule().DiscoverPackagesAsync([project]));

        Assert.All(snapshots, snapshot => Assert.Empty(snapshot.Packages));
    }

    private static LocalEngineProject CreateProject(
        EngineFamily family,
        string providerId,
        string root,
        string marker) =>
        new(
            family,
            providerId,
            $"{providerId}:test",
            "Projet test",
            root,
            Path.IsPathRooted(marker) ? marker : Path.Combine(root, marker),
            string.Empty,
            "Projet de test");

    private sealed class TestWorkspace : IDisposable
    {
        public TestWorkspace()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "CyEngineHub.EnginePackageTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string CreateDirectory(params string[] segments)
        {
            var path = Combine(segments);
            Directory.CreateDirectory(path);
            return path;
        }

        public string Write(string relativePath, string content)
        {
            var path = Path.Combine(
                Root,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private string Combine(IEnumerable<string> segments) =>
            segments.Aggregate(Root, Path.Combine);
    }
}
