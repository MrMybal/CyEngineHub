using CyEngineHub.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace CyEngineHub.Core.Tests;

public sealed class SqliteCatalogStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SettingsRoundTripThroughSqlite()
    {
        var store = new SqliteCatalogStore(Path.Combine(_testDirectory, "catalog.db"));
        await store.InitializeAsync();
        const string expected = "[42]";
        await store.SetAsync("sample", expected);
        var value = await store.GetAsync("sample");

        Assert.Equal(expected, value);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
