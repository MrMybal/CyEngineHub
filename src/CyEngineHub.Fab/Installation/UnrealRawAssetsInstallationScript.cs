using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Conversion;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Fab.Installation;

public sealed class UnrealRawAssetsInstallationScript(IProcessRunner runner, IFabProjectContentImportService importer) : IFabInstallationScript
{
    public const string ScriptId = "unreal.raw-assets";
    public FabInstallationScriptDescriptor Descriptor { get; } = new(ScriptId, "Unreal · Convertir les sources RAW et installer · Alpha",
        "Importe FBX/OBJ/glTF, textures et sons avec le moteur cible, dans un projet temporaire puis par installation transactionnelle.", "CyEngineHub",
        [LibraryPackageContentKind.RawAsset, LibraryPackageContentKind.Unknown], [FabInstallationTargetKind.Project],
        [new("allowLossy", "Accepter les limites de l’import RAW", "Un format d’échange ne garantit pas une équivalence universelle des matériaux/rigs. Le moteur cible valide les fichiers produits avant installation.", FabInstallationOptionKind.Boolean, "false", []),
         new("conflictPolicy", "Fichiers existants", "Arrêter ou remplacer avec sauvegarde pour rollback.", FabInstallationOptionKind.Choice, "fail", ["fail", "backup-replace"])], true);

    public async Task<FabInstallationScriptResult> InstallAsync(FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var converted = await new UnrealRawSourceConverter(runner).ConvertAsync(
            new(context.SourceDirectory, context.SourceDirectory + ".unreal", context.Configuration, context.Target.Engine), progress, cancellationToken).ConfigureAwait(false);
        var outputs = new Dictionary<string, string> { ["variant"] = converted.Directory, ["report"] = converted.ReportPath, ["contentKind"] = "ContentPack" };
        if (context.Configuration.GetValueOrDefault("prepareOnly") == "true") return new(Descriptor.Id, "Sources converties avec Unreal cible ; projet inchangé.", outputs);
        var installed = await new UnrealContentPackInstallationScript(importer).InstallAsync(context with
            { SourceDirectory = converted.Directory, ContentKind = LibraryPackageContentKind.ContentPack }, progress, cancellationToken).ConfigureAwait(false);
        foreach (var item in installed.Outputs) outputs[item.Key] = item.Value;
        return new(Descriptor.Id, "Sources converties puis installées. " + installed.Summary, outputs);
    }
}
