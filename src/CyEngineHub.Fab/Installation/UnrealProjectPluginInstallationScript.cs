using CyEngineHub.Fab.Packaging;
using CyEngineHub.Plugins.Discovery;
using CyEngineHub.Plugins.Installation;

namespace CyEngineHub.Fab.Installation;

/// <summary>Project-only entry point; extraction and transactional copying are shared
/// with the engine installer so the original plugin mount point remains unchanged.</summary>
public sealed class UnrealProjectPluginInstallationScript(
    PluginDiscoveryService pluginDiscovery,
    IPluginInstallationService pluginInstallation) : IFabInstallationScript
{
    public const string ScriptId = "unreal.project-plugin";
    private readonly UnrealPluginInstallationScript _installer = new(pluginDiscovery, pluginInstallation);

    public FabInstallationScriptDescriptor Descriptor => _installer.Descriptor with
    {
        Id = ScriptId,
        DisplayName = "Unreal · Plugin local au projet",
        Description = "Recherche le .uplugin dans le pack, y compris Engine/Plugins/Marketplace, puis installe uniquement sa racine dans Projet/Plugins. Le moteur et le pack source restent intacts.",
        SupportedContentKinds = [LibraryPackageContentKind.CodePlugin, LibraryPackageContentKind.EnginePackage],
        SupportedTargetKinds = [FabInstallationTargetKind.Project],
    };

    public async Task<FabInstallationScriptResult> InstallAsync(
        FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (context.Target.Kind != FabInstallationTargetKind.Project || context.Target.Project is null)
            throw new InvalidOperationException("Le script de plugin local exige un projet cible.");
        var result = await _installer.InstallAsync(context, progress, cancellationToken).ConfigureAwait(false);
        return result with { ScriptId = ScriptId };
    }
}
