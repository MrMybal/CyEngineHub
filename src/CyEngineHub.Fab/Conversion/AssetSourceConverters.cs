using System.Text.Json;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Fab.Conversion;

public sealed record AssetSourceConverterDescriptor(string Id, string DisplayName, string Description,
    IReadOnlyList<string> SourceExtensions, IReadOnlyList<FabInstallationOptionDefinition> Options, bool RequiresTargetEngine = false);
public sealed record AssetSourceConversionRequest(string SourceDirectory, string WorkRoot, IReadOnlyDictionary<string, string> Options, UnrealEngineInstallation? TargetEngine = null);
public sealed record AssetSourceConversionResult(string ConverterId, string Directory, string ReportPath, LibraryPackageContentKind ContentKind);

/// <summary>Conversion plug-in contract. Inputs are immutable; only a new owned workspace may be written.</summary>
public interface IAssetSourceConverter
{
    AssetSourceConverterDescriptor Descriptor { get; }
    Task<AssetSourceConversionResult> ConvertAsync(AssetSourceConversionRequest request,
        IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class AssetSourceConverterRegistry(IEnumerable<IAssetSourceConverter> converters)
{
    private readonly List<IAssetSourceConverter> _converters = [.. converters];
    public IReadOnlyList<IAssetSourceConverter> Converters => _converters;
    public IAssetSourceConverter? Find(string id) => _converters.FirstOrDefault(item => item.Descriptor.Id == id);
    public void Register(IAssetSourceConverter converter)
    {
        ArgumentNullException.ThrowIfNull(converter);
        if (Find(converter.Descriptor.Id) is not null) throw new InvalidOperationException("Identifiant de convertisseur déjà enregistré.");
        _converters.Add(converter);
    }
}

public static class AssetSourceConversionPlanner
{
    public static IReadOnlyList<string> ReadPackageEngineVersions(string directory)
    {
        var path = Path.Combine(directory, LibraryPackageService.ManifestFileName);
        if (!File.Exists(path)) return [];
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Manifeste trop volumineux.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.TryGetProperty("engineVersions", out var versions)
            ? versions.EnumerateArray().Take(32).Select(value => value.GetString() ?? "").Where(value => value.Length is > 0 and < 64).ToArray() : [];
    }
    public static IReadOnlySet<string> ReadPackageExtensions(string directory)
    {
        var path = Path.Combine(directory, LibraryPackageService.ManifestFileName);
        if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Manifeste trop volumineux.");
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        return json.RootElement.GetProperty("fileExtensions").EnumerateObject().Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> Suggest(IReadOnlySet<string> extensions)
    {
        if (extensions.Contains(".uplugin")) return [UnrealProjectPluginInstallationScript.ScriptId];
        if (extensions.Contains(".uasset") || extensions.Contains(".umap")) return [UnrealContentPackInstallationScript.ScriptId];
        if (extensions.Overlaps([".blend", ".usd", ".usda", ".usdc", ".usdz"]))
            return [BlenderConversionInstallationScript.ScriptId, UnrealRawAssetsInstallationScript.ScriptId];
        if (extensions.Overlaps([".max", ".c4d", ".ma", ".mb"]) && !extensions.Overlaps([".fbx", ".obj", ".gltf", ".glb"])) return [];
        if (extensions.Overlaps([".fbx", ".obj", ".gltf", ".glb", ".png", ".jpg", ".jpeg", ".tga", ".exr", ".hdr", ".wav", ".ogg"]))
            return [UnrealRawAssetsInstallationScript.ScriptId];
        return [];
    }
}
