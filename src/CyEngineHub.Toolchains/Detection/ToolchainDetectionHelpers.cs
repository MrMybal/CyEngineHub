using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using CyEngineHub.Toolchains.Compatibility;
using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Detection;

internal static class ToolchainDetectionHelpers
{
    public static StringComparer PathComparer { get; } = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static void AddInstallation(
        List<ToolchainInstallation> installations,
        ToolchainKind kind,
        string displayName,
        string version,
        string path,
        string source,
        ToolchainHealth health,
        IReadOnlyList<string>? issues = null,
        IReadOnlyDictionary<string, string>? details = null)
    {
        var normalizedPath = NormalizePath(path);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{kind}|{normalizedPath}"));
        installations.Add(new ToolchainInstallation(
            Convert.ToHexString(hash)[..16],
            kind,
            displayName,
            string.IsNullOrWhiteSpace(version) ? "Inconnue" : version,
            path,
            source,
            health,
            issues ?? [],
            details ?? new Dictionary<string, string>()));
    }

    public static void AddKnownExecutable(
        List<ToolchainInstallation> installations,
        ToolchainKind kind,
        string displayName,
        string path,
        string source,
        string? explicitVersion = null)
    {
        if (File.Exists(path))
        {
            AddInstallation(
                installations,
                kind,
                displayName,
                explicitVersion ?? ReadFileVersion(path),
                path,
                source,
                ToolchainHealth.Ready);
        }
    }

    public static void AddPathExecutable(
        List<ToolchainInstallation> installations,
        ToolchainKind kind,
        string displayName,
        string command)
    {
        var path = ExecutableLocator.Find(OperatingSystem.IsWindows() ? $"{command}.exe" : command)
                   ?? ExecutableLocator.Find(command);
        if (path is not null)
        {
            AddKnownExecutable(installations, kind, displayName, path, "PATH");
        }
    }

    public static EnvironmentVariableSnapshot ReadEnvironmentVariable(string name)
    {
        string? Read(EnvironmentVariableTarget target)
        {
            try
            {
                return Environment.GetEnvironmentVariable(name, target);
            }
            catch (Exception exception) when (
                exception is PlatformNotSupportedException or System.Security.SecurityException)
            {
                return null;
            }
        }

        return new EnvironmentVariableSnapshot(
            name,
            Read(EnvironmentVariableTarget.Process),
            Read(EnvironmentVariableTarget.User),
            Read(EnvironmentVariableTarget.Machine));
    }

    public static string ReadFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.ProductVersion ?? info.FileVersion ?? "Disponible";
        }
        catch (FileNotFoundException)
        {
            return "Disponible";
        }
    }

    public static string? ReadProperty(string path, string name)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(path).Take(1_000))
            {
                var separator = line.IndexOf('=');
                if (separator > 0 &&
                    line[..separator].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return line[(separator + 1)..].Trim();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    public static string? LatestVersion(IReadOnlyList<string> versions) =>
        versions.OrderByDescending(
                value => value,
                Comparer<string>.Create(ToolchainVersion.Compare))
            .FirstOrDefault();

    public static List<string> GetDirectoriesSafe(string path)
    {
        try
        {
            return Directory.Exists(path) ? Directory.GetDirectories(path).ToList() : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    public static void AddExistingDirectory(HashSet<string> paths, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
            if (Directory.Exists(fullPath))
            {
                paths.Add(fullPath);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
    }

    public static string NormalizePath(string path)
    {
        try
        {
            var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return OperatingSystem.IsWindows() ? normalized.ToUpperInvariant() : normalized;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        }
    }
}
