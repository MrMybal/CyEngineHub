using System.IO.Compression;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;

namespace CyEngineHub.Epic.Tests;

public sealed class LibraryPackageServiceTests
{
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
