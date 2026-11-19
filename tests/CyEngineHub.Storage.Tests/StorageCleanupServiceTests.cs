using CyEngineHub.Storage.Cleanup;
using CyEngineHub.Storage.Models;

namespace CyEngineHub.Storage.Tests;

public sealed class StorageCleanupServiceTests
{
    [Fact]
    public async Task SafeCleanDeletesOnlyExactRegenerableDirectory()
    {
        using var directory = new StorageTestDirectory();
        var target = directory.CreateDirectory("Intermediate");
        var file = directory.CreateFile(Path.Combine("Intermediate", "Nested", "data.bin"), 32);
        File.SetAttributes(file, File.GetAttributes(file) | FileAttributes.ReadOnly);
        var service = new StorageCleanupService();
        var plan = service.Prepare(CreateEntry(
            StorageCategory.ProjectIntermediate,
            directory.Root,
            target,
            isSafe: true));

        var result = await service.ExecuteAsync(plan);

        Assert.True(result.TargetRemoved);
        Assert.False(Directory.Exists(target));
        Assert.True(Directory.Exists(directory.Root));
    }

    [Fact]
    public void PrepareRejectsNonCleanableCategory()
    {
        using var directory = new StorageTestDirectory();
        var service = new StorageCleanupService();

        var exception = Assert.Throws<StorageException>(() => service.Prepare(CreateEntry(
            StorageCategory.VaultCache,
            directory.Root,
            directory.Root,
            isSafe: false)));

        Assert.Equal("PrepareCleanup", exception.Operation);
    }

    [Fact]
    public async Task PrepareAndExecuteRejectLinkedParentOfMissingTarget()
    {
        using var directory = new StorageTestDirectory();
        var target = directory.CreateDirectory("Saved", "Logs");
        var service = new StorageCleanupService();
        var entry = CreateEntry(StorageCategory.ProjectLogs, directory.Root, target, isSafe: true);
        var plan = service.Prepare(entry);
        var saved = Path.GetDirectoryName(target)!;
        Directory.Move(saved, Path.Combine(directory.Root, "OriginalSaved"));
        var empty = directory.CreateDirectory("Empty");
        if (OperatingSystem.IsWindows())
        {
            // Directory junctions work on Windows without symbolic-link privileges.
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            start.Environment["CYHUB_TEST_LINK"] = saved;
            start.Environment["CYHUB_TEST_TARGET"] = empty;
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command",
                "New-Item -ItemType Junction -Path $env:CYHUB_TEST_LINK -Target $env:CYHUB_TEST_TARGET -ErrorAction Stop | Out-Null" })
                start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!;
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }
        else Directory.CreateSymbolicLink(saved, empty);

        try
        {
            Assert.Equal(saved, Assert.Throws<StorageException>(() => service.Prepare(entry)).Path);
            Assert.Equal(saved, (await Assert.ThrowsAsync<StorageException>(() => service.ExecuteAsync(plan))).Path);
        }
        finally
        {
            Directory.Delete(saved, recursive: false);
        }
    }

    [Fact]
    public void PrepareRejectsPathThatDoesNotExactlyMatchCategory()
    {
        using var directory = new StorageTestDirectory();
        var target = directory.CreateDirectory("Saved");
        var service = new StorageCleanupService();

        var exception = Assert.Throws<StorageException>(() => service.Prepare(CreateEntry(
            StorageCategory.ProjectIntermediate,
            directory.Root,
            target,
            isSafe: true)));

        Assert.Equal("ValidateCleanupTarget", exception.Operation);
        Assert.True(Directory.Exists(target));
    }

    private static StorageEntry CreateEntry(
        StorageCategory category,
        string ownerRoot,
        string path,
        bool isSafe) => new(
        "ENTRY",
        category,
        StorageEntryRole.Generated,
        category.ToString(),
        "Test Project",
        ownerRoot,
        path,
        32,
        1,
        2,
        DateTimeOffset.UtcNow,
        StorageHealth.Ready,
        isSafe,
        "Analyse complète.");
}
