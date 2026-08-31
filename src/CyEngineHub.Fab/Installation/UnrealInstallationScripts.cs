using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Plugins.Discovery;
using CyEngineHub.Plugins.Installation;
using CyEngineHub.Plugins.Models;

namespace CyEngineHub.Fab.Installation;

public sealed class UnrealContentPackInstallationScript(
    IFabProjectContentImportService contentImport) : IFabInstallationScript
{
    public const string ScriptId = "unreal.content-pack";

    public FabInstallationScriptDescriptor Descriptor { get; } = new(
        ScriptId,
        "Unreal · Importer un pack de contenu",
        "Import transactionnel dans Content avec staging, sauvegarde des conflits et reçu de rollback.",
        "CyEngineHub",
        [
            LibraryPackageContentKind.ContentPack,
            LibraryPackageContentKind.RawAsset,
            LibraryPackageContentKind.Unknown,
        ],
        [FabInstallationTargetKind.Project],
        [
            new FabInstallationOptionDefinition(
                "conflictPolicy",
                "Fichiers déjà présents",
                "Remplacer avec sauvegarde permet un rollback. Le mode strict arrête l’import avant toute écriture.",
                FabInstallationOptionKind.Choice,
                "backup-replace",
                ["backup-replace", "fail"]),
        ],
        IsBuiltIn: true);

    public async Task<FabInstallationScriptResult> InstallAsync(
        FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var project = context.Target.Project
            ?? throw new InvalidOperationException("Le script de contenu Unreal exige un projet cible.");
        progress?.Report(new FabInstallationScriptProgress(0, "Analyse du pack de contenu…"));
        var plan = await contentImport.PrepareAsync(
                context.SourceDirectory,
                project.FilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var conflictPolicy = context.Configuration.GetValueOrDefault("conflictPolicy", "backup-replace");
        if (plan.ConflictCount > 0 && conflictPolicy.Equals("fail", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Import arrêté : {plan.ConflictCount:N0} fichier(s) existent déjà dans le projet.");
        }
        var importProgress = new Progress<FabProjectImportProgress>(value =>
            progress?.Report(new FabInstallationScriptProgress(
                value.Total == 0 ? null : (double)value.Completed / value.Total,
                value.Message)));
        var result = await contentImport.ExecuteAsync(plan, importProgress, cancellationToken)
            .ConfigureAwait(false);
        return new FabInstallationScriptResult(
            Descriptor.Id,
            $"{result.ImportedFileCount:N0} fichier(s) importé(s), {result.ReplacedFileCount:N0} remplacé(s).",
            new Dictionary<string, string>
            {
                ["receipt"] = result.Receipt.ReceiptPath,
                ["backup"] = result.BackupDirectory ?? string.Empty,
            });
    }
}

public sealed class UnrealPluginInstallationScript(
    PluginDiscoveryService pluginDiscovery,
    IPluginInstallationService pluginInstallation) : IFabInstallationScript
{
    public const string ScriptId = "unreal.plugin";

    public FabInstallationScriptDescriptor Descriptor { get; } = new(
        ScriptId,
        "Unreal · Installer un plugin",
        "Détecte le .uplugin, vérifie sa compatibilité puis effectue une installation transactionnelle.",
        "CyEngineHub",
        [LibraryPackageContentKind.CodePlugin, LibraryPackageContentKind.EnginePackage],
        [FabInstallationTargetKind.Project, FabInstallationTargetKind.Engine],
        [
            new FabInstallationOptionDefinition(
                "pluginName",
                "Nom interne du plugin",
                "Laissez vide pour sélectionner automatiquement le premier plugin valide du package.",
                FabInstallationOptionKind.Text,
                string.Empty,
                []),
            new FabInstallationOptionDefinition(
                "targetPlatform",
                "Plateforme cible",
                "Plateforme utilisée pour l’analyse de compatibilité des modules.",
                FabInstallationOptionKind.Choice,
                "Win64",
                ["Win64", "Linux", "Mac"]),
        ],
        IsBuiltIn: true);

    public async Task<FabInstallationScriptResult> InstallAsync(
        FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var engine = context.Target.Engine
            ?? throw new InvalidOperationException("Aucun moteur Unreal n’est associé à la cible.");
        progress?.Report(new FabInstallationScriptProgress(0, "Recherche du descripteur .uplugin…"));
        var plugins = await pluginDiscovery.RefreshAsync(
                new PluginDiscoveryRequest([], [], [context.SourceDirectory], MaximumDepth: 10),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var requestedName = context.Configuration.GetValueOrDefault("pluginName", string.Empty).Trim();
        var plugin = plugins.FirstOrDefault(item =>
                         item.Health != PluginHealth.Broken &&
                         (requestedName.Length == 0 ||
                          item.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase) ||
                          item.FriendlyName.Equals(requestedName, StringComparison.OrdinalIgnoreCase)))
                     ?? throw new InvalidOperationException(requestedName.Length == 0
                         ? "Ce package ne contient aucun plugin .uplugin valide."
                         : $"Le plugin {requestedName} est introuvable dans ce package.");
        var installTarget = context.Target.Kind == FabInstallationTargetKind.Engine
            ? new PluginInstallTarget(
                context.Target.Id,
                $"Moteur · {context.Target.DisplayName}",
                PluginInstallTargetKind.Engine,
                Path.Combine(engine.RootPath, "Engine", "Plugins"),
                engine)
            : new PluginInstallTarget(
                context.Target.Id,
                $"Projet · {context.Target.DisplayName}",
                PluginInstallTargetKind.Project,
                Path.Combine(context.Target.RootPath, "Plugins"),
                engine);
        var targetPlatform = context.Configuration.GetValueOrDefault("targetPlatform", "Win64");
        var plan = await pluginInstallation.PlanAsync(
                plugin,
                installTarget,
                targetPlatform,
                cancellationToken)
            .ConfigureAwait(false);
        var pluginProgress = new Progress<PluginInstallProgress>(value =>
            progress?.Report(new FabInstallationScriptProgress(
                value.TotalFiles == 0 ? null : (double)value.CompletedFiles / value.TotalFiles,
                value.Message)));
        var result = await pluginInstallation.InstallAsync(plan, pluginProgress, cancellationToken)
            .ConfigureAwait(false);
        return new FabInstallationScriptResult(
            Descriptor.Id,
            $"Plugin {plugin.Name} installé dans {context.Target.DisplayName}.",
            new Dictionary<string, string>
            {
                ["destination"] = result.DestinationDirectory,
                ["backup"] = result.BackupDirectory ?? string.Empty,
            });
    }
}
