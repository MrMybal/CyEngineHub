using UnrealBuildTool;
public class CyHubNativeBridge : ModuleRules
{
    public CyHubNativeBridge(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        bUseUnity = false;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine" });
        PrivateDependencyModuleNames.AddRange(new[] { "UnrealEd", "Json", "AssetRegistry", "MeshDescription", "StaticMeshDescription", "PhysicsCore", "Foliage", "BlueprintGraph", "Kismet", "KismetCompiler", "BSPUtils" });
    }
}
