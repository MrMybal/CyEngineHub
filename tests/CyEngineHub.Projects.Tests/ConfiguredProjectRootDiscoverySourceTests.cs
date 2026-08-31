using CyEngineHub.Projects.Detection;

namespace CyEngineHub.Projects.Tests;

public sealed class ConfiguredProjectRootDiscoverySourceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DiscoveryIsBoundedAndSkipsHeavyDirectories()
    {
        var validProject = Path.Combine(_testDirectory, "Games", "Beyond", "Beyond.uproject");
        var ignoredProject = Path.Combine(_testDirectory, "Content", "Ignored.uproject");
        Directory.CreateDirectory(Path.GetDirectoryName(validProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(ignoredProject)!);
        await File.WriteAllTextAsync(validProject, "{}");
        await File.WriteAllTextAsync(ignoredProject, "{}");
        var source = new ConfiguredProjectRootDiscoverySource(
            _ => Task.FromResult<IReadOnlyCollection<string>>([_testDirectory]));

        var projects = await source.DiscoverAsync();

        Assert.Contains(validProject, projects);
        Assert.DoesNotContain(ignoredProject, projects);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
