using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class FabPackageRevisionTests
{
    [Fact]
    public void DetectsContentChangesEvenWhenArtifactBuildLabelIsUnchanged()
    {
        var local = FabPackageRevision.FromManifest(Manifest("a.uasset", 1));
        var remote = FabPackageRevision.FromManifest(Manifest("a.uasset", 2));
        Assert.Equal(local.BuildVersion, remote.BuildVersion);
        Assert.Equal(FabPackageUpdateState.UpdateAvailable, FabPackageRevision.Compare(local, remote));
        Assert.Equal(FabPackageUpdateState.Current, FabPackageRevision.Compare(local, local));
    }

    [Fact]
    public void FingerprintIgnoresTransportAndFileOrderButIncludesPaths()
    {
        var manifest = Manifest("a.uasset", 1);
        var reordered = manifest with { Files = manifest.Files.Reverse().ToArray(), Chunks = new Dictionary<string, EpicManifestChunk>() };
        Assert.Equal(FabPackageRevision.FromManifest(manifest).ContentHash, FabPackageRevision.FromManifest(reordered).ContentHash);
        Assert.NotEqual(FabPackageRevision.FromManifest(manifest).ContentHash, FabPackageRevision.FromManifest(Manifest("renamed.uasset", 1)).ContentHash);
    }

    [Fact]
    public void LegacyPackagesWithoutFingerprintsAreNotFalselyReportedCurrent()
    {
        var remote = FabPackageRevision.FromManifest(Manifest("a.uasset", 1));
        var legacy = remote with { ContentHash = null };
        Assert.Equal(FabPackageUpdateState.Unknown, FabPackageRevision.Compare(legacy, remote));
        Assert.Equal(FabPackageUpdateState.Unknown, FabPackageRevision.Compare(null, remote));
        Assert.Equal(FabPackageUpdateState.UpdateAvailable, FabPackageRevision.Compare(legacy with { BuildVersion = "previous" }, remote));
        Assert.Equal(FabPackageUpdateState.Unknown, FabPackageRevision.Compare(remote with { AppName = "another" }, remote));
    }

    private static EpicDownloadManifest Manifest(string path, byte hash) => new("fixture", "stable-build", 1,
        new Dictionary<string, EpicManifestChunk>(),
        [new(path, Enumerable.Repeat(hash, 20).ToArray(), [], 5, []), new("b.uasset", new byte[20], [], 7, [])]);
}
