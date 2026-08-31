using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CyEngineHub.Engines;

namespace CyEngineHub.Unity.Integration;

public sealed partial class UnityEngineModule :
    ILocalEngineProvider,
    ILocalEnginePackageProvider,
    ILocalEnginePackageManagementProvider
{
    private const int MaximumInstallations = 128;
    private const long MaximumProjectVersionBytes = 128 * 1024;

    public EngineModuleDescriptor Descriptor { get; } = new(
        EngineFamily.Unity,
        "unity",
        "Unity",
        "Unity Technologies",
        EngineModuleStatus.Active,
        ["ProjectSettings/ProjectVersion.txt", "Assets/"],
        EngineCapability.InstallationDiscovery |
        EngineCapability.ProjectDiscovery |
        EngineCapability.EditorLaunch |
        EngineCapability.ProjectLaunch |
        EngineCapability.VersionManagement |
        EngineCapability.PackageInventory |
        EngineCapability.PackageInstallation |
        EngineCapability.PackageRemoval,
        "Détection locale bornée des Editors/projets Unity, inventaire UPM, packages embarqués transactionnels et lancement exact.");

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
            IsUnityProject,
            cancellationToken: cancellationToken);
        var projects = scan.ProjectDirectories
            .Select(path => ReadProject(path, warnings))
            .Where(project => project is not null)
            .Cast<LocalEngineProject>()
            .ToArray();
        if (scan.IsPartial)
        {
            warnings.Add(
                $"Analyse Unity partielle après {scan.InspectedDirectoryCount:N0} dossier(s).");
        }

        return Task.FromResult(new LocalEngineDiscoverySnapshot(
            EngineFamily.Unity,
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
            [],
            installation.RootPath);
    }

    public LocalEngineLaunchRequest CreateProjectLaunch(
        LocalEngineProject project,
        IReadOnlyCollection<LocalEngineInstallation> availableInstallations)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(availableInstallations);
        if (project.Family != EngineFamily.Unity ||
            !string.Equals(project.ProviderId, Descriptor.Id, StringComparison.Ordinal) ||
            !IsUnityProject(project.RootPath))
        {
            throw new InvalidOperationException("Le projet Unity n’est plus valide.");
        }

        var candidates = availableInstallations
            .Where(installation =>
                installation.Family == EngineFamily.Unity &&
                string.Equals(installation.ProviderId, Descriptor.Id, StringComparison.Ordinal) &&
                File.Exists(installation.EditorPath))
            .ToArray();
        var editor = string.IsNullOrWhiteSpace(project.RequiredEditorVersion)
            ? candidates.OrderByDescending(item => VersionSortKey(item.Version)).FirstOrDefault()
            : candidates.FirstOrDefault(item => string.Equals(
                NormalizeVersion(item.Version),
                NormalizeVersion(project.RequiredEditorVersion),
                StringComparison.OrdinalIgnoreCase));
        if (editor is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(project.RequiredEditorVersion)
                    ? "Aucun Unity Editor local n’est disponible pour ce projet."
                    : $"Unity {project.RequiredEditorVersion} est requis mais n’est pas détecté localement.");
        }

        return new LocalEngineLaunchRequest(
            editor.EditorPath,
            ["-projectPath", project.RootPath],
            editor.RootPath);
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
            InspectCandidateRoot(root, "Unity Hub", candidates, warnings, cancellationToken);
        }

        var installations = new List<LocalEngineInstallation>();
        foreach (var (editorPath, source) in candidates.Take(MaximumInstallations))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var version = ReadEditorVersion(editorPath);
            var editorDirectory = Path.GetDirectoryName(editorPath)!;
            var root = string.Equals(Path.GetFileName(editorDirectory), "Editor", StringComparison.OrdinalIgnoreCase)
                ? Directory.GetParent(editorDirectory)?.FullName ?? editorDirectory
                : editorDirectory;
            installations.Add(new LocalEngineInstallation(
                EngineFamily.Unity,
                "unity",
                $"unity:{Path.GetFullPath(editorPath)}",
                string.IsNullOrWhiteSpace(version) ? "Unity Editor" : $"Unity {version}",
                version,
                root,
                Path.GetFullPath(editorPath),
                source));
        }

        if (candidates.Count > MaximumInstallations)
        {
            warnings.Add($"Installations Unity limitées aux {MaximumInstallations} premiers Editors.");
        }

        return installations
            .OrderByDescending(item => VersionSortKey(item.Version))
            .ThenBy(item => item.EditorPath, comparer)
            .ToArray();
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

            TryAddEditor(Path.Combine(path, EditorExecutableName()), source, editors);
            TryAddEditor(Path.Combine(path, "Editor", EditorExecutableName()), source, editors);
            TryAddEditor(Path.Combine(path, "Unity.app", "Contents", "MacOS", "Unity"), source, editors);

            var hubEditors = Path.Combine(path, "Hub", "Editor");
            if (IsSafeDirectory(hubEditors))
            {
                InspectVersionDirectories(hubEditors, source, editors, cancellationToken);
            }

            InspectVersionDirectories(path, source, editors, cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            warnings.Add($"Chemin Unity ignoré : {candidate}");
        }
    }

    private static void InspectVersionDirectories(
        string root,
        string source,
        IDictionary<string, string> editors,
        CancellationToken cancellationToken)
    {
        if (!IsSafeDirectory(root))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(root).Take(MaximumInstallations))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsSafeDirectory(directory))
            {
                continue;
            }

            TryAddEditor(Path.Combine(directory, "Editor", EditorExecutableName()), source, editors);
            TryAddEditor(
                Path.Combine(directory, "Unity.app", "Contents", "MacOS", "Unity"),
                source,
                editors);
        }
    }

    private static void TryAddEditor(
        string path,
        string source,
        IDictionary<string, string> editors)
    {
        if (!File.Exists(path) || IsReparsePoint(path))
        {
            return;
        }

        var name = Path.GetFileName(path);
        if (!string.Equals(name, "Unity.exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(name, "Unity", StringComparison.Ordinal))
        {
            return;
        }

        editors.TryAdd(Path.GetFullPath(path), source);
    }

    private static IEnumerable<string> GetDefaultEditorRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var folder in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     }.Where(folder => !string.IsNullOrWhiteSpace(folder)))
            {
                yield return Path.Combine(folder, "Unity", "Hub", "Editor");
                yield return Path.Combine(folder, "Unity");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/Applications/Unity/Hub/Editor";
        }
        else
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(profile))
            {
                yield return Path.Combine(profile, "Unity", "Hub", "Editor");
            }
        }
    }

    private static bool IsUnityProject(string directory) =>
        Directory.Exists(Path.Combine(directory, "Assets")) &&
        File.Exists(Path.Combine(directory, "ProjectSettings", "ProjectVersion.txt"));

    private static LocalEngineProject? ReadProject(
        string root,
        List<string> warnings)
    {
        var marker = Path.Combine(root, "ProjectSettings", "ProjectVersion.txt");
        try
        {
            var info = new FileInfo(marker);
            if (!info.Exists || info.Length > MaximumProjectVersionBytes || IsReparsePoint(marker))
            {
                warnings.Add($"Version Unity illisible ou trop volumineuse : {marker}");
                return null;
            }

            var version = File.ReadLines(marker)
                .Take(32)
                .Select(line => Regex.Match(
                    line,
                    "^\\s*m_EditorVersion\\s*:\\s*(?<version>[^\\s]+)\\s*$",
                    RegexOptions.CultureInvariant))
                .FirstOrDefault(match => match.Success)?
                .Groups["version"].Value ?? string.Empty;
            var name = new DirectoryInfo(root).Name;
            return new LocalEngineProject(
                EngineFamily.Unity,
                "unity",
                $"unity-project:{Path.GetFullPath(root)}",
                name,
                Path.GetFullPath(root),
                marker,
                version,
                string.IsNullOrWhiteSpace(version)
                    ? "Projet Unity · version non déclarée"
                    : $"Projet Unity · Editor {version}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Projet Unity ignoré : {root}");
            return null;
        }
    }

    private static void ValidateInstallation(LocalEngineInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (installation.Family != EngineFamily.Unity ||
            !string.Equals(installation.ProviderId, "unity", StringComparison.Ordinal) ||
            !File.Exists(installation.EditorPath) ||
            IsReparsePoint(installation.EditorPath))
        {
            throw new InvalidOperationException("L’installation Unity n’est plus valide.");
        }
    }

    private static string ReadEditorVersion(string editorPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(editorPath)!);
        for (var depth = 0; depth < 5 && directory is not null; depth++, directory = directory.Parent)
        {
            if (Regex.IsMatch(directory.Name, "^[0-9]+\\.[0-9]+", RegexOptions.CultureInvariant))
            {
                return NormalizeVersion(directory.Name);
            }
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(editorPath).ProductVersion
                          ?? FileVersionInfo.GetVersionInfo(editorPath).FileVersion;
            return NormalizeVersion(version ?? string.Empty);
        }
        catch (FileNotFoundException)
        {
            return string.Empty;
        }
    }

    private static string NormalizeVersion(string version) =>
        version.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;

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

    private static string EditorExecutableName() => OperatingSystem.IsWindows() ? "Unity.exe" : "Unity";

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
