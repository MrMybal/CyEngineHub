using CyEngineHub.Unreal.Detection;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Unreal.Tests;

public sealed class EngineCatalogValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CyHubEngineValidation-" + Guid.NewGuid().ToString("N"));
    public EngineCatalogValidationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task MissingCachedOfficialAndCustomRootsAreNotInstalled()
    {
        var engines = new[] { Cached("UE_5.2", EngineInstallationType.EpicBinary), Cached("Custom_5.3", EngineInstallationType.Source) };
        Assert.Empty(await EngineCatalogValidation.ValidateAsync(engines));
        Assert.All(engines, engine => Assert.False(EngineCatalogValidation.IsPresent(engine)));
    }

    [Fact]
    public async Task IncompleteRootIsBrokenRatherThanHealthy()
    {
        var engine = Cached("Partial", EngineInstallationType.EpicBinary);
        Directory.CreateDirectory(engine.RootPath);
        var validated = Assert.Single(await EngineCatalogValidation.ValidateAsync([engine]));
        Assert.Equal(EngineHealth.Broken, validated.Health);
        Assert.False(EngineCatalogValidation.IsPresent(validated));
    }

    [Theory]
    [InlineData("UnrealEditor.exe")]
    [InlineData("UE4Editor.exe")]
    public async Task ReadsActualVersionFromDiskAndKeepsOfficialIdentity(string editor)
    {
        var engine = Cached("Installed", EngineInstallationType.EpicBinary);
        Directory.CreateDirectory(Path.Combine(engine.RootPath, "Engine", "Build"));
        Directory.CreateDirectory(Path.Combine(engine.RootPath, "Engine", "Binaries", "Win64"));
        await File.WriteAllTextAsync(Path.Combine(engine.RootPath, "Engine", "Build", "Build.version"), "{\"MajorVersion\":5,\"MinorVersion\":5,\"PatchVersion\":4}");
        await File.WriteAllTextAsync(Path.Combine(engine.RootPath, "Engine", "Binaries", "Win64", editor), "fixture");
        var validated = Assert.Single(await EngineCatalogValidation.ValidateAsync([engine]));
        Assert.Equal(new EngineVersion(5, 5, 4), validated.Version);
        Assert.Equal(engine.Id, validated.Id);
        Assert.Equal(EngineInstallationType.EpicBinary, validated.Type);
        Assert.True(EngineCatalogValidation.IsPresent(validated));
        Assert.NotEqual(EngineHealth.Broken, validated.Health);
    }

    private UnrealEngineInstallation Cached(string folder, EngineInstallationType type)
    {
        var path = Path.Combine(_root, folder);
        return new(folder, "5.2", folder, path, Path.Combine(path, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
            new(5, 2, 1), type, null, null, null, "fixture", EngineHealth.Healthy, [], DateTimeOffset.UtcNow);
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
