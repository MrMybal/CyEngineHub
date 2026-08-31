namespace CyEngineHub.Projects.Creation;

public static class ProjectFeaturePacks
{
    public static IReadOnlyList<ProjectFeaturePack> BuiltIn { get; } =
    [
        Pack("editor", "Éditeur essentiel", "Navigateur de contenu et gestionnaire de plugins.",
            ["EngineAssetDefinitions", "ContentBrowserAssetDataSource", "ContentBrowserFileDataSource", "PluginBrowser"]),
        Pack("input", "Enhanced Input", "Actions et contextes de mapping d’entrée.", ["EnhancedInput"]),
        Pack("gameplay", "Gameplay Abilities", "Abilities, effets et édition des Gameplay Tags.", ["GameplayAbilities", "GameplayTagsEditor"]),
        Pack("ui", "Interface Common UI", "Widgets Common UI et entrées communes.", ["CommonUI", "EnhancedInput"]),
        Pack("vr", "VR · OpenXR Forward", "OpenXR, rendu Forward et MSAA ; remplace les réglages incompatibles.",
            ["OpenXR", "XRBase", "EnhancedInput"],
            Render(("r.ForwardShading", "True"), ("r.AntiAliasingMethod", "3"),
                ("vr.InstancedStereo", "True"), ("r.DynamicGlobalIlluminationMethod", "0"),
                ("r.ReflectionMethod", "0"), ("r.Nanite.ProjectEnabled", "False"),
                ("r.Shadow.Virtual.Enable", "0"), ("r.RayTracing", "False"))),
        Pack("animation", "Animation · Rig & IK", "Control Rig et IK Rig.", ["ControlRig", "IKRig"]),
        Pack("vfx", "VFX · Niagara", "Systèmes de particules Niagara.", ["Niagara"]),
        Pack("audio", "Audio · MetaSounds", "Génération audio et widgets audio.", ["Metasound", "AudioWidgets"]),
        Pack("pcg", "Génération procédurale · PCG", "Outils PCG du moteur sélectionné.", ["PCG"]),
        Pack("modeling", "Modélisation & Geometry Script", "Modélisation dans l’éditeur et scripts de géométrie.",
            ["ModelingToolsEditorMode", "GeometryScripting"]),
        Pack("cinema", "Cinématique", "Movie Render Queue et scripting Sequencer.", ["MovieRenderPipeline", "SequencerScripting"]),
        Pack("2d", "Jeu 2D · Paper2D", "Sprites et flipbooks.", ["Paper2D"]),
        Pack("online", "Multijoueur · Online Subsystem", "Base réseau ; aucun fournisseur ou identifiant externe configuré.", ["OnlineSubsystem"]),
        Pack("python", "Automatisation · Python", "Scripts Python dans l’éditeur.", ["PythonScriptPlugin"]),
        Pack("ue5-render", "Rendu UE5 · Lumen & Nanite", "Réactive le pipeline UE5 par-dessus un profil léger.",
            [], Render(("r.ForwardShading", "False"), ("r.AntiAliasingMethod", "4"),
                ("r.DynamicGlobalIlluminationMethod", "1"), ("r.ReflectionMethod", "1"),
                ("r.Nanite.ProjectEnabled", "True"), ("r.Shadow.Virtual.Enable", "1"),
                ("r.GenerateMeshDistanceFields", "True"))),
    ];

    private static ProjectFeaturePack Pack(string id, string name, string description, string[] plugins,
        IReadOnlyList<ProjectIniSetting>? settings = null) =>
        new("unreal.feature." + id, name, description, plugins, [], settings ?? []);

    private static ProjectIniSetting[] Render(params (string Key, string Value)[] settings) =>
        settings.Select(pair => new ProjectIniSetting("DefaultEngine.ini",
            "/Script/Engine.RendererSettings", pair.Key, pair.Value)).ToArray();
}
