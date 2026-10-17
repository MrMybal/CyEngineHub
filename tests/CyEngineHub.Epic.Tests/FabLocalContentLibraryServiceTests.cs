using CyEngineHub.Fab.Library;
using CyEngineHub.Fab.Storage;
using System.Text.Json;

namespace CyEngineHub.Epic.Tests;

public sealed class FabLocalContentLibraryServiceTests
{
    [Fact]
    public async Task DiscoversNestedUnrealPacksWithBoundedMetadata()
    {
        using var temporary = new TemporaryDirectory();
        var packageRoot = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "Downloads",
            "EnvironmentPack"));
        var content = Directory.CreateDirectory(Path.Combine(
            packageRoot.FullName,
            "Content",
            "Environment"));
        await File.WriteAllTextAsync(
            Path.Combine(packageRoot.FullName, "EnvironmentPack.uproject"),
            "{\"EngineAssociation\":\"5.5\"}");
        await File.WriteAllTextAsync(Path.Combine(content.FullName, "Rock.uasset"), "asset");
        await File.WriteAllTextAsync(Path.Combine(content.FullName, "Overview.umap"), "map");
        await File.WriteAllTextAsync(Path.Combine(content.FullName, "Tool.exe"), "ignored");

        var packs = await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([Path.Combine(temporary.Path, "Downloads")]));

        var pack = Assert.Single(packs);
        Assert.Equal("EnvironmentPack", pack.DisplayName);
        Assert.Equal(FabLocalContentKind.Unreal, pack.Kind);
        Assert.Equal("5.5", pack.EngineAssociation);
        Assert.Equal(1, pack.AssetCount);
        Assert.Equal(1, pack.MapCount);
        Assert.Equal(2, pack.SupportedFileCount);
        Assert.Equal(FabLocalContentPackHealth.Ready, pack.Health);
        Assert.Empty(pack.Issues);
    }

    [Fact]
    public async Task AcceptsAnExplicitContentDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var content = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "DownloadedPack",
            "Content"));
        await File.WriteAllTextAsync(Path.Combine(content.FullName, "Asset.uasset"), "asset");

        var packs = await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([content.FullName]));

        var pack = Assert.Single(packs);
        Assert.Equal("DownloadedPack", pack.DisplayName);
        Assert.Equal(Path.GetFullPath(content.FullName), pack.ContentDirectory);
        Assert.Equal(FabLocalContentPackHealth.Warning, pack.Health);
        Assert.Contains(pack.Issues, issue => issue.Contains(".uproject", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoesNotScanPastTheConfiguredDepth()
    {
        using var temporary = new TemporaryDirectory();
        var tooDeep = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "Root",
            "Level1",
            "Level2",
            "Content"));
        await File.WriteAllTextAsync(Path.Combine(tooDeep.FullName, "Asset.uasset"), "asset");

        var packs = await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest(
                [Path.Combine(temporary.Path, "Root")],
                MaximumDepth: 1));

        Assert.Empty(packs);
    }

    [Fact]
    public async Task MarksPackPartialWhenTheFileLimitIsReached()
    {
        using var temporary = new TemporaryDirectory();
        var content = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "LargePack",
            "Content"));
        await File.WriteAllTextAsync(Path.Combine(content.FullName, "AssetA.uasset"), "a");
        await File.WriteAllTextAsync(Path.Combine(content.FullName, "AssetB.uasset"), "b");

        var packs = await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest(
                [temporary.Path],
                MaximumFilesPerPack: 1));

        var pack = Assert.Single(packs);
        Assert.Equal(FabLocalContentPackHealth.Partial, pack.Health);
        Assert.Equal(1, pack.SupportedFileCount);
        Assert.Contains(pack.Issues, issue => issue.Contains("limite de fichiers", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoesNotPresentAPluginAsAContentPack()
    {
        using var temporary = new TemporaryDirectory();
        var pluginRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "FabPlugin"));
        var content = Directory.CreateDirectory(Path.Combine(pluginRoot.FullName, "Content"));
        await File.WriteAllTextAsync(Path.Combine(pluginRoot.FullName, "FabPlugin.uplugin"), "{}");
        await File.WriteAllTextAsync(Path.Combine(content.FullName, "Asset.uasset"), "asset");

        var packs = await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([temporary.Path]));

        Assert.Empty(packs);
    }

    [Fact]
    public async Task DiscoversUnityPackWithVersionAndNativeMetadata()
    {
        using var temporary = new TemporaryDirectory();
        var packageRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Downloads", "NatureUnity"));
        var assets = Directory.CreateDirectory(Path.Combine(packageRoot.FullName, "Assets", "Nature"));
        var settings = Directory.CreateDirectory(Path.Combine(packageRoot.FullName, "ProjectSettings"));
        await File.WriteAllTextAsync(Path.Combine(settings.FullName, "ProjectVersion.txt"), "m_EditorVersion: 6000.0.42f1");
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Tree.glb"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Tree.prefab"), "prefab");
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Demo.unity"), "scene");
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Tree.glb.meta"), "guid: stable");

        var packs = await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([Path.Combine(temporary.Path, "Downloads")]));

        var pack = Assert.Single(packs);
        Assert.Equal(FabLocalContentKind.Unity, pack.Kind);
        Assert.Equal("NatureUnity", pack.DisplayName);
        Assert.Equal("6000.0.42f1", pack.EngineAssociation);
        Assert.Equal(2, pack.AssetCount);
        Assert.Equal(1, pack.MapCount);
        Assert.Equal(4, pack.SupportedFileCount);
        Assert.Equal(0, pack.BlockedFileCount);
        Assert.Equal(FabLocalContentPackHealth.Ready, pack.Health);
    }

    [Fact]
    public async Task AcceptsExplicitUnityAssetsDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var assets = Directory.CreateDirectory(Path.Combine(temporary.Path, "StandaloneUnityPack", "Assets"));
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Rock.fbx"), "mesh");

        var packs = await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([assets.FullName]));

        var pack = Assert.Single(packs);
        Assert.Equal(FabLocalContentKind.Unity, pack.Kind);
        Assert.Equal(Path.GetFullPath(assets.FullName), pack.ContentDirectory);
        Assert.Equal(FabLocalContentPackHealth.Warning, pack.Health);
        Assert.Contains(pack.Issues, issue => issue.Contains("Version Unity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnityPackReportsBlockedCode()
    {
        using var temporary = new TemporaryDirectory();
        var assets = Directory.CreateDirectory(Path.Combine(temporary.Path, "UnsafeUnityPack", "Assets"));
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Rock.fbx"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Installer.cs"), "class Installer {}");

        var pack = Assert.Single(await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([temporary.Path])));

        Assert.Equal(FabLocalContentKind.Unity, pack.Kind);
        Assert.Equal(1, pack.BlockedFileCount);
        Assert.Equal(FabLocalContentPackHealth.Warning, pack.Health);
        Assert.Contains(pack.Issues, issue => issue.Contains("import refusé", StringComparison.Ordinal));
    }

    [Fact]
    public void LegacyCachedPackDefaultsToUnreal()
    {
        var json = """
            {
              "Id":"legacy",
              "DisplayName":"OldPack",
              "PackageRoot":"C:\\OldPack",
              "ContentDirectory":"C:\\OldPack\\Content",
              "ProjectPath":null,
              "EngineAssociation":"5.5",
              "AssetCount":1,
              "MapCount":0,
              "SupportedFileCount":1,
              "SupportedBytes":10,
              "LastModifiedAt":"2026-01-01T00:00:00+00:00",
              "Health":0,
              "Issues":[],
              "DiscoveredAt":"2026-01-01T00:00:00+00:00"
            }
            """;

        var pack = JsonSerializer.Deserialize<FabLocalContentPack>(json);

        Assert.NotNull(pack);
        Assert.Equal(FabLocalContentKind.Unreal, pack.Kind);
        Assert.Equal(0, pack.BlockedFileCount);
    }

    [Fact]
    public async Task DiscoversGodotPackWithPortableAssetsAndVersion()
    {
        using var temporary = new TemporaryDirectory();
        var packRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Downloads", "NatureGodot"));
        var models = Directory.CreateDirectory(Path.Combine(packRoot.FullName, "Models"));
        await File.WriteAllTextAsync(
            Path.Combine(packRoot.FullName, "project.godot"),
            "[application]\nconfig/name=\"Nature\"\n[application]\nconfig/features=PackedStringArray(\"4.3\", \"GL Compatibility\")");
        await File.WriteAllTextAsync(Path.Combine(models.FullName, "Tree.glb"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(models.FullName, "Preview.tscn"), "[gd_scene]");

        var pack = Assert.Single(await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([Path.Combine(temporary.Path, "Downloads")])));

        Assert.Equal(FabLocalContentKind.Godot, pack.Kind);
        Assert.Equal("NatureGodot", pack.DisplayName);
        Assert.Equal("4.3", pack.EngineAssociation);
        Assert.Equal(1, pack.AssetCount);
        Assert.Equal(1, pack.SupportedFileCount);
        Assert.Equal(0, pack.BlockedFileCount);
        Assert.Equal(FabLocalContentPackHealth.Ready, pack.Health);
    }

    [Fact]
    public async Task GodotPackReportsBlockedScripts()
    {
        using var temporary = new TemporaryDirectory();
        var packRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "UnsafeGodot"));
        await File.WriteAllTextAsync(
            Path.Combine(packRoot.FullName, "project.godot"),
            "config/features=PackedStringArray(\"4.4\")");
        await File.WriteAllTextAsync(Path.Combine(packRoot.FullName, "Rock.glb"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(packRoot.FullName, "Installer.gd"), "extends Node");

        var pack = Assert.Single(await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([temporary.Path])));

        Assert.Equal(FabLocalContentKind.Godot, pack.Kind);
        Assert.Equal(1, pack.BlockedFileCount);
        Assert.Equal(FabLocalContentPackHealth.Warning, pack.Health);
        Assert.Contains(pack.Issues, issue => issue.Contains("Godot bloqué", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoversCryEnginePackUsingDeclaredAssetsDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var packRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "Downloads", "NatureCry"));
        var assets = Directory.CreateDirectory(Path.Combine(packRoot.FullName, "GameAssets", "Models"));
        await File.WriteAllTextAsync(
            Path.Combine(packRoot.FullName, "Nature.cryproject"),
            "{ \"content\": { \"assets\": [ \"GameAssets\" ] }, \"info\": { \"name\": \"Frozen Forest\" }, \"require\": { \"engine\": \"engine-5.7\" } }");
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Tree.fbx"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Tree.cryasset"), "native metadata");

        var pack = Assert.Single(await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([Path.Combine(temporary.Path, "Downloads")])));

        Assert.Equal(FabLocalContentKind.CryEngine, pack.Kind);
        Assert.Equal("Frozen Forest", pack.DisplayName);
        Assert.Equal("engine-5.7", pack.EngineAssociation);
        Assert.Equal(Path.GetFullPath(Path.Combine(packRoot.FullName, "GameAssets")), pack.ContentDirectory);
        Assert.Equal(1, pack.AssetCount);
        Assert.Equal(1, pack.SupportedFileCount);
        Assert.Equal(FabLocalContentPackHealth.Ready, pack.Health);
    }

    [Fact]
    public async Task CryEnginePackReportsBlockedCodeOutsideAssetsDirectory()
    {
        using var temporary = new TemporaryDirectory();
        var packRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "UnsafeCry"));
        var assets = Directory.CreateDirectory(Path.Combine(packRoot.FullName, "Assets"));
        var code = Directory.CreateDirectory(Path.Combine(packRoot.FullName, "Code"));
        await File.WriteAllTextAsync(
            Path.Combine(packRoot.FullName, "Unsafe.cryproject"),
            "{ \"content\": { \"assets\": [ \"Assets\" ] }, \"require\": { \"engine\": \"5.7.1\" } }");
        await File.WriteAllTextAsync(Path.Combine(assets.FullName, "Rock.glb"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(code.FullName, "Installer.cpp"), "void Install() {}");

        var pack = Assert.Single(await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([temporary.Path])));

        Assert.Equal(FabLocalContentKind.CryEngine, pack.Kind);
        Assert.Equal(1, pack.BlockedFileCount);
        Assert.Equal(FabLocalContentPackHealth.Warning, pack.Health);
        Assert.Contains(pack.Issues, issue => issue.Contains("CRYENGINE bloqué", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoversPortableAssetFolderWithoutEngineDescriptor()
    {
        using var temporary = new TemporaryDirectory();
        var packRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "PortableMeshes"));
        await File.WriteAllTextAsync(Path.Combine(packRoot.FullName, "Robot.fbx"), "mesh");
        await File.WriteAllTextAsync(Path.Combine(packRoot.FullName, "Robot_BaseColor.png"), "texture");
        await File.WriteAllTextAsync(Path.Combine(packRoot.FullName, "Ignore.exe"), "blocked");

        var pack = Assert.Single(await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([packRoot.FullName])));

        Assert.Equal(FabLocalContentKind.Generic, pack.Kind);
        Assert.Equal(2, pack.AssetCount);
        Assert.Equal(2, pack.SupportedFileCount);
        Assert.Equal(1, pack.BlockedFileCount);
        Assert.Contains(pack.Issues, issue => issue.Contains("portable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DiscoversEpicVaultPackageFromNativeManifestWithoutChangingSource()
    {
        using var temporary = new TemporaryDirectory();
        var packageRoot = Directory.CreateDirectory(Path.Combine(
            temporary.Path,
            "VaultCache",
            "EnvironmentPack_5.5"));
        var manifests = Directory.CreateDirectory(Path.Combine(packageRoot.FullName, "manifests"));
        const string chunkGuid = "00112233445566778899aabbccddeeff";
        var fileHash = Enumerable.Range(1, 20).Select(value => (byte)value).ToArray();
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["ManifestFileVersion"] = Blob(BitConverter.GetBytes(13U)),
            ["bIsFileData"] = false,
            ["AppNameString"] = "Environment Pack",
            ["BuildVersionString"] = "5.5.0",
            ["FileManifestList"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["Filename"] = "Content/Environment/Rock.uasset",
                    ["FileHash"] = Blob(fileHash),
                    ["FileChunkParts"] = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["Guid"] = chunkGuid,
                            ["Offset"] = Blob(BitConverter.GetBytes(0U)),
                            ["Size"] = Blob(BitConverter.GetBytes(3U)),
                        },
                    },
                    ["InstallTags"] = Array.Empty<string>(),
                },
            },
            ["ChunkHashList"] = new Dictionary<string, string>
            {
                [chunkGuid] = Blob(BitConverter.GetBytes(0x1122334455667788UL)),
            },
            ["DataGroupList"] = new Dictionary<string, string>
            {
                [chunkGuid] = Blob([7]),
            },
            ["ChunkFilesizeList"] = new Dictionary<string, string>
            {
                [chunkGuid] = Blob(BitConverter.GetBytes(64L)),
            },
        });
        var manifestPath = Path.Combine(manifests.FullName, "Environment.manifest");
        await File.WriteAllBytesAsync(manifestPath, manifest);

        var pack = Assert.Single(await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([Path.Combine(temporary.Path, "VaultCache")])));

        Assert.Equal(FabLocalContentKind.EpicVault, pack.Kind);
        Assert.Equal("Environment Pack", pack.DisplayName);
        Assert.Equal("5.5.0", pack.EngineAssociation);
        Assert.Equal(1, pack.AssetCount);
        Assert.Equal(Path.GetFullPath(manifestPath), pack.ProjectPath);
        Assert.True(File.Exists(manifestPath));
    }

    [Fact]
    public async Task DiscoversEpicVaultPackageFromLegacyNestedManifestLayout()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = await CreateVaultFixtureAsync(
            temporary.Path,
            Path.Combine("Data", "Manifests"));

        var pack = Assert.Single(await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([Path.Combine(temporary.Path, "VaultCache")])));

        Assert.Equal(FabLocalContentKind.EpicVault, pack.Kind);
        Assert.Equal(Path.GetFullPath(fixture.PackageRoot), pack.PackageRoot);
        Assert.Equal(Path.GetFullPath(fixture.ManifestPath), pack.ProjectPath);
    }

    [Fact]
    public async Task MaterializeEpicVaultReusesValidatedCacheWhenSourceChunkIsGone()
    {
        using var temporary = new TemporaryDirectory();
        var fixture = await CreateVaultFixtureAsync(temporary.Path, "manifests");
        var pack = Assert.Single(await new FabLocalContentLibraryService().DiscoverAsync(
            new FabLocalLibraryRequest([Path.Combine(temporary.Path, "VaultCache")])));
        var cacheRoot = Path.Combine(temporary.Path, "Cache", "NativeStores");

        var first = await EpicVaultNativeStoreService.MaterializeAsync(pack, cacheRoot);
        File.Delete(fixture.ChunkPath);
        var second = await EpicVaultNativeStoreService.MaterializeAsync(pack, cacheRoot);

        Assert.Equal(first.DestinationDirectory, second.DestinationDirectory);
        Assert.Equal("abc", await File.ReadAllTextAsync(Path.Combine(
            second.DestinationDirectory,
            "Content",
            "Environment",
            "Rock.uasset")));
        Assert.Single(Directory.GetDirectories(Path.Combine(cacheRoot, "EpicVault")));
    }

    [Fact]
    public async Task TrimEpicVaultCacheEvictsOldestManagedEntryOnly()
    {
        using var temporary = new TemporaryDirectory();
        var cacheRoot = Directory.CreateDirectory(Path.Combine(temporary.Path, "NativeStores"));
        var vaultRoot = Directory.CreateDirectory(Path.Combine(cacheRoot.FullName, "EpicVault"));
        var oldest = Directory.CreateDirectory(Path.Combine(vaultRoot.FullName, "Oldest"));
        var newest = Directory.CreateDirectory(Path.Combine(vaultRoot.FullName, "Newest"));
        var unmanaged = Directory.CreateDirectory(Path.Combine(vaultRoot.FullName, "Unmanaged"));
        foreach (var managed in new[] { oldest, newest })
        {
            await File.WriteAllTextAsync(
                Path.Combine(managed.FullName, ".cyenginehub-epic-vault-cache.json"),
                "{}");
            await File.WriteAllTextAsync(Path.Combine(managed.FullName, "payload.bin"), "123456789012");
        }
        await File.WriteAllTextAsync(Path.Combine(unmanaged.FullName, "keep.bin"), "unmanaged");
        Directory.SetLastWriteTimeUtc(oldest.FullName, DateTime.UtcNow.AddDays(-2));
        Directory.SetLastWriteTimeUtc(newest.FullName, DateTime.UtcNow.AddDays(-1));

        var result = await EpicVaultNativeStoreService.TrimCacheAsync(cacheRoot.FullName, 12);

        Assert.Equal(1, result.RemovedEntries);
        Assert.False(Directory.Exists(oldest.FullName));
        Assert.True(Directory.Exists(newest.FullName));
        Assert.True(Directory.Exists(unmanaged.FullName));
    }

    private static async Task<(string PackageRoot, string ManifestPath, string ChunkPath)> CreateVaultFixtureAsync(
        string temporaryRoot,
        string manifestRelativeDirectory)
    {
        var packageRoot = Directory.CreateDirectory(Path.Combine(
            temporaryRoot,
            "VaultCache",
            "EnvironmentPack_5.5"));
        var manifests = Directory.CreateDirectory(Path.Combine(
            packageRoot.FullName,
            manifestRelativeDirectory));
        const string chunkGuid = "00112233445566778899aabbccddeeff";
        const ulong rollingHash = 0x1122334455667788UL;
        var payload = "abc"u8.ToArray();
        var fileHash = Convert.FromHexString("A9993E364706816ABA3E25717850C26C9CD0D89D");
        var manifest = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            ["ManifestFileVersion"] = Blob(BitConverter.GetBytes(13U)),
            ["bIsFileData"] = false,
            ["AppNameString"] = "Environment Pack",
            ["BuildVersionString"] = "5.5.0",
            ["FileManifestList"] = new[]
            {
                new Dictionary<string, object?>
                {
                    ["Filename"] = "Content/Environment/Rock.uasset",
                    ["FileHash"] = Blob(fileHash),
                    ["FileChunkParts"] = new[]
                    {
                        new Dictionary<string, string>
                        {
                            ["Guid"] = chunkGuid,
                            ["Offset"] = Blob(BitConverter.GetBytes(0U)),
                            ["Size"] = Blob(BitConverter.GetBytes((uint)payload.Length)),
                        },
                    },
                    ["InstallTags"] = Array.Empty<string>(),
                },
            },
            ["ChunkHashList"] = new Dictionary<string, string>
            {
                [chunkGuid] = Blob(BitConverter.GetBytes(rollingHash)),
            },
            ["DataGroupList"] = new Dictionary<string, string>
            {
                [chunkGuid] = Blob([7]),
            },
            ["ChunkFilesizeList"] = new Dictionary<string, string>
            {
                [chunkGuid] = Blob(BitConverter.GetBytes(44L)),
            },
        });
        var manifestPath = Path.Combine(manifests.FullName, "Environment.manifest");
        await File.WriteAllBytesAsync(manifestPath, manifest);
        var chunks = Directory.CreateDirectory(Path.Combine(packageRoot.FullName, "ChunksV3", "07"));
        var chunkPath = Path.Combine(
            chunks.FullName,
            $"{rollingHash:X16}_{chunkGuid.ToUpperInvariant()}.chunk");
        await using (var stream = File.Create(chunkPath))
        {
            using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
            writer.Write(2_986_228_386U);
            writer.Write(1U);
            writer.Write(41U);
            writer.Write((uint)payload.Length);
            for (var index = 0; index < 4; index++)
            {
                writer.Write(Convert.ToUInt32(chunkGuid.Substring(index * 8, 8), 16));
            }
            writer.Write(rollingHash);
            writer.Write((byte)0);
            writer.Write(payload);
            writer.Flush();
        }
        return (packageRoot.FullName, manifestPath, chunkPath);
    }

    private static string Blob(IEnumerable<byte> bytes) => string.Concat(
        bytes.Select(value => value.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)));

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CyEngineHub-FabLibraryTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
