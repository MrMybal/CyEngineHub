using System.IO.Compression;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;

namespace CyEngineHub.Epic.Tests;

public sealed class LibraryPackageServiceTests
{
    private sealed class TransferProgress(Action<DirectFabDownloadProgress> report) : IProgress<DirectFabDownloadProgress>
    {
        public void Report(DirectFabDownloadProgress value) => report(value);
    }

    [Fact]
    public async Task CleanupPreservesAFileEditedSinceTheInventory()
    {
        var root = Directory.CreateTempSubdirectory("CyHub-package-changed-").FullName;
        try
        {
            var payload = Path.Combine(root, "model.txt");
            await File.WriteAllTextAsync(payload, "first");
            var tree = PackageFileTree.Read(root, CancellationToken.None);
            await File.WriteAllTextAsync(payload, "updated locally");

            Assert.Throws<IOException>(() => tree.RemoveCopiedContent(root));
            Assert.Equal("updated locally", await File.ReadAllTextAsync(payload));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task InventoryRejectsDirectoryLinksBeforeArchiving()
    {
        var root = Directory.CreateTempSubdirectory("CyHub-package-inventory-").FullName;
        try
        {
            var emptyFolder = Directory.CreateDirectory(Path.Combine(root, "empty")).FullName;
            var alias = Path.Combine(root, "alias");
            if (OperatingSystem.IsWindows())
            {
                var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
                start.Environment["CYHUB_TEST_LINK"] = alias;
                start.Environment["CYHUB_TEST_TARGET"] = emptyFolder;
                foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command",
                    "New-Item -ItemType Junction -Path $env:CYHUB_TEST_LINK -Target $env:CYHUB_TEST_TARGET -ErrorAction Stop | Out-Null" })
                    start.ArgumentList.Add(argument);
                using var process = System.Diagnostics.Process.Start(start)!;
                await process.WaitForExitAsync();
                Assert.Equal(0, process.ExitCode);
            }
            else Directory.CreateSymbolicLink(alias, emptyFolder);
            try
            {
                Assert.Throws<InvalidDataException>(() => PackageFileTree.Read(root, CancellationToken.None));
                Assert.Empty(Directory.GetFiles(emptyFolder));
            }
            finally { Directory.Delete(alias); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SourceCleanupRetainsNewFilesAndThePublishedCompletePackage()
    {
        var root = Directory.CreateTempSubdirectory("CyHub-storage-cleanup-").FullName;
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName;
            Directory.CreateDirectory(Path.Combine(source, "empty-directory"));
            await File.WriteAllTextAsync(Path.Combine(source, "payload.txt"), "original");
            var destination = Path.Combine(root, "published");
            var newFile = Path.Combine(source, "new.txt");
            var reports = new List<DirectFabDownloadProgress>();
            var progress = new LibraryPackageProgress(new TransferProgress(value =>
            {
                reports.Add(value);
                if (value.ProcessedBytes > 0) File.WriteAllText(newFile, "new data");
            }), DirectFabDownloadPhase.Placement);

            var warning = await MarketplaceStorageRouter.CopyPublishAndCleanAsync(source, destination, progress, CancellationToken.None);

            Assert.NotNull(warning);
            Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(destination, "payload.txt")));
            Assert.True(Directory.Exists(Path.Combine(destination, "empty-directory")));
            Assert.Equal("new data", await File.ReadAllTextAsync(newFile));
            Assert.Empty(Directory.GetDirectories(root, "*.incoming-*"));
            Assert.All(reports, report => Assert.Equal(DirectFabDownloadPhase.Placement, report.Phase));
            Assert.Contains(reports, report => report.ProcessedBytes == 8 && report.TotalProcessingBytes == 8);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task CanceledCopyKeepsOriginalAndNeverPublishesPartialPackage()
    {
        var root = Directory.CreateTempSubdirectory("CyHub-storage-cancel-").FullName;
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName;
            var payload = Path.Combine(source, "payload.txt");
            await File.WriteAllTextAsync(payload, "unchanged");
            var destination = Path.Combine(root, "published");
            using var stop = new CancellationTokenSource();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MarketplaceStorageRouter.CopyPublishAndCleanAsync(
                source, destination, new LibraryPackageProgress(new TransferProgress(value =>
                {
                    if (value.ProcessedBytes > 0) stop.Cancel();
                }), DirectFabDownloadPhase.Placement), stop.Token));

            Assert.Equal("unchanged", await File.ReadAllTextAsync(payload));
            Assert.False(Directory.Exists(destination));
            Assert.Empty(Directory.GetDirectories(root, "*.incoming-*"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task SameVolumeMoveNeedsNoSecondAllocationAndResidentPackageIsCountedOnce()
    {
        var root = Directory.CreateTempSubdirectory("CyHub-storage-quota-").FullName;
        try
        {
            var source = Directory.CreateDirectory(Path.Combine(root, "downloads", "pack")).FullName;
            await File.WriteAllTextAsync(Path.Combine(source, "payload.txt"), "pack");
            var library = Path.Combine(root, "library");
            var profile = new MarketplaceStorageProfile("Fab", root, root, [new(library)]);
            var download = new DirectFabDownloadResult("pack", "pack", source, 1, long.MaxValue, DateTimeOffset.UtcNow);

            var placed = await MarketplaceStorageRouter.PlaceAsync(download, profile);
            Assert.Equal("pack", await File.ReadAllTextAsync(Path.Combine(placed.DestinationDirectory, "payload.txt")));
            var atQuota = profile with { Libraries = [new(library, MaximumBytes: 4)] };
            var resident = await MarketplaceStorageRouter.PlaceAsync(placed with { DownloadedBytes = 4 }, atQuota);
            Assert.Equal(placed.DestinationDirectory, resident.DestinationDirectory);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task DerivedPackagePreservesSourceAndRecordsLineage()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "CyEngineHub-derived-test-" + Guid.NewGuid().ToString("N"));
        var sourceRoot = Path.Combine(temporaryRoot, "Source");
        var stagingRoot = Path.Combine(temporaryRoot, "Staging");
        Directory.CreateDirectory(Path.Combine(sourceRoot, "Content", "Environment"));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "Content", "Environment", "Rock.uasset"), "asset-data");
        var asset = new RemoteFabAsset("asset-derived", "ue", "Rock Pack", string.Empty, "Fab", "ASSET_PACK", null, null, [], []);
        var version = new RemoteFabProjectVersion("supplier-artifact", [], ["5.5"], ["Windows"]);
        try
        {
            var packagedSource = await LibraryPackageService.PackageAsync(
                asset,
                version,
                new DirectFabDownloadResult(asset.AssetId, asset.Title, sourceRoot, 1, 10, DateTimeOffset.UtcNow),
                LibraryPackageStorageMode.ZipFastest);
            var sourceArchiveHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(
                    Path.Combine(sourceRoot, LibraryPackageService.ArchiveFileName))));

            var derived = await DerivedLibraryPackageService.CreateAsync(new DerivedLibraryPackageRequest(
                asset,
                version,
                packagedSource.DestinationDirectory,
                stagingRoot,
                "Rock Pack optimisé",
                "Nanite corrigé",
                LibraryPackageContentKind.ContentPack,
                LibraryPackageStorageMode.ZipFastest,
                "derived:test"));

            Assert.True(File.Exists(Path.Combine(sourceRoot, LibraryPackageService.ArchiveFileName)));
            Assert.Equal(sourceArchiveHash, Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(
                    Path.Combine(sourceRoot, LibraryPackageService.ArchiveFileName)))));
            var manifestJson = await File.ReadAllTextAsync(derived.ManifestPath!);
            Assert.Contains("\"schemaVersion\": 2", manifestJson, StringComparison.Ordinal);
            Assert.Contains("\"sourceArtifactId\": \"supplier-artifact\"", manifestJson, StringComparison.Ordinal);
            using var manifest = System.Text.Json.JsonDocument.Parse(manifestJson);
            Assert.Equal(
                "Rock Pack optimisé",
                manifest.RootElement.GetProperty("derivation").GetProperty("name").GetString());
            using var archive = ZipFile.OpenRead(Path.Combine(derived.DestinationDirectory, LibraryPackageService.ArchiveFileName));
            Assert.Contains(archive.Entries, entry => entry.FullName == "Content/Environment/Rock.uasset");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ContentPackFromWorkspaceExcludesGeneratedProjectData()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "CyEngineHub-derived-project-test-" + Guid.NewGuid().ToString("N"));
        var projectRoot = Path.Combine(temporaryRoot, "Workspace");
        Directory.CreateDirectory(Path.Combine(projectRoot, "Content", "Pack"));
        Directory.CreateDirectory(Path.Combine(projectRoot, "Saved", "Logs"));
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Content", "Pack", "Mesh.uasset"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(projectRoot, "Saved", "Logs", "Editor.log"), "private-log");
        var asset = new RemoteFabAsset("asset-workspace", "ue", "Workspace", string.Empty, "Fab", "ASSET_PACK", null, null, [], []);
        var version = new RemoteFabProjectVersion("source", [], ["5.6"], ["Windows"]);
        try
        {
            var result = await DerivedLibraryPackageService.CreateAsync(new DerivedLibraryPackageRequest(
                asset, version, projectRoot, Path.Combine(temporaryRoot, "Output"), "Optimized", string.Empty,
                LibraryPackageContentKind.ContentPack, LibraryPackageStorageMode.ZipStore));
            using var archive = ZipFile.OpenRead(Path.Combine(result.DestinationDirectory, LibraryPackageService.ArchiveFileName));
            Assert.Contains(archive.Entries, entry => entry.FullName == "Content/Pack/Mesh.uasset");
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("Saved", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(archive.Entries, entry => entry.FullName.Contains("Editor.log", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task StorageRouterUsesTheFirstOrderedLibraryWithEnoughQuota()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "CyEngineHub-storage-router-test-" + Guid.NewGuid().ToString("N"));
        var download = Path.Combine(temporaryRoot, "Downloads", "Fab", "TestPack");
        var firstLibrary = Path.Combine(temporaryRoot, "Library-1");
        var secondLibrary = Path.Combine(temporaryRoot, "Library-2");
        Directory.CreateDirectory(download);
        await File.WriteAllTextAsync(Path.Combine(download, "content.zip"), "package-data");
        try
        {
            var result = new DirectFabDownloadResult(
                "asset-1",
                "Test Pack",
                download,
                1,
                12,
                DateTimeOffset.UtcNow,
                StorageMode: "ZipUltra",
                StoredBytes: 12);
            var profile = new MarketplaceStorageProfile(
                "Fab",
                Path.Combine(temporaryRoot, "Cache"),
                Path.Combine(temporaryRoot, "Downloads"),
                [
                    new MarketplaceLibraryLocation(firstLibrary, MaximumBytes: 1),
                    new MarketplaceLibraryLocation(secondLibrary, MaximumBytes: 1024),
                ]);

            var placed = await MarketplaceStorageRouter.PlaceAsync(result, profile);

            Assert.StartsWith(
                Path.GetFullPath(secondLibrary),
                Path.GetFullPath(placed.DestinationDirectory),
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(placed.DestinationDirectory, "content.zip")));
            Assert.False(Directory.Exists(download));
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public async Task CreatesFastZipWithReadableSidecarAndClassifiesCodePlugin()
    {
        var temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "CyEngineHub-package-test-" + Guid.NewGuid().ToString("N"));
        var downloadRoot = Path.Combine(temporaryRoot, "PluginPack");
        Directory.CreateDirectory(Path.Combine(downloadRoot, "Source"));
        await File.WriteAllTextAsync(Path.Combine(downloadRoot, "PluginPack.uplugin"), "{}");
        await File.WriteAllTextAsync(Path.Combine(downloadRoot, "Source", "PluginPack.cpp"), "// test");
        try
        {
            var asset = new RemoteFabAsset(
                "asset-1",
                "ue",
                "Plugin Pack",
                string.Empty,
                "Fab",
                "CODE_PLUGIN",
                null,
                null,
                ["Code Plugins"],
                []);
            var version = new RemoteFabProjectVersion(
                "artifact-1",
                [],
                ["5.5"],
                ["Windows"]);
            var download = new DirectFabDownloadResult(
                asset.AssetId,
                asset.Title,
                downloadRoot,
                2,
                9,
                DateTimeOffset.UtcNow,
                new DirectFabSourceManifest("EpicBuildPatchManifest", "Plugin", "1.0", 15, 2, 9));

            var reports = new List<DirectFabDownloadProgress>();
            var result = await LibraryPackageService.PackageAsync(
                asset,
                version,
                download,
                LibraryPackageStorageMode.ZipFastest,
                new TransferProgress(reports.Add));

            var archivePath = Path.Combine(downloadRoot, LibraryPackageService.ArchiveFileName);
            var manifestPath = Path.Combine(downloadRoot, LibraryPackageService.ManifestFileName);
            Assert.True(File.Exists(archivePath));
            Assert.True(File.Exists(manifestPath));
            Assert.False(File.Exists(Path.Combine(downloadRoot, "PluginPack.uplugin")));
            Assert.Equal("CodePlugin", result.PackageKind);
            Assert.Equal("ZipFastest", result.StorageMode);
            Assert.True(result.UncompressedBytes > 0);
            Assert.All(reports, report => Assert.Equal(DirectFabDownloadPhase.Packaging, report.Phase));
            Assert.Contains(reports, report => report.ProcessedBytes == result.UncompressedBytes && report.Fraction == 1);
            Assert.Contains(reports, report => report.Message.Contains("Nettoyage") && report.Fraction is null);
            Assert.True(result.StoredBytes > 0);
            Assert.Equal(new FileInfo(archivePath).Length, result.StoredBytes);
            var json = await File.ReadAllTextAsync(manifestPath);
            Assert.Contains("\"contentKind\": \"codePlugin\"", json, StringComparison.Ordinal);
            Assert.Contains("\"storageMode\": \"zipFastest\"", json, StringComparison.Ordinal);
            using var archive = ZipFile.OpenRead(archivePath);
            Assert.Contains(archive.Entries, entry => entry.FullName == "PluginPack.uplugin");
            Assert.Contains(archive.Entries, entry => entry.FullName == "Source/PluginPack.cpp");
        }
        finally
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
        }
    }
}
