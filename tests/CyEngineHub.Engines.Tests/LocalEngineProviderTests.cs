using CyEngineHub.CryEngine.Integration;
using CyEngineHub.Godot.Integration;
using CyEngineHub.Unity.Integration;

namespace CyEngineHub.Engines.Tests;

public sealed class LocalEngineProviderTests
{
    [Fact]
    public async Task UnityDiscoversProjectAndCreatesExactVersionLaunch()
    {
        using var temporary = new TemporaryDirectory();
        var editor = CreateFile(
            temporary.Path,
            "UnityEditors",
            "6000.0.0f1",
            "Editor",
            "Unity.exe");
        var project = Path.Combine(temporary.Path, "Projects", "UnityGame");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        CreateFile(
            project,
            "ProjectSettings",
            "ProjectVersion.txt",
            content: "m_EditorVersion: 6000.0.0f1\n");
        var provider = new UnityEngineModule();

        var snapshot = await provider.DiscoverAsync(new LocalEngineDiscoveryRequest(
            [Path.Combine(temporary.Path, "UnityEditors")],
            [Path.Combine(temporary.Path, "Projects")]));
        var installation = Assert.Single(
            snapshot.Installations,
            item => string.Equals(item.EditorPath, editor, StringComparison.OrdinalIgnoreCase));
        var discoveredProject = Assert.Single(snapshot.Projects);
        var launch = provider.CreateProjectLaunch(discoveredProject, [installation]);

        Assert.Equal("6000.0.0f1", installation.Version);
        Assert.Equal("6000.0.0f1", discoveredProject.RequiredEditorVersion);
        Assert.Equal(editor, launch.ExecutablePath);
        Assert.Equal(["-projectPath", project], launch.Arguments);
    }

    [Fact]
    public async Task UnityRefusesToOpenProjectWithAnUnmatchedEditor()
    {
        using var temporary = new TemporaryDirectory();
        CreateFile(
            temporary.Path,
            "UnityEditors",
            "6000.0.0f1",
            "Editor",
            "Unity.exe");
        var project = Path.Combine(temporary.Path, "UnityGame");
        Directory.CreateDirectory(Path.Combine(project, "Assets"));
        CreateFile(
            project,
            "ProjectSettings",
            "ProjectVersion.txt",
            content: "m_EditorVersion: 9999.99.0f1\n");
        var provider = new UnityEngineModule();
        var snapshot = await provider.DiscoverAsync(new LocalEngineDiscoveryRequest(
            [Path.Combine(temporary.Path, "UnityEditors")],
            [project]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.CreateProjectLaunch(Assert.Single(snapshot.Projects), snapshot.Installations));

        Assert.Contains("9999.99.0f1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GodotDiscoversNamedDotNetProjectAndCreatesCompatibleLaunch()
    {
        using var temporary = new TemporaryDirectory();
        var editor = CreateFile(
            temporary.Path,
            ["GodotEditors", "Godot_v4.3-stable_mono_win64.exe"],
            string.Empty);
        var project = Path.Combine(temporary.Path, "Projects", "GodotGame");
        Directory.CreateDirectory(project);
        CreateFile(
            project,
            "project.godot",
            content: "[application]\nconfig/name=\"Orbit Forge\"\n[application]\nconfig/features=PackedStringArray(\"4.3\", \"C#\")\n");
        CreateFile(project, "OrbitForge.csproj", content: "<Project />");
        var provider = new GodotEngineModule();

        var snapshot = await provider.DiscoverAsync(new LocalEngineDiscoveryRequest(
            [Path.Combine(temporary.Path, "GodotEditors")],
            [Path.Combine(temporary.Path, "Projects")]));
        var installation = Assert.Single(
            snapshot.Installations,
            item => string.Equals(item.EditorPath, editor, StringComparison.OrdinalIgnoreCase));
        var discoveredProject = Assert.Single(snapshot.Projects);
        var launch = provider.CreateProjectLaunch(discoveredProject, [installation]);

        Assert.Equal("Orbit Forge", discoveredProject.Name);
        Assert.Equal("4.3", discoveredProject.RequiredEditorVersion);
        Assert.Contains(".NET", discoveredProject.Description, StringComparison.Ordinal);
        Assert.Equal(["--editor", "--path", project], launch.Arguments);
    }

    [Fact]
    public async Task CryEngineDiscoversDescriptorProjectAndCreatesIdentityMatchedLaunch()
    {
        using var temporary = new TemporaryDirectory();
        const string engineId = "7893db92-d8c1-4ae5-b3e2-2ce0a0e77782";
        var engineRoot = Path.Combine(temporary.Path, "CryEngines", "5.7.1");
        CreateFile(
            engineRoot,
            "cryengine.cryengine",
            $$"""
            {
              "info": {
                "id": "{{engineId}}",
                "name": "CRYENGINE 5.7 LTS",
                "version": "5.7.1"
              }
            }
            """);
        var editor = CreateFile(engineRoot, ["bin", "win_x64", "Sandbox.exe"], string.Empty);
        var project = Path.Combine(temporary.Path, "Projects", "CryGame");
        var marker = CreateFile(
            project,
            "Game.cryproject",
            $$"""
            {
              "version": 3,
              "content": { "assets": [ "Assets" ], "code": [ "Code" ] },
              "info": { "name": "Frozen Orbit" },
              "require": { "engine": "{{engineId}}" }
            }
            """);
        var provider = new CryEngineModule();

        var snapshot = await provider.DiscoverAsync(new LocalEngineDiscoveryRequest(
            [Path.Combine(temporary.Path, "CryEngines")],
            [Path.Combine(temporary.Path, "Projects")]));
        // The provider also discovers real registered installations on developer machines.
        var installation = Assert.Single(snapshot.Installations, item =>
            string.Equals(item.EditorPath, editor, StringComparison.OrdinalIgnoreCase));
        var discoveredProject = Assert.Single(snapshot.Projects);
        var launch = provider.CreateProjectLaunch(discoveredProject, [installation]);

        Assert.Equal("CRYENGINE 5.7 LTS", installation.DisplayName);
        Assert.Equal("5.7.1", installation.Version);
        Assert.Equal("Frozen Orbit", discoveredProject.Name);
        Assert.Equal(engineId, discoveredProject.RequiredEditorVersion);
        Assert.Contains("assets Assets", discoveredProject.Description, StringComparison.Ordinal);
        Assert.Equal(editor, launch.ExecutablePath);
        Assert.Equal(["-project", marker], launch.Arguments);
        Assert.Equal(project, launch.WorkingDirectory);
    }

    [Fact]
    public async Task CryEngineRefusesProjectWhenRequiredIdentityIsUnavailable()
    {
        using var temporary = new TemporaryDirectory();
        var engineRoot = Path.Combine(temporary.Path, "CryEngine", "5.7.1");
        CreateFile(
            engineRoot,
            "cryengine.cryengine",
            "{ \"info\": { \"id\": \"available-engine\", \"version\": \"5.7.1\" } }");
        CreateFile(engineRoot, ["bin", "win_x64", "Sandbox.exe"], string.Empty);
        var project = Path.Combine(temporary.Path, "CryGame");
        CreateFile(
            project,
            "Game.cryproject",
            "{ \"info\": { \"name\": \"Cry Game\" }, \"require\": { \"engine\": \"missing-engine\" } }");
        var provider = new CryEngineModule();
        var snapshot = await provider.DiscoverAsync(new LocalEngineDiscoveryRequest(
            [engineRoot],
            [project]));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            provider.CreateProjectLaunch(Assert.Single(snapshot.Projects), snapshot.Installations));

        Assert.Contains("missing-engine", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CryEngineSupportsProjectWithColocatedEngineRequirement()
    {
        using var temporary = new TemporaryDirectory();
        var engineRoot = Path.Combine(temporary.Path, "PortableCryEngine");
        var editor = CreateFile(engineRoot, ["bin", "win_x64", "Sandbox.exe"], string.Empty);
        var project = Path.Combine(engineRoot, "Projects", "PortableGame");
        CreateFile(
            project,
            "Portable.cryproject",
            "{ \"info\": { \"name\": \"Portable Game\" }, \"require\": { \"engine\": \".\" } }");
        var provider = new CryEngineModule();
        var snapshot = await provider.DiscoverAsync(new LocalEngineDiscoveryRequest(
            [engineRoot],
            [Path.Combine(engineRoot, "Projects")]));

        var launch = provider.CreateProjectLaunch(
            Assert.Single(snapshot.Projects),
            snapshot.Installations);

        Assert.Equal(editor, launch.ExecutablePath);
    }

    [Fact]
    public async Task CryEngineIgnoresMalformedProjectDescriptor()
    {
        using var temporary = new TemporaryDirectory();
        var project = Path.Combine(temporary.Path, "BrokenCryGame");
        CreateFile(project, "Broken.cryproject", "{ invalid json");
        var provider = new CryEngineModule();

        var snapshot = await provider.DiscoverAsync(new LocalEngineDiscoveryRequest([], [project]));

        Assert.Empty(snapshot.Projects);
        Assert.Contains(snapshot.Warnings, warning =>
            warning.Contains("invalide", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SharedScannerSkipsGeneratedDirectoriesAndReportsBounds()
    {
        using var temporary = new TemporaryDirectory();
        var root = Directory.CreateDirectory(Path.Combine(temporary.Path, "Projects")).FullName;
        var valid = Directory.CreateDirectory(Path.Combine(root, "ValidProject")).FullName;
        File.WriteAllText(Path.Combine(valid, "project.godot"), string.Empty);
        var generated = Directory.CreateDirectory(Path.Combine(root, "Library", "NestedProject")).FullName;
        File.WriteAllText(Path.Combine(generated, "project.godot"), string.Empty);

        var scan = BoundedProjectDirectoryScanner.Find(
            [root],
            directory => File.Exists(Path.Combine(directory, "project.godot")));
        var bounded = BoundedProjectDirectoryScanner.Find(
            [root],
            _ => false,
            maximumDirectories: 1);

        Assert.Equal(valid, Assert.Single(scan.ProjectDirectories));
        Assert.True(bounded.IsPartial);
    }

    private static string CreateFile(
        string root,
        params string[] segments) =>
        CreateFile(root, segments, string.Empty);

    private static string CreateFile(
        string root,
        string segment,
        string content) =>
        CreateFile(root, [segment], content);

    private static string CreateFile(
        string root,
        string first,
        string second,
        string content) =>
        CreateFile(root, [first, second], content);

    private static string CreateFile(
        string root,
        string first,
        string second,
        string third,
        string fourth) =>
        CreateFile(root, [first, second, third, fourth], string.Empty);

    private static string CreateFile(string root, string[] segments, string content)
    {
        var path = segments.Aggregate(root, Path.Combine);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return Path.GetFullPath(path);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CyEngineHub-ProviderTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
