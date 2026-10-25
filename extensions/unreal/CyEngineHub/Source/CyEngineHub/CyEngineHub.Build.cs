using UnrealBuildTool;

public class CyEngineHub : ModuleRules
{
    public CyEngineHub(ReadOnlyTargetRules Target) : base(Target)
    {
        PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;
        bUseUnity = false;
        PrivateDependencyModuleNames.AddRange(new[] { "Core", "CoreUObject", "Engine", "Slate", "SlateCore", "UnrealEd", "LevelEditor", "ToolMenus", "Json", "AssetRegistry" });
        if (Target.Platform == UnrealTargetPlatform.Win64)
            PublicSystemLibraries.AddRange(new[] { "bcrypt.lib", "secur32.lib" });
    }
}
