using CyEngineHub.Build.Presets;
using CyEngineHub.Core.Settings;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Build.Tests;

public sealed class UnrealBuildPresetTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Preset Engine {Guid.NewGuid():N}");

    [Fact]
    public async Task CatalogPersistsAndDeletesCustomPresets()
    {
        var settings = new MemorySettingsStore();
        var catalog = new UnrealBuildPresetCatalog(settings);
        var custom = new UnrealBuildPreset(
            "custom:test-server",
            "Test Server",
            UnrealBuildPresetKind.Target,
            "{Project}Server",
            "Linux",
            "Shipping");

        await catalog.SaveCustomAsync(custom);
        var reloaded = await new UnrealBuildPresetCatalog(settings).GetAllAsync();

        Assert.Contains(reloaded, item => item.Id == custom.Id && item.Name == custom.Name);
        Assert.Contains(reloaded, item => item.Id == "builtin:editor-development");
        Assert.True(await catalog.DeleteCustomAsync(custom.Id));
        Assert.DoesNotContain(await catalog.GetAllAsync(), item => item.Id == custom.Id);
    }

    [Fact]
    public void TargetPresetExpandsProjectTokenAndAttachesUproject()
    {
        var engine = CreateEngine("Build.bat");
        var project = CreateProject();
        var preset = new UnrealBuildPreset(
            "custom:linux-server",
            "Linux Server",
            UnrealBuildPresetKind.Target,
            "{Project}Server",
            "Linux",
            "Shipping",
            MaximumParallelActions: 8);

        var operation = UnrealBuildPresetCommandFactory.Create(
            preset,
            engine,
            project,
            Path.Combine(project.RootPath, "Saved", "Packages"));
        var command = operation.ProcessRequest.RawArguments;

        Assert.NotNull(command);
        Assert.Contains("\"BeyondServer\"", command, StringComparison.Ordinal);
        Assert.Contains("\"Linux\"", command, StringComparison.Ordinal);
        Assert.Contains($"\"-Project={project.FilePath}\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-MaxParallelActions=8\"", command, StringComparison.Ordinal);
    }

    [Fact]
    public void PackagePresetMapsCookFlavorAndFlags()
    {
        var engine = CreateEngine("RunUAT.bat");
        var project = CreateProject();
        var preset = new UnrealBuildPreset(
            "custom:android",
            "Android ASTC",
            UnrealBuildPresetKind.Package,
            string.Empty,
            "Android",
            "Development",
            CookFlavor: "ASTC",
            Compressed: true);

        var operation = UnrealBuildPresetCommandFactory.Create(
            preset,
            engine,
            project,
            Path.Combine(project.RootPath, "Saved", "Packages"));
        var command = operation.ProcessRequest.RawArguments;

        Assert.NotNull(command);
        Assert.Contains("\"BuildCookRun\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-targetplatform=Android\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-cookflavor=ASTC\"", command, StringComparison.Ordinal);
        Assert.Contains("\"-compressed\"", command, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private UnrealEngineInstallation CreateEngine(string scriptName)
    {
        var batchDirectory = Path.Combine(_testDirectory, "Engine", "Build", "BatchFiles");
        Directory.CreateDirectory(batchDirectory);
        File.WriteAllText(Path.Combine(batchDirectory, scriptName), "@exit /b 0");
        return new UnrealEngineInstallation(
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
    }

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

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }

        public Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _values[key] = value;
            return Task.CompletedTask;
        }
    }
}
