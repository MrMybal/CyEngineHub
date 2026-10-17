using CyEngineHub.Toolchains.Detection;

namespace CyEngineHub.Toolchains.Tests;

public sealed class UnrealUtilityCatalogTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "CyHubToolsQa-" + Guid.NewGuid().ToString("N"))).FullName;
    private static string Create(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "synthetic fixture, never executable");
        return Path.GetFullPath(path);
    }
    [Theory]
    [InlineData("UEPrereqSetup_x64.exe")]
    [InlineData("UE4PrereqSetup_x64.exe")]
    public void FindsPrerequisiteInstallersWithoutClaimingTheyAreInstalled(string name)
    {
        var path = Create(_root, "Engine/Extras/Redist/en-us/" + name);
        var item = UnrealUtilityCatalog.Discover(_root, []).Single(item => item.Id == "prerequisites");
        Assert.Equal(path, item.ExecutablePath);
        Assert.True(item.ChangesSystem);
        Assert.Equal("Exécutable trouvé", item.Status);
        Assert.Contains("ne signifie pas", item.Description, StringComparison.Ordinal);
    }
    [Theory]
    [InlineData("UnrealVersionSelector.exe")]
    [InlineData("UnrealVersionSelector-Win64-Shipping.exe")]
    [InlineData("UnrealEngineVersionSelector.exe")]
    public void FindsEngineSelectorVariants(string name)
    {
        var path = Create(_root, "Engine/Binaries/Win64/" + name);
        Assert.Equal(path, UnrealUtilityCatalog.Discover(_root, []).Single(item => item.Id == "version-selector").ExecutablePath);
    }
    [Fact]
    public void UsesExplicitLauncherLocationAndPrefersTheSelectedEngine()
    {
        var launcher = Path.Combine(_root, "Launcher");
        var launcherSelector = Create(launcher, "Engine/Binaries/Win64/UnrealVersionSelector.exe");
        Assert.Equal(launcherSelector, UnrealUtilityCatalog.Discover(null, [launcher]).Single(item => item.Id == "version-selector").ExecutablePath);
        var engine = Path.Combine(_root, "EngineInstall");
        var engineSelector = Create(engine, "Engine/Binaries/Win64/UnrealVersionSelector.exe");
        Assert.Equal(engineSelector, UnrealUtilityCatalog.Discover(engine, [launcher]).Single(item => item.Id == "version-selector").ExecutablePath);
    }
    [Fact]
    public void MissingFilesAreNotLaunchableAndNoDirectoryIsCreated()
    {
        var absent = Path.Combine(_root, "Absent");
        var items = UnrealUtilityCatalog.Discover(absent, []);
        Assert.Equal(4, items.Count);
        Assert.All(items, item => { Assert.False(item.IsAvailable); Assert.Null(item.ExecutablePath); });
        Assert.False(Directory.Exists(absent));
    }
    [Fact]
    public void RefreshOmitsDeletedToolsAndKeepsUnrelatedFiles()
    {
        var insights = Create(_root, "Engine/Binaries/Win64/UnrealInsights.exe");
        var keep = Create(_root, "Unrelated/keep.txt");
        Assert.True(UnrealUtilityCatalog.Discover(_root, []).Single(item => item.Id == "insights").IsAvailable);
        File.Delete(insights);
        Assert.False(UnrealUtilityCatalog.Discover(_root, []).Single(item => item.Id == "insights").IsAvailable);
        Assert.True(File.Exists(keep));
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
