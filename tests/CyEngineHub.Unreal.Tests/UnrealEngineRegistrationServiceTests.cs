using CyEngineHub.Unreal.Registration;

namespace CyEngineHub.Unreal.Tests;

public sealed class UnrealEngineRegistrationServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "CyEngineHub.Tests",
        $"Registry Engine {Guid.NewGuid():N}");

    [Fact]
    public void RegisterIsIdempotentForTheSameRoot()
    {
        CreateValidEngine(_testDirectory);
        var store = new MemoryRegistrationStore();
        var service = new UnrealEngineRegistrationService(store);

        var first = service.Register(_testDirectory);
        var second = service.Register(_testDirectory + Path.DirectorySeparatorChar);

        Assert.Equal(first, second);
        Assert.StartsWith("{", first.Identifier, StringComparison.Ordinal);
        Assert.EndsWith("}", first.Identifier, StringComparison.Ordinal);
        Assert.Single(store.Registrations);
    }

    [Fact]
    public void UnregisterDeletesOnlyTheMatchingRoot()
    {
        CreateValidEngine(_testDirectory);
        var otherRoot = Path.Combine(_testDirectory, "Other");
        CreateValidEngine(otherRoot);
        var store = new MemoryRegistrationStore();
        var service = new UnrealEngineRegistrationService(store);
        var target = service.Register(_testDirectory);
        var other = service.Register(otherRoot);

        var removed = service.Unregister(_testDirectory);

        Assert.True(removed);
        Assert.DoesNotContain(store.Registrations, item => item.Identifier == target.Identifier);
        Assert.Contains(store.Registrations, item => item.Identifier == other.Identifier);
    }

    [Fact]
    public void RegisterRejectsAnInvalidEngineRoot()
    {
        Directory.CreateDirectory(_testDirectory);
        var service = new UnrealEngineRegistrationService(new MemoryRegistrationStore());

        Assert.Throws<UnrealEngineRegistrationException>(() => service.Register(_testDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    private static void CreateValidEngine(string rootPath)
    {
        Directory.CreateDirectory(Path.Combine(rootPath, "Engine", "Binaries"));
        Directory.CreateDirectory(Path.Combine(rootPath, "Engine", "Build"));
    }

    private sealed class MemoryRegistrationStore : IUnrealEngineRegistrationStore
    {
        public List<UnrealEngineRegistration> Registrations { get; } = [];

        public IReadOnlyList<UnrealEngineRegistration> ReadAll() => Registrations.ToArray();

        public void Write(UnrealEngineRegistration registration) => Registrations.Add(registration);

        public bool Delete(string identifier)
        {
            var index = Registrations.FindIndex(item => item.Identifier == identifier);
            if (index < 0)
            {
                return false;
            }

            Registrations.RemoveAt(index);
            return true;
        }
    }
}
