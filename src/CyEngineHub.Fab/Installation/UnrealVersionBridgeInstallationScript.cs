using System.Text.RegularExpressions;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Settings;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Fab.Installation;

public static class UnrealVersionBridgeSettings
{
    public const string StorageRoot = "plugins.unrealVersionBridge.storageRoot.v1";
    public const string ConversionProfile = "plugins.unrealVersionBridge.profile.v1";
    public const string KeepStaging = "plugins.unrealVersionBridge.keepStaging.v1";
    public const string ValidateOutput = "plugins.unrealVersionBridge.validateOutput.v1";
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
    DateTimeOffset GeneratedAt);

public sealed class UnrealVersionBridgeInstallationScript(
    IEngineCatalog engineCatalog,
    ISettingsStore settings,
    IProcessRunner processRunner,
    IFabProjectContentImportService contentImport,
    string defaultStorageRoot) : IFabInstallationScript
{
    public const string ScriptId = "unreal.version-bridge";

    public FabInstallationScriptDescriptor Descriptor { get; } = new(
        ScriptId,
        "Unreal · Convertir puis installer",
        "Exporte avec le moteur source, reconstruit avec la version cible, crée une variante locale puis l’installe.",
        "CyEngineHub",
        [LibraryPackageContentKind.ContentPack, LibraryPackageContentKind.RawAsset, LibraryPackageContentKind.Unknown],
        [FabInstallationTargetKind.Project],
        [
            new("sourceVersion", "Version Unreal source", "Auto choisit la version la plus récente déclarée par le pack.", FabInstallationOptionKind.Text, "auto", []),
            new("profile", "Profil de conversion", "Sûr limite les types; étendu tente aussi skeletal meshes, animations, sons et tables.", FabInstallationOptionKind.Choice, "safe", ["safe", "extended"]),
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
        var engines = await engineCatalog.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var sourceVersion = ResolveSourceVersion(
            context.Configuration.GetValueOrDefault("sourceVersion", "auto"),
            context.Version.EngineVersions);
        var sourceEngine = SelectSourceEngine(engines, sourceVersion)
            ?? throw new InvalidOperationException(
                $"Unreal Engine {FormatVersion(sourceVersion)} doit être installé pour lire ce pack.");
        if (Compare(sourceEngine.Version, targetEngine.Version) <= 0)
        {
            progress?.Report(new(0, "Le pack n’est pas plus récent : installation standard."));
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
        var service = new UnrealVersionBridgeService(processRunner);
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
                    validateOutput),
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        var installed = await InstallContentAsync(converted.VariantDirectory, project.FilePath, context.Configuration, progress, cancellationToken)
            .ConfigureAwait(false);
        return new(
            Descriptor.Id,
            $"Variante UE {FormatVersion(targetEngine.Version)} créée ({converted.ImportedAssetCount:N0} asset(s)) puis installée. {installed.Summary}",
            new Dictionary<string, string>
            {
                ["variant"] = converted.VariantDirectory,
                ["manifest"] = converted.ManifestPath,
                ["report"] = converted.ReportPath,
                ["receipt"] = installed.Outputs.GetValueOrDefault("receipt", string.Empty),
                ["backup"] = installed.Outputs.GetValueOrDefault("backup", string.Empty),
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
