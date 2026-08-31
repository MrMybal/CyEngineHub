using CyEngineHub.Build.Models;

namespace CyEngineHub.Build.Presets;

public enum UnrealBuildPresetKind
{
    Target,
    Package,
}

public sealed record UnrealBuildPreset(
    string Id,
    string Name,
    UnrealBuildPresetKind Kind,
    string Target,
    string Platform,
    string Configuration,
    bool AttachProject = true,
    UnrealBuildAction Action = UnrealBuildAction.Build,
    int? MaximumParallelActions = null,
    bool Cook = true,
    bool Stage = true,
    bool Package = true,
    bool Archive = true,
    bool Pak = true,
    bool IoStore = true,
    bool Compressed = false,
    bool Distribution = false,
    bool IncludePrerequisites = true,
    string? CookFlavor = null);

public static class UnrealBuildPresetDefaults
{
    public const string BuiltInPrefix = "builtin:";

    public static IReadOnlyList<UnrealBuildPreset> All { get; } =
    [
        new(
            "builtin:editor-development",
            "Editor Development",
            UnrealBuildPresetKind.Target,
            "{Project}Editor",
            "Win64",
            "Development"),
        new(
            "builtin:editor-debuggame",
            "Editor DebugGame",
            UnrealBuildPresetKind.Target,
            "{Project}Editor",
            "Win64",
            "DebugGame"),
        new(
            "builtin:game-shipping",
            "Game Win64 Shipping",
            UnrealBuildPresetKind.Target,
            "{Project}",
            "Win64",
            "Shipping"),
        new(
            "builtin:linux-server",
            "Linux Server Shipping",
            UnrealBuildPresetKind.Target,
            "{Project}Server",
            "Linux",
            "Shipping"),
        new(
            "builtin:package-win64",
            "Package Win64 Shipping",
            UnrealBuildPresetKind.Package,
            string.Empty,
            "Win64",
            "Shipping"),
        new(
            "builtin:package-android-astc",
            "Package Android ASTC",
            UnrealBuildPresetKind.Package,
            string.Empty,
            "Android",
            "Development",
            CookFlavor: "ASTC"),
    ];

    public static bool IsBuiltIn(string id) =>
        id.StartsWith(BuiltInPrefix, StringComparison.OrdinalIgnoreCase);
}
