using CyEngineHub.Toolchains.Models;
using static CyEngineHub.Toolchains.Detection.ToolchainDetectionHelpers;

namespace CyEngineHub.Toolchains.Detection;

internal static class AndroidToolchainDetector
{
    public static IReadOnlyList<string> Detect(
        IReadOnlyList<EnvironmentVariableSnapshot> variables,
        List<ToolchainInstallation> installations,
        List<string> diagnostics)
    {
        var androidRoots = new HashSet<string>(PathComparer);
        foreach (var variable in variables.Where(item =>
                     item.Name is "ANDROID_HOME" or "ANDROID_SDK_ROOT"))
        {
            AddExistingDirectory(androidRoots, variable.ProcessValue);
            AddExistingDirectory(androidRoots, variable.UserValue);
            AddExistingDirectory(androidRoots, variable.MachineValue);
        }

        AddExistingDirectory(
            androidRoots,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Android",
                "Sdk"));
        var adbPaths = new List<string>();
        foreach (var root in androidRoots)
        {
            DetectSdk(root, installations, adbPaths);
        }

        DetectJava(variables, installations);
        if (androidRoots.Count == 0)
        {
            diagnostics.Add("Android SDK non détecté.");
        }

        return adbPaths;
    }

    private static void DetectSdk(
        string root,
        List<ToolchainInstallation> installations,
        List<string> adbPaths)
    {
        var platforms = ChildNames(Path.Combine(root, "platforms"));
        var buildTools = ChildNames(Path.Combine(root, "build-tools"));
        var issues = new List<string>();
        if (platforms.Length == 0)
        {
            issues.Add("Aucune plateforme Android installée.");
        }

        if (buildTools.Length == 0)
        {
            issues.Add("Android build-tools absents.");
        }

        AddInstallation(
            installations,
            ToolchainKind.AndroidSdk,
            "Android SDK",
            LatestVersion(platforms) ?? "Sans plateforme",
            root,
            "Environnement / chemin standard",
            issues.Count == 0 ? ToolchainHealth.Ready : ToolchainHealth.Warning,
            issues,
            new Dictionary<string, string>
            {
                ["Platforms"] = string.Join(';', platforms),
                ["BuildTools"] = string.Join(';', buildTools),
            });

        foreach (var ndkRoot in GetDirectoriesSafe(Path.Combine(root, "ndk")))
        {
            AddNdk(installations, ndkRoot);
        }

        var legacyNdk = Path.Combine(root, "ndk-bundle");
        if (Directory.Exists(legacyNdk))
        {
            AddNdk(installations, legacyNdk);
        }

        foreach (var cmakeRoot in GetDirectoriesSafe(Path.Combine(root, "cmake")))
        {
            AddKnownExecutable(
                installations,
                ToolchainKind.CMake,
                $"CMake Android {Path.GetFileName(cmakeRoot)}",
                Path.Combine(cmakeRoot, "bin", OperatingSystem.IsWindows() ? "cmake.exe" : "cmake"),
                "Android SDK",
                Path.GetFileName(cmakeRoot));
        }

        var adb = Path.Combine(
            root,
            "platform-tools",
            OperatingSystem.IsWindows() ? "adb.exe" : "adb");
        if (File.Exists(adb))
        {
            adbPaths.Add(adb);
            AddKnownExecutable(
                installations,
                ToolchainKind.Adb,
                "Android Debug Bridge",
                adb,
                "Android SDK");
        }
    }

    private static void AddNdk(
        List<ToolchainInstallation> installations,
        string ndkRoot)
    {
        var version = ReadProperty(Path.Combine(ndkRoot, "source.properties"), "Pkg.Revision")
                      ?? Path.GetFileName(ndkRoot);
        var hasLlvm = Directory.Exists(Path.Combine(ndkRoot, "toolchains", "llvm"));
        AddInstallation(
            installations,
            ToolchainKind.AndroidNdk,
            $"Android NDK {version}",
            version,
            ndkRoot,
            "Android SDK",
            hasLlvm ? ToolchainHealth.Ready : ToolchainHealth.Warning,
            hasLlvm ? [] : ["Toolchain LLVM absente."]);
    }

    private static void DetectJava(
        IReadOnlyList<EnvironmentVariableSnapshot> variables,
        List<ToolchainInstallation> installations)
    {
        var candidates = new HashSet<string>(PathComparer);
        foreach (var variable in variables.Where(item => item.Name == "JAVA_HOME"))
        {
            AddJavaCandidate(candidates, variable.ProcessValue);
            AddJavaCandidate(candidates, variable.UserValue);
            AddJavaCandidate(candidates, variable.MachineValue);
        }

        AddJavaCandidate(
            candidates,
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Android",
                "Android Studio",
                "jbr"));
        var pathJava = ExecutableLocator.Find(OperatingSystem.IsWindows() ? "java.exe" : "java");
        if (pathJava is not null)
        {
            candidates.Add(pathJava);
        }

        foreach (var candidate in candidates)
        {
            var executable = Directory.Exists(candidate)
                ? Path.Combine(candidate, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java")
                : candidate;
            if (!File.Exists(executable))
            {
                continue;
            }

            var home = Directory.GetParent(Path.GetDirectoryName(executable)!)?.FullName
                       ?? Path.GetDirectoryName(executable)!;
            var version = ReadProperty(Path.Combine(home, "release"), "JAVA_VERSION")?.Trim('"')
                          ?? ReadFileVersion(executable);
            var compiler = Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "javac.exe" : "javac");
            AddInstallation(
                installations,
                ToolchainKind.Java,
                $"Java / JDK {version}",
                version,
                home,
                "Environnement / Android Studio / PATH",
                File.Exists(compiler) ? ToolchainHealth.Ready : ToolchainHealth.Warning,
                File.Exists(compiler) ? [] : ["javac absent : runtime Java sans JDK complet."]);
        }
    }

    private static string[] ChildNames(string path) => GetDirectoriesSafe(path)
        .Select(Path.GetFileName)
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .ToArray();

    private static void AddJavaCandidate(HashSet<string> paths, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var path = value.Trim().Trim('"');
        if (Directory.Exists(path) || File.Exists(path))
        {
            paths.Add(Path.GetFullPath(path));
        }
    }
}
