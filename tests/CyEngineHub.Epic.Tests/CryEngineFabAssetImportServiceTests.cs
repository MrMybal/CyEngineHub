using CyEngineHub.Fab.Import;

namespace CyEngineHub.Epic.Tests;

public sealed class CryEngineFabAssetImportServiceTests
{
    [Fact]
    public async Task ImportCopiesOnlyPortableAssetsIntoRecognizedProject()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack", "Models"));
        var sourceProject = Path.Combine(temporary.Path, "NaturePack", "Nature.cryproject");
        await File.WriteAllTextAsync(
            sourceProject,
            "{ \"content\": { \"assets\": [ \"Models\" ] }, \"info\": { \"name\": \"Nature\" } }");
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Preview.tscn"), "[gd_scene]");
        var target = await CreateCryEngineProjectAsync(temporary.Path);
        var service = new CryEngineFabAssetImportService();

        var plan = await service.PrepareAsync(sourceProject, target);
        var result = await service.ExecuteAsync(plan);

        Assert.Single(plan.Files);
        Assert.Equal(2, plan.SkippedFileCount);
        Assert.Equal("mesh", await File.ReadAllTextAsync(Path.Combine(plan.TargetImportDirectory, "Models", "Tree.glb")));
        Assert.False(File.Exists(Path.Combine(plan.TargetImportDirectory, "Models", "Preview.tscn")));
        Assert.True(File.Exists(result.Receipt.ReceiptPath));
        Assert.DoesNotContain(Path.Combine(target, "Assets", "CyEngineHubImports"), result.Receipt.ReceiptPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PrepareRejectsCryEngineScriptsAndExtensions()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "UnsafePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Model.glb"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Gameplay.cpp"), "void Run() {}");
        var target = await CreateCryEngineProjectAsync(temporary.Path);

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => new CryEngineFabAssetImportService().PrepareAsync(source.FullName, target));

        Assert.Equal("InventoryCryEnginePack", exception.Operation);
        Assert.Contains("bloqué", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReplacementRollbackRestoresPreviousImport()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "new");
        var target = await CreateCryEngineProjectAsync(temporary.Path);
        var installed = Directory.CreateDirectory(Path.Combine(target, "Assets", "CyEngineHubImports", "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(installed.FullName, "Tree.glb"), "old");
        var service = new CryEngineFabAssetImportService();

        var import = await service.ExecuteAsync(await service.PrepareAsync(source.FullName, target));
        var rollback = await service.ExecuteRollbackAsync(await service.PrepareRollbackAsync(import.Receipt));

        Assert.True(rollback.RestoredPreviousImport);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(installed.FullName, "Tree.glb")));
        Assert.False(rollback.Receipt.IsApplied);
    }

    [Fact]
    public async Task RollbackRejectsModifiedImportedAsset()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "mesh");
        var target = await CreateCryEngineProjectAsync(temporary.Path);
        var service = new CryEngineFabAssetImportService();
        var import = await service.ExecuteAsync(await service.PrepareAsync(source.FullName, target));
        var installed = Path.Combine(import.Receipt.TargetImportDirectory, "Tree.glb");
        await File.WriteAllTextAsync(installed, "user-change");

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => service.PrepareRollbackAsync(import.Receipt));

        Assert.Equal("ValidateCryEngineImport", exception.Operation);
        Assert.Equal("user-change", await File.ReadAllTextAsync(installed));
    }

    [Fact]
    public async Task RollbackRejectsModifiedBackup()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "new");
        var target = await CreateCryEngineProjectAsync(temporary.Path);
        var installed = Directory.CreateDirectory(Path.Combine(target, "Assets", "CyEngineHubImports", "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(installed.FullName, "Tree.glb"), "old");
        var service = new CryEngineFabAssetImportService();
        var import = await service.ExecuteAsync(await service.PrepareAsync(source.FullName, target));
        await File.WriteAllTextAsync(Path.Combine(import.Receipt.BackupDirectory!, "Tree.glb"), "tampered");

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => service.PrepareRollbackAsync(import.Receipt));

        Assert.Equal("PrepareCryEngineRollback", exception.Operation);
        Assert.Contains("backup", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RollbackRejectsForgedTargetDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var source = Directory.CreateDirectory(Path.Combine(temporary.Path, "NaturePack"));
        await File.WriteAllTextAsync(Path.Combine(source.FullName, "Tree.glb"), "mesh");
        var target = await CreateCryEngineProjectAsync(temporary.Path);
        var service = new CryEngineFabAssetImportService();
        var import = await service.ExecuteAsync(await service.PrepareAsync(source.FullName, target));
        var unrelated = Directory.CreateDirectory(Path.Combine(target, "Assets", "CyEngineHubImports", "Unrelated"));
        await File.WriteAllTextAsync(Path.Combine(unrelated.FullName, "Tree.glb"), "mesh");
        var forged = import.Receipt with { TargetImportDirectory = unrelated.FullName };

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => service.PrepareRollbackAsync(forged));

        Assert.Equal("ValidateCryEngineReceipt", exception.Operation);
        Assert.True(Directory.Exists(unrelated.FullName));
    }

    private static async Task<string> CreateCryEngineProjectAsync(string root)
    {
        var project = Directory.CreateDirectory(Path.Combine(root, "CryEngineTarget"));
        Directory.CreateDirectory(Path.Combine(project.FullName, "Assets"));
        await File.WriteAllTextAsync(
            Path.Combine(project.FullName, "Target.cryproject"),
            "{ \"content\": { \"assets\": [ \"Assets\" ] }, \"info\": { \"name\": \"Target\" } }");
        return project.FullName;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"CyEngineHub-CryEngineFabTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
