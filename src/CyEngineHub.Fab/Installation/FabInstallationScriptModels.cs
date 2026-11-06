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
 * @section API / Installation
 * @symbol CyEngineHub.Fab.Installation.IFabInstallationScript
 * `Descriptor` describes the script and its configurable options. `InstallAsync`
 * receives an asset/version, its content kind, prepared source directory, target
 * and configuration. Report progress with `FabInstallationScriptProgress`, honor
 * cancellation and return `FabInstallationScriptResult`. Keep original packages
 * intact and validate destination paths. A successful return is not proof of
 * semantic compatibility in a different engine; inspect the generated reports.
 *
 * ## Signature and imports {#signature}
 * ```csharp
 * using CyEngineHub.Fab.Installation;
 * Task<FabInstallationScriptResult> InstallAsync(
 *     FabInstallationScriptContext context,
 *     IProgress<FabInstallationScriptProgress>? progress = null,
 *     CancellationToken cancellationToken = default);
 * ```
 * ## Parameters and result {#parameters}
 * `context` supplies `Asset`, `Version`, `ContentKind`, `SourceDirectory`,
 * `Target` and string-valued `Configuration`. A target identifies its kind,
 * root path, engine version and optional project/engine model. `progress` is
 * optional and reports a nullable `Fraction` plus `Message`.
 * Respect cancellation. The task returns `ScriptId`, `Summary` and `Outputs`;
 * output paths must identify actual contained results, not arbitrary files.
 * ## Calling a trusted script {#example}
 * ```csharp
 * FabInstallationScriptResult result = await script.InstallAsync(
 *     context, progress: null, cancellationToken: cancellationToken);
 * ```
 * `script` is an approved `IFabInstallationScript`; the host supplies the reviewed
 * context. This is not permission to execute code contained in an asset download.
 * See [installation](doc:install) and [extension SDK](doc:extensions-sdk).
 */
/**
 * @doc api-installation-script
 * @locale fr
 * @title Contrat des scripts d'installation
 * @section API / Installation
 * @symbol CyEngineHub.Fab.Installation.IFabInstallationScript
 * `Descriptor` décrit le script et ses options. `InstallAsync` reçoit l'asset,
 * sa version, son type de contenu, les sources préparées, la cible et la configuration.
 * Signalez la progression avec `FabInstallationScriptProgress`, respectez l'annulation
 * et renvoyez `FabInstallationScriptResult`. Conservez les packages originaux et
 * validez les chemins cibles. Le succès d'une copie ne prouve pas la compatibilité
 * sémantique dans un autre moteur : consultez les rapports générés.
 *
 * ## Signature et imports {#signature}
 * ```csharp
 * using CyEngineHub.Fab.Installation;
 * Task<FabInstallationScriptResult> InstallAsync(
 *     FabInstallationScriptContext context,
 *     IProgress<FabInstallationScriptProgress>? progress = null,
 *     CancellationToken cancellationToken = default);
 * ```
 * ## Paramètres et résultat {#parameters}
 * `context` fournit `Asset`, `Version`, `ContentKind`, `SourceDirectory`, `Target`
 * et `Configuration`, dont les valeurs sont des chaînes. La cible indique son
 * type, chemin racine, version moteur et modèle projet/moteur éventuel.
 * `progress` est facultatif : il transmet `Fraction`, éventuellement null, et
 * `Message`. Respectez l’annulation. La tâche renvoie `ScriptId`, `Summary` et
 * `Outputs` ; les chemins doivent désigner de vrais résultats contenus dans la
 * destination prévue, pas des fichiers arbitraires.
 * ## Appeler un script de confiance {#example}
 * ```csharp
 * FabInstallationScriptResult result = await script.InstallAsync(
 *     context, progress: null, cancellationToken: cancellationToken);
 * ```
 * `script` est un `IFabInstallationScript` approuvé ; l’hôte fournit le contexte
 * relu. Cela n’autorise pas à exécuter le code d’un téléchargement d’asset.
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
