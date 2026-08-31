using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using CyEngineHub.Engines;
using CyEngineHub.Projects.Creation;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private const string ProjectFeaturePacksSetting = "projects.creation.feature-packs.v1";
    private readonly List<ProjectFeaturePack> _customProjectFeaturePacks = [];
    private readonly Dictionary<string, bool> _manualProjectPlugins = new(StringComparer.OrdinalIgnoreCase);
    private ProjectConfigurationProfile? _effectiveProjectProfile;
    private bool _isRecomposingProject;
    private int _projectCreatorTabIndex;
    private bool _isEditingProjectFeaturePack;
    private string? _editingFeaturePackId;
    private string? _duplicatedFeaturePackId;
    private bool _projectProfileDisableEnginePluginsByDefault;
    private ProjectIniDocumentViewModel? _selectedProjectIniDocument;
    private string _newProjectIniFileName = "DefaultEngine.ini";
    private string _projectCompositionIssues = string.Empty;
    private bool _isProjectCreationPluginLoading;
    private string _projectIniPreviewError = string.Empty;

    public ObservableCollection<ProjectFeaturePackOptionViewModel> ProjectCreationFeaturePacks { get; } = [];
    public ObservableCollection<ProjectIniDocumentViewModel> ProjectCreationIniDocuments { get; } = [];
    public int ProjectCreatorTabIndex { get => _projectCreatorTabIndex; set => SetProperty(ref _projectCreatorTabIndex, value); }
    public ICommand NewProjectFeaturePackCommand { get; private set; } = null!;
    public ICommand ResetProjectCompositionCommand { get; private set; } = null!;
    public ICommand ResetProjectIniDocumentCommand { get; private set; } = null!;
    public ICommand AddProjectIniDocumentCommand { get; private set; } = null!;
    public ProjectIniDocumentViewModel? SelectedProjectIniDocument
    {
        get => _selectedProjectIniDocument;
        set => SetProperty(ref _selectedProjectIniDocument, value);
    }
    public string NewProjectIniFileName { get => _newProjectIniFileName; set => SetProperty(ref _newProjectIniFileName, value); }
    public string ProjectCompositionIssues
    {
        get => _projectCompositionIssues;
        private set { if (SetProperty(ref _projectCompositionIssues, value)) OnPropertyChanged(nameof(CanCreateProjectConfiguration)); }
    }
    public bool IsProjectCreationPluginLoading
    {
        get => _isProjectCreationPluginLoading;
        private set
        {
            if (SetProperty(ref _isProjectCreationPluginLoading, value))
            {
                OnPropertyChanged(nameof(CanCreateProjectConfiguration));
                OnPropertyChanged(nameof(ManagedProjectProfileEngineSummary));
            }
        }
    }
    public bool CanCreateProjectConfiguration => !IsUnrealProjectCreation ||
        (SelectedProjectCreationEngine is not null && !IsProjectCreationPluginLoading &&
         _projectCreationEnginePlugins.Length > 0 && ProjectCompositionIssues.Length == 0 &&
         _projectIniPreviewError.Length == 0 && ProjectCreationIniDocuments.All(document => document.Error.Length == 0));
    public string ProjectCompositionSummary =>
        $"{SelectedProjectCreationConfiguration?.Name ?? "Sans profil"} · {ProjectCreationFeaturePacks.Count(pack => pack.IsSelected)} pack(s) · " +
        $"{ProjectCreationPlugins.Count(plugin => plugin.IsActive)} plugins actifs, dépendances incluses · {ProjectCreationIniDocuments.Count} fichier(s) INI";
    public string ProjectPluginPolicySummary => _effectiveProjectProfile?.DisableEnginePluginsByDefault == true
        ? "Base minimale : plugins moteur désactivés par défaut. Seuls les choix explicites et leurs dépendances sont activés."
        : "Base moteur : les plugins activés par défaut restent hérités, sauf override du profil, des packs ou de vos choix.";
    public bool ProjectProfileDisableEnginePluginsByDefault
    {
        get => _projectProfileDisableEnginePluginsByDefault;
        set => SetProperty(ref _projectProfileDisableEnginePluginsByDefault, value);
    }
    public bool IsEditingProjectFeaturePack
    {
        get => _isEditingProjectFeaturePack;
        private set
        {
            if (SetProperty(ref _isEditingProjectFeaturePack, value))
            {
                OnPropertyChanged(nameof(ProjectProfileEditorTitle));
                OnPropertyChanged(nameof(ProjectProfileSaveLabel));
                OnPropertyChanged(nameof(ProjectProfileDeleteLabel));
                OnPropertyChanged(nameof(CanDeleteSelectedProjectProfile));
            }
        }
    }
    public string ProjectProfileEditorTitle => IsEditingProjectFeaturePack ? "ÉDITEUR DE PACK DE FONCTIONNALITÉS" : "ÉDITEUR DE PROFIL UNREAL";
    public string ProjectProfileSaveLabel => IsEditingProjectFeaturePack ? "Enregistrer le pack" : "Enregistrer le profil";
    public string ProjectProfileDeleteLabel => IsEditingProjectFeaturePack ? "Supprimer le pack" : "Supprimer le profil";

    private void InitializeProjectComposition()
    {
        NewProjectFeaturePackCommand = new RelayCommand(() =>
        {
            OpenNewProjectProfile();
            _editingFeaturePackId = null;
            _duplicatedFeaturePackId = null;
            IsEditingProjectFeaturePack = true;
            ProjectProfileName = "Nouveau pack de fonctionnalités";
            ProjectProfileDescription = "Plugins et réglages ajoutés par-dessus le profil.";
            OnPropertyChanged(nameof(CanDeleteSelectedProjectProfile));
        });
        ResetProjectCompositionCommand = new RelayCommand(() =>
        {
            _manualProjectPlugins.Clear();
            ProjectCreationIniDocuments.Clear();
            RecomposeProjectConfiguration();
        });
        ResetProjectIniDocumentCommand = new RelayCommand(() =>
        {
            if (SelectedProjectIniDocument is { } document) ProjectCreationIniDocuments.Remove(document);
            RebuildProjectIniPreview();
        });
        AddProjectIniDocumentCommand = new RelayCommand(() =>
        {
            try
            {
                var name = UnrealProjectIni.NormalizeFileName(NewProjectIniFileName);
                var document = ProjectCreationIniDocuments.FirstOrDefault(item => item.FileName.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (document is null)
                {
                    document = new(name, "; Ajoutez [Section] puis Cle=Valeur." + Environment.NewLine, OnProjectIniEdited, isEdited: true);
                    ProjectCreationIniDocuments.Add(document);
                }
                SelectedProjectIniDocument = document;
                RecomposeProjectConfiguration();
            }
            catch (ArgumentException exception) { ProjectCompositionIssues = exception.Message; }
        });
        RebuildProjectFeaturePacks();
    }

    private void RebuildProjectFeaturePacks(string? selectId = null)
    {
        var selected = ProjectCreationFeaturePacks.Where(item => item.IsSelected).Select(item => item.Pack.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_duplicatedFeaturePackId is not null) selected.Remove(_duplicatedFeaturePackId);
        if (selectId is not null) selected.Add(selectId);
        var order = ProjectCreationFeaturePacks.Select((item, index) => (item.Pack.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.OrdinalIgnoreCase);
        _isRecomposingProject = true;
        try
        {
            ProjectCreationFeaturePacks.Clear();
            foreach (var pack in ProjectFeaturePacks.BuiltIn.Concat(_customProjectFeaturePacks)
                         .OrderBy(pack => order.GetValueOrDefault(pack.Id, int.MaxValue)))
            {
                ProjectCreationFeaturePacks.Add(new(pack, RecomposeProjectConfiguration, OpenProjectFeaturePack, MoveProjectFeaturePack)
                    { IsSelected = selected.Contains(pack.Id), Order = ProjectCreationFeaturePacks.Count + 1 });
            }
        }
        finally { _isRecomposingProject = false; }
        RecomposeProjectConfiguration();
    }

    private void MoveProjectFeaturePack(ProjectFeaturePackOptionViewModel pack, int delta)
    {
        var index = ProjectCreationFeaturePacks.IndexOf(pack);
        var next = index + delta;
        if (index < 0 || next < 0 || next >= ProjectCreationFeaturePacks.Count) return;
        ProjectCreationFeaturePacks.Move(index, next);
        for (var i = 0; i < ProjectCreationFeaturePacks.Count; i++) ProjectCreationFeaturePacks[i].Order = i + 1;
        RecomposeProjectConfiguration();
    }

    private ProjectConfigurationProfile? ComposeProjectConfiguration()
    {
        var profile = SelectedProjectCreationConfiguration?.Profile;
        if (profile is null) return null;
        profile = profile with { EnabledPlugins = profile.EnabledPlugins.Concat(
            SelectedProjectCreationPreset?.Preset.DefaultPlugins ?? []).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() };
        return ProjectConfigurationComposer.Compose(profile,
            ProjectCreationFeaturePacks.Where(item => item.IsSelected).Select(item => item.Pack), _manualProjectPlugins);
    }

    private void RecomposeProjectConfiguration()
    {
        if (_isRecomposingProject) return;
        _isRecomposingProject = true;
        try
        {
            _effectiveProjectProfile = ComposeProjectConfiguration();
            var enabled = (_effectiveProjectProfile?.EnabledPlugins ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var disabled = (_effectiveProjectProfile?.DisabledPlugins ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
            NewProjectPlugins = string.Join(", ", enabled);
            NewProjectDisabledPlugins = string.Join(", ", disabled);
            var inventory = _projectCreationEnginePlugins.GroupBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var active = inventory.Values.Where(plugin => plugin.EnabledByDefault == true &&
                    _effectiveProjectProfile?.DisableEnginePluginsByDefault != true && !disabled.Contains(plugin.Name))
                .Select(plugin => plugin.Name).Concat(enabled).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dependencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var issues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>(active);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (queue.TryDequeue(out var name))
            {
                if (!visited.Add(name)) continue;
                if (!inventory.TryGetValue(name, out var descriptor))
                {
                    if (inventory.Count > 0) issues.Add("Plugin absent : " + name);
                    continue;
                }
                foreach (var dependency in descriptor.Dependencies.Where(item => item.Enabled))
                {
                    if (dependency.Optional && !inventory.ContainsKey(dependency.Name)) continue;
                    if (disabled.Contains(dependency.Name))
                    {
                        issues.Add(name + " exige " + dependency.Name + ", explicitement désactivé.");
                        continue;
                    }
                    dependencies.Add(dependency.Name);
                    if (active.Add(dependency.Name)) queue.Enqueue(dependency.Name);
                }
            }

            var existing = ProjectCreationPlugins.ToDictionary(option => option.Name, StringComparer.OrdinalIgnoreCase);
            var options = new List<ProjectProfilePluginOptionViewModel>();
            foreach (var name in inventory.Keys.Concat(enabled).Concat(disabled).Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(name => inventory.GetValueOrDefault(name)?.FriendlyName ?? name, StringComparer.CurrentCultureIgnoreCase))
            {
                var plugin = inventory.GetValueOrDefault(name);
                bool? state = _manualProjectPlugins.TryGetValue(name, out var manual) ? manual : null;
                var origin = dependencies.Contains(name) ? "Dépendance d’un plugin actif" :
                    enabled.Contains(name) || disabled.Contains(name) ? "Profil / pack de fonctionnalités" :
                    _effectiveProjectProfile?.DisableEnginePluginsByDefault == true ? "Base minimale · désactivé" : "Valeur du moteur (héritée)";
                if (existing.TryGetValue(name, out var retained) &&
                    retained.FriendlyName == (plugin?.FriendlyName ?? name) &&
                    retained.Category == (plugin?.Category ?? "Absent") &&
                    retained.Source == (plugin?.SourceLabel ?? "Absent du moteur sélectionné"))
                {
                    retained.UpdateComposition(state, active.Contains(name) && !disabled.Contains(name), origin);
                    options.Add(retained);
                    continue;
                }
                var option = new ProjectProfilePluginOptionViewModel(name, plugin?.FriendlyName ?? name,
                    plugin?.Category ?? "Absent", plugin?.SourceLabel ?? "Absent du moteur sélectionné",
                    state, active.Contains(name) && !disabled.Contains(name), origin);
                option.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName != nameof(ProjectProfilePluginOptionViewModel.State) || _isRecomposingProject) return;
                    if (option.State.HasValue) _manualProjectPlugins[option.Name] = option.State.Value;
                    else _manualProjectPlugins.Remove(option.Name);
                    RecomposeProjectConfiguration();
                };
                options.Add(option);
            }
            if (!ProjectCreationPlugins.SequenceEqual(options))
            {
                ProjectCreationPlugins.Clear();
                foreach (var option in options) ProjectCreationPlugins.Add(option);
            }
            var category = SelectedProjectCreationPluginCategory;
            var categories = ProjectCreationPlugins.Select(item => item.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
                .Prepend("Toutes les catégories").ToArray();
            if (!ProjectCreationPluginCategories.SequenceEqual(categories)) ProjectCreationPluginCategories.ReplaceWith(categories);
            SelectedProjectCreationPluginCategory = ProjectCreationPluginCategories.Contains(category) ? category : "Toutes les catégories";
            ApplyProjectCreationPluginFilter();
            ProjectCompositionIssues = string.Join(Environment.NewLine, issues.Take(12));
            RebuildProjectIniPreview();
            UpdateProjectCreationPluginCompatibilitySummary();
            OnPropertyChanged(nameof(ProjectCreationPluginCount));
            OnPropertyChanged(nameof(ProjectCreationOverriddenPluginCount));
            OnPropertyChanged(nameof(ProjectCompositionSummary));
            OnPropertyChanged(nameof(ProjectPluginPolicySummary));
            OnPropertyChanged(nameof(CanCreateProjectConfiguration));
        }
        catch (ArgumentException exception) { ProjectCompositionIssues = exception.Message; }
        finally { _isRecomposingProject = false; }
    }

    private void RebuildProjectIniPreview()
    {
        if (!IsUnrealProjectCreation) return;
        var profile = _effectiveProjectProfile ?? ComposeProjectConfiguration();
        IReadOnlyDictionary<string, string> generated;
        try
        {
            generated = UnrealProjectIni.Generate(profile, NewProjectName.Trim(),
                SelectedProjectCreationPreset?.Preset.UsesLegacyRenderingProfile == true);
            _projectIniPreviewError = string.Empty;
        }
        catch (ArgumentException exception)
        {
            _projectIniPreviewError = exception.Message;
            OnPropertyChanged(nameof(CanCreateProjectConfiguration));
            return;
        }
        var selected = SelectedProjectIniDocument?.FileName;
        foreach (var old in ProjectCreationIniDocuments.Where(item => !item.IsEdited && !generated.ContainsKey(item.FileName)).ToArray())
            ProjectCreationIniDocuments.Remove(old);
        foreach (var file in generated)
        {
            var document = ProjectCreationIniDocuments.FirstOrDefault(item => item.FileName.Equals(file.Key, StringComparison.OrdinalIgnoreCase));
            if (document is null) ProjectCreationIniDocuments.Add(new(file.Key, file.Value, OnProjectIniEdited));
            else document.UpdateGenerated(file.Value);
        }
        SelectedProjectIniDocument = ProjectCreationIniDocuments.FirstOrDefault(item => item.FileName == selected) ??
            ProjectCreationIniDocuments.FirstOrDefault();
        OnPropertyChanged(nameof(ProjectCompositionSummary));
        OnPropertyChanged(nameof(CanCreateProjectConfiguration));
    }

    private void OnProjectIniEdited()
    {
        OnPropertyChanged(nameof(ProjectCompositionSummary));
        OnPropertyChanged(nameof(CanCreateProjectConfiguration));
    }

    private async Task LoadProjectFeaturePacksAsync()
    {
        _customProjectFeaturePacks.Clear();
        var json = await _settings.GetAsync(ProjectFeaturePacksSetting).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var builtInIds = ProjectFeaturePacks.BuiltIn.Select(pack => pack.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                _customProjectFeaturePacks.AddRange((JsonSerializer.Deserialize<ProjectFeaturePack[]>(json) ?? [])
                    .Where(pack => !string.IsNullOrWhiteSpace(pack.Id) && !builtInIds.Contains(pack.Id)).Take(200)
                    .Select(pack => pack with { IsBuiltIn = false, EnabledPlugins = pack.EnabledPlugins ?? [],
                        DisabledPlugins = pack.DisabledPlugins ?? [], IniSettings = pack.IniSettings ?? [] }));
            }
            catch (JsonException exception) { AppendRawLog("WARN", "Projects/Features", exception.Message); }
        }
        RebuildProjectFeaturePacks();
    }

    private void OpenProjectFeaturePack(ProjectFeaturePackOptionViewModel option)
    {
        OpenNewProjectProfile();
        IsEditingProjectFeaturePack = true;
        _editingFeaturePackId = option.Pack.IsBuiltIn ? null : option.Pack.Id;
        _duplicatedFeaturePackId = option.Pack.IsBuiltIn ? option.Pack.Id : null;
        ProjectProfileName = option.Name + (option.Pack.IsBuiltIn ? " (copie)" : string.Empty);
        ProjectProfileDescription = option.Description;
        PopulateProjectProfilePlugins(option.Pack.EnabledPlugins, option.Pack.DisabledPlugins);
        foreach (var setting in option.Pack.IniSettings)
            ProjectProfileIniSettings.Add(new(setting, RemoveProjectProfileIniSetting));
        OnPropertyChanged(nameof(CanDeleteSelectedProjectProfile));
    }

    private async Task SaveProjectFeaturePackAsync()
    {
        if (string.IsNullOrWhiteSpace(ProjectProfileName) || ProjectProfileName.Length > 100 || ProjectProfileDescription.Length > 500)
            throw new InvalidOperationException("Indiquez un nom de 1 à 100 caractères et une description de 500 caractères maximum.");
        var settings = ProjectProfileIniSettings.Select(item => item.ToModel()).ToArray();
        if (settings.Length > 5000) throw new InvalidOperationException("Maximum 5 000 réglages INI.");
        foreach (var setting in settings) UnrealProjectIni.ValidateSetting(setting);
        var pack = new ProjectFeaturePack(_editingFeaturePackId ?? CreateProjectProfileId(ProjectProfileName),
            ProjectProfileName.Trim(), ProjectProfileDescription.Trim(),
            ProjectProfilePlugins.Where(item => item.State == true).Select(item => item.Name).ToArray(),
            ProjectProfilePlugins.Where(item => item.State == false).Select(item => item.Name).ToArray(),
            settings) { IsBuiltIn = false };
        _customProjectFeaturePacks.RemoveAll(item => item.Id.Equals(pack.Id, StringComparison.OrdinalIgnoreCase));
        _customProjectFeaturePacks.Add(pack);
        await _settings.SetAsync(ProjectFeaturePacksSetting, JsonSerializer.Serialize(_customProjectFeaturePacks)).ConfigureAwait(true);
        RebuildProjectFeaturePacks(pack.Id);
        _duplicatedFeaturePackId = null;
        IsProjectProfileEditorVisible = false;
        StatusMessage = $"Pack « {pack.DisplayName} » enregistré et appliqué.";
    }

    private async Task DeleteProjectFeaturePackAsync()
    {
        if (_editingFeaturePackId is null) return;
        _customProjectFeaturePacks.RemoveAll(item => item.Id.Equals(_editingFeaturePackId, StringComparison.OrdinalIgnoreCase));
        await _settings.SetAsync(ProjectFeaturePacksSetting, JsonSerializer.Serialize(_customProjectFeaturePacks)).ConfigureAwait(true);
        RebuildProjectFeaturePacks();
        IsProjectProfileEditorVisible = false;
        StatusMessage = "Pack personnalisé supprimé ; les modèles intégrés sont conservés.";
    }
}
