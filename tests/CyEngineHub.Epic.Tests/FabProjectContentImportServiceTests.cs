using System.Text.Json;
using CyEngineHub.Fab.Import;

namespace CyEngineHub.Epic.Tests;

public sealed class FabProjectContentImportServiceTests
{
    [Fact]
    public async Task ImportPreservesContentLayoutAndBacksUpConflicts()
    {
        using var temporary = new TemporaryDirectory();
        var sourceRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "FabPack"));
        var sourceContent = Directory.CreateDirectory(Path.Combine(
            sourceRoot.FullName,
            "Content",
            "EnvironmentPack"));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot.FullName, "FabPack.uproject"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Asset.uasset"), "new-asset");
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Overview.umap"), "new-map");
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Ignored.exe"), "not-copied");

        var targetRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "TargetProject"));
        var targetProject = Path.Combine(targetRoot.FullName, "TargetProject.uproject");
        await File.WriteAllTextAsync(targetProject, "{}");
        var targetPack = Directory.CreateDirectory(Path.Combine(
            targetRoot.FullName,
            "Content",
            "EnvironmentPack"));
        var existing = Path.Combine(targetPack.FullName, "Asset.uasset");
        await File.WriteAllTextAsync(existing, "old-asset");
        var service = new FabProjectContentImportService();

        var plan = await service.PrepareAsync(sourceRoot.FullName, targetProject);

        Assert.Equal(2, plan.Files.Count);
        Assert.Equal(1, plan.ConflictCount);
        Assert.Equal(1, plan.SkippedFileCount);
        var result = await service.ExecuteAsync(plan);

        Assert.Equal(2, result.ImportedFileCount);
        Assert.Equal(1, result.ReplacedFileCount);
        Assert.Equal("new-asset", await File.ReadAllTextAsync(existing));
        Assert.Equal(
            "new-map",
            await File.ReadAllTextAsync(Path.Combine(targetPack.FullName, "Overview.umap")));
        Assert.False(File.Exists(Path.Combine(targetPack.FullName, "Ignored.exe")));
        Assert.NotNull(result.BackupDirectory);
        Assert.Equal(
            "old-asset",
            await File.ReadAllTextAsync(Path.Combine(
                result.BackupDirectory,
                "EnvironmentPack",
                "Asset.uasset")));
        Assert.False(Directory.Exists(Path.Combine(
            targetRoot.FullName,
            "Saved",
            "CyEngineHubFabStaging",
            plan.Id.ToString("N"))));
    }

    [Fact]
    public async Task ExecuteRejectsSourceChangedAfterConfirmation()
    {
        using var temporary = new TemporaryDirectory();
        var sourceContent = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "FabPack",
            "Content"));
        var asset = Path.Combine(sourceContent.FullName, "Asset.uasset");
        await File.WriteAllTextAsync(asset, "initial");
        var targetRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Target"));
        var targetProject = Path.Combine(targetRoot.FullName, "Target.uproject");
        await File.WriteAllTextAsync(targetProject, "{}");
        var service = new FabProjectContentImportService();
        var plan = await service.PrepareAsync(sourceContent.FullName, targetProject);
        await File.WriteAllTextAsync(asset, "changed-after-plan");

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => service.ExecuteAsync(plan));

        Assert.Equal("ExecuteImport", exception.Operation);
        Assert.False(File.Exists(Path.Combine(targetRoot.FullName, "Content", "Asset.uasset")));
    }

    [Fact]
    public async Task PrepareRejectsSourceInsideTargetProject()
    {
        using var temporary = new TemporaryDirectory();
        var targetRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Target"));
        var targetProject = Path.Combine(targetRoot.FullName, "Target.uproject");
        await File.WriteAllTextAsync(targetProject, "{}");
        var sourceContent = Directory.CreateDirectory(Path.Combine(
            targetRoot.FullName,
            "DownloadedFabPack",
            "Content"));
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Asset.uasset"), "asset");

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => new FabProjectContentImportService().PrepareAsync(
                sourceContent.FullName,
                targetProject));

        Assert.Equal("ValidateRoots", exception.Operation);
    }

    [Fact]
    public async Task PrepareRejectsContentFromNewerEngineVersion()
    {
        using var temporary = new TemporaryDirectory();
        var sourceRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "FabPack"));
        var sourceContent = Directory.CreateDirectory(Path.Combine(sourceRoot.FullName, "Content"));
        await File.WriteAllTextAsync(
            Path.Combine(sourceRoot.FullName, "FabPack.uproject"),
            "{\"EngineAssociation\":\"5.6\"}");
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Asset.uasset"), "asset");
        var targetRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Target"));
        var targetProject = Path.Combine(targetRoot.FullName, "Target.uproject");
        await File.WriteAllTextAsync(targetProject, "{\"EngineAssociation\":\"5.5\"}");

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => new FabProjectContentImportService().PrepareAsync(
                sourceRoot.FullName,
                targetProject));

        Assert.Equal("ValidateCompatibility", exception.Operation);
    }

    [Fact]
    public async Task ReceiptHistoryAndRollbackRestoreTheProject()
    {
        using var temporary = new TemporaryDirectory();
        var sourceContent = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "FabPack",
            "Content",
            "Props"));
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Chair.uasset"), "new-chair");
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Table.uasset"), "new-table");
        var targetRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Target"));
        var targetProject = Path.Combine(targetRoot.FullName, "Target.uproject");
        await File.WriteAllTextAsync(targetProject, "{}");
        var targetProps = Directory.CreateDirectory(Path.Combine(
            targetRoot.FullName,
            "Content",
            "Props"));
        var chair = Path.Combine(targetProps.FullName, "Chair.uasset");
        var table = Path.Combine(targetProps.FullName, "Table.uasset");
        await File.WriteAllTextAsync(chair, "old-chair");
        var service = new FabProjectContentImportService();

        var import = await service.ExecuteAsync(
            await service.PrepareAsync(sourceContent.Parent!.FullName, targetProject));
        var history = await service.LoadHistoryAsync([targetProject]);

        var receipt = Assert.Single(history);
        Assert.Equal(import.Receipt.Id, receipt.Id);
        Assert.True(receipt.IsApplied);
        Assert.True(File.Exists(receipt.ReceiptPath));
        Assert.Equal("new-chair", await File.ReadAllTextAsync(chair));
        Assert.Equal("new-table", await File.ReadAllTextAsync(table));

        var plan = await service.PrepareRollbackAsync(receipt);
        var rollback = await service.ExecuteRollbackAsync(plan);

        Assert.Equal(1, rollback.RestoredFileCount);
        Assert.Equal(1, rollback.DeletedFileCount);
        Assert.False(rollback.Receipt.IsApplied);
        Assert.Equal("old-chair", await File.ReadAllTextAsync(chair));
        Assert.False(File.Exists(table));
        var reloadedReceipt = Assert.Single(await service.LoadHistoryAsync([targetProject]));
        Assert.False(reloadedReceipt.IsApplied);
    }

    [Fact]
    public async Task LegacyReceiptHistoryRemainsVisibleAndMigratesOnRollback()
    {
        using var temporary = new TemporaryDirectory();
        var sourceContent = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "FabPack",
            "Content"));
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Asset.uasset"), "asset");
        var targetRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Target"));
        var targetProject = Path.Combine(targetRoot.FullName, "Target.uproject");
        await File.WriteAllTextAsync(targetProject, "{}");
        var service = new FabProjectContentImportService();
        var import = await service.ExecuteAsync(
            await service.PrepareAsync(sourceContent.FullName, targetProject));
        var legacyHistory = Directory.CreateDirectory(Path.Combine(
            targetRoot.FullName,
            "Saved",
            "CyUEHubFabImports"));
        var legacyReceiptPath = Path.Combine(
            legacyHistory.FullName,
            Path.GetFileName(import.Receipt.ReceiptPath));
        File.Move(import.Receipt.ReceiptPath, legacyReceiptPath);

        var legacyReceipt = Assert.Single(await service.LoadHistoryAsync([targetProject]));

        Assert.Equal(legacyReceiptPath, legacyReceipt.ReceiptPath);
        var rollback = await service.ExecuteRollbackAsync(
            await service.PrepareRollbackAsync(legacyReceipt));
        Assert.False(rollback.Receipt.IsApplied);
        Assert.Contains(
            "CyEngineHubFabImports",
            rollback.Receipt.ReceiptPath,
            StringComparison.Ordinal);
        Assert.True(File.Exists(rollback.Receipt.ReceiptPath));
        Assert.False(File.Exists(legacyReceiptPath));
    }

    [Fact]
    public async Task RollbackRejectsAReceiptWithAForgedReceiptPath()
    {
        using var temporary = new TemporaryDirectory();
        var sourceContent = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "FabPack",
            "Content"));
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Asset.uasset"), "asset");
        var targetRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Target"));
        var targetProject = Path.Combine(targetRoot.FullName, "Target.uproject");
        await File.WriteAllTextAsync(targetProject, "{}");
        var service = new FabProjectContentImportService();
        var import = await service.ExecuteAsync(
            await service.PrepareAsync(sourceContent.FullName, targetProject));
        var forgedReceipt = import.Receipt with
        {
            ReceiptPath = Path.Combine(temporary.Path, "forged.json"),
        };

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => service.PrepareRollbackAsync(forgedReceipt));

        Assert.Equal("PrepareRollback", exception.Operation);
        Assert.True(import.Receipt.IsApplied);
        Assert.True(File.Exists(import.Receipt.ReceiptPath));
    }

    [Fact]
    public async Task RollbackIsBlockedWhenImportedFileWasModified()
    {
        using var temporary = new TemporaryDirectory();
        var sourceContent = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "FabPack",
            "Content"));
        await File.WriteAllTextAsync(Path.Combine(sourceContent.FullName, "Asset.uasset"), "imported");
        var targetRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Target"));
        var targetProject = Path.Combine(targetRoot.FullName, "Target.uproject");
        await File.WriteAllTextAsync(targetProject, "{}");
        var service = new FabProjectContentImportService();
        var import = await service.ExecuteAsync(
            await service.PrepareAsync(sourceContent.FullName, targetProject));
        var installed = Path.Combine(targetRoot.FullName, "Content", "Asset.uasset");
        await File.WriteAllTextAsync(installed, "user-change");

        var exception = await Assert.ThrowsAsync<FabContentOperationException>(
            () => service.PrepareRollbackAsync(import.Receipt));

        Assert.Equal("PrepareRollback", exception.Operation);
        Assert.Contains("changé", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("user-change", await File.ReadAllTextAsync(installed));
        Assert.True(import.Receipt.IsApplied);
    }

    [Fact]
    public async Task HistoryIgnoresCorruptReceiptFiles()
    {
        using var temporary = new TemporaryDirectory();
        var targetRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Target"));
        var targetProject = Path.Combine(targetRoot.FullName, "Target.uproject");
        await File.WriteAllTextAsync(targetProject, "{}");
        var historyDirectory = Directory.CreateDirectory(Path.Combine(
            targetRoot.FullName,
            "Saved",
            "CyEngineHubFabImports"));
        await File.WriteAllTextAsync(Path.Combine(historyDirectory.FullName, "invalid.json"), "{not-json");
        var malformedId = Guid.NewGuid();
        var malformed = new FabImportReceipt(
            malformedId,
            temporary.Path,
            "Malformed",
            targetProject,
            null!,
            [new FabImportedFileReceipt("Asset.uasset", false, 1, DateTimeOffset.UtcNow, new string('0', 64), null, null, null)],
            1,
            null,
            "unknown",
            DateTimeOffset.UtcNow,
            null);
        await File.WriteAllTextAsync(
            Path.Combine(historyDirectory.FullName, $"{malformedId:N}.json"),
            JsonSerializer.Serialize(malformed));

        var history = await new FabProjectContentImportService().LoadHistoryAsync([targetProject]);

        Assert.Empty(history);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CyEngineHub-FabImportTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
