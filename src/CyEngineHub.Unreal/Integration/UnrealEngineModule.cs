using CyEngineHub.Engines;

namespace CyEngineHub.Unreal.Integration;

public sealed class UnrealEngineModule : IEngineModule
{
    public EngineModuleDescriptor Descriptor { get; } = new(
        EngineFamily.Unreal,
        "unreal",
        "Unreal Engine",
        "Epic Games",
        EngineModuleStatus.Active,
        [".uproject", ".uplugin"],
        EngineCapability.InstallationDiscovery |
        EngineCapability.ProjectDiscovery |
        EngineCapability.EditorLaunch |
        EngineCapability.ProjectLaunch |
        EngineCapability.Build |
        EngineCapability.Package |
        EngineCapability.PluginManagement |
        EngineCapability.AssetLibrary |
        EngineCapability.SourceBuild |
        EngineCapability.VersionManagement,
        "Provider complet actuellement actif : installations, projets, builds, plugins, Fab et sources.");
}
