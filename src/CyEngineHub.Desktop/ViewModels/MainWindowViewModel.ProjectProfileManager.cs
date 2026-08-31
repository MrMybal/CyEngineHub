using System.Windows.Input;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private ProjectCreationConfigurationOptionViewModel? _selectedManagedProjectProfile;
    private bool _isRebuildingProjectProfileOptions;

    public ICommand ShowProjectProfilesCommand { get; private set; } = null!;
    public ICommand EditManagedProjectProfileCommand { get; private set; } = null!;
    public ICommand DuplicateManagedProjectProfileCommand { get; private set; } = null!;
    public bool IsSettingsProjectProfilesSelected => SelectedSettingsCategory?.Id == "project-profiles";
    private bool IsStandaloneProjectProfileEditing => IsSettingsVisible && IsSettingsProjectProfilesSelected && !IsProjectCreatorVisible;

    public ProjectCreationConfigurationOptionViewModel? SelectedManagedProjectProfile
    {
        get => _selectedManagedProjectProfile;
        set
        {
            if (_isRebuildingProjectProfileOptions) return;
            if (SetProperty(ref _selectedManagedProjectProfile, value))
            {
                OnPropertyChanged(nameof(HasManagedProjectProfile));
                OnPropertyChanged(nameof(ManagedProjectProfileEditLabel));
            }
        }
    }
    public bool HasManagedProjectProfile => SelectedManagedProjectProfile is not null;
    public string ManagedProjectProfileEngineSummary => IsProjectCreationPluginLoading
        ? "Inventaire des plugins en arrière-plan…"
        : SelectedProjectCreationEngine is { } engine
            ? $"{_projectCreationEnginePlugins.Length:N0} plugin(s) disponibles dans {engine.Name}. Aucun fichier moteur ne sera modifié."
            : "Sélectionnez un moteur installé pour proposer ses plugins dans l’éditeur.";
    public string ManagedProjectProfileEditLabel => SelectedManagedProjectProfile?.IsBuiltIn == true
        ? "Personnaliser une copie" : "Modifier le profil";

    private void InitializeProjectProfileManager()
    {
        ShowProjectProfilesCommand = new RelayCommand(() =>
        {
            IsProjectCreatorVisible = false;
            OpenSettings();
            SelectedSettingsCategory = SettingsCategories.First(category => category.Id == "project-profiles");
            SelectedManagedProjectProfile ??= SelectedProjectCreationConfiguration;
        });
        EditManagedProjectProfileCommand = new RelayCommand(() =>
            OpenProjectProfile(SelectedManagedProjectProfile?.Profile));
        DuplicateManagedProjectProfileCommand = new RelayCommand(() =>
            OpenProjectProfile(SelectedManagedProjectProfile?.Profile, duplicate: true));
    }
}
