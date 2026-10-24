using System.Text.Json;
using CyEngineHub.Projects.Creation;

namespace CyEngineHub.Projects.Tests;

public sealed class UnrealPluginTemplateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyHubPluginTemplates-" + Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData("Blank", "Runtime")]
    [InlineData("Content Only", null)]
    [InlineData("Blueprint Library", "Runtime")]
    [InlineData("Editor Mode", "Editor")]
    [InlineData("Editor Standalone Window", "Editor")]
    [InlineData("Editor Toolbar Button", "Editor")]
    [InlineData("Third Party Library", "Runtime")]
    public async Task CreatesSelectedNativeTemplateAndEnablesItsCorrectModule(string template, string? expectedModule)
    {
        var info = UnrealPluginTemplates.Find(template);
        var source = Path.Combine(_root, "UE", "Engine", "Plugins", "Editor", "PluginBrowser", "Templates", info.Directory);
        Directory.CreateDirectory(Path.Combine(source, "Resources"));
        var icon = new byte[] { 0, 255, 128, 10 };
        await File.WriteAllBytesAsync(Path.Combine(source, "Resources", "Icon128.png"), icon);
        if (expectedModule is not null)
        {
            var code = Path.Combine(source, "Source", "PLUGIN_NAME", "Public"); Directory.CreateDirectory(code);
            await File.WriteAllTextAsync(Path.Combine(code, "PLUGIN_NAME.h"), "class PLUGIN_NAME_API FPLUGIN_NAME; // template " + template);
        }
        var service = new ProjectCreationService();
        var result = await service.CreateAsync(new(service.Presets.Single(item => item.Id == "unreal-cpp"), "Fixture", Directory.CreateDirectory(Path.Combine(_root, "Projects")).FullName,
            "5.5", [], PostCreationScripts: [new(UnrealCreatePluginPostCreationScript.ScriptId,
                new Dictionary<string, string> { ["pluginName"] = "MyTools", ["template"] = template })], EngineRoot: Path.Combine(_root, "UE")));
        var plugin = Path.Combine(result.ProjectRoot, "Plugins", "MyTools");
        using var descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(plugin, "MyTools.uplugin")));
        var modules = descriptor.RootElement.GetProperty("Modules");
        Assert.Equal(expectedModule is null ? 0 : 1, modules.GetArrayLength());
        if (expectedModule is not null)
        {
            Assert.Equal(expectedModule, modules[0].GetProperty("Type").GetString());
            var code = await File.ReadAllTextAsync(Path.Combine(plugin, "Source", "MyTools", "Public", "MyTools.h"));
            Assert.Contains("class MYTOOLS_API FMyTools", code); Assert.Contains(template, code); Assert.DoesNotContain("PLUGIN_NAME", code);
        }
        Assert.Equal(icon, await File.ReadAllBytesAsync(Path.Combine(plugin, "Resources", "Icon128.png")));
        Assert.Equal([template], UnrealPluginTemplates.Available(Path.Combine(_root, "UE")));
        using var project = JsonDocument.Parse(await File.ReadAllTextAsync(result.ProjectMarker));
        Assert.True(project.RootElement.GetProperty("Plugins").EnumerateArray().Single(item => item.GetProperty("Name").GetString() == "MyTools").GetProperty("Enabled").GetBoolean());
    }
    [Fact]
    public async Task MissingTemplateFailsTransactionWithoutPublishingPartialProject()
    {
        var service = new ProjectCreationService(); Directory.CreateDirectory(_root);
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => service.CreateAsync(new(service.Presets.Single(item => item.Id == "unreal-cpp"), "Missing", _root,
            "5.5", [], PostCreationScripts: [new(UnrealCreatePluginPostCreationScript.ScriptId,
                new Dictionary<string, string> { ["template"] = "Blueprint Library" })], EngineRoot: Path.Combine(_root, "UE"))));
        Assert.False(Directory.Exists(Path.Combine(_root, "Missing")));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
