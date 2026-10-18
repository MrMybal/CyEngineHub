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

    [Fact]
    public void SetAssociationReplacesOnlyThisRootsValues()
    {
        CreateValidEngine(_testDirectory);
        var store = new MemoryRegistrationStore();
        store.Write(new("Old-1", _testDirectory)); store.Write(new("Old-2", _testDirectory));
        store.Write(new("Other", Path.Combine(_testDirectory, "Other")));
        var service = new UnrealEngineRegistrationService(store);
        var result = service.SetAssociation(_testDirectory, "  Oculus-5.5-QA  ");
        Assert.Equal("Oculus-5.5-QA", service.FindByRoot(_testDirectory)!.Identifier);
        Assert.Equal(result, service.SetAssociation(_testDirectory, "Oculus-5.5-QA"));
        Assert.Equal(2, store.Registrations.Count);
        Assert.Contains(store.Registrations, item => item.Identifier == "Other");
    }

    [Theory]
    [InlineData("5.5")]
    [InlineData("bad\\key")]
    [InlineData("bad/key")]
    [InlineData("bad\nkey")]
    [InlineData("")]
    public void InvalidIdentifierDoesNotChangeAssociations(string identifier)
    {
        CreateValidEngine(_testDirectory);
        var store = new MemoryRegistrationStore();
        var service = new UnrealEngineRegistrationService(store);
        var old = service.Register(_testDirectory);
        Assert.Throws<UnrealEngineRegistrationException>(() => service.SetAssociation(_testDirectory, identifier));
        Assert.Equal(old, Assert.Single(store.Registrations));
    }

    [Fact]
    public void CollisionWithAnotherRootNeverOverwritesIt()
    {
        CreateValidEngine(_testDirectory);
        var store = new MemoryRegistrationStore();
        store.Write(new("Oculus-5.5", Path.Combine(_testDirectory, "Other")));
        var service = new UnrealEngineRegistrationService(store);
        Assert.Throws<UnrealEngineRegistrationException>(() => service.SetAssociation(_testDirectory, "oculus-5.5"));
        Assert.Equal(Path.Combine(_testDirectory, "Other"), Assert.Single(store.Registrations).RootPath);
    }

    [Fact]
    public void PartialRemovalFailureRestoresPreviousValues()
    {
        CreateValidEngine(_testDirectory);
        var store = new MemoryRegistrationStore();
        store.Write(new("Old-1", _testDirectory)); store.Write(new("Old-2", _testDirectory));
        store.FailDelete = "Old-2";
        var service = new UnrealEngineRegistrationService(store);
        Assert.Throws<IOException>(() => service.SetAssociation(_testDirectory, "New"));
        Assert.Equal("Old-1,Old-2", string.Join(",", store.Registrations.Select(item => item.Identifier).Order()));
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

        public string? FailDelete { get; set; }
        public void Write(UnrealEngineRegistration registration)
        {
            Registrations.RemoveAll(item => item.Identifier.Equals(registration.Identifier, StringComparison.OrdinalIgnoreCase));
            Registrations.Add(registration);
        }

        public bool Delete(string identifier)
        {
            if (FailDelete == identifier) { FailDelete = null; throw new IOException("Fixture failure"); }
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
