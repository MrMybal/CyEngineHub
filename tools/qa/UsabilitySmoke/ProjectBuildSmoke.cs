using Avalonia.Controls;
using CyEngineHub.Core.Processes;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Unreal.Models;

sealed class ProjectBuildFixtureRunner : IProcessRunner
{
    public readonly List<ProcessRequest> Requests = [];
    public TaskCompletionSource Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource CodeGenerationGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool OmitSolution;
    public int GenerationExitCode;
    public async Task<ProcessResult> RunAsync(ProcessRequest request, IProgress<ProcessOutputLine>? output = null, CancellationToken cancellationToken = default)
    {
        Requests.Add(request); var started = DateTimeOffset.UtcNow;
        var raw = request.RawArguments ?? string.Join(' ', request.Arguments);
        var generation = raw.Contains("-ProjectFiles", StringComparison.Ordinal);
        var project = System.Text.RegularExpressions.Regex.Match(raw, "-Project=([^\"]+)").Groups[1].Value;
        if (generation && !OmitSolution && GenerationExitCode == 0)
            await File.WriteAllTextAsync(Path.ChangeExtension(project, ".sln"), "Microsoft Visual Studio Solution File", cancellationToken);
        if (!generation)
        {
            output?.Report(new(DateTimeOffset.UtcNow, ProcessOutputChannel.StandardOutput, "@progress 'Generating code...' 100%"));
            await CodeGenerationGate.Task.WaitAsync(cancellationToken);
        }
        output?.Report(new(DateTimeOffset.UtcNow, ProcessOutputChannel.StandardOutput, generation ? "@progress 'Writing project files...' 100%" : "@progress 'Compiling C++ source code...' 50%"));
        if (!generation) await Gate.Task.WaitAsync(cancellationToken);
        return new(1, generation ? GenerationExitCode : 0, started, DateTimeOffset.UtcNow, []);
    }
}
static partial class Program
{
    static async Task TestProjectBuild(Window window, MainWindowViewModel vm, UnrealEngineInstallation engine)
    {
        var batch = Path.Combine(engine.RootPath, "Engine", "Build", "BatchFiles", "Build.bat");
        Directory.CreateDirectory(Path.GetDirectoryName(batch)!); await File.WriteAllTextAsync(batch, "fixture, never executed");
        var plugin = Path.Combine(engine.RootPath, "Engine", "Plugins", "EnhancedInput", "EnhancedInput.uplugin");
        Directory.CreateDirectory(Path.GetDirectoryName(plugin)!); await File.WriteAllTextAsync(plugin, "{\"FileVersion\":3,\"Version\":1,\"FriendlyName\":\"Fixture\"}");
        foreach (var name in CyEngineHub.Projects.Creation.UnrealPluginTemplates.Names)
            Directory.CreateDirectory(Path.Combine(engine.RootPath, "Engine", "Plugins", "Editor", "PluginBrowser", "Templates", CyEngineHub.Projects.Creation.UnrealPluginTemplates.Find(name).Directory));
        Call(vm, "RefreshProjectPluginTemplateChoices");
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshProjectCreationEnginePluginsAsync", Private)!.Invoke(vm, [engine])!;
        vm.ShowProjectsCommand.Execute(null); vm.ToggleProjectCreatorCommand.Execute(null);
        vm.NewProjectParentDirectory = Output; vm.NewProjectName = "AutoCpp";
        vm.SelectedProjectCreationCodeKind = "C++";
        await WaitFor(() => !vm.IsProjectCreationPluginLoading);
        Check(vm.CanCreateProjectConfiguration, "Real creation validation accepts the fixture engine and its discovered plugin");
        var script = vm.ProjectPostCreationScripts.Single(item => item.Id == CyEngineHub.Projects.Creation.UnrealCreatePluginPostCreationScript.ScriptId);
        script.IsSelected = true;
        await ImGuiDiagnostics.ClickAsync(window, "tab/Post-création");
        await ImGuiDiagnostics.ClickAsync(window, "post-creation-option/template");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "plugin-templates.png"));
        await ImGuiDiagnostics.ClickAsync(window, "post-creation-choice/template/2");
        Check(script.Options.Single(item => item.Id == "template").Value == "Blueprint Library", "Post-creation UI lists actual engine plugin templates independently of the module type");
        Call(vm, "RebuildProjectPostCreationScriptOptions");
        script = vm.ProjectPostCreationScripts.Single(item => item.Id == CyEngineHub.Projects.Creation.UnrealCreatePluginPostCreationScript.ScriptId);
        Check(script.Options.Single(item => item.Id == "template").Value == "Blueprint Library", "Refreshing post-creation scripts preserves the chosen plugin template");
        script.IsSelected = false;
        await (Task)typeof(MainWindowViewModel).GetMethod("CreateProjectAsync", Private)!.Invoke(vm, null)!;
        await WaitFor(() => ProjectBuildRunner.Requests.Count == 2 && vm.SelectedProjectBuildTask is { IsProgressIndeterminate: true } task && task.Message.Contains("Generating code", StringComparison.Ordinal));
        Check(vm.SelectedProjectBuildTask!.ProgressPercent < 99, "UHT finishing does not falsely mark C++ compilation 99 percent complete");
        ProjectBuildRunner.CodeGenerationGate.TrySetResult();
        await WaitFor(() => ProjectBuildRunner.Requests.Count == 2 && vm.SelectedProjectBuildTask?.ProgressPercent > 30);
        Check(vm.IsProjectDetailsVisible && vm.SelectedProjectDetails?.Name == "AutoCpp", "Creating a C++ project immediately opens its own fiche, not Builds");
        Check(File.Exists(Path.Combine(Output, "AutoCpp", "AutoCpp.sln")), "Automatic creation generates and verifies .sln before invoking the build");
        Check(vm.HasSelectedProjectBuild && vm.SelectedProjectBuildTask!.CanCancel, "Project fiche exposes the live project-specific build task");
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(ImGuiDiagnostics.Elements(window).Contains("progress/SelectedProjectBuildTask.ProgressPercent/Active"), "Running project build uses the active progress color");
        Check(!ImGuiDiagnostics.Elements(window).Any(key => key.Contains("CosmosProject", StringComparison.Ordinal)), "Project fiche has no Cosmos-specific import settings");
        await ImGuiDiagnostics.ClickAsync(window, "command/ShowSelectedProjectPluginsCommand");
        Check(vm.IsPluginsVisible && vm.NavigationBackLabel == "← Retour au projet" && vm.SelectedPluginTarget?.Target.Id == "project:" + vm.SelectedProjectDetails!.Project.Id,
            "Project plugins open with the right installation target and a return-to-project header");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "project-plugins-back.png"));
        await ImGuiDiagnostics.ClickAsync(window, "action/NavigateBackCommand");
        Check(vm.IsProjectDetailsVisible && vm.SelectedProjectDetails?.Name == "AutoCpp", "Plugins header returns to the exact project fiche");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "project-build-running.png"));
        await ImGuiDiagnostics.ClickAsync(window, "command/ShowSelectedProjectBuildsCommand");
        Check(vm.IsBuildsVisible, "The project progress card opens its build logs");
        Check(vm.NavigationBackLabel == "← Retour au projet" && ImGuiDiagnostics.Elements(window).Contains("action/NavigateBackCommand"), "Builds also has the shared project return header");
        await ImGuiDiagnostics.ClickAsync(window, "command/BackFromBuildsCommand");
        Check(vm.IsProjectDetailsVisible && vm.SelectedProjectDetails?.Name == "AutoCpp", "Builds back button returns to the exact project fiche");
        vm.ShowActionsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "task/" + vm.SelectedProjectBuildTask!.Id, clicks: 2, horizontal: .2f);
        Check(vm.IsProjectDetailsVisible && vm.SelectedProjectDetails?.Name == "AutoCpp", "Double-clicking an Actions row opens its linked project fiche");
        ProjectBuildRunner.Gate.TrySetResult();
        await WaitFor(() => vm.SelectedProjectBuildTask?.CanCancel == false);
        Check(vm.SelectedProjectBuildTask?.ProgressPercent == 100, "Successful project build reaches 100 percent");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "project-build-completed.png"));
        Check(ImGuiDiagnostics.Elements(window).Contains("progress/SelectedProjectBuildTask.ProgressPercent/Completed"), "A successful fiche build switches from blue to green");
        vm.ShowActionsCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(ImGuiDiagnostics.Elements(window).Contains("progress/" + vm.SelectedProjectBuildTask!.Id + "/Completed"), "The same completed state is green in Actions");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "actions-completed-green.png"));
        foreach (var exit in new[] { 0, 6 })
        {
            vm.ShowProjectsCommand.Execute(null); vm.ToggleProjectCreatorCommand.Execute(null);
            vm.NewProjectName = "FailedCpp" + exit; ProjectBuildRunner.OmitSolution = true; ProjectBuildRunner.GenerationExitCode = exit;
            var count = ProjectBuildRunner.Requests.Count;
            await (Task)typeof(MainWindowViewModel).GetMethod("CreateProjectAsync", Private)!.Invoke(vm, null)!;
            await WaitFor(() => vm.SelectedProjectBuildTask is { CanCancel: false });
            Check(ProjectBuildRunner.Requests.Count == count + 1 && vm.SelectedProjectBuildTask!.Status == "Échec", "GPF failure or absent .sln prevents compiling an unprepared project (exit " + exit + ")");
            Check(vm.SelectedProjectDetails?.Name == "FailedCpp" + exit, "A setup failure preserves the created project and leaves its fiche accessible");
        }
        var before = ProjectBuildRunner.Requests.Count;
        vm.ShowProjectsCommand.Execute(null); vm.ToggleProjectCreatorCommand.Execute(null);
        vm.NewProjectName = "AutoBlueprint"; vm.SelectedProjectCreationCodeKind = "Blueprint uniquement";
        await (Task)typeof(MainWindowViewModel).GetMethod("CreateProjectAsync", Private)!.Invoke(vm, null)!;
        Check(vm.SelectedProjectDetails?.Name == "AutoBlueprint" && !vm.HasSelectedProjectBuild && ProjectBuildRunner.Requests.Count == before,
            "Blueprint creation opens its fiche without launching unnecessary C++ compilation");
        vm.ShowProjectsCommand.Execute(null); vm.ToggleProjectCreatorCommand.Execute(null);
        vm.NewProjectName = "CanceledCpp"; vm.SelectedProjectCreationCodeKind = "C++";
        await WaitFor(() => !vm.IsProjectCreationPluginLoading);
        ProjectBuildRunner.OmitSolution = false; ProjectBuildRunner.GenerationExitCode = 0;
        ProjectBuildRunner.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await (Task)typeof(MainWindowViewModel).GetMethod("CreateProjectAsync", Private)!.Invoke(vm, null)!;
        await WaitFor(() => vm.SelectedProjectBuildTask is { CanCancel: true, ProgressPercent: > 30 });
        vm.SelectedProjectBuildTask!.CancelCommand.Execute(null);
        await WaitFor(() => vm.SelectedProjectBuildTask?.Status == "Annulé");
        Check(vm.SelectedProjectDetails?.Name == "CanceledCpp", "Cancel from the fiche stops the build and preserves the project");
    }
    static async Task WaitFor(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        while (!ready()) await Task.Delay(20, timeout.Token);
    }
}
