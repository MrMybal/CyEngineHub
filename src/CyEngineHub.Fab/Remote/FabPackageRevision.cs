using System.Security.Cryptography;
using System.Text;

namespace CyEngineHub.Fab.Remote;

public enum FabPackageUpdateState { Current, UpdateAvailable, Unknown }

public static class FabPackageRevision
{
    public static DirectFabSourceManifest FromManifest(EpicDownloadManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        // Signed download URLs, chunk grouping and compression can change without changing content.
        // Include unambiguous paths, lengths and per-file hashes; never download asset chunks here.
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in manifest.Files.OrderBy(file => file.RelativePath, StringComparer.Ordinal))
        {
            var path = Encoding.UTF8.GetBytes(file.RelativePath.Replace('\\', '/'));
            hash.AppendData(BitConverter.GetBytes(path.Length)); hash.AppendData(path);
            hash.AppendData(BitConverter.GetBytes(file.Size)); hash.AppendData(file.Sha1);
        }
        return new("EpicBuildPatchManifest", manifest.AppName, manifest.BuildVersion, manifest.Version,
            manifest.Files.Count, manifest.DownloadSize, Convert.ToHexString(hash.GetHashAndReset()));
    }

    public static FabPackageUpdateState Compare(DirectFabSourceManifest? local, DirectFabSourceManifest? remote)
    {
        if (local is null || remote is null || string.IsNullOrWhiteSpace(remote.BuildVersion) ||
            !string.Equals(local.AppName, remote.AppName, StringComparison.Ordinal)) return FabPackageUpdateState.Unknown;
        if (!string.IsNullOrWhiteSpace(local.ContentHash) && !string.IsNullOrWhiteSpace(remote.ContentHash))
            return local.ContentHash == remote.ContentHash ? FabPackageUpdateState.Current : FabPackageUpdateState.UpdateAvailable;
        if (string.IsNullOrWhiteSpace(local.BuildVersion)) return FabPackageUpdateState.Unknown;
        return local.BuildVersion != remote.BuildVersion ? FabPackageUpdateState.UpdateAvailable : FabPackageUpdateState.Unknown;
    }
}
