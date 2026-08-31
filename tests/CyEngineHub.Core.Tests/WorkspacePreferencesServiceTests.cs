using CyEngineHub.Core.Settings;

namespace CyEngineHub.Core.Tests;

public sealed class WorkspacePreferencesServiceTests
{
    [Fact]
    public async Task DefaultsEnableKnownProvidersWithoutLogs()
    {
        var service = new WorkspacePreferencesService(new MemorySettingsStore());

        var preferences = await service.LoadAsync();

        Assert.True(preferences.EnabledProviders["unreal"]);
        Assert.True(preferences.EnabledProviders["unity"]);
        Assert.True(preferences.AutoDiscoverProjects);
        Assert.False(preferences.ShowLogs);
    }

    [Fact]
    public async Task RoundTripPersistsFoldersAliasesVisibilityAndProviderState()
    {
        var settings = new MemorySettingsStore();
        var service = new WorkspacePreferencesService(settings);
        var preferences = WorkspacePreferences.Default with
        {
            EnabledProviders = new Dictionary<string, bool>
            {
                ["unreal"] = true,
                ["unity"] = false,
            },
            AutoDiscoverProjects = false,
            ShowLogs = true,
            Items =
            [
                new WorkspaceItemPreference(
                    WorkspaceItemKind.Project,
                    "project:one",
                    "Mon projet",
                    "Studio/Jeux/Prototype",
                    IsHidden: true,
                    IsExplicit: true),
            ],
        };

        await service.SaveAsync(preferences);
        var loaded = await service.LoadAsync();

        Assert.False(loaded.EnabledProviders["unity"]);
        Assert.False(loaded.AutoDiscoverProjects);
        Assert.True(loaded.ShowLogs);
        var item = Assert.Single(loaded.Items);
        Assert.Equal("Mon projet", item.Alias);
        Assert.Equal("Studio/Jeux/Prototype", item.FolderPath);
        Assert.True(item.IsHidden);
        Assert.True(item.IsExplicit);
    }

    [Theory]
    [InlineData("../Outside")]
    [InlineData("Studio/./Projet")]
    [InlineData("Studio/../../Projet")]
    public void FolderNormalizationRejectsTraversal(string folder)
    {
        Assert.Throws<ArgumentException>(() =>
            WorkspacePreferencesService.NormalizeFolderPath(folder));
    }

    private sealed class MemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(
            string key,
            string value,
            CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }
    }
}
