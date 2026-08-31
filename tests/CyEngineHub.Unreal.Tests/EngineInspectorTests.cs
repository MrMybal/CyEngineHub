using System.Text.Json;
using CyEngineHub.Unreal.Detection;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Unreal.Tests;

public sealed class EngineInspectorTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Moteur avec espaces é-{Guid.NewGuid():N}");

    [Fact]
    public async Task InspectorReadsSourceForkMetadataAndHealth()
    {
        var buildDirectory = Path.Combine(_testDirectory, "Engine", "Build");
        var editorPath = Path.Combine(
            _testDirectory,
            "Engine",
            "Binaries",
            "Win64",
            "UnrealEditor.exe");
        Directory.CreateDirectory(buildDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(editorPath)!);
        Directory.CreateDirectory(Path.Combine(
            _testDirectory,
            "Engine",
            "Binaries",
            "DotNET",
            "UnrealBuildTool"));
        Directory.CreateDirectory(Path.Combine(
            _testDirectory,
            "Engine",
            "Build",
            "BatchFiles"));
        Directory.CreateDirectory(Path.Combine(_testDirectory, "Engine", "Source"));
        Directory.CreateDirectory(Path.Combine(_testDirectory, ".git"));
        await File.WriteAllTextAsync(editorPath, string.Empty);
        await File.WriteAllTextAsync(
            Path.Combine(
                _testDirectory,
                "Engine",
                "Binaries",
                "DotNET",
                "UnrealBuildTool",
                "UnrealBuildTool.dll"),
            string.Empty);
        await File.WriteAllTextAsync(
            Path.Combine(_testDirectory, "Engine", "Build", "BatchFiles", "RunUAT.bat"),
            string.Empty);
        await File.WriteAllTextAsync(
            Path.Combine(buildDirectory, "Build.version"),
            JsonSerializer.Serialize(new
            {
                MajorVersion = 5,
                MinorVersion = 8,
                PatchVersion = 1,
                Changelist = 123456,
                BranchName = "++UE5+Release-5.8",
                BuildId = "test-build",
            }));

        var engine = await new EngineInspector().InspectAsync(new EngineCandidate(
            editorPath,
            "Configured paths",
            "{CUSTOM-58}",
            "CyEngine 5.8"));

        Assert.NotNull(engine);
        Assert.Equal(new EngineVersion(5, 8, 1), engine.Version);
        Assert.Equal(EngineInstallationType.SourceFork, engine.Type);
        Assert.Equal(EngineHealth.Healthy, engine.Health);
        Assert.Equal("CyEngine 5.8", engine.DisplayName);
        Assert.Equal("{CUSTOM-58}", engine.AssociationId);
        Assert.Empty(engine.HealthIssues);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}

