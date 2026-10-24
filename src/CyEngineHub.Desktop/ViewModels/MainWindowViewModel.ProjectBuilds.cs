using System.Windows.Input;
using Avalonia.Threading;
using CyEngineHub.Build.Commands;
using CyEngineHub.Build.Execution;
using CyEngineHub.Build.Models;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly HashSet<string> _activeProjectOperations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, BuildSession> _projectBuildSessions = new(StringComparer.OrdinalIgnoreCase);
    private BuildSession? _displayedBuildSession;
    private HubTaskLink? _buildReturnLink;
    public ICommand BackFromBuildsCommand => new RelayCommand(() =>
    {
        if (_buildReturnLink is { } target) NavigateTaskLink(target);
        else ShowSection(PageSection.Projects);
    });
    public string BuildBackLabel => _buildReturnLink?.Kind == "engine" ? "← Retour au moteur" : "← Retour au projet";
    public TaskItemViewModel? SelectedProjectBuildTask
    {
        get
        {
            var path = SelectedProjectDetails?.Project.FilePath ?? SelectedLocalProjectDetails?.Project.MarkerPath;
            var matches = Tasks.Where(task => task.Category == "Build" && path is not null && task.Links.Any(link => link.Kind == "project" && SameWorkspaceKey(link.Id, path)));
            return matches.FirstOrDefault(task => task.CanCancel) ?? matches.FirstOrDefault();
        }
    }
    public bool HasSelectedProjectBuild => SelectedProjectBuildTask is not null;
    private void NotifyProjectBuildState()
    {
        OnPropertyChanged(nameof(SelectedProjectBuildTask));
        OnPropertyChanged(nameof(HasSelectedProjectBuild));
    }

    private void OpenSelectedProjectBuilds()
    {
        var path = SelectedProjectDetails?.Project.FilePath ?? SelectedLocalProjectDetails?.Project.MarkerPath;
        if (path is not null)
        {
            _buildReturnLink = ProjectTaskLink(path, SelectedProjectDetails?.Name ?? SelectedLocalProjectDetails?.Name ?? "Projet");
            SelectedPresetProject = Projects.FirstOrDefault(item => SameWorkspaceKey(item.Project.FilePath, path));
            if (_projectBuildSessions.TryGetValue(path, out var session)) DisplayBuildSession(session);
        }
        OnPropertyChanged(nameof(BuildBackLabel));
        ShowSection(PageSection.Builds);
    }

    private Task PrepareProjectEditorAsync(UnrealProject project, UnrealEngineInstallation? selectedEngine = null, bool stayOnProject = false)
    {
        var engine = selectedEngine ?? ResolveEngine(project.EngineAssociation)
            ?? throw new InvalidOperationException($"Aucun moteur local ne correspond à l’association '{project.EngineAssociation}'.");
        var steps = new List<UnrealBuildStep>();
        if (project.Modules.Count > 0 && (stayOnProject || !File.Exists(Path.ChangeExtension(project.FilePath, ".sln"))))
            steps.Add(new("Générer la solution Visual Studio", () => UnrealBuildCommandFactory.CreateGenerateProjectFiles(
                new(engine, project, UnrealProjectFileFormat.VisualStudio2022, IncludeEngineSource: engine.Type != EngineInstallationType.EpicBinary)), project.FilePath));
        steps.Add(new("Compiler l’éditeur du projet", () => UnrealBuildCommandFactory.CreateTargetBuild(
            new(engine, $"{project.Name}Editor", "Win64", "Development", Project: project))));
        return ExecuteUnrealStepsAsync($"Préparer / Build {project.Name}Editor · Win64 Development", steps,
            ProjectTaskLink(project.FilePath, project.Name), !stayOnProject);
    }

    private async Task ExecuteUnrealStepsAsync(string title, IReadOnlyList<UnrealBuildStep> steps,
        HubTaskLink? target = null, bool navigateToBuilds = true)
    {
        var projectPath = target?.Kind == "project" ? target.Id : null;
        if (projectPath is not null && !_activeProjectOperations.Add(projectPath))
        {
            StatusMessage = "Une préparation ou compilation de ce projet est déjà en cours ou en attente.";
            return;
        }
        try
        {
            await using var persistentLog = BuildLogFileWriter.Create(_operationLogsDirectory, title);
            var session = new BuildSession(title) { Summary = "En attente dans la file…" };
            if (_projectBuildSessions.Count >= 20)
            {
                var expired = _projectBuildSessions.Keys.FirstOrDefault(path => !_activeProjectOperations.Contains(path) && !SameWorkspaceKey(path, projectPath ?? string.Empty));
                if (expired is not null) _projectBuildSessions.Remove(expired);
            }
            if (projectPath is not null) _projectBuildSessions[projectPath] = session;
            if (navigateToBuilds)
            {
                if (projectPath is not null) SelectedPresetProject = Projects.FirstOrDefault(item => SameWorkspaceKey(item.Project.FilePath, projectPath));
                _buildReturnLink = target;
                OnPropertyChanged(nameof(BuildBackLabel));
                DisplayBuildSession(session);
                ShowSection(PageSection.Builds);
            }
            var handle = _taskScheduler.Enqueue(new HubTaskDefinition("Build", title,
                target is null ? [] : [target], async (context, cancellationToken) =>
                {
                    for (var index = 0; index < steps.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var step = steps[index];
                        Dispatcher.UIThread.Post(() =>
                        {
                            session.Summary = step.Title;
                            if (ReferenceEquals(_displayedBuildSession, session)) BuildSummary = session.Summary;
                        });
                        var start = steps.Count == 2 ? index == 0 ? 0 : .15 : 0;
                        var span = steps.Count == 2 ? index == 0 ? .15 : .85 : 1;
                        context.ReportIndeterminate(step.Title);
                        var request = step.Request();
                        var preview = new BuildLogLine(DateTimeOffset.Now, BuildLogSeverity.Information, UnrealBuildCommandFactory.FormatPreview(request));
                        persistentLog.Write(preview); PostBuildLine(session, preview);
                        var lastFraction = 0d;
                        var progress = new InlineProgress<BuildLogLine>(line =>
                        {
                            // UHT and C++ emit independent 0–100 ranges. UHT reaching 100 must
                            // not make the compilation bar stick at 99 before C++ even starts.
                            var preparingCode = step.ProjectSolution is null && line.Text.StartsWith("@progress", StringComparison.Ordinal) &&
                                !line.Text.Contains("Compiling C++ source code", StringComparison.OrdinalIgnoreCase);
                            if (preparingCode)
                                context.ReportIndeterminate(step.Title + " · " + line.Text);
                            else if (UnrealBuildProgressParser.Parse(line.Text) is { } fraction)
                            {
                                lastFraction = Math.Max(lastFraction, fraction);
                                context.Report(Math.Min(.99, start + lastFraction * span), step.Title + " · " + line.Text);
                            }
                            PostBuildLine(session, line);
                        });
                        var result = await _buildExecutor.ExecuteAsync(request, progress, persistentLog, cancellationToken).ConfigureAwait(false);
                        if (result.ExitCode != 0)
                        {
                            var lastError = result.Tail.LastOrDefault(line => line.Severity is BuildLogSeverity.Error or BuildLogSeverity.Fatal);
                            throw new BuildOperationException(lastError is null ? $"Unreal a retourné le code {result.ExitCode}." :
                                BuildLogClassifier.Diagnose(lastError.Text) ?? lastError.Text, title, request.WorkingDirectory ?? string.Empty);
                        }
                        if (step.ProjectSolution is { } descriptor)
                        {
                            var solution = Path.ChangeExtension(descriptor, ".sln");
                            if (!File.Exists(solution) || new FileInfo(solution).Length == 0)
                                throw new IOException("Unreal a terminé sans générer la solution attendue : " + solution + ". Le build n’a pas été lancé.");
                        }
                    }
                    context.Report(1, "Opération Unreal terminée et vérifiée.");
                }));
            var snapshot = _taskScheduler.GetSnapshot().First(item => item.Id == handle.Id);
            if (!Tasks.Any(item => item.Id == handle.Id)) Tasks.Insert(0, new TaskItemViewModel(snapshot, CancelTask, NavigateTaskLink));
            NotifyProjectBuildState(); NotifyActivityQueues();
            var completed = await handle.Completion.ConfigureAwait(true);
            session.Summary = completed.Status switch
            {
                HubTaskStatus.Completed => "Terminé · " + persistentLog.FilePath,
                HubTaskStatus.Canceled => "Opération annulée ; arbre de processus arrêté. Log : " + persistentLog.FilePath,
                _ => $"Échec : {completed.Error} · Log : {persistentLog.FilePath}",
            };
            if (ReferenceEquals(_displayedBuildSession, session)) BuildSummary = session.Summary;
            StatusMessage = title + " · " + (completed.Status == HubTaskStatus.Completed ? "terminé" : completed.Status == HubTaskStatus.Canceled ? "annulé" : "échec");
            NotifyProjectBuildState();
        }
        catch (Exception exception) { ShowError(exception); }
        finally { if (projectPath is not null) _activeProjectOperations.Remove(projectPath); }
    }

    private void PostBuildLine(BuildSession session, BuildLogLine line) => Dispatcher.UIThread.Post(() =>
    {
        AppendRawLog(line.Severity.ToString().ToUpperInvariant(), "Build", line.Text);
        var entry = new BuildLogItemViewModel(line);
        session.Lines.Add(entry);
        if (session.Lines.Count > 2_000) session.Lines.RemoveAt(0);
        if (!ReferenceEquals(_displayedBuildSession, session)) return;
        BuildLogs.Add(entry);
        while (BuildLogs.Count > 2_000) BuildLogs.RemoveAt(0);
        OnPropertyChanged(nameof(HasNoBuildLogs));
    });

    private void DisplayBuildSession(BuildSession session)
    {
        _displayedBuildSession = session;
        ActiveBuildTitle = session.Title; BuildSummary = session.Summary;
        BuildLogs.Clear(); foreach (var line in session.Lines) BuildLogs.Add(line);
        OnPropertyChanged(nameof(HasNoBuildLogs));
    }
    private sealed record UnrealBuildStep(string Title, Func<ProcessRequest> Request, string? ProjectSolution = null);
    private sealed class BuildSession(string title)
    {
        public string Title { get; } = title;
        public string Summary { get; set; } = string.Empty;
        public List<BuildLogItemViewModel> Lines { get; } = [];
    }
}
