namespace CyEngineHub.Engines;

public enum EngineFamily
{
    Unreal,
    Unity,
    Godot,
    CryEngine,
    Custom,
}

public enum EngineModuleStatus
{
    Active,
    Preview,
    Planned,
    Disabled,
}

[Flags]
public enum EngineCapability
{
    None = 0,
    InstallationDiscovery = 1 << 0,
    ProjectDiscovery = 1 << 1,
    EditorLaunch = 1 << 2,
    ProjectLaunch = 1 << 3,
    Build = 1 << 4,
    Package = 1 << 5,
    PluginManagement = 1 << 6,
    AssetLibrary = 1 << 7,
    SourceBuild = 1 << 8,
    VersionManagement = 1 << 9,
    PackageInventory = 1 << 10,
    PackageInstallation = 1 << 11,
    PackageRemoval = 1 << 12,
}

public sealed record EngineModuleDescriptor(
    EngineFamily Family,
    string Id,
    string DisplayName,
    string Vendor,
    EngineModuleStatus Status,
    IReadOnlyList<string> ProjectMarkers,
    EngineCapability Capabilities,
    string Summary);

public interface IEngineModule
{
    EngineModuleDescriptor Descriptor { get; }
}

public interface IEngineModuleRegistry
{
    IReadOnlyList<IEngineModule> Modules { get; }

    IEngineModule GetRequired(EngineFamily family);
}

public sealed class EngineModuleRegistry : IEngineModuleRegistry
{
    private readonly Dictionary<EngineFamily, IEngineModule> _byFamily;

    public EngineModuleRegistry(IEnumerable<IEngineModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var validated = modules.Select(Validate).ToArray();
        var duplicate = validated
            .GroupBy(module => module.Descriptor.Family)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Plusieurs modules déclarent le moteur {duplicate.Key}.",
                nameof(modules));
        }

        _byFamily = validated.ToDictionary(module => module.Descriptor.Family);
        Modules = validated
            .OrderBy(module => module.Descriptor.Status)
            .ThenBy(module => module.Descriptor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<IEngineModule> Modules { get; }

    public IEngineModule GetRequired(EngineFamily family) =>
        _byFamily.TryGetValue(family, out var module)
            ? module
            : throw new KeyNotFoundException($"Aucun module moteur n’est enregistré pour {family}.");

    private static IEngineModule Validate(IEngineModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        var descriptor = module.Descriptor
            ?? throw new ArgumentException("Un module moteur doit fournir un descripteur.", nameof(module));
        if (string.IsNullOrWhiteSpace(descriptor.Id) ||
            string.IsNullOrWhiteSpace(descriptor.DisplayName) ||
            string.IsNullOrWhiteSpace(descriptor.Vendor) ||
            string.IsNullOrWhiteSpace(descriptor.Summary) ||
            descriptor.ProjectMarkers is null ||
            descriptor.ProjectMarkers.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                $"Le descripteur du module {descriptor.Family} est incomplet.",
                nameof(module));
        }

        return module;
    }
}

public sealed class StaticEngineModule(EngineModuleDescriptor descriptor) : IEngineModule
{
    public EngineModuleDescriptor Descriptor { get; } = descriptor;
}
