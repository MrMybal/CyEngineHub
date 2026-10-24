using CyEngineHub.Build.Commands;
using CyEngineHub.Build.Models;
using CyEngineHub.Infrastructure.Processes;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Build.Tests;

public sealed class UnrealBuildCommandFactoryTests : IDisposable
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SolutionGenerationSupportsSourceWrappersAndEpicInstalledBuildFallback(bool sourceWrapper)
    {
        var engine = CreateEngine();
        var project = CreateProject();
        var directory = Path.Combine(engine.RootPath, "Engine", "Build", "BatchFiles");
        Directory.CreateDirectory(directory);
        var script = sourceWrapper ? Path.Combine(engine.RootPath, "GenerateProjectFiles.bat") : Path.Combine(directory, "Build.bat");
        await File.WriteAllTextAsync(script, "@echo off\r\necho ARGS=%*\r\nexit /b 0\r\n");
        var request = UnrealBuildCommandFactory.CreateGenerateProjectFiles(new(engine, project, UnrealProjectFileFormat.VisualStudio2022, IncludeEngineSource: false));
        Assert.Contains("-Project=" + project.FilePath, request.RawArguments, StringComparison.Ordinal);
        Assert.Contains("-2022", request.RawArguments, StringComparison.Ordinal);
        Assert.Contains("-NoEngine", request.RawArguments, StringComparison.Ordinal);
        if (!sourceWrapper) Assert.Contains("-ProjectFiles", request.RawArguments, StringComparison.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            var result = await new SystemProcessRunner().RunAsync(request);
            Assert.True(result.Succeeded);
            Assert.Contains(result.CapturedOutput, line => line.Text.Contains("-Project=" + project.FilePath, StringComparison.Ordinal));
        }
    }
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Engine with spaces {Guid.NewGuid():N}");
    private readonly string _packageDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Plugin package {Guid.NewGuid():N}");

    [Fact]
    public async Task TargetBuildExecutesBatchWithStructuredArguments()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var engine = CreateEngine();
        var project = CreateProject();
        var batchDirectory = Path.Combine(
            engine.RootPath,
            "Engine",
            "Build",
            "BatchFiles");
        Directory.CreateDirectory(batchDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(batchDirectory, "Build.bat"),
            """
            @echo off
            echo TARGET=%~1
            echo PLATFORM=%~2
            echo CONFIG=%~3
            echo PROJECT=%~4
            exit /b 0
            """);
        var request = UnrealBuildCommandFactory.CreateTargetBuild(new UnrealTargetBuildRequest(
            engine,
            "BeyondEditor",
            Project: project));

        var result = await new SystemProcessRunner().RunAsync(request);
        var output = string.Join(
            Environment.NewLine,
            result.CapturedOutput.Select(line => line.Text));

        Assert.True(
            result.Succeeded,
            $"Exit code {result.ExitCode}{Environment.NewLine}" +
            $"{UnrealBuildCommandFactory.FormatPreview(request)}{Environment.NewLine}{output}");
        Assert.Contains("TARGET=BeyondEditor", output, StringComparison.Ordinal);
        Assert.Contains("PLATFORM=Win64", output, StringComparison.Ordinal);
        Assert.Contains("CONFIG=Development", output, StringComparison.Ordinal);
        Assert.Contains($"PROJECT=-Project={project.FilePath}", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetupExecutesRootBatchWithAdditionalArguments()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var engine = CreateEngine();
        Directory.CreateDirectory(engine.RootPath);
        await File.WriteAllTextAsync(
            Path.Combine(engine.RootPath, "Setup.bat"),
            "@echo off\r\necho SETUP_ARG=%~1\r\nexit /b 0\r\n");
        var request = UnrealBuildCommandFactory.CreateSetup(new SetupEngineRequest(
            engine,
            ["-exclude=Android"]));

        var result = await new SystemProcessRunner().RunAsync(request);
        var output = string.Join(
            Environment.NewLine,
            result.CapturedOutput.Select(line => line.Text));

        Assert.True(result.Succeeded, output);
        Assert.Contains("SETUP_ARG=-exclude=Android", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageCommandContainsExplicitBuildCookRunStages()
    {
        var engine = CreateEngine();
        var project = CreateProject();
        var batchDirectory = Path.Combine(
            engine.RootPath,
            "Engine",
            "Build",
            "BatchFiles");
        Directory.CreateDirectory(batchDirectory);
        await File.WriteAllTextAsync(Path.Combine(batchDirectory, "RunUAT.bat"), "@exit /b 0");
        var archive = Path.Combine(project.RootPath, "Saved", "Packages");

        var request = UnrealBuildCommandFactory.CreatePackage(new UnrealPackageRequest(
            engine,
            project,
            "Win64",
            "Shipping",
            archive,
            Compressed: true));
        var command = request.RawArguments;

        Assert.NotNull(command);
        Assert.Contains("\"BuildCookRun\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-build\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-cook\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-stage\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-package\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-archive\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-pak\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-iostore\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-compressed\"", command, StringComparison.Ordinal);
        Assert.DoesNotContain("\"-distribution\"", command, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TargetRejectsShellMetacharacters()
    {
        var engine = CreateEngine();
        var batchDirectory = Path.Combine(
            engine.RootPath,
            "Engine",
            "Build",
            "BatchFiles");
        Directory.CreateDirectory(batchDirectory);
        await File.WriteAllTextAsync(Path.Combine(batchDirectory, "Build.bat"), "@exit /b 0");

        Assert.Throws<ArgumentException>(() =>
            UnrealBuildCommandFactory.CreateTargetBuild(new UnrealTargetBuildRequest(
                engine,
                "BeyondEditor & whoami")));
    }

    [Fact]
    public async Task BuildPluginCommandContainsExplicitSafeArguments()
    {
        var engine = CreateEngine();
        var batchDirectory = Path.Combine(engine.RootPath, "Engine", "Build", "BatchFiles");
        var pluginRoot = Path.Combine(engine.RootPath, "Engine", "Plugins", "CyPlugin");
        Directory.CreateDirectory(batchDirectory);
        Directory.CreateDirectory(pluginRoot);
        await File.WriteAllTextAsync(Path.Combine(batchDirectory, "RunUAT.bat"), "@exit /b 0");
        var descriptorPath = Path.Combine(pluginRoot, "CyPlugin.uplugin");
        await File.WriteAllTextAsync(descriptorPath, "{}");

        var request = UnrealBuildCommandFactory.CreateBuildPlugin(new BuildPluginRequest(
            engine,
            descriptorPath,
            _packageDirectory,
            ["Win64", "Linux"],
            NoHostPlatform: true,
            StrictIncludes: true));

        Assert.NotNull(request.RawArguments);
        Assert.Contains("\"BuildPlugin\"", request.RawArguments, StringComparison.Ordinal);
        Assert.Contains($"\"-Plugin={descriptorPath}\"", request.RawArguments, StringComparison.Ordinal);
        Assert.Contains($"\"-Package={_packageDirectory}\"", request.RawArguments, StringComparison.Ordinal);
        Assert.Contains("\"-TargetPlatforms=Win64+Linux\"", request.RawArguments, StringComparison.Ordinal);
        Assert.Contains("\"-NoHostPlatform\"", request.RawArguments, StringComparison.Ordinal);
        Assert.Contains("\"-StrictIncludes\"", request.RawArguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildPluginRejectsNonEmptyPackageDirectory()
    {
        var engine = CreateEngine();
        var batchDirectory = Path.Combine(engine.RootPath, "Engine", "Build", "BatchFiles");
        var pluginRoot = Path.Combine(engine.RootPath, "PluginSource");
        Directory.CreateDirectory(batchDirectory);
        Directory.CreateDirectory(pluginRoot);
        Directory.CreateDirectory(_packageDirectory);
        await File.WriteAllTextAsync(Path.Combine(batchDirectory, "RunUAT.bat"), "@exit /b 0");
        var descriptorPath = Path.Combine(pluginRoot, "CyPlugin.uplugin");
        await File.WriteAllTextAsync(descriptorPath, "{}");
        await File.WriteAllTextAsync(Path.Combine(_packageDirectory, "keep.txt"), "do not delete");

        Assert.Throws<BuildOperationException>(() =>
            UnrealBuildCommandFactory.CreateBuildPlugin(new BuildPluginRequest(
                engine,
                descriptorPath,
                _packageDirectory,
                ["Win64"])));
        Assert.True(File.Exists(Path.Combine(_packageDirectory, "keep.txt")));
    }

    [Fact]
    public async Task BuildPluginRejectsOutputInsideEngine()
    {
        var engine = CreateEngine();
        var batchDirectory = Path.Combine(engine.RootPath, "Engine", "Build", "BatchFiles");
        var pluginRoot = Path.Combine(engine.RootPath, "PluginSource");
        Directory.CreateDirectory(batchDirectory);
        Directory.CreateDirectory(pluginRoot);
        await File.WriteAllTextAsync(Path.Combine(batchDirectory, "RunUAT.bat"), "@exit /b 0");
        var descriptorPath = Path.Combine(pluginRoot, "CyPlugin.uplugin");
        await File.WriteAllTextAsync(descriptorPath, "{}");

        Assert.Throws<BuildOperationException>(() =>
            UnrealBuildCommandFactory.CreateBuildPlugin(new BuildPluginRequest(
                engine,
                descriptorPath,
                Path.Combine(engine.RootPath, "UnsafePackage"),
                ["Win64"])));
    }

    private UnrealEngineInstallation CreateEngine() => new(
        "ENGINE",
        "5.8",
        "Test Engine",
        _testDirectory,
        Path.Combine(_testDirectory, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
        new EngineVersion(5, 8, 0),
        EngineInstallationType.SourceFork,
        "test",
        null,
        null,
        "test",
        EngineHealth.Healthy,
        [],
        DateTimeOffset.UtcNow);

    private UnrealProject CreateProject()
    {
        var projectRoot = Path.Combine(_testDirectory, "Projects", "Beyond Project");
        Directory.CreateDirectory(projectRoot);
        var projectFile = Path.Combine(projectRoot, "Beyond.uproject");
        File.WriteAllText(projectFile, "{}");
        return new UnrealProject(
            "PROJECT",
            "Beyond",
            projectFile,
            projectRoot,
            "5.8",
            null,
            null,
            [],
            [],
            ProjectHealth.Healthy,
            [],
            DateTimeOffset.UtcNow);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }

        if (Directory.Exists(_packageDirectory))
        {
            Directory.Delete(_packageDirectory, recursive: true);
        }
    }
}
