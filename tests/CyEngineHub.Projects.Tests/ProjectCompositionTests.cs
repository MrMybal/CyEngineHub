using System.Text.Json;
using CyEngineHub.Projects.Creation;

namespace CyEngineHub.Projects.Tests;

public sealed class ProjectCompositionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyHubComposition-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectCreationService _service = new();
    public ProjectCompositionTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task LiteActuallyDisablesDefaultEnginePluginsInDescriptor()
    {
        var lite = Profile("unreal-ue5-lite");
        Assert.True(lite.DisableEnginePluginsByDefault);
        Assert.InRange(lite.EnabledPlugins.Count, 1, 12);
        var result = await Create("Lite", lite);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.ProjectMarker));
        Assert.True(json.RootElement.GetProperty("DisableEnginePluginsByDefault").GetBoolean());
        Assert.Contains("ContentBrowserAssetDataSource", lite.EnabledPlugins);
    }

    [Fact]
    public void PacksAndManualOverridesFollowTheirExplicitOrder()
    {
        var profile = Profile("unreal-ue5-lite") with { DisabledPlugins = ["Niagara"] };
        var enable = Pack("enable", ["Niagara"], [], "1");
        var disable = Pack("disable", [], ["Niagara"], "2");
        var composed = ProjectConfigurationComposer.Compose(profile, [enable, disable]);
        Assert.Contains("Niagara", composed.DisabledPlugins);
        Assert.DoesNotContain("Niagara", composed.EnabledPlugins);
        Assert.Equal("2", composed.IniSettings.Single(setting => setting.Key == "r.Test").Value);
        composed = ProjectConfigurationComposer.Compose(profile, [disable, enable]);
        Assert.Contains("Niagara", composed.EnabledPlugins);
        Assert.Equal("1", composed.IniSettings.Single(setting => setting.Key == "r.Test").Value);
        composed = ProjectConfigurationComposer.Compose(profile, [enable, disable], new Dictionary<string, bool> { ["Niagara"] = true });
        Assert.Contains("Niagara", composed.EnabledPlugins);
        Assert.True(composed.DisableEnginePluginsByDefault);
    }

    [Theory]
    [InlineData("unreal-ue5-lite")]
    [InlineData("unreal-ue4-style")]
    public void MinimalProfilesDoNotReferenceDisabledAclPluginAssets(string id)
    {
        var ini = UnrealProjectIni.Generate(Profile(id), "Minimal")["DefaultEngine.ini"];
        Assert.Contains("[Animation.DefaultObjectSettings]", ini);
        Assert.Contains("BoneCompressionSettings=\"/Engine/Animation/DefaultAnimBoneCompressionSettings\"", ini);
        Assert.Contains("CurveCompressionSettings=\"/Engine/Animation/DefaultAnimCurveCompressionSettings\"", ini);
        Assert.DoesNotContain("/ACLPlugin/", ini);
    }

    [Fact]
    public async Task ExplicitPluginChoiceCanOverrideAProfileDisable()
    {
        var profile = Profile("unreal-standard") with { DisabledPlugins = ["Niagara"] };
        var result = await _service.CreateAsync(new(_service.Presets.Single(p => p.Id == "unreal-blueprint"),
            "Manual", _root, "5.8", ["Niagara"], profile));
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(result.ProjectMarker));
        Assert.True(json.RootElement.GetProperty("Plugins").EnumerateArray()
            .Single(p => p.GetProperty("Name").GetString() == "Niagara").GetProperty("Enabled").GetBoolean());
    }

    [Fact]
    public async Task PreviewAndCreatedIniFilesAreIdentical()
    {
        var profile = Profile("unreal-ue4-style");
        var packs = new[] { Pack("audio", ["Metasound"], [], "25") };
        var composed = ProjectConfigurationComposer.Compose(profile, packs);
        var preview = UnrealProjectIni.Generate(composed, "Preview");
        var result = await Create("Preview", profile, packs);
        foreach (var file in preview)
            Assert.Equal(file.Value, await File.ReadAllTextAsync(Path.Combine(result.ProjectRoot, "Config", file.Key)));
    }

    [Fact]
    public async Task EditedIniIsWrittenExactlyAfterProfileAndPacks()
    {
        const string edited = "; Mon override\n[/Script/Engine.RendererSettings]\nr.AntiAliasingMethod=1\n";
        var result = await _service.CreateAsync(new(_service.Presets.Single(p => p.Id == "unreal-blueprint"),
            "Edited", _root, "5.8", [], Profile("unreal-ue4-style"),
            FeaturePacks: [ProjectFeaturePacks.BuiltIn.Single(p => p.Id == "unreal.feature.vr")],
            IniFileOverrides: new Dictionary<string, string> { ["DefaultEngine.ini"] = edited }));
        Assert.Equal(edited, await File.ReadAllTextAsync(Path.Combine(result.ProjectRoot, "Config", "DefaultEngine.ini")));
    }

    [Fact]
    public void MergeKeepsArrayOperationsAndOnlyLastScalarValue()
    {
        var settings = ProjectConfigurationComposer.MergeIniSettings([
            new("Input", "Input", "+ActionMappings", "(Name=A)"),
            new("DefaultInput.ini", "Input", "+ActionMappings", "(Name=B)"),
            new("DefaultInput.ini", "Input", "Scalar", "old"),
            new("DefaultInput.ini", "Input", "Scalar", "new"),
            new("DefaultInput.ini", "Input", "-ActionMappings", "(Name=A)")]);
        Assert.Equal(4, settings.Count);
        Assert.Equal("new", settings.Single(s => s.Key == "Scalar").Value);
        Assert.Equal(2, settings.Count(s => s.Key == "+ActionMappings"));
    }

    [Fact]
    public async Task InvalidIniStopsCreationBeforeAnyProjectIsWritten()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new(
            _service.Presets.Single(p => p.Id == "unreal-blueprint"), "Invalid", _root, "5.8", [],
            Profile("unreal-standard"), IniFileOverrides: new Dictionary<string, string> { ["DefaultEngine.ini"] = "no section or equals" })));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("../DefaultEngine.ini")]
    [InlineData("..\\DefaultEngine.ini")]
    [InlineData("C:bad.ini")]
    public void IniCannotEscapeConfig(string fileName) =>
        Assert.Throws<ArgumentException>(() => UnrealProjectIni.Parse(fileName, "[Section]\nKey=Value"));

    [Fact]
    public void Ue4StyleUsesDeferredTaaSsrAndStaticLighting()
    {
        var profile = Profile("unreal-ue4-style");
        Assert.Equal("2", profile.EngineSettings["r.AntiAliasingMethod"]);
        Assert.Equal("2", profile.EngineSettings["r.ReflectionMethod"]);
        Assert.Equal("0", profile.EngineSettings["r.DynamicGlobalIlluminationMethod"]);
        Assert.Equal("False", profile.EngineSettings["r.Nanite.ProjectEnabled"]);
        Assert.Equal("False", profile.EngineSettings["r.RayTracing"]);
        Assert.Equal("True", profile.EngineSettings["r.AllowStaticLighting"]);
    }

    [Fact]
    public void FeaturePacksRoundTripWithoutLosingEdits()
    {
        var pack = Pack("custom", ["Niagara"], ["OpenXR"], "12") with { IsBuiltIn = false };
        var roundTrip = JsonSerializer.Deserialize<ProjectFeaturePack>(JsonSerializer.Serialize(pack))!;
        Assert.Equal(pack.EnabledPlugins, roundTrip.EnabledPlugins);
        Assert.Equal(pack.DisabledPlugins, roundTrip.DisabledPlugins);
        Assert.Equal(pack.IniSettings, roundTrip.IniSettings);
        Assert.False(roundTrip.IsBuiltIn);
    }

    private ProjectConfigurationProfile Profile(string id) => _service.ConfigurationProfiles.Single(p => p.Id == id);
    private Task<ProjectCreationResult> Create(string name, ProjectConfigurationProfile profile, IReadOnlyList<ProjectFeaturePack>? packs = null) =>
        _service.CreateAsync(new(_service.Presets.Single(p => p.Id == "unreal-blueprint"), name, _root, "5.8", [], profile, FeaturePacks: packs));
    private static ProjectFeaturePack Pack(string id, string[] enabled, string[] disabled, string value) =>
        new(id, id, "", enabled, disabled, [new("DefaultEngine.ini", "/Script/Engine.RendererSettings", "r.Test", value)]);
    public void Dispose() => Directory.Delete(_root, true);
}
