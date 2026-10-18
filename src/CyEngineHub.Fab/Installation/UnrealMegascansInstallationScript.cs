using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Fab.Installation;

public sealed class UnrealMegascansInstallationScript(
    IProcessRunner processRunner,
    IFabProjectContentImportService contentImport,
    string defaultVariantStorageRoot) : IFabInstallationScript
{
    public const string ScriptId = "unreal.megascans";

    public FabInstallationScriptDescriptor Descriptor { get; } = new(
        ScriptId,
        "Unreal · Importer Megascans",
        "Installe directement les variantes UAsset ou convertit les FBX/OBJ et textures RAW avec la version Unreal du projet cible.",
        "CyEngineHub",
        [
            LibraryPackageContentKind.ContentPack,
            LibraryPackageContentKind.RawAsset,
            LibraryPackageContentKind.Unknown,
        ],
        [FabInstallationTargetKind.Project],
        [
            new FabInstallationOptionDefinition(
                "importMode",
                "Mode d’import",
                "Auto utilise le type de variante Megascans. RAW force la création de .uasset avec Unreal; UAsset effectue une installation transactionnelle.",
                FabInstallationOptionKind.Choice,
                "auto",
                ["auto", "raw", "uasset"]),
            new FabInstallationOptionDefinition(
                "conflictPolicy",
                "Fichiers déjà présents",
                "Remplacer avec sauvegarde permet un rollback. Le mode strict arrête l’installation avant toute écriture.",
                FabInstallationOptionKind.Choice,
                "backup-replace",
                ["backup-replace", "fail"]),
            new FabInstallationOptionDefinition(
                "validateOutput",
                "Valider les assets créés",
                "Recharge et sauvegarde les .uasset produits par l’import RAW avant leur installation.",
                FabInstallationOptionKind.Boolean,
                "true",
                []),
        ],
        IsBuiltIn: true);

    public async Task<FabInstallationScriptResult> InstallAsync(
        FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var project = context.Target.Project
            ?? throw new InvalidOperationException("L’import Megascans exige un projet Unreal cible.");
        var mode = NormalizeMode(context.Configuration.GetValueOrDefault("importMode", "auto"));
        var rawVariant = mode == "raw" ||
                         (mode == "auto" && IsRawVariant(context.Version.ArtifactId));

        if (!rawVariant)
        {
            if (context.Configuration.GetValueOrDefault("prepareOnly") == "true")
                return new(Descriptor.Id, "Variante UAsset prête ; projet inchangé.", new Dictionary<string, string> { ["variant"] = context.SourceDirectory });
            progress?.Report(new(0, "Installation de la variante Megascans UAsset…"));
            var installed = await InstallContentAsync(
                    context.SourceDirectory,
                    project.FilePath,
                    context.Configuration,
                    progress,
                    cancellationToken)
                .ConfigureAwait(false);
            return installed with
            {
                ScriptId = Descriptor.Id,
                Summary = $"Variante Megascans UAsset installée. {installed.Summary}",
            };
        }

        var targetEngine = context.Target.Engine
            ?? throw new InvalidOperationException(
                $"Unreal Engine {context.Target.EngineVersion} doit être installé pour convertir cette variante Megascans RAW.");
        if (!Directory.Exists(context.SourceDirectory))
        {
            throw new InvalidOperationException("Le téléchargement Megascans RAW est introuvable.");
        }

        var validateOutput = !context.Configuration.TryGetValue("validateOutput", out var configuredValidation) ||
                             !bool.TryParse(configuredValidation, out var parsedValidation) ||
                             parsedValidation;
        progress?.Report(new(
            0.05,
            $"Import RAW avec Unreal {targetEngine.Version.Major}.{targetEngine.Version.Minor}…"));
        var converted = await new UnrealVersionBridgeService(processRunner).ConvertAsync(
                new UnrealVersionBridgeRequest(
                    context.Asset.AssetId,
                    context.Version.ArtifactId,
                    context.SourceDirectory,
                    targetEngine,
                    targetEngine,
                    defaultVariantStorageRoot,
                    "safe",
                    KeepStaging: false,
                    ValidateOutput: validateOutput,
                    Mode: "portable",
                    AllowLossyConversion: true),
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        if (context.Configuration.GetValueOrDefault("prepareOnly") == "true")
            return new(Descriptor.Id, "Sources RAW importées en UAsset ; projet inchangé.", new Dictionary<string, string>
            { ["variant"] = converted.VariantDirectory, ["manifest"] = converted.ManifestPath, ["contentKind"] = "ContentPack" });
        var installedRaw = await InstallContentAsync(
                converted.VariantDirectory,
                project.FilePath,
                context.Configuration,
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        return new FabInstallationScriptResult(
            Descriptor.Id,
            $"Megascans RAW converti avec Unreal {targetEngine.Version.Major}.{targetEngine.Version.Minor} " +
            $"({converted.ImportedAssetCount:N0} asset(s)) puis installé. {installedRaw.Summary}",
            new Dictionary<string, string>
            {
                ["variant"] = converted.VariantDirectory,
                ["manifest"] = converted.ManifestPath,
                ["report"] = converted.ReportPath,
                ["receipt"] = installedRaw.Outputs.GetValueOrDefault("receipt", string.Empty),
                ["backup"] = installedRaw.Outputs.GetValueOrDefault("backup", string.Empty),
            });
    }

    private async Task<FabInstallationScriptResult> InstallContentAsync(
        string sourceDirectory,
        string projectFile,
        IReadOnlyDictionary<string, string> configuration,
        IProgress<FabInstallationScriptProgress>? progress,
        CancellationToken cancellationToken)
    {
        var plan = await contentImport.PrepareAsync(sourceDirectory, projectFile, cancellationToken)
            .ConfigureAwait(false);
        if (plan.ConflictCount > 0 &&
            configuration.GetValueOrDefault("conflictPolicy", "backup-replace")
                .Equals("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Installation arrêtée : {plan.ConflictCount:N0} conflit(s) avec le projet.");
        }

        var importProgress = new Progress<FabProjectImportProgress>(value =>
            progress?.Report(new FabInstallationScriptProgress(
                value.Total == 0 ? null : (double)value.Completed / value.Total,
                value.Message)));
        var result = await contentImport.ExecuteAsync(plan, importProgress, cancellationToken)
            .ConfigureAwait(false);
        return new FabInstallationScriptResult(
            Descriptor.Id,
            $"{result.ImportedFileCount:N0} fichier(s) installé(s), {result.ReplacedFileCount:N0} remplacé(s).",
            new Dictionary<string, string>
            {
                ["receipt"] = result.Receipt.ReceiptPath,
                ["backup"] = result.BackupDirectory ?? string.Empty,
            });
    }

    private static bool IsRawVariant(string artifactId) =>
        artifactId.StartsWith("quixel-raw:", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeMode(string value) => value.Trim().ToLowerInvariant() switch
    {
        "raw" => "raw",
        "uasset" => "uasset",
        _ => "auto",
    };
}
