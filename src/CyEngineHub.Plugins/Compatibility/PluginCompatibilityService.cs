using System.Globalization;
using CyEngineHub.Plugins.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Plugins.Compatibility;

public interface IPluginCompatibilityService
{
    PluginCompatibility Evaluate(
        UnrealPluginDescriptor plugin,
        UnrealEngineInstallation engine,
        string targetPlatform = "Win64");
}

public sealed class PluginCompatibilityService : IPluginCompatibilityService
{
    public PluginCompatibility Evaluate(
        UnrealPluginDescriptor plugin,
        UnrealEngineInstallation engine,
        string targetPlatform = "Win64")
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentNullException.ThrowIfNull(engine);
        var warnings = new List<string>();
        var isCompatible = true;
        var requiresRebuild = false;

        if (TryParseMajorMinor(plugin.EngineVersion, out var major, out var minor) &&
            (major != engine.Version.Major || minor != engine.Version.Minor))
        {
            if (plugin.HasSource)
            {
                requiresRebuild = true;
                warnings.Add(
                    $"Prévu pour UE {major}.{minor}; une recompilation pour {engine.Version.Major}.{engine.Version.Minor} est recommandée.");
            }
            else
            {
                isCompatible = false;
                warnings.Add(
                    $"Plugin binaire prévu pour UE {major}.{minor}, incompatible avec {engine.Version.Major}.{engine.Version.Minor}.");
            }
        }

        if (plugin.SupportedTargetPlatforms.Count > 0 &&
            !plugin.SupportedTargetPlatforms.Contains(targetPlatform, StringComparer.OrdinalIgnoreCase))
        {
            isCompatible = false;
            warnings.Add($"La plateforme {targetPlatform} n’est pas déclarée dans SupportedTargetPlatforms.");
        }

        if (plugin.Modules.Count > 0 && plugin.Modules.All(module =>
                (module.PlatformAllowList.Count > 0 &&
                 !module.PlatformAllowList.Contains(targetPlatform, StringComparer.OrdinalIgnoreCase)) ||
                module.PlatformDenyList.Contains(targetPlatform, StringComparer.OrdinalIgnoreCase)))
        {
            isCompatible = false;
            warnings.Add($"Tous les modules excluent la plateforme {targetPlatform}.");
        }

        return new PluginCompatibility(isCompatible, requiresRebuild, warnings);
    }

    private static bool TryParseMajorMinor(string? value, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 &&
               int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major) &&
               int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor);
    }
}
