using CyEngineHub.Engines;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _changingProjectCodeKind;
    public IReadOnlyList<string> ProjectCreationFamilies => ProjectCreationPresets
        .Select(item => ProjectCreationFamilyName(item.Preset.Engine)).Distinct(StringComparer.Ordinal).ToArray();
    public string SelectedProjectCreationFamily
    {
        get => SelectedProjectCreationPreset is { } preset ? ProjectCreationFamilyName(preset.Preset.Engine) : string.Empty;
        set
        {
            if (value == SelectedProjectCreationFamily) return;
            var candidates = ProjectCreationPresets.Where(item => ProjectCreationFamilyName(item.Preset.Engine) == value).ToArray();
            SelectedProjectCreationPreset = candidates.FirstOrDefault(item => item.Preset.IsCodeProject == (SelectedProjectCreationPreset?.Preset.IsCodeProject == true))
                ?? candidates.FirstOrDefault();
        }
    }
    public IReadOnlyList<string> ProjectCreationCodeKinds { get; } = ["Blueprint uniquement", "C++"];
    public string SelectedProjectCreationCodeKind
    {
        get => SelectedProjectCreationPreset?.Preset.IsCodeProject == true ? "C++" : "Blueprint uniquement";
        set
        {
            if (!IsUnrealProjectCreation || !ProjectCreationCodeKinds.Contains(value) || value == SelectedProjectCreationCodeKind) return;
            var preset = ProjectCreationPresets.FirstOrDefault(item => item.Preset.Engine == EngineFamily.Unreal &&
                item.Preset.IsCodeProject == (value == "C++") && !item.Preset.UsesLegacyRenderingProfile);
            if (preset is null) return;
            // Code/Blueprint is independent from the chosen plugins, feature packs and INI overrides.
            _changingProjectCodeKind = true;
            try { SelectedProjectCreationPreset = preset; }
            finally { _changingProjectCodeKind = false; }
        }
    }
    public string ProjectCreationCodeKindDescription => SelectedProjectCreationPreset?.Preset.IsCodeProject == true
        ? "Modules C++ et cibles Editor / Game générés. Les Blueprints restent disponibles."
        : "Projet sans code C++ ni dossier Source. Vous pourrez ajouter du C++ plus tard dans Unreal.";
    public string ProjectCreationDestinationPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(NewProjectParentDirectory) || string.IsNullOrWhiteSpace(NewProjectName))
                return "Le nouveau sous-dossier portera le nom du projet. Choisissez un dossier parent existant.";
            var name = NewProjectName.Trim();
            if (name.Length > 64 || !char.IsAsciiLetter(name[0]) || name.Any(value => !char.IsAsciiLetterOrDigit(value) && value != '_'))
                return "Nom invalide : 64 caractères maximum, lettres A–Z, chiffres et underscore ; commencez par une lettre.";
            try { return "Dossier du nouveau projet : " + Path.GetFullPath(Path.Combine(NewProjectParentDirectory.Trim(), NewProjectName.Trim())); }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            { return "Nom ou dossier invalide : vérifiez les champs ci-dessus."; }
        }
    }
    private static string ProjectCreationFamilyName(EngineFamily family) => family switch
    {
        EngineFamily.Unreal => "Unreal Engine",
        EngineFamily.CryEngine => "CRYENGINE",
        _ => family.ToString(),
    };
}
