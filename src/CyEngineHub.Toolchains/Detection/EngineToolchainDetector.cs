using CyEngineHub.Toolchains.Models;
using CyEngineHub.Unreal.Models;
using static CyEngineHub.Toolchains.Detection.ToolchainDetectionHelpers;

namespace CyEngineHub.Toolchains.Detection;

internal static class EngineToolchainDetector
{
    public static void Detect(
        IReadOnlyList<UnrealEngineInstallation> engines,
        IReadOnlyList<EnvironmentVariableSnapshot> variables,
        List<ToolchainInstallation> installations,
        List<string> diagnostics)
    {
        foreach (var engine in engines)
        {
            DetectBundledDotNet(engine, installations);
            DetectGradle(engine, installations);
            DetectInTreeLinux(engine, installations);
        }

        DetectEnvironmentLinux(variables, installations);
        AddPathExecutable(installations, ToolchainKind.CMake, "CMake", "cmake");
        AddPathExecutable(installations, ToolchainKind.Ninja, "Ninja", "ninja");
        AddPathExecutable(installations, ToolchainKind.Clang, "Clang", "clang");
        AddPathExecutable(installations, ToolchainKind.Gradle, "Gradle", "gradle");
        AddPathExecutable(installations, ToolchainKind.Adb, "Android Debug Bridge", "adb");
        if (!installations.Any(item => item.Kind == ToolchainKind.LinuxToolchain))
        {
            diagnostics.Add("Cross-toolchain Linux Unreal non détectée.");
        }
    }

    private static void DetectBundledDotNet(
        UnrealEngineInstallation engine,
        List<ToolchainInstallation> installations)
    {
        var root = Path.Combine(engine.RootPath, "Engine", "Binaries", "ThirdParty", "DotNet");
        foreach (var versionDirectory in GetDirectoriesSafe(root))
        {
            var executable = Path.Combine(
                versionDirectory,
                OperatingSystem.IsWindows() ? "win-x64" : "linux-x64",
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(executable))
            {
                AddInstallation(
                    installations,
                    ToolchainKind.DotNet,
                    $".NET fourni avec {engine.DisplayName}",
                    Path.GetFileName(versionDirectory),
                    executable,
                    "Unreal Engine",
                    ToolchainHealth.Ready,
                    details: new Dictionary<string, string> { ["EngineId"] = engine.Id });
            }
        }
    }

    private static void DetectGradle(
        UnrealEngineInstallation engine,
        List<ToolchainInstallation> installations)
    {
        var path = Path.Combine(
            engine.RootPath,
            "Engine",
            "Build",
            "Android",
            "Java",
            OperatingSystem.IsWindows() ? "gradlew.bat" : "gradlew");
        if (File.Exists(path))
        {
            AddInstallation(
                installations,
                ToolchainKind.Gradle,
                $"Gradle wrapper · {engine.DisplayName}",
                "Wrapper UE",
                path,
                "Unreal Engine",
                ToolchainHealth.Ready,
                details: new Dictionary<string, string> { ["EngineId"] = engine.Id });
        }
    }

    private static void DetectInTreeLinux(
        UnrealEngineInstallation engine,
        List<ToolchainInstallation> installations)
    {
        var root = Path.Combine(
            engine.RootPath,
            "Engine",
            "Extras",
            "ThirdPartyNotUE",
            "SDKs",
            OperatingSystem.IsWindows() ? "HostWin64" : "HostLinux",
            "Linux_x64");
        AddLinuxDirectories(root, $"Fourni avec {engine.DisplayName}", installations, engine.Id);
    }

    private static void DetectEnvironmentLinux(
        IReadOnlyList<EnvironmentVariableSnapshot> variables,
        List<ToolchainInstallation> installations)
    {
        foreach (var variable in variables.Where(item =>
                     item.Name is "UE_SDKS_ROOT" or "LINUX_MULTIARCH_ROOT"))
        {
            foreach (var value in new[] { variable.ProcessValue, variable.UserValue, variable.MachineValue }
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(PathComparer))
            {
                var root = Path.GetFullPath(value!);
                if (variable.Name == "UE_SDKS_ROOT")
                {
                    root = Path.Combine(
                        root,
                        OperatingSystem.IsWindows() ? "HostWin64" : "HostLinux",
                        "Linux_x64");
                }

                AddLinuxDirectories(root, variable.Name, installations, null);
            }
        }
    }

    private static void AddLinuxDirectories(
        string root,
        string source,
        List<ToolchainInstallation> installations,
        string? engineId)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var directories = GetDirectoriesSafe(root);
        if (directories.Count == 0 && Path.GetFileName(root).StartsWith('v'))
        {
            directories = [root];
        }

        foreach (var directory in directories.Where(item =>
                     Path.GetFileName(item).StartsWith('v')))
        {
            var details = new Dictionary<string, string>();
            if (engineId is not null)
            {
                details["EngineId"] = engineId;
            }

            var version = Path.GetFileName(directory);
            AddInstallation(
                installations,
                ToolchainKind.LinuxToolchain,
                $"Linux {version}",
                version,
                directory,
                source,
                ToolchainHealth.Ready,
                details: details);
        }
    }
}
