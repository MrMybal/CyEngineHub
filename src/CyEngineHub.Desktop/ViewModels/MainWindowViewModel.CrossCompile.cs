using System.Collections.ObjectModel;
using System.Windows.Input;
using CyEngineHub.Build.Commands;
using CyEngineHub.Build.Presets;
using CyEngineHub.Toolchains.Compatibility;
using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private ProjectCardViewModel? _crossCompileProject;
    private string _crossCompilePlatform = "Linux";
    private string _crossCompileConfiguration = "Development";
    public ObservableCollection<ProjectCardViewModel> CrossCompileProjects { get; } = [];
    public IReadOnlyList<string> CrossCompilePlatforms { get; } = ["Win64", "Linux", "Android"];
    public IReadOnlyList<string> CrossCompileConfigurations { get; } = ["Development", "Shipping", "DebugGame"];
    public bool IsUnrealCrossCompileVisible => SelectedEngineDetails is not null;
    public ProjectCardViewModel? CrossCompileProject
    {
        get => _crossCompileProject;
        set { if (SetProperty(ref _crossCompileProject, value)) NotifyCrossCompile(); }
    }
    public string CrossCompilePlatform
    {
        get => _crossCompilePlatform;
        set { if (SetProperty(ref _crossCompilePlatform, value)) NotifyCrossCompile(); }
    }
    public string CrossCompileConfiguration
    {
        get => _crossCompileConfiguration;
        set { if (SetProperty(ref _crossCompileConfiguration, value)) NotifyCrossCompile(); }
    }
    private EngineToolchainSnapshot? CrossCompileSnapshot => SelectedEngineToolchains
        .FirstOrDefault(card => card.Snapshot.EngineId == SelectedEngineDetails?.Engine.Id)?.Snapshot;
    public IReadOnlyList<ToolchainCheckItemViewModel> CrossCompileChecks => CrossCompileSnapshot is { } snapshot
        ? PlatformToolchainAssessment.ChecksFor(snapshot, CrossCompilePlatform).Select(check => new ToolchainCheckItemViewModel(check,
            () => ShowPrerequisiteRemediation(snapshot.EngineId, check), snapshot.EngineVersion)).ToArray() : [];
    public bool HasCrossCompileChecks => CrossCompileChecks.Count > 0;
    public string CrossCompileStatus => CrossCompileSnapshot is not { } snapshot
        ? "Analyse nécessaire pour ce moteur (aucun résultat validé en cache)."
        : string.Join(" · ", PlatformToolchainAssessment.ChecksFor(snapshot, CrossCompilePlatform)
            .Select(check => $"{check.Label} : {check.Health} (requis {check.Required})"));
    private bool CanCrossCompile => CrossCompileProject is not null && CrossCompileSnapshot is { } snapshot &&
        PlatformToolchainAssessment.CanBuild(snapshot, CrossCompilePlatform) &&
        ResolveEngine(CrossCompileProject.Project.EngineAssociation)?.Id == SelectedEngineDetails?.Engine.Id;
    public string CrossCompileStatusColor
    {
        get
        {
            if (CrossCompileSnapshot is not { } snapshot) return "#F5A623";
            var checks = PlatformToolchainAssessment.ChecksFor(snapshot, CrossCompilePlatform);
            return checks.Any(check => check.Health == ToolchainHealth.Missing) ? "#E8747C" :
                checks.Any(check => check.Health == ToolchainHealth.Warning) ? "#F5A623" : "#5CD26E";
        }
    }
    public string CrossCompilePreview
    {
        get
        {
            if (CrossCompileProject is null || SelectedEngineDetails is null) return "Sélectionnez un projet associé à ce moteur.";
            try { return UnrealBuildCommandFactory.FormatPreview(CreateCrossCompileOperation().ProcessRequest); }
            catch (Exception) { return "Scripts Unreal absents ou projet indisponible : vérifiez l’installation."; }
        }
    }
    private AsyncRelayCommand? _runCrossCompileCommand;
    public ICommand RunCrossCompileCommand => _runCrossCompileCommand ??= new AsyncRelayCommand(
        () => { var operation = CreateCrossCompileOperation(); return ExecuteUnrealOperationAsync(operation.Title, operation.ProcessRequest); },
        () => CanCrossCompile, ShowError);

    private UnrealBuildPresetOperation CreateCrossCompileOperation()
    {
        var engine = SelectedEngineDetails?.Engine ?? throw new InvalidOperationException("Sélectionnez Unreal.");
        var project = CrossCompileProject?.Project ?? throw new InvalidOperationException("Sélectionnez un projet.");
        if (ResolveEngine(project.EngineAssociation)?.Id != engine.Id)
            throw new InvalidOperationException("Le projet utilise une autre installation Unreal.");
        var preset = new UnrealBuildPreset("engine-cross-compile", "Compiler et packager " + CrossCompilePlatform,
            UnrealBuildPresetKind.Package, "", CrossCompilePlatform, CrossCompileConfiguration,
            CookFlavor: CrossCompilePlatform == "Android" ? "ASTC" : null);
        return UnrealBuildPresetCommandFactory.Create(preset, engine, project,
            Path.Combine(project.RootPath, "Saved", "CyEngineHubPackages", CrossCompilePlatform));
    }
    private void RefreshCrossCompile()
    {
        var previousId = CrossCompileProject?.Project.Id;
        CrossCompileProjects.Clear();
        if (SelectedEngineDetails is { } engine)
            foreach (var project in Projects.Where(project => ResolveEngine(project.Project.EngineAssociation)?.Id == engine.Engine.Id))
                CrossCompileProjects.Add(project);
        CrossCompileProject = CrossCompileProjects.FirstOrDefault(project => project.Project.Id == previousId) ?? CrossCompileProjects.FirstOrDefault();
        OnPropertyChanged(nameof(IsUnrealCrossCompileVisible));
        NotifyCrossCompile();
    }
    private void NotifyCrossCompile()
    {
        OnPropertyChanged(nameof(CrossCompileStatus));
        OnPropertyChanged(nameof(CrossCompileStatusColor));
        OnPropertyChanged(nameof(CrossCompileChecks));
        OnPropertyChanged(nameof(HasCrossCompileChecks));
        OnPropertyChanged(nameof(CrossCompilePreview));
        _runCrossCompileCommand?.NotifyCanExecuteChanged();
    }
}
