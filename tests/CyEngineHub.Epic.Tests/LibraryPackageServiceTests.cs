using System.IO.Compression;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;

namespace CyEngineHub.Epic.Tests;

public sealed class LibraryPackageServiceTests
{
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

            var result = await LibraryPackageService.PackageAsync(
                asset,
                version,
                download,
                LibraryPackageStorageMode.ZipFastest);

            var archivePath = Path.Combine(downloadRoot, LibraryPackageService.ArchiveFileName);
            var manifestPath = Path.Combine(downloadRoot, LibraryPackageService.ManifestFileName);
            Assert.True(File.Exists(archivePath));
            Assert.True(File.Exists(manifestPath));
            Assert.False(File.Exists(Path.Combine(downloadRoot, "PluginPack.uplugin")));
            Assert.Equal("CodePlugin", result.PackageKind);
            Assert.Equal("ZipFastest", result.StorageMode);
            Assert.True(result.UncompressedBytes > 0);
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
