using System.Text.Json;
using CyEngineHub.Engines;
using CyEngineHub.Projects.Creation;

namespace CyEngineHub.Projects.Tests;

public sealed class ProjectCreationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"CyEngineHub-ProjectCreation-{Guid.NewGuid():N}");

    public ProjectCreationServiceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task CreatesUnrealCppLegacyProfileWithDeclaredPlugins()
    {
        var service = new ProjectCreationService();
        var preset = service.Presets.Single(item => item.Id == "unreal-cpp-ue4-profile");

        var result = await service.CreateAsync(new ProjectCreationRequest(
            preset,
            "SpaceGame",
            _root,
            "5.5",
            ["EnhancedInput", "GameplayAbilities"]));

        Assert.True(File.Exists(result.ProjectMarker));
        Assert.True(File.Exists(Path.Combine(result.ProjectRoot, "Source", "SpaceGame", "SpaceGame.Build.cs")));
        var engineConfig = await File.ReadAllTextAsync(
            Path.Combine(result.ProjectRoot, "Config", "DefaultEngine.ini"));
        Assert.Contains("r.Nanite.ProjectEnabled=False", engineConfig, StringComparison.Ordinal);
        using var descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(result.ProjectMarker));
        var plugins = descriptor.RootElement.GetProperty("Plugins")
            .EnumerateArray()
            .Select(item => item.GetProperty("Name").GetString())
            .ToArray();
        Assert.Contains("EnhancedInput", plugins);
        Assert.Contains("GameplayAbilities", plugins);
    }

    [Fact]
    public async Task CreatesGodotPresetWithMainScene()
    {
        var service = new ProjectCreationService();
        var preset = service.Presets.Single(item => item.Engine == EngineFamily.Godot);

        var result = await service.CreateAsync(new ProjectCreationRequest(
            preset,
            "GodotGame",
            _root,
            "4.5",
            []));

        Assert.Equal("project.godot", Path.GetFileName(result.ProjectMarker));
        Assert.True(File.Exists(Path.Combine(result.ProjectRoot, "main.tscn")));
    }

    [Fact]
    public async Task RefusesToOverwriteAnExistingTarget()
    {
        Directory.CreateDirectory(Path.Combine(_root, "ExistingGame"));
        var service = new ProjectCreationService();
        var preset = service.Presets.Single(item => item.Id == "unreal-blueprint");

        await Assert.ThrowsAsync<IOException>(() => service.CreateAsync(new ProjectCreationRequest(
            preset,
            "ExistingGame",
            _root,
            "5.6",
            [])));
    }

    [Fact]
    public async Task CreatesForwardVrBlueprintAndRunsPluginPostCreationScript()
    {
        var service = new ProjectCreationService();
        var preset = service.Presets.Single(item => item.Id == "unreal-blueprint");
        var profile = service.ConfigurationProfiles.Single(item => item.Id == "unreal-forward-vr");

        var result = await service.CreateAsync(new ProjectCreationRequest(
            preset,
            "VrStudio",
            _root,
            "5.6",
            [],
            profile,
            ["OnlineSubsystem"],
            ["VR Base"],
            ["fab-asset-a", "quixel-asset-b"],
            [
                new ProjectPostCreationScriptSelection(
                    UnrealCreatePluginPostCreationScript.ScriptId,
                    new Dictionary<string, string>
                    {
                        ["pluginName"] = "VrStudioTools",
                        ["friendlyName"] = "VR Studio Tools",
                        ["createdBy"] = "Cy Studio",
                        ["description"] = "Outils du projet VR.",
                        ["moduleType"] = "Editor",
                        ["canContainContent"] = "true",
                    }),
            ]));

        var engineConfig = await File.ReadAllTextAsync(
            Path.Combine(result.ProjectRoot, "Config", "DefaultEngine.ini"));
        Assert.Contains("r.ForwardShading=True", engineConfig, StringComparison.Ordinal);
        Assert.Contains("vr.InstancedStereo=True", engineConfig, StringComparison.Ordinal);
        var pluginRoot = Path.Combine(result.ProjectRoot, "Plugins", "VrStudioTools");
        Assert.True(File.Exists(Path.Combine(pluginRoot, "VrStudioTools.uplugin")));
        Assert.True(File.Exists(Path.Combine(
            pluginRoot,
            "Source",
            "VrStudioTools",
            "VrStudioTools.Build.cs")));

        using var descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(result.ProjectMarker));
        var plugins = descriptor.RootElement.GetProperty("Plugins").EnumerateArray().ToArray();
        Assert.Contains(plugins, item =>
            item.GetProperty("Name").GetString() == "OpenXR" &&
            item.GetProperty("Enabled").GetBoolean());
        Assert.Contains(plugins, item =>
            item.GetProperty("Name").GetString() == "OnlineSubsystem" &&
            !item.GetProperty("Enabled").GetBoolean());
        Assert.Contains(plugins, item =>
            item.GetProperty("Name").GetString() == "VrStudioTools" &&
            item.GetProperty("Enabled").GetBoolean());

        Assert.True(File.Exists(result.ReceiptPath));
        using var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(result.ReceiptPath));
        Assert.Equal(
            "unreal-forward-vr",
            receipt.RootElement.GetProperty("ConfigurationProfileId").GetString());
        Assert.Equal(
            "VR Base",
            receipt.RootElement.GetProperty("StarterCollections")[0].GetString());
        Assert.Equal(
            UnrealCreatePluginPostCreationScript.ScriptId,
            receipt.RootElement.GetProperty("PostCreationResults")[0]
                .GetProperty("ScriptId")
                .GetString());
    }

    [Fact]
    public void ExposesACompleteSetOfUnrealConfigurationProfiles()
    {
        var profiles = new ProjectCreationService().ConfigurationProfiles;
        var ids = profiles.Select(profile => profile.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("unreal-no-lumen", ids);
        Assert.Contains("unreal-no-nanite", ids);
        Assert.Contains("unreal-no-lumen-nanite", ids);
        Assert.Contains("unreal-raytracing", ids);
        Assert.Contains("unreal-ue5-optimized", ids);
        Assert.Contains("unreal-ue5-high-end", ids);
        Assert.Contains("unreal-forward-vr", ids);
        Assert.Contains("unreal-mobile-vr", ids);
        Assert.Contains("unreal-ue5-lite", ids);
        Assert.All(profiles, profile => Assert.True(profile.IsBuiltIn));
    }

    [Fact]
    public async Task CreatesUnrealProjectFromAUserProfileWithPluginsAndMultipleIniFiles()
    {
        var service = new ProjectCreationService();
        var preset = service.Presets.Single(item => item.Id == "unreal-blueprint");
        var profile = new ProjectConfigurationProfile(
            "user.studio-performance.test",
            "Studio Performance",
            "Profil créé dans l’éditeur.",
            EngineFamily.Unreal,
            new Dictionary<string, string>
            {
                ["r.Nanite.ProjectEnabled"] = "False",
            },
            ["EnhancedInput", "OpenXR"],
            ["OnlineSubsystem"])
        {
            IsBuiltIn = false,
            IniSettings =
            [
                new ProjectIniSetting(
                    "DefaultInput.ini",
                    "/Script/Engine.InputSettings",
                    "bEnableMouseSmoothing",
                    "False"),
                new ProjectIniSetting(
                    "Game.ini",
                    "/Script/EngineSettings.GeneralProjectSettings",
                    "ProjectName",
                    "MustNotOverrideTheNewProjectName"),
                new ProjectIniSetting(
                    "DefaultGame.ini",
                    "/Script/EngineSettings.GeneralProjectSettings",
                    "CompanyName",
                    "Cy Studio"),
            ],
        };

        var result = await service.CreateAsync(new ProjectCreationRequest(
            preset,
            "ProfileGame",
            _root,
            "5.6",
            [],
            profile));

        var engineConfig = await File.ReadAllTextAsync(
            Path.Combine(result.ProjectRoot, "Config", "DefaultEngine.ini"));
        var inputConfig = await File.ReadAllTextAsync(
            Path.Combine(result.ProjectRoot, "Config", "DefaultInput.ini"));
        var gameConfig = await File.ReadAllTextAsync(
            Path.Combine(result.ProjectRoot, "Config", "DefaultGame.ini"));
        Assert.Contains("r.Nanite.ProjectEnabled=False", engineConfig, StringComparison.Ordinal);
        Assert.Contains("[/Script/Engine.InputSettings]", inputConfig, StringComparison.Ordinal);
        Assert.Contains("bEnableMouseSmoothing=False", inputConfig, StringComparison.Ordinal);
        Assert.Contains("ProjectName=ProfileGame", gameConfig, StringComparison.Ordinal);
        Assert.DoesNotContain("MustNotOverrideTheNewProjectName", gameConfig, StringComparison.Ordinal);
        Assert.Contains("CompanyName=Cy Studio", gameConfig, StringComparison.Ordinal);

        using var descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(result.ProjectMarker));
        var plugins = descriptor.RootElement.GetProperty("Plugins").EnumerateArray().ToArray();
        Assert.Contains(plugins, item =>
            item.GetProperty("Name").GetString() == "OpenXR" &&
            item.GetProperty("Enabled").GetBoolean());
        Assert.Contains(plugins, item =>
            item.GetProperty("Name").GetString() == "OnlineSubsystem" &&
            !item.GetProperty("Enabled").GetBoolean());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
