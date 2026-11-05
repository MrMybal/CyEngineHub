using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Fab.Installation;

public enum FabInstallationTargetKind
{
    Project,
    Engine,
}

public enum FabInstallationOptionKind
{
    Boolean,
    Text,
    Choice,
}

public sealed record FabInstallationOptionDefinition(
    string Id,
    string DisplayName,
    string Description,
    FabInstallationOptionKind Kind,
    string DefaultValue,
    IReadOnlyList<string> Choices,
    bool IsRequired = false);

public sealed record FabInstallationScriptDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string Publisher,
    IReadOnlyList<LibraryPackageContentKind> SupportedContentKinds,
    IReadOnlyList<FabInstallationTargetKind> SupportedTargetKinds,
    IReadOnlyList<FabInstallationOptionDefinition> Options,
    bool IsBuiltIn);

public sealed record FabInstallationScriptTarget(
    string Id,
    string DisplayName,
    FabInstallationTargetKind Kind,
    string RootPath,
    string EngineVersion,
    UnrealProject? Project,
    UnrealEngineInstallation? Engine);

public sealed record FabInstallationScriptContext(
    RemoteFabAsset Asset,
    RemoteFabProjectVersion Version,
    LibraryPackageContentKind ContentKind,
    string SourceDirectory,
    FabInstallationScriptTarget Target,
    IReadOnlyDictionary<string, string> Configuration);

public sealed record FabInstallationScriptProgress(
    double? Fraction,
    string Message);

public sealed record FabInstallationScriptResult(
    string ScriptId,
    string Summary,
    IReadOnlyDictionary<string, string> Outputs);

/**
 * @doc api-installation-script
 * @locale en
 * @title Installation script contract
 * @section SDK API
 * @symbol CyEngineHub.Fab.Installation.IFabInstallationScript
 * `Descriptor` describes the script and its configurable options. `InstallAsync`
 * receives an asset/version, its content kind, prepared source directory, target
 * and configuration. Report progress with `FabInstallationScriptProgress`, honor
 * cancellation and return `FabInstallationScriptResult`. Keep original packages
 * intact and validate destination paths. A successful return is not proof of
 * semantic compatibility in a different engine; inspect the generated reports.
 * See [installation](doc:install) and [extension SDK](doc:extensions-sdk).
 */
/**
 * @doc api-installation-script
 * @locale fr
 * @title Contrat des scripts d'installation
 * @section SDK API
 * @symbol CyEngineHub.Fab.Installation.IFabInstallationScript
 * `Descriptor` décrit le script et ses options. `InstallAsync` reçoit l'asset,
 * sa version, son type de contenu, les sources préparées, la cible et la configuration.
 * Signalez la progression avec `FabInstallationScriptProgress`, respectez l'annulation
 * et renvoyez `FabInstallationScriptResult`. Conservez les packages originaux et
 * validez les chemins cibles. Le succès d'une copie ne prouve pas la compatibilité
 * sémantique dans un autre moteur : consultez les rapports générés.
 * Voir [l'installation](doc:install) et [le SDK](doc:extensions-sdk).
 */
public interface IFabInstallationScript
{
    FabInstallationScriptDescriptor Descriptor { get; }

    Task<FabInstallationScriptResult> InstallAsync(
        FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
