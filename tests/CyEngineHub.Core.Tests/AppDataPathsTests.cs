using CyEngineHub.Infrastructure.AppData;

namespace CyEngineHub.Core.Tests;

public sealed class AppDataPathsTests
{
    [Fact]
    public void ResolveMigratesLegacyProfileAndDatabase()
    {
        using var temporary = new TemporaryDirectory();
        var executableDirectory = Directory.CreateDirectory(
            Path.Combine(temporary.Path, "Executable")).FullName;
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temporary.Path, "LocalAppData")).FullName;
        var legacyRoot = Directory.CreateDirectory(
            Path.Combine(localAppData, "CyUEHub")).FullName;
        File.WriteAllText(Path.Combine(legacyRoot, "cyuehub.db"), "legacy-database");

        var paths = AppDataPaths.Resolve(executableDirectory, localAppData);

        Assert.Equal(Path.Combine(localAppData, "CyEngineHub"), paths.RootDirectory);
        Assert.Equal(Path.Combine(paths.RootDirectory, "cyenginehub.db"), paths.DatabasePath);
        Assert.Equal("legacy-database", File.ReadAllText(paths.DatabasePath));
        Assert.False(Directory.Exists(legacyRoot));
        Assert.False(paths.IsPortable);
    }

    [Fact]
    public void ResolveMigratesLegacyPortableDatabaseInPlace()
    {
        using var temporary = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(temporary.Path, "portable.flag"), string.Empty);
        var dataDirectory = Directory.CreateDirectory(
            Path.Combine(temporary.Path, "Data")).FullName;
        var legacyDatabase = Path.Combine(dataDirectory, "cyuehub.db");
        File.WriteAllText(legacyDatabase, "portable-database");

        var paths = AppDataPaths.Resolve(temporary.Path);

        Assert.True(paths.IsPortable);
        Assert.Equal(Path.Combine(dataDirectory, "cyenginehub.db"), paths.DatabasePath);
        Assert.Equal("portable-database", File.ReadAllText(paths.DatabasePath));
        Assert.False(File.Exists(legacyDatabase));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CyEngineHub-AppDataTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
