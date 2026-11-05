using System.Text.Json;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Fab.Conversion;

public sealed record AssetSourceConverterDescriptor(string Id, string DisplayName, string Description,
    IReadOnlyList<string> SourceExtensions, IReadOnlyList<FabInstallationOptionDefinition> Options, bool RequiresTargetEngine = false);
public sealed record AssetSourceConversionRequest(string SourceDirectory, string WorkRoot, IReadOnlyDictionary<string, string> Options, UnrealEngineInstallation? TargetEngine = null);
public sealed record AssetSourceConversionResult(string ConverterId, string Directory, string ReportPath, LibraryPackageContentKind ContentKind);

/**
 * @doc api-source-converter
 * @locale en
 * @title Asset source converter contract
 * @section SDK API
 * @symbol CyEngineHub.Fab.Conversion.IAssetSourceConverter
 * Implement `Descriptor` and `ConvertAsync(request, progress, cancellationToken)`.
 * Read `SourceDirectory` without modifying it; write only to a new owned directory
 * under `WorkRoot`. Return the converter ID, result directory, report path and
 * content kind. Honor cancellation and report progress. Registration uses
 * `AssetSourceConverterRegistry.Register`; duplicate IDs are rejected.
 * `RequiresTargetEngine` declares whether the caller must supply a target engine.
 * This contract does not imply that every input format or Unreal version is supported.
 * See [conversion procedures](doc:conversion) and [source converter reference](doc:reference-docs-source-converters).
 */
/**
 * @doc api-source-converter
 * @locale fr
 * @title Contrat des convertisseurs de sources
 * @section SDK API
 * @symbol CyEngineHub.Fab.Conversion.IAssetSourceConverter
 * Implémentez `Descriptor` et `ConvertAsync(request, progress, cancellationToken)`.
 * Lisez `SourceDirectory` sans le modifier ; écrivez uniquement dans un nouveau
 * dossier de travail possédé sous `WorkRoot`. Renvoyez l'identifiant du convertisseur,
 * le dossier résultat, le chemin du rapport et le type de contenu. Respectez
 * l'annulation et signalez la progression. `AssetSourceConverterRegistry.Register`
 * refuse les identifiants dupliqués. `RequiresTargetEngine` indique si un moteur
 * cible doit être fourni. Ce contrat ne garantit pas la prise en charge de tous les formats.
 * Voir [la conversion](doc:conversion) et [la référence](doc:reference-docs-source-converters).
 */
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
