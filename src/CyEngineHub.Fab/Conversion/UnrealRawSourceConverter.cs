using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Fab.Conversion;

public sealed class UnrealRawSourceConverter(IProcessRunner runner) : IAssetSourceConverter
{
    public const string ConverterId = "unreal.raw-package";
    public AssetSourceConverterDescriptor Descriptor { get; } = new(ConverterId, "Unreal · Sources RAW → package UAsset · Alpha",
        "Crée un dérivé local sérialisé avec le moteur cible dans un projet temporaire, sans installer dans un projet utilisateur.",
        [".fbx", ".obj", ".gltf", ".glb", ".png", ".jpg", ".jpeg", ".tga", ".exr", ".hdr", ".wav", ".ogg"],
        [new("allowLossy", "Accepter les limites de l’import RAW", "Les formats d’échange ne reproduisent pas universellement tous les shaders/rigs du DCC. L’import et la validation du moteur cible sont exécutés hors projet.",
            FabInstallationOptionKind.Boolean, "false", [])], true);
    public async Task<AssetSourceConversionResult> ConvertAsync(AssetSourceConversionRequest request,
        IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var engine = request.TargetEngine ?? throw new InvalidOperationException("Choisissez un moteur Unreal installé pour convertir en UAsset.");
        if (request.Options.GetValueOrDefault("allowLossy") != "true") throw new InvalidOperationException("Confirmez les limites de l’import RAW avant la conversion Unreal.");
        var result = await new UnrealVersionBridgeService(runner).ConvertAsync(new("local-source", Guid.NewGuid().ToString("N"),
            request.SourceDirectory, engine, engine, request.WorkRoot, "safe", KeepStaging: false, ValidateOutput: true, Mode: "portable", AllowLossyConversion: true), progress, cancellationToken).ConfigureAwait(false);
        return new(ConverterId, result.VariantDirectory, result.ReportPath, LibraryPackageContentKind.ContentPack);
    }
}
