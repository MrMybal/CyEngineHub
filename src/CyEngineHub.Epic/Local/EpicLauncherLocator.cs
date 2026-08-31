using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CyEngineHub.Epic.Local;

public interface IEpicLauncherLocator
{
    string? FindLauncherExecutable();

    bool IsLauncherRunning();
}

public sealed class EpicLauncherLocator : IEpicLauncherLocator
{
    private static readonly string[] RelativeExecutablePaths =
    [
        Path.Combine("Launcher", "Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
        Path.Combine("Portal", "Binaries", "Win64", "EpicGamesLauncher.exe"),
    ];

    public string? FindLauncherExecutable()
    {
        var candidates = new List<string>
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "Epic Games",
                RelativeExecutablePaths[0]),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Epic Games",
                RelativeExecutablePaths[0]),
        };
        if (OperatingSystem.IsWindows())
        {
            candidates.AddRange(ReadWindowsInstallLocations());
        }

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(File.Exists);
    }

    public bool IsLauncherRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("EpicGamesLauncher");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<string> ReadWindowsInstallLocations()
    {
        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in views)
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var product = uninstall.OpenSubKey(subKeyName);
                    if (product is null)
                    {
                        continue;
                    }

                    if (!string.Equals(
                            product.GetValue("DisplayName") as string,
                            "Epic Games Launcher",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var installLocation = product.GetValue("InstallLocation") as string;
                    if (string.IsNullOrWhiteSpace(installLocation))
                    {
                        continue;
                    }

                    foreach (var relativePath in RelativeExecutablePaths)
                    {
                        yield return Path.Combine(installLocation, relativePath);
                    }
                }
            }
        }
    }
}
