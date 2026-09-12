using System.Text.Json;
using CyEngineHub.Core.Settings;
using CyEngineHub.Fab.Storage;

namespace CyEngineHub.Epic.Tests;

public sealed class MarketplaceSettingsPersistenceTests
{
    private static string Root => Path.Combine(Path.GetTempPath(), "CyHub-settings-no-files");
    private static MarketplaceStorageProfile Profile(string market) => new(market,
        Path.Combine(Root, market, "cache"), Path.Combine(Root, market, "downloads"),
        [new(Path.Combine(Root, market, "first"), 123456), new(Path.Combine(Root, market, "second"), 0, false)]);

    [Theory]
    [InlineData("fab")]
    [InlineData("quixel")]
    [InlineData("unity")]
    public async Task SavesCompleteSnapshotIncludingOrderQuotasAndDisabledLibraries(string market)
    {
        var settings = new MemorySettings();
        var profile = Profile(market);
        var key = $"marketplaces.{market}.storageProfile.v1";
        await new MarketplaceStorageSettingsStore(settings).SaveAsync(key, profile);
        var loaded = JsonSerializer.Deserialize<MarketplaceStorageProfile>((await settings.GetAsync(key))!)!;
        Assert.Equal(profile.CacheDirectory, loaded.CacheDirectory);
        Assert.Equal(profile.DownloadDirectory, loaded.DownloadDirectory);
        Assert.Equal(profile.Libraries, loaded.Libraries);
        Assert.Equal(1, settings.Writes);
    }

    [Theory]
    [InlineData("relative")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("disabled")]
    [InlineData("negative")]
    public async Task InvalidDraftPreservesLastValidSnapshotAndDoesNotBlockOtherMarket(string invalid)
    {
        var settings = new MemorySettings();
        var store = new MarketplaceStorageSettingsStore(settings);
        var original = Profile("fab");
        await store.SaveAsync("fab", original);
        var saved = await settings.GetAsync("fab");
        var draft = invalid switch
        {
            "relative" => original with { CacheDirectory = "relative-path" },
            "empty" => original with { DownloadDirectory = "" },
            "duplicate" => original with { Libraries = [original.Libraries[0], original.Libraries[0]] },
            "disabled" => original with { Libraries = [original.Libraries[0] with { Enabled = false }] },
            _ => original with { Libraries = [original.Libraries[0] with { MaximumBytes = -1 }] }
        };
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("fab", draft));
        await store.SaveAsync("quixel", Profile("quixel"));
        Assert.Equal(saved, await settings.GetAsync("fab"));
        Assert.NotNull(await settings.GetAsync("quixel"));
    }

    [Fact]
    public async Task SavingPathsDoesNotCreateDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var profile = new MarketplaceStorageProfile("fab", Path.Combine(root, "cache"),
            Path.Combine(root, "download"), [new(Path.Combine(root, "library"))]);
        await new MarketplaceStorageSettingsStore(new MemorySettings()).SaveAsync("fab", profile);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task ConnectorReadsLegacyThenPersistsAtomicSnapshot()
    {
        var settings = new MemorySettings();
        var prefix = "marketplaces.connectors.cosmos";
        await settings.SetAsync(prefix + ".enabled.v1", "false");
        await settings.SetAsync(prefix + ".downloads.v1", Root);
        var store = new MarketplaceConnectorSettingsStore(settings);
        Assert.Equal(new(false, Root), await store.LoadAsync("cosmos", "unused"));
        var updated = new MarketplaceConnectorSettings(true, Path.Combine(Root, "updated"));
        var previousWrites = settings.Writes;
        await store.SaveAsync("cosmos", updated);
        Assert.Equal(previousWrites + 1, settings.Writes);
        Assert.Equal(updated, await new MarketplaceConnectorSettingsStore(settings).LoadAsync("cosmos", "unused"));
        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync("cosmos", updated with { DownloadDirectory = "" }));
        Assert.Equal(updated, await store.LoadAsync("cosmos", "unused"));
    }

    private sealed class MemorySettings : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = [];
        public int Writes { get; private set; }
        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(_values.GetValueOrDefault(key));
        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            Writes++;
            return Task.CompletedTask;
        }
    }
}
