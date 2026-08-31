using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CyEngineHub.Unreal.Detection;

public sealed class WindowsRegistryEngineDiscoverySource : IEngineDiscoverySource
{
    public string Name => "Unreal registry";

    public Task<IReadOnlyList<EngineCandidate>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<IReadOnlyList<EngineCandidate>>([]);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<EngineCandidate>>(DiscoverWindows());
    }

    [SupportedOSPlatform("windows")]
    private static List<EngineCandidate> DiscoverWindows()
    {
        var results = new List<EngineCandidate>();
        ReadCustomBuilds(results);
        ReadEpicBuilds(results, RegistryView.Registry64);
        ReadEpicBuilds(results, RegistryView.Registry32);
        return results;
    }

    [SupportedOSPlatform("windows")]
    private static void ReadCustomBuilds(List<EngineCandidate> results)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Epic Games\Unreal Engine\Builds");
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames())
            {
                if (key.GetValue(name) is string path && !string.IsNullOrWhiteSpace(path))
                {
                    results.Add(new EngineCandidate(path, "Unreal registry (custom)", name));
                }
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ReadEpicBuilds(List<EngineCandidate> results, RegistryView view)
    {
        try
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var root = hive.OpenSubKey(@"SOFTWARE\EpicGames\Unreal Engine");
            if (root is null)
            {
                return;
            }

            foreach (var version in root.GetSubKeyNames())
            {
                using var versionKey = root.OpenSubKey(version);
                if (versionKey?.GetValue("InstalledDirectory") is string path &&
                    !string.IsNullOrWhiteSpace(path))
                {
                    results.Add(new EngineCandidate(
                        path,
                        "Epic registry",
                        version,
                        $"Unreal Engine {version}"));
                }
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}

