using CyEngineHub.Core.Processes;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Packaging;

namespace CyEngineHub.Fab.Installation;

public sealed class UnrealRelocatedContentInstallationScript(
    IProcessRunner processRunner,
    IFabProjectContentImportService contentImport,
    string storageRoot) : IFabInstallationScript
{
    public const string ScriptId = "unreal.relocated-content";

    public FabInstallationScriptDescriptor Descriptor { get; } = new(
        ScriptId,
        "Unreal · Déplacer un pack Fab",
        "Conserve les chemins natifs dans un projet temporaire, déplace le pack avec Unreal, valide les références puis l’installe.",
        "CyEngineHub",
        [LibraryPackageContentKind.ContentPack],
        [FabInstallationTargetKind.Project],
        [
            new(
                "destinationFolder",
                "Dossier de destination",
                "Dossier parent sous /Game. Exemple : Environments/Marketplace.",
                FabInstallationOptionKind.Text,
                "CyEngineHubImports",
                [],
                IsRequired: true),
            new(
                "preserveRootFolders",
                "Conserver les dossiers racine du pack",
                "Ajoute les dossiers racine d’origine sous la destination. Recommandé si le pack en possède plusieurs.",
                FabInstallationOptionKind.Boolean,
                "true",
                []),
            new(
                "conflictPolicy",
                "Fichiers déjà présents",
                "Remplacer avec sauvegarde permet un rollback dans le projet cible.",
                FabInstallationOptionKind.Choice,
                "backup-replace",
                ["backup-replace", "fail"]),
            new(
                "keepStaging",
                "Conserver le projet temporaire",
                "Conserve le projet de travail après succès. En cas d’erreur il est toujours conservé.",
                FabInstallationOptionKind.Boolean,
                "false",
                []),
        ],
        IsBuiltIn: true);

    public async Task<FabInstallationScriptResult> InstallAsync(
        FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var project = context.Target.Project
            ?? throw new InvalidOperationException("Le déplacement exige un projet Unreal cible.");
        var engine = context.Target.Engine
            ?? throw new InvalidOperationException(
                $"Unreal Engine {context.Target.EngineVersion} doit être installé pour déplacer ce pack.");
        var destination = context.Configuration.GetValueOrDefault(
            "destinationFolder",
            "CyEngineHubImports");
        var preserveRoots = ReadBoolean(context.Configuration, "preserveRootFolders", true);
        var keepStaging = ReadBoolean(context.Configuration, "keepStaging", false);
        var relocated = await new UnrealRelocatedContentService(processRunner).RelocateAsync(
                new(
                    context.Asset.AssetId,
                    context.Version.ArtifactId,
                    context.SourceDirectory,
                    engine,
                    storageRoot,
                    destination,
                    preserveRoots,
                    keepStaging),
                progress,
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new(0.88, "Installation transactionnelle dans le projet cible…"));
        var plan = await contentImport.PrepareAsync(
                relocated.VariantDirectory,
                project.FilePath,
                cancellationToken)
            .ConfigureAwait(false);
        if (plan.ConflictCount > 0 &&
            context.Configuration.GetValueOrDefault("conflictPolicy", "backup-replace")
                .Equals("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Installation arrêtée : {plan.ConflictCount:N0} conflit(s) avec le projet cible.");
        }

        var importProgress = new Progress<FabProjectImportProgress>(value =>
            progress?.Report(new(
                value.Total == 0 ? null : 0.88 + 0.12 * value.Completed / value.Total,
                value.Message)));
        var installed = await contentImport.ExecuteAsync(plan, importProgress, cancellationToken)
            .ConfigureAwait(false);
        return new(
            Descriptor.Id,
            $"{relocated.MovedAssetCount:N0} asset(s) déplacé(s) et validé(s), puis " +
            $"{installed.ImportedFileCount:N0} fichier(s) installé(s) dans le projet cible.",
            new Dictionary<string, string>
            {
                ["variant"] = relocated.VariantDirectory,
                ["manifest"] = relocated.ManifestPath,
                ["report"] = relocated.ReportPath,
                ["editorLog"] = relocated.EditorLogPath,
                ["receipt"] = installed.Receipt.ReceiptPath,
                ["backup"] = installed.BackupDirectory ?? string.Empty,
            });
    }

    private static bool ReadBoolean(
        IReadOnlyDictionary<string, string> configuration,
        string key,
        bool fallback) =>
        configuration.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : fallback;
}
