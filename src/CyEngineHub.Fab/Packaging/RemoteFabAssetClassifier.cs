using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Fab.Packaging;

public sealed record RemoteFabAssetClassification(
    LibraryPackageContentKind Kind,
    string Reason);

public static class RemoteFabAssetClassifier
{
    public static RemoteFabAssetClassification Classify(
        RemoteFabAsset asset,
        RemoteFabProjectVersion? version = null)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (version?.ArtifactId.StartsWith("quixel-raw:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new(LibraryPackageContentKind.RawAsset, "Artefact Megascans RAW");
        }
        if (version?.ArtifactId.StartsWith("quixel-tier:", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new(LibraryPackageContentKind.ContentPack, "Artefact Megascans UAsset");
        }

        var distribution = Normalize(asset.DistributionMethod);
        if (distribution.Contains("COMPLETE_PROJECT", StringComparison.Ordinal))
        {
            return new(LibraryPackageContentKind.CompleteProject, "Manifest Fab · COMPLETE_PROJECT");
        }
        if (distribution.Contains("CODE_PLUGIN", StringComparison.Ordinal) ||
            distribution.Equals("PLUGIN", StringComparison.Ordinal))
        {
            return new(LibraryPackageContentKind.CodePlugin, "Manifest Fab · CODE_PLUGIN");
        }
        if (distribution.Equals("ENGINE", StringComparison.Ordinal) ||
            distribution.Contains("ENGINE_PACKAGE", StringComparison.Ordinal))
        {
            return new(LibraryPackageContentKind.EnginePackage, "Manifest Fab · ENGINE");
        }
        if (distribution.Contains("ASSET_PACK", StringComparison.Ordinal) ||
            distribution.Contains("CONTENT_PACK", StringComparison.Ordinal))
        {
            return new(LibraryPackageContentKind.ContentPack, "Manifest Fab · ASSET_PACK");
        }

        IEnumerable<RemoteFabProjectVersion> inspectedVersions =
            version is null ? asset.ProjectVersions : [version];
        if (inspectedVersions.Any(item =>
                item.ArtifactId.StartsWith("quixel-raw:", StringComparison.OrdinalIgnoreCase) ||
                item.Platforms.Any(platform =>
                    platform.Contains("FBX", StringComparison.OrdinalIgnoreCase) ||
                    platform.Contains("OBJ", StringComparison.OrdinalIgnoreCase) ||
                    platform.Contains("GLTF", StringComparison.OrdinalIgnoreCase) ||
                    platform.Contains("GLB", StringComparison.OrdinalIgnoreCase))))
        {
            return new(LibraryPackageContentKind.RawAsset, "Formats source multi-moteur détectés");
        }

        var metadata = string.Join(' ', asset.Categories.Append(asset.Title));
        if (metadata.Contains("code plugin", StringComparison.OrdinalIgnoreCase) ||
            metadata.Contains("code plugins", StringComparison.OrdinalIgnoreCase))
        {
            return new(LibraryPackageContentKind.CodePlugin, "Catégorie Fab de plugin code");
        }
        if (asset.ProjectVersions.Count > 0)
        {
            return new(LibraryPackageContentKind.ContentPack, "Manifest de contenu Unreal disponible");
        }
        return new(LibraryPackageContentKind.Unknown, "Type absent du manifest Fab");
    }

    private static string Normalize(string value) => value
        .Trim()
        .Replace('-', '_')
        .Replace(' ', '_')
        .ToUpperInvariant();
}
