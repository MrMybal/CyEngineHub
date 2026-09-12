using CyEngineHub.Core.Processes;
using CyEngineHub.Engines;

namespace CyEngineHub.Extensions.Tests;

// Executes the production planner and tool logic in-process; never starts an installed editor.
public sealed class InterEngineBridgeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyHubInterEngineQa-" + Guid.NewGuid().ToString("N"));
    public InterEngineBridgeTests() => Directory.CreateDirectory(_root);
    public static IEnumerable<object[]> Directions()
    {
        EngineFamily[] engines = [EngineFamily.Unreal, EngineFamily.Unity, EngineFamily.Godot, EngineFamily.CryEngine];
        foreach (var source in engines) foreach (var target in engines) if (source != target) yield return new object[] { source, target };
    }

    [Theory]
    [MemberData(nameof(Directions))]
    public async Task AllDirectionsPreservePortableDependenciesAndExcludeNativeCode(EngineFamily sourceEngine, EngineFamily targetEngine)
    {
        var (service, extension) = await Service();
        var source = Directory.CreateDirectory(Path.Combine(_root, "Source", "Models")).FullName;
        await File.WriteAllTextAsync(Path.Combine(source, "triangle.gltf"), "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"triangle.bin\",\"byteLength\":36}]}");
        await File.WriteAllBytesAsync(Path.Combine(source, "triangle.bin"), new byte[36]);
        await File.WriteAllTextAsync(Path.Combine(source, "triangle.obj"), "mtllib triangle.mtl\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        await File.WriteAllTextAsync(Path.Combine(source, "triangle.mtl"), "newmtl qa\nKd 1 0 0\n");
        await File.WriteAllTextAsync(Path.Combine(source, "native.uasset"), "not portable");
        await File.WriteAllTextAsync(Path.Combine(source, "script.cs"), "not portable");
        var plan = await service.PrepareAsync(extension, Path.GetDirectoryName(source)!, Project(targetEngine), sourceEngine, targetEngine);
        var result = await service.ExecuteAsync(plan);
        Assert.True(result.ProcessResult.Succeeded);
        foreach (var name in new[] { "triangle.gltf", "triangle.bin", "triangle.obj", "triangle.mtl" })
        {
            var destination = Path.Combine(plan.OutputPath, "Models", name);
            Assert.True(File.Exists(destination), "Portable dependency missing: " + name);
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(source, name)), await File.ReadAllBytesAsync(destination));
        }
        Assert.Equal(4, plan.InputSummary.TransferableFileCount);
        Assert.False(File.Exists(Path.Combine(plan.OutputPath, "Models", "native.uasset")));
        Assert.False(File.Exists(Path.Combine(plan.OutputPath, "Models", "script.cs")));
        Assert.Empty(Directory.GetDirectories(plan.OutputPath, ".cyenginehub-bridge-*"));
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("mutated")]
    [InlineData("canceled")]
    public async Task InvalidPlanCannotPublishFiles(string mode)
    {
        var (service, extension) = await Service();
        var source = Path.Combine(_root, "mesh.obj"); await File.WriteAllTextAsync(source, "v 0 0 0");
        var plan = await service.PrepareAsync(extension, source, Project(EngineFamily.Unity), EngineFamily.Unreal, EngineFamily.Unity);
        if (mode == "expired") plan = plan with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) };
        if (mode == "mutated") await File.AppendAllTextAsync(source, "\nf 1 2 3");
        using var cancel = new CancellationTokenSource(); if (mode == "canceled") cancel.Cancel();
        await Assert.ThrowsAnyAsync<Exception>(() => service.ExecuteAsync(plan, cancellationToken: cancel.Token));
        Assert.False(Directory.Exists(plan.OutputPath));
    }

    [Fact]
    public async Task ExistingOutputIsNotOverwrittenAndSourceRemainsIntact()
    {
        var (service, extension) = await Service();
        var source = Path.Combine(_root, "mesh.obj"); await File.WriteAllTextAsync(source, "new");
        var plan = await service.PrepareAsync(extension, source, Project(EngineFamily.Godot), EngineFamily.Unreal, EngineFamily.Godot);
        Directory.CreateDirectory(plan.OutputPath); var existing = Path.Combine(plan.OutputPath, "mesh.obj");
        await File.WriteAllTextAsync(existing, "old");
        var result = await service.ExecuteAsync(plan);
        Assert.False(result.ProcessResult.Succeeded);
        Assert.Equal("old", await File.ReadAllTextAsync(existing));
        Assert.Equal("new", await File.ReadAllTextAsync(source));
    }

    [Theory]
    [InlineData(".gltf", "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"mesh.bin\"}]}")]
    [InlineData(".obj", "mtllib mesh.mtl\nv 0 0 0")]
    public async Task SingleModelWithExternalDependenciesRequiresItsPackFolder(string extension, string content)
    {
        var (service, package) = await Service();
        var input = Path.Combine(_root, "mesh" + extension); await File.WriteAllTextAsync(input, content);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PrepareAsync(package, input,
            Project(EngineFamily.Unity), EngineFamily.Unreal, EngineFamily.Unity));
        Assert.Contains("dossier du pack", error.Message, StringComparison.Ordinal);
        var output = Path.Combine(_root, "StandaloneOutput");
        var code = await CyEngineHub.AssetBridge.Tool.Program.Main(["--input", input, "--output", output, "--source-engine", "Unreal", "--target-engine", "Unity"]);
        Assert.NotEqual(0, code); Assert.False(Directory.Exists(output));
    }

    private async Task<(HubExtensionExecutionService, HubExtensionSnapshot)> Service()
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "Extension")).FullName;
        File.Copy(Path.Combine(AppContext.BaseDirectory, "BuiltInManifests", "cyenginehub.asset-bridge", "extension.json"), Path.Combine(folder, "extension.json"));
        Directory.CreateDirectory(Path.Combine(folder, "tools"));
        await File.WriteAllTextAsync(Path.Combine(folder, "tools", "CyEngineHub.AssetBridge.Tool.exe"), "identity fixture; never executed");
        var manifest = await new HubExtensionManifestParser().ParseAsync(folder);
        return (new HubExtensionExecutionService(new ToolRunner()), new(manifest, folder, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null));
    }
    private string Project(EngineFamily engine)
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, engine.ToString())).FullName;
        if (engine == EngineFamily.Unreal) File.WriteAllText(Path.Combine(root, "Qa.uproject"), "{\"FileVersion\":3}");
        if (engine == EngineFamily.Godot) File.WriteAllText(Path.Combine(root, "project.godot"), "config_version=5\n[application]\nconfig/name=\"QA\"");
        if (engine == EngineFamily.Unity)
        { Directory.CreateDirectory(Path.Combine(root, "Assets")); Directory.CreateDirectory(Path.Combine(root, "ProjectSettings")); File.WriteAllText(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.0.0f1"); }
        if (engine == EngineFamily.CryEngine)
        { Directory.CreateDirectory(Path.Combine(root, "Assets")); File.WriteAllText(Path.Combine(root, "Qa.cryproject"), "{\"info\":{\"name\":\"QA\"},\"content\":{\"assets\":[\"Assets\"]}}"); }
        return root;
    }
    private sealed class ToolRunner : IProcessRunner
    {
        public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); var start = DateTimeOffset.UtcNow;
            var code = await CyEngineHub.AssetBridge.Tool.Program.Main(request.Arguments.ToArray());
            return new(0, code, start, DateTimeOffset.UtcNow, []);
        }
    }
    public void Dispose() { Directory.Delete(_root, recursive: true); GC.SuppressFinalize(this); }
}
