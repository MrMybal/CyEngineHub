using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CyEngineHub.Engines;

namespace CyEngineHub.Godot.Integration;

public sealed partial class GodotEngineModule :
    ILocalEngineProvider,
    ILocalEnginePackageProvider,
    ILocalEnginePackageManagementProvider
{
    private const int MaximumInstallations = 128;
    private const long MaximumProjectFileBytes = 512 * 1024;

    public EngineModuleDescriptor Descriptor { get; } = new(
        EngineFamily.Godot,
        "godot",
        "Godot Engine",
        "Godot Foundation",
        EngineModuleStatus.Active,
        ["project.godot"],
        EngineCapability.InstallationDiscovery |
        EngineCapability.ProjectDiscovery |
        EngineCapability.EditorLaunch |
        EngineCapability.ProjectLaunch |
        EngineCapability.VersionManagement |
        EngineCapability.PackageInventory |
        EngineCapability.PackageInstallation |
        EngineCapability.PackageRemoval,
        "Détection bornée des éditeurs/projets Godot, inventaire et installation transactionnelle d’addons, puis lancement compatible.");

    public Task<LocalEngineDiscoverySnapshot> DiscoverAsync(
        LocalEngineDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var warnings = new List<string>();
        var installations = DiscoverInstallations(
            request.EngineRoots,
            warnings,
            cancellationToken);
        var scan = BoundedProjectDirectoryScanner.Find(
            request.ProjectRoots,
            directory => File.Exists(Path.Combine(directory, "project.godot")),
            cancellationToken: cancellationToken);
        var projects = scan.ProjectDirectories
            .Select(path => ReadProject(path, warnings))
            .Where(project => project is not null)
            .Cast<LocalEngineProject>()
            .ToArray();
        if (scan.IsPartial)
        {
            warnings.Add(
                $"Analyse Godot partielle après {scan.InspectedDirectoryCount:N0} dossier(s).");
        }

        return Task.FromResult(new LocalEngineDiscoverySnapshot(
            EngineFamily.Godot,
            installations,
            projects,
            scan.IsPartial,
            warnings));
    }

    public LocalEngineLaunchRequest CreateEditorLaunch(LocalEngineInstallation installation)
    {
        ValidateInstallation(installation);
        return new LocalEngineLaunchRequest(
            installation.EditorPath,
            ["--project-manager"],
            installation.RootPath);
    }

    public LocalEngineLaunchRequest CreateProjectLaunch(
        LocalEngineProject project,
        IReadOnlyCollection<LocalEngineInstallation> availableInstallations)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(availableInstallations);
        if (project.Family != EngineFamily.Godot ||
            !string.Equals(project.ProviderId, Descriptor.Id, StringComparison.Ordinal) ||
            !File.Exists(Path.Combine(project.RootPath, "project.godot")))
        {
            throw new InvalidOperationException("Le projet Godot n’est plus valide.");
        }

        var candidates = availableInstallations
            .Where(installation =>
                installation.Family == EngineFamily.Godot &&
                string.Equals(installation.ProviderId, Descriptor.Id, StringComparison.Ordinal) &&
                File.Exists(installation.EditorPath))
            .OrderByDescending(item => VersionSortKey(item.Version))
            .ToArray();
        var requiredCompatibility = CompatibilityVersion(project.RequiredEditorVersion);
        var editor = string.IsNullOrWhiteSpace(requiredCompatibility)
            ? candidates.FirstOrDefault()
            : candidates.FirstOrDefault(item => string.Equals(
                CompatibilityVersion(item.Version),
                requiredCompatibility,
                StringComparison.OrdinalIgnoreCase));
        if (editor is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(requiredCompatibility)
                    ? "Aucun éditeur Godot local n’est disponible pour ce projet."
                    : $"Godot {requiredCompatibility}.x est requis mais aucune version compatible n’est détectée.");
        }

        return new LocalEngineLaunchRequest(
            editor.EditorPath,
            ["--editor", "--path", project.RootPath],
            project.RootPath);
    }

    private static LocalEngineInstallation[] DiscoverInstallations(
        IEnumerable<string> configuredRoots,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var candidates = new Dictionary<string, string>(comparer);
        foreach (var root in configuredRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCandidateRoot(root, "Configuré", candidates, warnings, cancellationToken);
        }

        foreach (var root in GetDefaultEditorRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCandidateRoot(root, "Système", candidates, warnings, cancellationToken);
        }

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Take(256);
        foreach (var directory in pathDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectPathDirectory(directory, candidates);
        }

        var installations = candidates
            .Take(MaximumInstallations)
            .Select(pair => CreateInstallation(pair.Key, pair.Value))
            .OrderByDescending(item => VersionSortKey(item.Version))
            .ThenBy(item => item.EditorPath, comparer)
            .ToArray();
        if (candidates.Count > MaximumInstallations)
        {
            warnings.Add($"Installations Godot limitées aux {MaximumInstallations} premiers éditeurs.");
        }

        return installations;
    }

    private static void InspectCandidateRoot(
        string candidate,
        string source,
        IDictionary<string, string> editors,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.GetFullPath(candidate.Trim().Trim('"'));
            if (File.Exists(path))
            {
                TryAddEditor(path, source, editors);
                return;
            }

            if (!IsSafeDirectory(path))
            {
                return;
            }

            InspectEditorFiles(path, source, editors);
            foreach (var child in Directory.EnumerateDirectories(path).Take(MaximumInstallations))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsSafeDirectory(child))
                {
                    InspectEditorFiles(child, source, editors);
                    TryAddEditor(
                        Path.Combine(child, "Godot.app", "Contents", "MacOS", "Godot"),
                        source,
                        editors);
                }
            }

            TryAddEditor(
                Path.Combine(path, "Godot.app", "Contents", "MacOS", "Godot"),
                source,
                editors);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            warnings.Add($"Chemin Godot ignoré : {candidate}");
        }
    }

    private static void InspectEditorFiles(
        string directory,
        string source,
        IDictionary<string, string> editors)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory)
                         .Where(IsGodotEditorName)
                         .Take(MaximumInstallations))
            {
                TryAddEditor(file, source, editors);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // An inaccessible optional installation location is simply ignored.
        }
    }

    private static void InspectPathDirectory(
        string directory,
        IDictionary<string, string> editors)
    {
        try
        {
            var fullPath = Path.GetFullPath(directory.Trim().Trim('"'));
            if (!IsSafeDirectory(fullPath))
            {
                return;
            }

            foreach (var name in OperatingSystem.IsWindows()
                         ? new[] { "godot.exe", "godot4.exe", "godot-mono.exe" }
                         : new[] { "godot", "godot4", "godot-mono" })
            {
                TryAddEditor(Path.Combine(fullPath, name), "PATH", editors);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A malformed PATH entry is ignored.
        }
    }

    private static void TryAddEditor(
        string path,
        string source,
        IDictionary<string, string> editors)
    {
        if (!File.Exists(path) || !IsGodotEditorName(path) || IsReparsePoint(path))
        {
            return;
        }

        editors.TryAdd(Path.GetFullPath(path), source);
    }

    private static bool IsGodotEditorName(string path)
    {
        var name = Path.GetFileName(path);
        if (OperatingSystem.IsWindows())
        {
            return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                   name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase) &&
                   !name.Contains("console", StringComparison.OrdinalIgnoreCase);
        }

        return name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("godot", StringComparison.Ordinal);
    }

    private static IEnumerable<string> GetDefaultEditorRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(local))
            {
                yield return Path.Combine(local, "Programs", "Godot");
            }

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return Path.Combine(programFiles, "Godot");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Godot.app";
        }
        else
        {
            yield return "/usr/bin";
            yield return "/usr/local/bin";
        }
    }

    private static LocalEngineInstallation CreateInstallation(string editorPath, string source)
    {
        var version = ReadEditorVersion(editorPath);
        var dotNet = Path.GetFileName(editorPath).Contains("mono", StringComparison.OrdinalIgnoreCase);
        return new LocalEngineInstallation(
            EngineFamily.Godot,
            "godot",
            $"godot:{Path.GetFullPath(editorPath)}",
            string.IsNullOrWhiteSpace(version)
                ? dotNet ? "Godot .NET" : "Godot Engine"
                : $"Godot {version}{(dotNet ? " .NET" : string.Empty)}",
            version,
            Path.GetDirectoryName(editorPath)!,
            Path.GetFullPath(editorPath),
            source);
    }

    private static LocalEngineProject? ReadProject(string root, List<string> warnings)
    {
        var marker = Path.Combine(root, "project.godot");
        try
        {
            var info = new FileInfo(marker);
            if (!info.Exists || info.Length > MaximumProjectFileBytes || IsReparsePoint(marker))
            {
                warnings.Add($"Descripteur Godot illisible ou trop volumineux : {marker}");
                return null;
            }

            var name = string.Empty;
            var version = string.Empty;
            foreach (var line in File.ReadLines(marker).Take(4_096))
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    var nameMatch = Regex.Match(
                        line,
                        "^\\s*config/name\\s*=\\s*\"(?<name>(?:\\\\.|[^\"])*)\"",
                        RegexOptions.CultureInvariant);
                    if (nameMatch.Success)
                    {
                        name = nameMatch.Groups["name"].Value
                            .Replace("\\\"", "\"", StringComparison.Ordinal)
                            .Replace("\\\\", "\\", StringComparison.Ordinal);
                    }
                }

                if (string.IsNullOrWhiteSpace(version) &&
                    line.Contains("config/features", StringComparison.Ordinal))
                {
                    var versionMatch = Regex.Match(
                        line,
                        "\"(?<version>[0-9]+(?:\\.[0-9]+)?)\"",
                        RegexOptions.CultureInvariant);
                    if (versionMatch.Success)
                    {
                        version = versionMatch.Groups["version"].Value;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = new DirectoryInfo(root).Name;
            }

            var usesDotNet = Directory.EnumerateFiles(root, "*.csproj", SearchOption.TopDirectoryOnly).Any();
            return new LocalEngineProject(
                EngineFamily.Godot,
                "godot",
                $"godot-project:{Path.GetFullPath(root)}",
                name,
                Path.GetFullPath(root),
                marker,
                version,
                $"Projet Godot{(string.IsNullOrWhiteSpace(version) ? string.Empty : $" · {version}")}" +
                (usesDotNet ? " · .NET" : string.Empty));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Projet Godot ignoré : {root}");
            return null;
        }
    }

    private static void ValidateInstallation(LocalEngineInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (installation.Family != EngineFamily.Godot ||
            !string.Equals(installation.ProviderId, "godot", StringComparison.Ordinal) ||
            !File.Exists(installation.EditorPath) ||
            !IsGodotEditorName(installation.EditorPath) ||
            IsReparsePoint(installation.EditorPath))
        {
            throw new InvalidOperationException("L’installation Godot n’est plus valide.");
        }
    }

    private static string ReadEditorVersion(string editorPath)
    {
        var name = Path.GetFileNameWithoutExtension(editorPath);
        var match = Regex.Match(
            name,
            "Godot(?:_v)?(?<version>[0-9]+\\.[0-9]+(?:\\.[0-9]+)?(?:-[A-Za-z0-9.]+)?)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (match.Success)
        {
            return match.Groups["version"].Value;
        }

        try
        {
            return FileVersionInfo.GetVersionInfo(editorPath).ProductVersion?.Trim() ?? string.Empty;
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
    }

    private static string CompatibilityVersion(string version)
    {
        var match = Regex.Match(version, "^(?<major>[0-9]+)(?:\\.(?<minor>[0-9]+))?");
        return !match.Success
            ? string.Empty
            : match.Groups["minor"].Success
                ? $"{match.Groups["major"].Value}.{match.Groups["minor"].Value}"
                : match.Groups["major"].Value;
    }

    private static Version VersionSortKey(string value)
    {
        var match = Regex.Match(value, "^(?<major>[0-9]+)\\.(?<minor>[0-9]+)(?:\\.(?<patch>[0-9]+))?");
        return match.Success
            ? new Version(
                int.Parse(match.Groups["major"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["minor"].Value, CultureInfo.InvariantCulture),
                match.Groups["patch"].Success
                    ? int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture)
                    : 0)
            : new Version(0, 0, 0);
    }

    private static bool IsSafeDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) && !IsReparsePoint(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }
}
