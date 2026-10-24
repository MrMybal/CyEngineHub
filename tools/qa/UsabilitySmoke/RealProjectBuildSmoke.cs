using CyEngineHub.Build.Commands;
using CyEngineHub.Build.Execution;
using CyEngineHub.Build.Models;
using CyEngineHub.Infrastructure.Processes;
using CyEngineHub.Projects.Creation;
using CyEngineHub.Projects.Parsing;
using CyEngineHub.Unreal.Models;

static partial class Program
{
    static async Task RunRealProjectBuild(string[] args)
    {
        var index = Array.IndexOf(args, "--engine-root");
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("--engine-root required");
        var root = Path.GetFullPath(args[index + 1]);
        Check(File.Exists(Path.Combine(root, "Engine", "Build", "InstalledBuild.txt")), "Real smoke only accepts an installed engine, never a source engine build");
        Directory.CreateDirectory(Output);
        var engine = new UnrealEngineInstallation("qa:real", "5.5", "QA installed engine", root,
            Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealEditor.exe"), new(5, 5, 4),
            EngineInstallationType.EpicBinary, null, null, null, "QA", EngineHealth.Healthy, [], DateTimeOffset.UtcNow);
        var service = new ProjectCreationService();
        var scripts = UnrealPluginTemplates.Available(root).Select((template, position) => new ProjectPostCreationScriptSelection(
            UnrealCreatePluginPostCreationScript.ScriptId, new Dictionary<string, string>
            { ["pluginName"] = "QaPlugin" + position, ["template"] = template })).ToArray();
        var existing = Array.IndexOf(args, "--project-root");
        string marker;
        if (existing >= 0)
        {
            var projectRoot = Path.GetFullPath(args[existing + 1]);
            if (!projectRoot.StartsWith(Path.GetFullPath("artifacts") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only QA projects under artifacts may be resumed.");
            marker = Path.Combine(projectRoot, "CyHubCppQa.uproject");
        }
        else
        {
            var created = await service.CreateAsync(new(service.Presets.Single(item => item.Id == "unreal-cpp"), "CyHubCppQa", Output,
                "5.5", [], service.ConfigurationProfiles.Single(item => item.Id == "unreal-ue5-lite"), PostCreationScripts: scripts, EngineRoot: root));
            marker = created.ProjectMarker;
        }
        var project = await new UprojectDescriptorParser().ParseAsync(marker);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        var executor = new UnrealBuildExecutor(new SystemProcessRunner());
        await using var log = BuildLogFileWriter.Create(Path.Combine(Output, "Logs"), "Real installed engine C++ and native plugin templates");
        var progress = new NativeSmokeProgress();
        var gpf = await executor.ExecuteAsync(UnrealBuildCommandFactory.CreateGenerateProjectFiles(new(engine, project,
            UnrealProjectFileFormat.VisualStudio2022, IncludeEngineSource: false)), progress, log, timeout.Token);
        Check(gpf.ExitCode == 0 && File.Exists(Path.ChangeExtension(project.FilePath, ".sln")), "Real installed engine generates the Visual Studio solution through Build.bat -ProjectFiles");
        var build = await executor.ExecuteAsync(UnrealBuildCommandFactory.CreateTargetBuild(new(engine, project.Name + "Editor",
            Project: project, MaximumParallelActions: 2, AdditionalArguments: ["-NoUBA"])), progress, log, timeout.Token);
        Check(build.ExitCode == 0 && File.Exists(Path.Combine(project.RootPath, "Binaries", "Win64", "UnrealEditor-CyHubCppQa.dll")), "Real generated C++ project compiles with native plugin templates");
        Console.WriteLine("OUTPUT " + Output); Console.WriteLine("LOG " + log.FilePath);
    }
    sealed class NativeSmokeProgress : IProgress<BuildLogLine>
    {
        public void Report(BuildLogLine value)
        {
            if (value.Severity is BuildLogSeverity.Error or BuildLogSeverity.Fatal || UnrealBuildProgressParser.Parse(value.Text) is not null)
                Console.WriteLine(value.Text);
        }
    }
}
