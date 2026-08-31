using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Fab.Installation;

public enum FabInstallationTargetKind
{
    Project,
    Engine,
}

public enum FabInstallationOptionKind
{
    Boolean,
    Text,
    Choice,
}

public sealed record FabInstallationOptionDefinition(
    string Id,
    string DisplayName,
    string Description,
    FabInstallationOptionKind Kind,
    string DefaultValue,
    IReadOnlyList<string> Choices,
    bool IsRequired = false);

public sealed record FabInstallationScriptDescriptor(
    string Id,
    string DisplayName,
    string Description,
    string Publisher,
    IReadOnlyList<LibraryPackageContentKind> SupportedContentKinds,
    IReadOnlyList<FabInstallationTargetKind> SupportedTargetKinds,
    IReadOnlyList<FabInstallationOptionDefinition> Options,
    bool IsBuiltIn);

public sealed record FabInstallationScriptTarget(
    string Id,
    string DisplayName,
    FabInstallationTargetKind Kind,
    string RootPath,
    string EngineVersion,
    UnrealProject? Project,
    UnrealEngineInstallation? Engine);

public sealed record FabInstallationScriptContext(
    RemoteFabAsset Asset,
    RemoteFabProjectVersion Version,
    LibraryPackageContentKind ContentKind,
    string SourceDirectory,
    FabInstallationScriptTarget Target,
    IReadOnlyDictionary<string, string> Configuration);

public sealed record FabInstallationScriptProgress(
    double? Fraction,
    string Message);

public sealed record FabInstallationScriptResult(
    string ScriptId,
    string Summary,
    IReadOnlyDictionary<string, string> Outputs);

public interface IFabInstallationScript
{
    FabInstallationScriptDescriptor Descriptor { get; }

    Task<FabInstallationScriptResult> InstallAsync(
        FabInstallationScriptContext context,
        IProgress<FabInstallationScriptProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
