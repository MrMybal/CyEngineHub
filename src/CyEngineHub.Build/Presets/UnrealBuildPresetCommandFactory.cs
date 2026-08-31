using CyEngineHub.Build.Commands;
using CyEngineHub.Build.Models;
using CyEngineHub.Core.Processes;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Build.Presets;

public sealed record UnrealBuildPresetOperation(string Title, ProcessRequest ProcessRequest);

public static class UnrealBuildPresetCommandFactory
{
    public static UnrealBuildPresetOperation Create(
        UnrealBuildPreset preset,
        UnrealEngineInstallation engine,
        UnrealProject project,
        string archiveDirectory)
    {
        var normalized = UnrealBuildPresetValidator.ValidateAndNormalize(preset);
        return normalized.Kind switch
        {
            UnrealBuildPresetKind.Target => CreateTarget(normalized, engine, project),
            UnrealBuildPresetKind.Package => CreatePackage(
                normalized,
                engine,
                project,
                archiveDirectory),
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset.Kind, "Type inconnu."),
        };
    }

    private static UnrealBuildPresetOperation CreateTarget(
        UnrealBuildPreset preset,
        UnrealEngineInstallation engine,
        UnrealProject project)
    {
        var target = preset.Target.Replace(
            "{Project}",
            project.Name,
            StringComparison.OrdinalIgnoreCase);
        var request = UnrealBuildCommandFactory.CreateTargetBuild(new UnrealTargetBuildRequest(
            engine,
            target,
            preset.Platform,
            preset.Configuration,
            preset.Action,
            preset.AttachProject ? project : null,
            preset.MaximumParallelActions));
        return new UnrealBuildPresetOperation(
            $"{preset.Name} · {project.Name}",
            request);
    }

    private static UnrealBuildPresetOperation CreatePackage(
        UnrealBuildPreset preset,
        UnrealEngineInstallation engine,
        UnrealProject project,
        string archiveDirectory)
    {
        var request = UnrealBuildCommandFactory.CreatePackage(new UnrealPackageRequest(
            engine,
            project,
            preset.Platform,
            preset.Configuration,
            archiveDirectory,
            Build: preset.Action != UnrealBuildAction.Clean,
            Cook: preset.Cook,
            Stage: preset.Stage,
            Package: preset.Package,
            Archive: preset.Archive,
            Pak: preset.Pak,
            IoStore: preset.IoStore,
            Compressed: preset.Compressed,
            Distribution: preset.Distribution,
            IncludePrerequisites: preset.IncludePrerequisites,
            CookFlavor: preset.CookFlavor));
        return new UnrealBuildPresetOperation(
            $"{preset.Name} · {project.Name}",
            request);
    }
}
