using CyEngineHub.Fab.Import;

namespace CyEngineHub.Epic.Tests;

public sealed class PortableDependencyImportTests
{
    [Theory]
    [InlineData("Unreal")]
    [InlineData("Unity")]
    [InlineData("Godot")]
    [InlineData("CryEngine")]
    public async Task ImportAndRollbackPreserveModelCompanions(string engine)
    {
        var root = Path.Combine(Path.GetTempPath(), "CyHubPortableImportQa-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "Pack", "Content")).FullName;
            string[] names = ["mesh.gltf", "mesh.bin", "mesh.obj", "mesh.mtl"];
            foreach (var name in names) await File.WriteAllTextAsync(Path.Combine(source, name), "qa-original-" + name);
            var project = Directory.CreateDirectory(Path.Combine(root, "Project")).FullName;
            string destination;
            Func<Task> rollback;
            if (engine == "Unreal")
            {
                // This importer installs native packs; raw-only packs require the editor import pipeline.
                await File.WriteAllTextAsync(Path.Combine(source, "Native.uasset"), "native fixture, never opened");
                var marker = Path.Combine(project, "Qa.uproject"); await File.WriteAllTextAsync(marker, "{}");
                var service = new FabProjectContentImportService();
                var plan = await service.PrepareAsync(Path.GetDirectoryName(source)!, marker);
                var result = await service.ExecuteAsync(plan); destination = plan.TargetContentDirectory;
                rollback = async () => { await service.ExecuteRollbackAsync(await service.PrepareRollbackAsync(result.Receipt)); };
            }
            else if (engine == "Unity")
            {
                Directory.CreateDirectory(Path.Combine(project, "Assets")); Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
                await File.WriteAllTextAsync(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: 6000.0.0f1");
                var service = new UnityFabAssetImportService(); var plan = await service.PrepareAsync(source, project);
                var result = await service.ExecuteAsync(plan); destination = plan.TargetImportDirectory;
                rollback = async () => { await service.ExecuteRollbackAsync(await service.PrepareRollbackAsync(result.Receipt)); };
            }
            else if (engine == "Godot")
            {
                await File.WriteAllTextAsync(Path.Combine(project, "project.godot"), "config_version=5\n[application]\nconfig/name=\"QA\"");
                var service = new GodotFabAssetImportService(); var plan = await service.PrepareAsync(source, project);
                var result = await service.ExecuteAsync(plan); destination = plan.TargetImportDirectory;
                rollback = async () => { await service.ExecuteRollbackAsync(await service.PrepareRollbackAsync(result.Receipt)); };
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(project, "Assets"));
                await File.WriteAllTextAsync(Path.Combine(project, "Qa.cryproject"), "{\"content\":{\"assets\":[\"Assets\"]}}");
                var service = new CryEngineFabAssetImportService(); var plan = await service.PrepareAsync(source, project);
                var result = await service.ExecuteAsync(plan); destination = plan.TargetImportDirectory;
                rollback = async () => { await service.ExecuteRollbackAsync(await service.PrepareRollbackAsync(result.Receipt)); };
            }
            foreach (var name in names) Assert.Equal("qa-original-" + name, await File.ReadAllTextAsync(Path.Combine(destination, name)));
            await rollback();
            foreach (var name in names)
            {
                Assert.False(File.Exists(Path.Combine(destination, name)));
                Assert.Equal("qa-original-" + name, await File.ReadAllTextAsync(Path.Combine(source, name)));
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
