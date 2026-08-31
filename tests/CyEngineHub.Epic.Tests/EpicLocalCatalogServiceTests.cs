using System.Text.Json;
using CyEngineHub.Epic.Local;
using CyEngineHub.Epic.Models;

namespace CyEngineHub.Epic.Tests;

public sealed class EpicLocalCatalogServiceTests
{
    [Fact]
    public async Task RefreshAsyncMergesSafeLocalSourcesAndCountsCaches()
    {
        using var temporary = new TemporaryDirectory();
        var commonData = Path.Combine(temporary.Path, "Common");
        var localData = Path.Combine(temporary.Path, "Local");
        var manifestDirectory = Directory.CreateDirectory(Path.Combine(
            commonData,
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests"));
        var installDirectory = Directory.CreateDirectory(Path.Combine(temporary.Path, "UE_5.5"));
        var launcherDataDirectory = Directory.CreateDirectory(Path.Combine(
            commonData,
            "Epic",
            "UnrealEngineLauncher"));
        Directory.CreateDirectory(Path.Combine(
            commonData,
            "Epic",
            "EpicGamesLauncher",
            "VaultCache",
            "AssetA"));
        Directory.CreateDirectory(Path.Combine(localData, "FabPlugins", "PluginA"));
        Directory.CreateDirectory(Path.Combine(localData, "FabPlugins", "PluginB"));
        var launcher = Path.Combine(temporary.Path, "EpicGamesLauncher.exe");
        await File.WriteAllTextAsync(launcher, string.Empty);
        await File.WriteAllTextAsync(
            Path.Combine(launcherDataDirectory.FullName, "LauncherInstalled.dat"),
            JsonSerializer.Serialize(new
            {
                InstallationList = new[]
                {
                    new
                    {
                        ArtifactId = "engine-artifact",
                        AppName = "UE_5.5",
                        InstallLocation = installDirectory.FullName,
                        NamespaceId = "ue",
                    },
                },
            }));
        await File.WriteAllTextAsync(
            Path.Combine(manifestDirectory.FullName, "engine.item"),
            JsonSerializer.Serialize(new
            {
                ArtifactId = "engine-artifact",
                AppName = "UE_5.5",
                DisplayName = "Unreal Engine 5.5",
                AppVersionString = "5.5.4",
                InstallLocation = installDirectory.FullName,
                CatalogNamespace = "ue",
            }));
        var service = new EpicLocalCatalogService(
            commonData,
            localData,
            new FakeLauncherLocator(launcher, isRunning: true));

        var snapshot = await service.RefreshAsync();

        Assert.True(snapshot.IsLauncherInstalled);
        Assert.True(snapshot.IsLauncherRunning);
        Assert.Equal(launcher, snapshot.LauncherPath);
        Assert.Equal(1, snapshot.VaultItemCount);
        Assert.Equal(2, snapshot.FabPluginPackageCount);
        var artifact = Assert.Single(snapshot.Artifacts);
        Assert.Equal("Unreal Engine 5.5", artifact.DisplayName);
        Assert.Equal("5.5.4", artifact.Version);
        Assert.Contains("LauncherInstalled.dat", artifact.Source, StringComparison.Ordinal);
        Assert.Contains("engine.item", artifact.Source, StringComparison.Ordinal);
        Assert.Empty(snapshot.Diagnostics);
    }

    private sealed class FakeLauncherLocator(string path, bool isRunning) : IEpicLauncherLocator
    {
        public string? FindLauncherExecutable() => path;

        public bool IsLauncherRunning() => isRunning;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CyEngineHub-EpicCatalogTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
