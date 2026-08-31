using CyEngineHub.Engines;

namespace CyEngineHub.Projects.Creation;

public sealed record ProjectConfigurationProfile(
    string Id,
    string DisplayName,
    string Description,
    EngineFamily Engine,
    IReadOnlyDictionary<string, string> EngineSettings,
    IReadOnlyList<string> EnabledPlugins,
    IReadOnlyList<string> DisabledPlugins)
{
    public IReadOnlyList<ProjectIniSetting> IniSettings { get; init; } = [];
    public bool IsBuiltIn { get; init; } = true;
    public bool DisableEnginePluginsByDefault { get; init; }
}

public sealed record ProjectFeaturePack(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> EnabledPlugins,
    IReadOnlyList<string> DisabledPlugins,
    IReadOnlyList<ProjectIniSetting> IniSettings)
{
    public bool IsBuiltIn { get; init; } = true;
}

public sealed record ProjectIniSetting(
    string FileName,
    string Section,
    string Key,
    string Value);

public enum ProjectPostCreationOptionKind
{
    Boolean,
    Text,
    Choice,
}

public sealed record ProjectPostCreationOptionDefinition(
    string Id,
    string DisplayName,
    string Description,
    ProjectPostCreationOptionKind Kind,
    string DefaultValue,
    IReadOnlyList<string> Choices,
    bool IsRequired = false);

public sealed record ProjectPostCreationScriptDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string Publisher,
    IReadOnlyList<EngineFamily> SupportedEngines,
    IReadOnlyList<ProjectPostCreationOptionDefinition> Options,
    bool IsBuiltIn);

public sealed record ProjectPostCreationScriptSelection(
    string ScriptId,
    IReadOnlyDictionary<string, string> Configuration);

public sealed record ProjectPostCreationContext(
    string ProjectRoot,
    string ProjectMarker,
    string ProjectName,
    EngineFamily Engine,
    string EngineAssociation,
    string? EngineRoot,
    IReadOnlyDictionary<string, string> Configuration);

public sealed record ProjectPostCreationScriptResult(
    string ScriptId,
    string Summary,
    IReadOnlyDictionary<string, string> Outputs);

public interface IProjectPostCreationScript
{
    ProjectPostCreationScriptDescriptor Descriptor { get; }

    Task<ProjectPostCreationScriptResult> ExecuteAsync(
        ProjectPostCreationContext context,
        CancellationToken cancellationToken = default);
}

public interface IProjectPostCreationScriptRegistry
{
    IReadOnlyList<IProjectPostCreationScript> Scripts { get; }

    IProjectPostCreationScript? Find(string id);

    Task<int> RefreshExternalAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default);
}

public sealed record ProjectCreationReceipt(
    int SchemaVersion,
    DateTimeOffset CreatedAt,
    string ProjectName,
    string Engine,
    string EngineAssociation,
    string ProjectType,
    string PresetId,
    string ConfigurationProfileId,
    IReadOnlyList<string> EnabledPlugins,
    IReadOnlyList<string> DisabledPlugins,
    IReadOnlyList<string> StarterCollections,
    IReadOnlyList<string> StarterAssetIds,
    IReadOnlyList<ProjectPostCreationScriptResult> PostCreationResults);
