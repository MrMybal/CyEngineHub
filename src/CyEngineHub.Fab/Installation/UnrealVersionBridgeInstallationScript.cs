using System.Text.RegularExpressions;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Settings;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Unreal.Models;
using CyEngineHub.Plugins.Compatibility;
using CyEngineHub.Plugins.Discovery;
using CyEngineHub.Plugins.Installation;
using CyEngineHub.Plugins.Parsing;

namespace CyEngineHub.Fab.Installation;

public static class UnrealVersionBridgeSettings
{
    public const string StorageRoot = "plugins.unrealVersionBridge.storageRoot.v1";
    public const string ConversionProfile = "plugins.unrealVersionBridge.profile.v1";
    public const string KeepStaging = "plugins.unrealVersionBridge.keepStaging.v1";
    public const string ValidateOutput = "plugins.unrealVersionBridge.validateOutput.v1";
    public const string AssetProfileFile = "plugins.unrealVersionBridge.assetProfileFile.v1";
    public const string AgentPython = "plugins.unrealVersionBridge.agentPython.v1";
}

public sealed record UnrealVersionBridgeVariantManifest(
    int SchemaVersion,
    string AssetId,
    string ArtifactId,
    string SourceVersion,
    string TargetVersion,
    string Profile,
    int ImportedAssetCount,
    int UnsupportedAssetCount,
    IReadOnlyList<string> UnsupportedAssets,
    string ContentDirectory,
    DateTimeOffset GeneratedAt,
    string ConversionMode = "portable");

public sealed class UnrealVersionBridgeInstallationScript(
    IEngineCatalog engineCatalog,
    ISettingsStore settings,
    IProcessRunner processRunner,
    IFabProjectContentImportService contentImport,
    string defaultStorageRoot,
    string? nativePluginRoot = null) : IFabInstallationScript
{
    public const string ScriptId = "unreal.version-bridge";

    public FabInstallationScriptDescriptor Descriptor { get; } = new(
        ScriptId,
        "Unreal · Convertir puis installer",
        "Alpha · Conversion native contrôlée, avec réouverture cible. Assistance IA / MCP facultative pour réparer les blocages et adapter les plugins dans une copie. Compilation et validation obligatoires ; ni downgrade universel ni équivalence visuelle garantis.",
        "CyEngineHub",
        [LibraryPackageContentKind.ContentPack, LibraryPackageContentKind.RawAsset, LibraryPackageContentKind.Unknown,
            LibraryPackageContentKind.CodePlugin, LibraryPackageContentKind.EnginePackage],
        [FabInstallationTargetKind.Project],
        [
            new("enableAgentFallback", "Assistance IA / MCP si la conversion bloque", "Optionnelle : prépare une copie de réparation et une configuration MCP CyToolsCore. Aucun agent n’est lancé ni fournisseur sélectionné automatiquement ; aucune installation tant que la tâche n’est pas reprise et validée.", FabInstallationOptionKind.Boolean, "false", []),
            new("allowAgentCodeExecution", "Autoriser les scripts et la compilation de l’agent", "Attention : Python, C++, Build.cs et plugins s’exécutent avec les droits de votre compte Windows. Le dossier isolé n’est pas un bac à sable OS. Laissez désactivé pour une analyse en lecture seule.", FabInstallationOptionKind.Boolean, "false", []),
            new("agentWorkspace", "Reprendre une tâche IA (dossier)", "Vide : conversion habituelle. Sinon : dossier de réparation préparé pour ce pack/couple de moteurs. Le hub recompile le plugin et recharge tous les packages avant de publier et d’installer.", FabInstallationOptionKind.Text, string.Empty, []),
            new("conversionMode", "Méthode", "Natif : pixels source, graphes de matériaux et références pris en charge, contrôle C++ obligatoire. Portable : export/import expérimental sur accord explicite.", FabInstallationOptionKind.Choice, "native", ["native", "portable"]),
            new("assetProfileFile", "Profils par type (JSON)", "Vide : catalogue intégré adapté au couple (5.7 vers 5.5/5.3 ou 5.6 vers 5.5). Un fichier personnalisé n'active aucun code non implémenté.", FabInstallationOptionKind.Text, string.Empty, []),
            new("analyzeOnly", "Analyser seulement", "Produit un rapport par asset sans convertir, publier ni installer. Les diagnostics sont conservés.", FabInstallationOptionKind.Boolean, "false", []),
            new("registerDerivedVariant", "Ajouter le downgrade aux dérivés du pack", "Ajoute la conversion validée dans la fiche du pack pour la réinstaller sans reconvertir. Les originaux restent intacts. Désactivé : la sortie technique reste dans le dossier de variantes mais n’est pas ajoutée à la fiche.", FabInstallationOptionKind.Boolean, "true", []),
            new("allowLossyConversion", "Accepter les pertes du mode portable", "Consentement explicite à l'export/import : propriétés et données dérivées non garanties. Les assets non pris en charge restent bloquants.", FabInstallationOptionKind.Boolean, "false", []),
            new("sourceVersion", "Version Unreal source", "Auto choisit la version la plus récente déclarée par le pack.", FabInstallationOptionKind.Text, "auto", []),
            new("profile", "Ancien préréglage d'export", "Les profils par type gardent la priorité. Les exports squelettiques et animations non validés restent bloqués.", FabInstallationOptionKind.Choice, "safe", ["safe", "extended"]),
            new("variantStorageRoot", "Stockage des variantes", "Vide utilise le dossier défini dans l’interface du plugin.", FabInstallationOptionKind.Text, string.Empty, []),
            new("conflictPolicy", "Fichiers déjà présents", "Le remplacement avec sauvegarde autorise le rollback.", FabInstallationOptionKind.Choice, "backup-replace", ["backup-replace", "fail"]),
            new("keepStaging", "Conserver les projets temporaires", "Utile pour diagnostiquer un échec.", FabInstallationOptionKind.Boolean, "false", []),
            new("validateOutput", "Valider dans le moteur cible", "Recharge et sauvegarde les assets reconstruits.", FabInstallationOptionKind.Boolean, "true", []),
        ],
        IsBuiltIn: true);

    public async Task<FabInstallationScriptResult> InstallAsync(
        FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var project = context.Target.Project
            ?? throw new InvalidOperationException("La conversion exige un projet Unreal cible.");
        var targetEngine = context.Target.Engine
            ?? throw new InvalidOperationException("La version Unreal du projet cible n’est pas installée.");
        if (!bool.TryParse(context.Configuration.GetValueOrDefault("registerDerivedVariant", "true"), out var register))
            throw new InvalidOperationException("Option de dérivé invalide.");
        var engines = await engineCatalog.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var sourceVersion = ResolveSourceVersion(
            context.Configuration.GetValueOrDefault("sourceVersion", "auto"),
            context.Version.EngineVersions);
        var sourceEngine = SelectSourceEngine(engines, sourceVersion)
            ?? throw new InvalidOperationException(
                $"Unreal Engine {FormatVersion(sourceVersion)} doit être installé pour lire ce pack.");
        var analyzeOnly = bool.TryParse(context.Configuration.GetValueOrDefault("analyzeOnly"), out var analyze) && analyze;
        var pluginPack = context.ContentKind is LibraryPackageContentKind.CodePlugin or LibraryPackageContentKind.EnginePackage;
        if (Compare(sourceEngine.Version, targetEngine.Version) <= 0 && string.IsNullOrWhiteSpace(context.Configuration.GetValueOrDefault("agentWorkspace")) &&
            !(pluginPack && ParseAgentBoolean(context.Configuration, "enableAgentFallback") && Compare(sourceEngine.Version, targetEngine.Version) != 0))
        {
            if (analyzeOnly)
                throw new InvalidOperationException("L'analyse par profils concerne les conversions vers une version antérieure. Aucune installation effectuée.");
            if (context.Configuration.GetValueOrDefault("prepareOnly") == "true")
                return new(Descriptor.Id, "Aucune conversion requise ; projet cible inchangé.",
                    new Dictionary<string, string> { ["variant"] = context.SourceDirectory });
            progress?.Report(new(0, "Le pack n’est pas plus récent : installation standard."));
            if (pluginPack)
                return await InstallPluginAsync(context, progress, cancellationToken).ConfigureAwait(false);
            return await InstallContentAsync(context.SourceDirectory, project.FilePath, context.Configuration, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        var profile = NormalizeProfile(context.Configuration.GetValueOrDefault(
            "profile",
            await settings.GetAsync(UnrealVersionBridgeSettings.ConversionProfile, cancellationToken).ConfigureAwait(false) ?? "safe"));
        var configuredRoot = context.Configuration.GetValueOrDefault("variantStorageRoot", string.Empty);
        if (string.IsNullOrWhiteSpace(configuredRoot))
        {
            configuredRoot = await settings.GetAsync(UnrealVersionBridgeSettings.StorageRoot, cancellationToken).ConfigureAwait(false);
        }
        var storageRoot = string.IsNullOrWhiteSpace(configuredRoot) ? defaultStorageRoot : configuredRoot;
        var keepStaging = await ResolveBooleanAsync(context.Configuration, "keepStaging", UnrealVersionBridgeSettings.KeepStaging, false, cancellationToken)
            .ConfigureAwait(false);
        var validateOutput = await ResolveBooleanAsync(context.Configuration, "validateOutput", UnrealVersionBridgeSettings.ValidateOutput, true, cancellationToken)
            .ConfigureAwait(false);
        var service = new UnrealVersionBridgeService(processRunner, nativePluginRoot);
        var assetProfileFile = context.Configuration.GetValueOrDefault("assetProfileFile");
        if (string.IsNullOrWhiteSpace(assetProfileFile))
            assetProfileFile = await settings.GetAsync(UnrealVersionBridgeSettings.AssetProfileFile, cancellationToken).ConfigureAwait(false);
        var converted = await service.ConvertAsync(
                new(
                    context.Asset.AssetId,
                    context.Version.ArtifactId,
                    context.SourceDirectory,
                    sourceEngine,
                    targetEngine,
                    storageRoot,
                    profile,
                    keepStaging,
                    validateOutput,
                    context.Configuration.GetValueOrDefault("conversionMode", "native"),
                    assetProfileFile,
                    bool.TryParse(context.Configuration.GetValueOrDefault("allowLossyConversion"), out var allowLosses) && allowLosses,
                    analyzeOnly,
                    ParseAgentBoolean(context.Configuration, "enableAgentFallback"),
                    ParseAgentBoolean(context.Configuration, "allowAgentCodeExecution"),
                    context.Configuration.GetValueOrDefault("agentWorkspace"),
                    await settings.GetAsync(UnrealVersionBridgeSettings.AgentPython, cancellationToken).ConfigureAwait(false)),
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        if (converted.AnalysisOnly)
            return new(Descriptor.Id,
                $"Analyse préliminaire : {converted.UnsupportedAssetCount:N0} asset(s) bloquant(s). Contrôles C++ supplémentaires requis en natif. Aucune conversion ni installation. Rapport : {converted.ReportPath}",
                new Dictionary<string, string> { ["report"] = converted.ReportPath });
        if (converted.AgentWorkspace is not null)
            return new(Descriptor.Id, $"En attente d’un agent IA : {converted.AgentWorkspace}. Connectez mcp.json, puis renseignez ce dossier dans « Reprendre une tâche IA ». Aucun asset converti, publié ou installé.",
                new Dictionary<string, string> { ["agentPending"] = "true", ["agentWorkspace"] = converted.AgentWorkspace,
                    ["mcpConfig"] = Path.Combine(converted.AgentWorkspace, "mcp.json"), ["report"] = converted.ReportPath,
                    ["registerDerivedVariant"] = "false" });
        if (context.Configuration.GetValueOrDefault("prepareOnly") == "true")
            return new(Descriptor.Id, "Conversion préparée ; projet cible inchangé.", new Dictionary<string, string>
            {
                ["variant"] = converted.VariantDirectory, ["manifest"] = converted.ManifestPath,
                ["report"] = converted.ReportPath, ["sourceVersion"] = FormatVersion(targetEngine.Version),
                ["registerDerivedVariant"] = register ? "true" : "false",
            });
        FabInstallationScriptResult installed;
        if (converted.ContainsPlugins)
            installed = await InstallPluginAsync(context with { SourceDirectory = converted.VariantDirectory }, progress, cancellationToken).ConfigureAwait(false);
        else
            installed = await InstallContentAsync(converted.VariantDirectory, project.FilePath, context.Configuration, progress, cancellationToken).ConfigureAwait(false);
        return new(
            Descriptor.Id,
            $"Variante UE {FormatVersion(targetEngine.Version)} créée ({converted.ImportedAssetCount:N0} asset(s)) puis installée. {installed.Summary}",
            new Dictionary<string, string>
            {
                ["variant"] = converted.VariantDirectory,
                ["manifest"] = converted.ManifestPath,
                ["report"] = converted.ReportPath,
                ["registerDerivedVariant"] = register ? "true" : "false",
                ["receipt"] = installed.Outputs.GetValueOrDefault("receipt", string.Empty),
                ["backup"] = installed.Outputs.GetValueOrDefault("backup", string.Empty),
                ["destination"] = installed.Outputs.GetValueOrDefault("destination", string.Empty),
            });
    }

    private async Task<FabInstallationScriptResult> InstallContentAsync(
        string source,
        string project,
        IReadOnlyDictionary<string, string> configuration,
        IProgress<FabInstallationScriptProgress>? progress,
        CancellationToken cancellationToken)
    {
        var plan = await contentImport.PrepareAsync(source, project, cancellationToken).ConfigureAwait(false);
        if (plan.ConflictCount > 0 && configuration.GetValueOrDefault("conflictPolicy", "backup-replace") == "fail")
        {
            throw new InvalidOperationException($"Installation arrêtée : {plan.ConflictCount:N0} conflit(s).");
        }
        var importProgress = new Progress<FabProjectImportProgress>(value => progress?.Report(new(
            value.Total == 0 ? null : (double)value.Completed / value.Total,
            value.Message)));
        var result = await contentImport.ExecuteAsync(plan, importProgress, cancellationToken).ConfigureAwait(false);
        return new(
            Descriptor.Id,
            $"{result.ImportedFileCount:N0} fichier(s) installé(s).",
            new Dictionary<string, string>
            {
                ["receipt"] = result.Receipt.ReceiptPath,
                ["backup"] = result.BackupDirectory ?? string.Empty,
            });
    }

    private static Task<FabInstallationScriptResult> InstallPluginAsync(FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress, CancellationToken token)
    {
        var parser = new UpluginDescriptorParser();
        var installer = new UnrealProjectPluginInstallationScript(new PluginDiscoveryService(parser),
            new PluginInstallationService(new PluginCompatibilityService(), parser));
        return installer.InstallAsync(context, progress, token);
    }

    private async Task<bool> ResolveBooleanAsync(
        IReadOnlyDictionary<string, string> configuration,
        string configurationKey,
        string settingKey,
        bool fallback,
        CancellationToken cancellationToken)
    {
        var value = configuration.GetValueOrDefault(configurationKey, string.Empty);
        if (string.IsNullOrWhiteSpace(value)) value = await settings.GetAsync(settingKey, cancellationToken).ConfigureAwait(false);
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static bool ParseAgentBoolean(IReadOnlyDictionary<string, string> configuration, string key)
    {
        var value = configuration.GetValueOrDefault(key, "false");
        return bool.TryParse(value, out var parsed) ? parsed : throw new InvalidOperationException("Option IA invalide : " + key);
    }

    private static UnrealEngineInstallation? SelectSourceEngine(IReadOnlyList<UnrealEngineInstallation> engines, EngineVersion requested) => engines
        .Where(engine => engine.Version.Major == requested.Major && engine.Version.Minor == requested.Minor && File.Exists(ResolveCommandEditor(engine)))
        .OrderByDescending(engine => engine.Health == EngineHealth.Healthy)
        .ThenByDescending(engine => engine.Version.Patch)
        .FirstOrDefault();

    private static EngineVersion ResolveSourceVersion(string requested, IReadOnlyList<string> declared)
    {
        var candidates = requested.Equals("auto", StringComparison.OrdinalIgnoreCase) ? declared : [requested];
        var parsed = candidates.Select(ParseVersion)
            .Where(version => version.IsKnown)
            .OrderByDescending(version => version.Major)
            .ThenByDescending(version => version.Minor)
            .ThenByDescending(version => version.Patch)
            .FirstOrDefault();
        return parsed.IsKnown ? parsed : throw new InvalidOperationException("Configurez la version Unreal source du pack.");
    }

    private static EngineVersion ParseVersion(string value)
    {
        var match = Regex.Match(value ?? string.Empty, @"(?<major>\d+)\.(?<minor>\d+)(?:\.(?<patch>\d+))?");
        return match.Success
            ? new(
                int.Parse(match.Groups["major"].Value, System.Globalization.CultureInfo.InvariantCulture),
                int.Parse(match.Groups["minor"].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value, System.Globalization.CultureInfo.InvariantCulture) : 0)
            : EngineVersion.Unknown;
    }

    private static int Compare(EngineVersion left, EngineVersion right) =>
        (left.Major, left.Minor, left.Patch).CompareTo((right.Major, right.Minor, right.Patch));

    private static string FormatVersion(EngineVersion version) => $"{version.Major}.{version.Minor}";

    private static string NormalizeProfile(string value) => value.Equals("extended", StringComparison.OrdinalIgnoreCase) ? "extended" : "safe";

    internal static string ResolveCommandEditor(UnrealEngineInstallation engine)
    {
        var directory = Path.GetDirectoryName(engine.EditorPath);
        var command = directory is null ? string.Empty : Path.Combine(directory, OperatingSystem.IsWindows() ? "UnrealEditor-Cmd.exe" : "UnrealEditor-Cmd");
        return File.Exists(command) ? command : engine.EditorPath;
    }
}
