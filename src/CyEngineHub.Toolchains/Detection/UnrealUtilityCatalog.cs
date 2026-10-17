namespace CyEngineHub.Toolchains.Detection;

public sealed record UnrealUtility(string Id, string Name, string Description, string? ExecutablePath,
    string SearchLocation, bool ChangesSystem)
{
    public bool IsAvailable => ExecutablePath is not null;
    public string Status => IsAvailable ? "Exécutable trouvé" : "Non trouvé";
}

/// <summary>Inspects explicit known locations only; never starts an executable.</summary>
public static class UnrealUtilityCatalog
{
    public static IReadOnlyList<UnrealUtility> Discover(string? engineRoot, IReadOnlyList<string> launcherRoots)
    {
        var root = string.IsNullOrWhiteSpace(engineRoot) ? null : Path.GetFullPath(engineRoot);
        string[] EnginePaths(params string[] paths) => root is null ? [] : paths.Select(path => Path.GetFullPath(Path.Combine(root, path))).ToArray();
        UnrealUtility Find(string id, string name, string description, bool changesSystem, string[] paths) =>
            new(id, name, description, paths.FirstOrDefault(File.Exists),
                paths.Length == 0 ? "Sélectionnez un moteur installé." : string.Join(Environment.NewLine, paths), changesSystem);
        var selectors = EnginePaths("Engine/Binaries/Win64/UnrealVersionSelector.exe",
                "Engine/Binaries/Win64/UnrealVersionSelector-Win64-Shipping.exe",
                "Engine/Binaries/Win64/UnrealEngineVersionSelector.exe")
            .Concat(launcherRoots.Select(launcherRoot => Path.GetFullPath(Path.Combine(launcherRoot, "Engine/Binaries/Win64/UnrealVersionSelector.exe")))).ToArray();
        return
        [
            Find("prerequisites", "Prérequis Unreal (x64)",
                "Installateur des composants nécessaires à l’exécution d’Unreal. Sa présence ne signifie pas que les prérequis sont déjà installés. L’assistant peut demander des droits administrateur.", true,
                EnginePaths("Engine/Extras/Redist/en-us/UEPrereqSetup_x64.exe", "Engine/Extras/Redist/en-us/UE4PrereqSetup_x64.exe")),
            Find("version-selector", "Unreal Version Selector",
                "Sélecteur et associations des fichiers .uproject. Son lancement peut proposer l’enregistrement du moteur ou la modification des associations Windows. Aucune option silencieuse n’est ajoutée.", true, selectors),
            Find("insights", "Unreal Insights", "Analyse des traces et performances Unreal.", false,
                EnginePaths("Engine/Binaries/Win64/UnrealInsights.exe", "Engine/Binaries/Win64/UnrealInsights-Win64-Shipping.exe")),
            Find("swarm", "Swarm Agent", "Outil Unreal de calcul distribué Lightmass. Sa disponibilité dépend de l’installation du moteur.", false,
                EnginePaths("Engine/Binaries/DotNET/SwarmAgent.exe")),
        ];
    }
}
