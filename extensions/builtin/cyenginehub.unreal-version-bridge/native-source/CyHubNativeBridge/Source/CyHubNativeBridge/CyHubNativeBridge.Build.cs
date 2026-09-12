using UnrealBuildTool;
public class CyHubNativeBridge : ModuleRules
{
    public CyHubNativeBridge(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        PublicDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine" });
        PrivateDependencyModuleNames.AddRange(new[] { "UnrealEd", "Json", "AssetRegistry" });
    }
}
