using CyEngineHub.Fab.Import;

namespace CyEngineHub.Epic.Tests;

public sealed class UnityFabAssetImportServiceTests
{
    [Fact]
    public async Task ImportCopiesAssetsAndPreservesMetaFiles()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack", "Assets", "Models"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb.meta"), "guid: stable");
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "notes.unknown"), "skip");
        var project = await CreateUnityProjectAsync(temporary.Path);
        var service = new UnityFabAssetImportService();

        var plan = await service.PrepareAsync(Path.Combine(temporary.Path, "NaturePack"), project);
        var result = await service.ExecuteAsync(plan);

        Assert.Equal(2, result.ImportedFileCount);
        Assert.Equal(1, plan.SkippedFileCount);
        Assert.False(plan.ReplacesExistingImport);
        Assert.Equal("mesh", await File.ReadAllTextAsync(Path.Combine(plan.TargetImportDirectory, "Models", "Tree.glb")));
        Assert.Equal("guid: stable", await File.ReadAllTextAsync(Path.Combine(plan.TargetImportDirectory, "Models", "Tree.glb.meta")));
        Assert.True(File.Exists(result.Receipt.ReceiptPath));
        Assert.DoesNotContain(Path.Combine(project, "Assets"), result.Receipt.ReceiptPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplacementRollbackRestoresPreviousImport()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "new");
        var project = await CreateUnityProjectAsync(temporary.Path);
        var target = Directory.CreateDirectory(Path.Combine(project, "Assets", "CyEngineHubImports", "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(target.FullName, "Tree.glb"), "old");
        await File.WriteAllTextAsync(target.FullName + ".meta", "guid: folder");
        var service = new UnityFabAssetImportService();

        var import = await service.ExecuteAsync(await service.PrepareAsync(source.FullName, project));
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(target.FullName, "Tree.glb")));
        Assert.NotNull(import.Receipt.BackupDirectory);

        var rollback = await service.ExecuteRollbackAsync(await service.PrepareRollbackAsync(import.Receipt));

        Assert.True(rollback.RestoredPreviousImport);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(target.FullName, "Tree.glb")));
        Assert.Equal("guid: folder", await File.ReadAllTextAsync(target.FullName + ".meta"));
        Assert.False(rollback.Receipt.IsApplied);
    }

    [Fact]
    public async Task PrepareRejectsPackContainingCode()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "UnsafePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Model.fbx"), "model");
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Installer.cs"), "class Installer {}");
        var project = await CreateUnityProjectAsync(temporary.Path);

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => new UnityFabAssetImportService().PrepareAsync(source.FullName, project));

        Assert.Equal("InventoryUnityPack", exception.Operation);
        Assert.Contains("bloqué", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RollbackRejectsModifiedImportedAsset()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "mesh");
        var project = await CreateUnityProjectAsync(temporary.Path);
        var service = new UnityFabAssetImportService();
        var import = await service.ExecuteAsync(await service.PrepareAsync(source.FullName, project));
        var installed = Path.Combine(import.Receipt.TargetImportDirectory, "Tree.glb");
        await File.WriteAllTextAsync(installed, "user-change");

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => service.PrepareRollbackAsync(import.Receipt));

        Assert.Equal("ValidateUnityImport", exception.Operation);
        Assert.Equal("user-change", await File.ReadAllTextAsync(installed));
    }

    [Fact]
    public async Task RollbackAllowsUnityGeneratedMetaFilesAndRemovesNewImport()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack", "Models"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "mesh");
        var project = await CreateUnityProjectAsync(temporary.Path);
        var service = new UnityFabAssetImportService();
        var import = await service.ExecuteAsync(await service.PrepareAsync(Path.Combine(temporary.Path, "NaturePack"), project));
        await File.WriteAllTextAsync(Path.Combine(import.Receipt.TargetImportDirectory, "Models.meta"), "generated folder meta");
        await File.WriteAllTextAsync(Path.Combine(import.Receipt.TargetImportDirectory, "Models", "Tree.glb.meta"), "generated asset meta");
        await File.WriteAllTextAsync(import.Receipt.TargetImportDirectory + ".meta", "generated root meta");

        var result = await service.ExecuteRollbackAsync(await service.PrepareRollbackAsync(import.Receipt));

        Assert.False(result.RestoredPreviousImport);
        Assert.False(Directory.Exists(import.Receipt.TargetImportDirectory));
        Assert.False(File.Exists(import.Receipt.TargetImportDirectory + ".meta"));
    }

    [Fact]
    public async Task RollbackRejectsForgedTargetDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "mesh");
        var project = await CreateUnityProjectAsync(temporary.Path);
        var service = new UnityFabAssetImportService();
        var import = await service.ExecuteAsync(await service.PrepareAsync(source.FullName, project));
        var unrelated = Directory.CreateDirectory(Path.Combine(project, "Assets", "CyEngineHubImports", "Unrelated"));
        await File.WriteAllTextAsync(Path.Combine(unrelated.FullName, "Tree.glb"), "mesh");
        var forged = import.Receipt with { TargetImportDirectory = unrelated.FullName };

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => service.PrepareRollbackAsync(forged));

        Assert.Equal("ValidateUnityReceipt", exception.Operation);
        Assert.True(Directory.Exists(unrelated.FullName));
        Assert.True(import.Receipt.IsApplied);
    }

    [Fact]
    public async Task RollbackRejectsModifiedBackupTree()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "new");
        var project = await CreateUnityProjectAsync(temporary.Path);
        var target = Directory.CreateDirectory(Path.Combine(project, "Assets", "CyEngineHubImports", "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(target.FullName, "Tree.glb"), "old");
        var service = new UnityFabAssetImportService();
        var import = await service.ExecuteAsync(await service.PrepareAsync(source.FullName, project));
        await File.WriteAllTextAsync(Path.Combine(import.Receipt.BackupDirectory!, "Tree.glb"), "tampered");

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => service.PrepareRollbackAsync(import.Receipt));

        Assert.Equal("PrepareUnityRollback", exception.Operation);
        Assert.Contains("backup", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(target.FullName, "Tree.glb")));
    }

    private static async Task<string> CreateUnityProjectAsync(string root)
    {
        var project = Directory.CreateDirectory(Path.Combine(root, "UnityTarget"));
        Directory.CreateDirectory(Path.Combine(project.FullName, "Assets"));
        var settings = Directory.CreateDirectory(Path.Combine(project.FullName, "ProjectSettings"));
        await File.WriteAllTextAsync(Path.Combine(settings.FullName, "ProjectVersion.txt"), "m_EditorVersion: 6000.0.0f1");
        return project.FullName;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"CyEngineHub-UnityFabTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
