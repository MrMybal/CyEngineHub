using CyEngineHub.Engines;
using CyEngineHub.CryEngine.Integration;
using CyEngineHub.Godot.Integration;
using CyEngineHub.Unreal.Integration;
using CyEngineHub.Unity.Integration;

namespace CyEngineHub.Engines.Tests;

public sealed class EngineModuleRegistryTests
{
    [Fact]
    public void RegistrySupportsUnrealUnityGodotAndCryEngineProviders()
    {
        var registry = new EngineModuleRegistry(
        [
            new UnrealEngineModule(),
            new UnityEngineModule(),
            new GodotEngineModule(),
            new CryEngineModule(),
        ]);

        Assert.Equal(4, registry.Modules.Count);
        Assert.Equal(EngineModuleStatus.Active, registry.GetRequired(EngineFamily.Unreal).Descriptor.Status);
        Assert.Equal(EngineModuleStatus.Active, registry.GetRequired(EngineFamily.Unity).Descriptor.Status);
        Assert.Equal(EngineModuleStatus.Active, registry.GetRequired(EngineFamily.Godot).Descriptor.Status);
        Assert.Equal(EngineModuleStatus.Active, registry.GetRequired(EngineFamily.CryEngine).Descriptor.Status);
        Assert.True(registry.GetRequired(EngineFamily.Unity).Descriptor.Capabilities.HasFlag(
            EngineCapability.ProjectLaunch));
        Assert.True(registry.GetRequired(EngineFamily.Unity).Descriptor.Capabilities.HasFlag(
            EngineCapability.PackageInventory));
        Assert.True(registry.GetRequired(EngineFamily.Godot).Descriptor.Capabilities.HasFlag(
            EngineCapability.InstallationDiscovery));
        Assert.Contains(
            "project.godot",
            registry.GetRequired(EngineFamily.Godot).Descriptor.ProjectMarkers);
        Assert.Contains(
            ".cryproject",
            registry.GetRequired(EngineFamily.CryEngine).Descriptor.ProjectMarkers);
        Assert.True(registry.GetRequired(EngineFamily.CryEngine).Descriptor.Capabilities.HasFlag(
            EngineCapability.ProjectLaunch));
        Assert.All(
            registry.Modules.Where(module => module.Descriptor.Family is
                EngineFamily.Unity or EngineFamily.Godot or EngineFamily.CryEngine),
            module => Assert.IsAssignableFrom<ILocalEnginePackageProvider>(module));
        Assert.All(
            registry.Modules.Where(module => module.Descriptor.Family is
                EngineFamily.Unity or EngineFamily.Godot),
            module =>
            {
                Assert.True(module.Descriptor.Capabilities.HasFlag(EngineCapability.PackageInstallation));
                Assert.True(module.Descriptor.Capabilities.HasFlag(EngineCapability.PackageRemoval));
                Assert.IsAssignableFrom<ILocalEnginePackageManagementProvider>(module);
            });
        Assert.False(registry.GetRequired(EngineFamily.CryEngine).Descriptor.Capabilities.HasFlag(
            EngineCapability.PackageInstallation));
        Assert.False(registry.GetRequired(EngineFamily.CryEngine).Descriptor.Capabilities.HasFlag(
            EngineCapability.PackageRemoval));
    }

    [Fact]
    public void RegistryRejectsDuplicateProviders()
    {
        var modules = new[]
        {
            Create(EngineFamily.Unity, EngineModuleStatus.Planned, ".unity"),
            Create(EngineFamily.Unity, EngineModuleStatus.Preview, "ProjectSettings"),
        };

        Assert.Throws<ArgumentException>(() => new EngineModuleRegistry(modules));
    }

    [Fact]
    public void RegistryRejectsIncompleteDescriptors()
    {
        var module = new StaticEngineModule(new EngineModuleDescriptor(
            EngineFamily.Custom,
            string.Empty,
            "Custom",
            "Vendor",
            EngineModuleStatus.Planned,
            [],
            EngineCapability.None,
            "Extension provider"));

        Assert.Throws<ArgumentException>(() => new EngineModuleRegistry([module]));
    }

    private static StaticEngineModule Create(
        EngineFamily family,
        EngineModuleStatus status,
        string marker) =>
        new StaticEngineModule(new EngineModuleDescriptor(
            family,
            family.ToString().ToLowerInvariant(),
            family.ToString(),
            "Vendor",
            status,
            [marker],
            EngineCapability.ProjectDiscovery,
            "Provider de test"));
}
