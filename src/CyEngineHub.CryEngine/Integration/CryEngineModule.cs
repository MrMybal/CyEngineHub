using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyEngineHub.Engines;

namespace CyEngineHub.CryEngine.Integration;

public sealed partial class CryEngineModule : ILocalEngineProvider, ILocalEnginePackageProvider
{
    private const int MaximumInstallations = 128;
    private const int MaximumInstallationDirectories = 2_048;
    private const int MaximumInstallationDepth = 4;
    private const long MaximumDescriptorBytes = 1024 * 1024;
    private const long MaximumRegistryBytes = 4L * 1024 * 1024;

    private static readonly string[] EditorRelativePaths =
    [
        Path.Combine("bin", "win_x64", "Sandbox.exe"),
        Path.Combine("bin", "win_x64_release", "Sandbox.exe"),
        Path.Combine("Bin64", "Sandbox.exe"),
        Path.Combine("Bin32", "Sandbox.exe"),
    ];

    public EngineModuleDescriptor Descriptor { get; } = new(
        EngineFamily.CryEngine,
        "cryengine",
        "CRYENGINE",
        "Crytek",
        EngineModuleStatus.Active,
        [".cryproject"],
        EngineCapability.InstallationDiscovery |
        EngineCapability.ProjectDiscovery |
        EngineCapability.EditorLaunch |
        EngineCapability.ProjectLaunch |
        EngineCapability.VersionManagement |
        EngineCapability.PackageInventory,
        "Détection locale bornée des installations/projets CRYENGINE, inventaire des plugins déclarés et lancement Sandbox.");

    public Task<LocalEngineDiscoverySnapshot> DiscoverAsync(
        LocalEngineDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var warnings = new List<string>();
        var installations = DiscoverInstallations(request.EngineRoots, warnings, cancellationToken);
        var scan = BoundedProjectDirectoryScanner.Find(
            request.ProjectRoots,
            ContainsCryProject,
            cancellationToken: cancellationToken);
        var projects = scan.ProjectDirectories
            .Select(path => ReadProject(path, warnings))
            .Where(project => project is not null)
            .Cast<LocalEngineProject>()
            .ToArray();
        if (scan.IsPartial)
        {
            warnings.Add(
                $"Analyse CRYENGINE partielle après {scan.InspectedDirectoryCount:N0} dossier(s).");
        }

        return Task.FromResult(new LocalEngineDiscoverySnapshot(
            EngineFamily.CryEngine,
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
        if (project.Family != EngineFamily.CryEngine ||
            !string.Equals(project.ProviderId, Descriptor.Id, StringComparison.Ordinal) ||
            !File.Exists(project.MarkerPath) ||
            !IsCryProjectName(project.MarkerPath) ||
            IsReparsePoint(project.MarkerPath))
        {
            throw new InvalidOperationException("Le projet CRYENGINE n’est plus valide.");
        }

        var candidates = availableInstallations
            .Where(IsValidInstallation)
            .OrderByDescending(item => VersionSortKey(item.Version))
            .ThenBy(item => item.EditorPath, PathComparer())
            .ToArray();
        var required = project.RequiredEditorVersion.Trim();
        LocalEngineInstallation? installation;
        if (required == ".")
        {
            installation = candidates.FirstOrDefault(item =>
                IsSameOrChild(project.RootPath, item.RootPath) ||
                IsSameOrChild(item.RootPath, project.RootPath));
        }
        else if (string.IsNullOrWhiteSpace(required))
        {
            installation = candidates.FirstOrDefault();
        }
        else
        {
            installation = candidates.FirstOrDefault(item => MatchesRequiredEngine(item, required));
        }

        if (installation is null)
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(required)
                    ? "Aucun Sandbox CRYENGINE local n’est disponible pour ce projet."
                    : required == "."
                        ? "Ce projet requiert une installation CRYENGINE colocalisée qui n’a pas été détectée."
                        : $"Le moteur CRYENGINE requis '{required}' n’est pas installé localement.");
        }

        return new LocalEngineLaunchRequest(
            installation.EditorPath,
            ["-project", project.MarkerPath],
            project.RootPath);
    }

    private static LocalEngineInstallation[] DiscoverInstallations(
        IEnumerable<string> configuredRoots,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, EngineCandidate>(PathComparer());
        foreach (var root in configuredRoots.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCandidateTree(root, "Configuré", null, candidates, warnings, cancellationToken);
        }

        foreach (var root in DefaultLauncherRoots())
        {
            cancellationToken.ThrowIfCancellationRequested();
            InspectCandidateTree(root, "CryEngine Launcher", null, candidates, warnings, cancellationToken);
        }

        if (OperatingSystem.IsWindows())
        {
            InspectOfficialRegistry(candidates, warnings, cancellationToken);
        }

        if (candidates.Count > MaximumInstallations)
        {
            warnings.Add(
                $"Installations CRYENGINE limitées aux {MaximumInstallations} premiers éditeurs.");
        }

        return candidates.Values
            .Take(MaximumInstallations)
            .Select(CreateInstallation)
            .OrderByDescending(item => VersionSortKey(item.Version))
            .ThenBy(item => item.EditorPath, PathComparer())
            .ToArray();
    }

    private static void InspectOfficialRegistry(
        IDictionary<string, EngineCandidate> candidates,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (string.IsNullOrWhiteSpace(programData))
        {
            return;
        }

        var registryPath = Path.Combine(programData, "Crytek", "CRYENGINE", "cryengine.json");
        try
        {
            var info = new FileInfo(registryPath);
            if (!info.Exists || info.Length > MaximumRegistryBytes || IsReparsePoint(registryPath))
            {
                return;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(registryPath), JsonOptions());
            foreach (var registered in EnumerateRegisteredEngines(document.RootElement, null).Take(256))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var registeredPath = NormalizeRegisteredPath(
                    registered.Path,
                    Path.GetDirectoryName(registryPath)!);
                InspectCandidateTree(
                    registeredPath,
                    "Registre CryEngine",
                    registered.Identity,
                    candidates,
                    warnings,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            warnings.Add("Le registre local CRYENGINE est illisible et a été ignoré.");
        }
    }

    private static IEnumerable<RegisteredEngine> EnumerateRegisteredEngines(
        JsonElement element,
        string? inheritedIdentity)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var identity = ReadString(element, "id", "guid", "engine_id") ?? inheritedIdentity;
            var path = ReadString(element, "uri", "path", "root", "rootPath", "engine_path");
            if (!string.IsNullOrWhiteSpace(path))
            {
                yield return new RegisteredEngine(path, identity);
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(value) &&
                        (value.EndsWith(".cryengine", StringComparison.OrdinalIgnoreCase) ||
                         Directory.Exists(value)))
                    {
                        yield return new RegisteredEngine(value, identity ?? property.Name);
                    }
                }
                else if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                {
                    foreach (var nested in EnumerateRegisteredEngines(
                                 property.Value,
                                 identity ?? property.Name))
                    {
                        yield return nested;
                    }
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in EnumerateRegisteredEngines(item, inheritedIdentity))
                {
                    yield return nested;
                }
            }
        }
    }

    private static void InspectCandidateTree(
        string candidate,
        string source,
        string? identityHint,
        IDictionary<string, EngineCandidate> editors,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.GetFullPath(candidate.Trim().Trim('"'));
            if (File.Exists(path))
            {
                if (path.EndsWith(".cryengine", StringComparison.OrdinalIgnoreCase))
                {
                    var root = Path.GetDirectoryName(path)!;
                    InspectEngineRoot(root, source, identityHint, editors);
                }
                else
                {
                    TryAddEditor(path, source, identityHint, editors);
                }

                return;
            }

            if (!IsSafeDirectory(path))
            {
                return;
            }

            var pending = new Stack<(string Path, int Depth)>();
            var visited = new HashSet<string>(PathComparer());
            pending.Push((path, 0));
            var inspected = 0;
            while (pending.Count > 0 && inspected < MaximumInstallationDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, depth) = pending.Pop();
                if (!visited.Add(directory) || !IsSafeDirectory(directory))
                {
                    continue;
                }

                inspected++;
                InspectEngineRoot(directory, source, identityHint, editors);
                if (depth >= MaximumInstallationDepth || editors.Count >= MaximumInstallations * 2)
                {
                    continue;
                }

                foreach (var child in Directory.EnumerateDirectories(directory)
                             .Take(MaximumInstallationDirectories - inspected))
                {
                    if (IsSafeDirectory(child) && !ShouldSkipDirectory(child))
                    {
                        pending.Push((Path.GetFullPath(child), depth + 1));
                    }
                }
            }

            if (pending.Count > 0)
            {
                warnings.Add($"Analyse d’installation CRYENGINE partielle : {candidate}");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            warnings.Add($"Chemin CRYENGINE ignoré : {candidate}");
        }
    }

    private static void InspectEngineRoot(
        string root,
        string source,
        string? identityHint,
        IDictionary<string, EngineCandidate> editors)
    {
        var descriptor = FindEngineDescriptor(root);
        var descriptorInfo = descriptor is null
            ? EngineDescriptorInfo.Empty
            : ReadEngineDescriptor(descriptor);
        var identity = descriptorInfo.Identity ?? identityHint;
        foreach (var relativePath in EditorRelativePaths)
        {
            TryAddEditor(
                Path.Combine(root, relativePath),
                source,
                identity,
                editors,
                descriptorInfo);
        }
    }

    private static void TryAddEditor(
        string path,
        string source,
        string? identity,
        IDictionary<string, EngineCandidate> editors,
        EngineDescriptorInfo? descriptor = null)
    {
        if (!File.Exists(path) ||
            !string.Equals(Path.GetFileName(path), "Sandbox.exe", StringComparison.OrdinalIgnoreCase) ||
            IsReparsePoint(path))
        {
            return;
        }

        var editorPath = Path.GetFullPath(path);
        var root = ResolveEngineRoot(editorPath);
        var info = descriptor ?? ReadEngineDescriptor(FindEngineDescriptor(root));
        editors.TryAdd(editorPath, new EngineCandidate(
            editorPath,
            root,
            source,
            info.Identity ?? identity,
            info.Name,
            info.Version));
    }

    private static LocalEngineInstallation CreateInstallation(EngineCandidate candidate)
    {
        var version = !string.IsNullOrWhiteSpace(candidate.Version)
            ? candidate.Version
            : ReadVersionFromPath(candidate.RootPath, candidate.EditorPath);
        var name = !string.IsNullOrWhiteSpace(candidate.Name)
            ? candidate.Name
            : string.IsNullOrWhiteSpace(version) ? "CRYENGINE Sandbox" : $"CRYENGINE {version}";
        var identity = NormalizeIdentity(candidate.Identity);
        return new LocalEngineInstallation(
            EngineFamily.CryEngine,
            "cryengine",
            $"cryengine:{identity}@{candidate.EditorPath}",
            name,
            version,
            candidate.RootPath,
            candidate.EditorPath,
            candidate.Source);
    }

    private static LocalEngineProject? ReadProject(string root, List<string> warnings)
    {
        string? marker;
        try
        {
            marker = Directory.EnumerateFiles(root, "*.cryproject", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, PathComparer())
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Projet CRYENGINE ignoré : {root}");
            return null;
        }

        if (marker is null)
        {
            return null;
        }

        try
        {
            var info = new FileInfo(marker);
            if (info.Length > MaximumDescriptorBytes || IsReparsePoint(marker))
            {
                warnings.Add($"Descripteur CRYENGINE illisible ou trop volumineux : {marker}");
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(marker), JsonOptions());
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("La racine doit être un objet JSON.");
            }

            var projectInfo = TryGetObject(document.RootElement, "info");
            var requirement = TryGetObject(document.RootElement, "require");
            var name = projectInfo is { } value
                ? ReadString(value, "name")
                : null;
            name = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileNameWithoutExtension(marker)
                : name.Trim();
            var requiredEngine = requirement is { } required
                ? ReadString(required, "engine")?.Trim() ?? string.Empty
                : string.Empty;
            var assetDirectories = ReadAssetDirectories(document.RootElement);
            var description = "Projet CRYENGINE" +
                              (string.IsNullOrWhiteSpace(requiredEngine)
                                  ? " · moteur non déclaré"
                                  : $" · engine {requiredEngine}") +
                              (assetDirectories.Length == 0
                                  ? string.Empty
                                  : $" · assets {string.Join(", ", assetDirectories)}");
            return new LocalEngineProject(
                EngineFamily.CryEngine,
                "cryengine",
                $"cryengine-project:{Path.GetFullPath(marker)}",
                name,
                Path.GetFullPath(root),
                Path.GetFullPath(marker),
                requiredEngine,
                description);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            warnings.Add($"Descripteur CRYENGINE invalide ignoré : {marker}");
            return null;
        }
    }

    private static bool MatchesRequiredEngine(LocalEngineInstallation installation, string required)
    {
        var normalized = NormalizeIdentity(required);
        if (installation.Id.StartsWith(
                $"cryengine:{normalized}@",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var requiredVersion = ExtractVersion(required);
        return !string.IsNullOrWhiteSpace(requiredVersion) &&
               string.Equals(
                   CompatibilityVersion(installation.Version),
                   CompatibilityVersion(requiredVersion),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateInstallation(LocalEngineInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (!IsValidInstallation(installation))
        {
            throw new InvalidOperationException("L’installation CRYENGINE n’est plus valide.");
        }
    }

    private static bool IsValidInstallation(LocalEngineInstallation installation) =>
        installation.Family == EngineFamily.CryEngine &&
        string.Equals(installation.ProviderId, "cryengine", StringComparison.Ordinal) &&
        File.Exists(installation.EditorPath) &&
        string.Equals(Path.GetFileName(installation.EditorPath), "Sandbox.exe", StringComparison.OrdinalIgnoreCase) &&
        !IsReparsePoint(installation.EditorPath);

    private static EngineDescriptorInfo ReadEngineDescriptor(string? path)
    {
        if (path is null)
        {
            return EngineDescriptorInfo.Empty;
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > MaximumDescriptorBytes || IsReparsePoint(path))
            {
                return EngineDescriptorInfo.Empty;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path), JsonOptions());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return EngineDescriptorInfo.Empty;
            }

            var details = TryGetObject(root, "info");
            var identity = details is { } detail
                ? ReadString(detail, "id", "guid")
                : null;
            identity ??= ReadString(root, "id", "guid");
            var name = details is { } named
                ? ReadString(named, "name")
                : null;
            name ??= ReadString(root, "name");
            var version = details is { } versioned
                ? ReadString(versioned, "version")
                : null;
            version ??= ReadString(root, "engine_version", "engineVersion");
            return new EngineDescriptorInfo(identity, name, version);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return EngineDescriptorInfo.Empty;
        }
    }

    private static string[] ReadAssetDirectories(JsonElement root)
    {
        var content = TryGetObject(root, "content");
        if (content is null ||
            !TryGetProperty(content.Value, "assets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return assets.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Take(32)
            .ToArray();
    }

    private static string? FindEngineDescriptor(string root)
    {
        try
        {
            return Directory.EnumerateFiles(root, "*.cryengine", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, PathComparer())
                .FirstOrDefault();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ResolveEngineRoot(string editorPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(editorPath)!);
        for (var depth = 0; depth < 4 && directory.Parent is not null; depth++)
        {
            if (FindEngineDescriptor(directory.FullName) is not null ||
                Directory.Exists(Path.Combine(directory.FullName, "Engine")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        var editorDirectory = new DirectoryInfo(Path.GetDirectoryName(editorPath)!);
        return editorDirectory.Parent?.Parent?.FullName ?? editorDirectory.FullName;
    }

    private static string ReadVersionFromPath(string root, string editorPath)
    {
        foreach (var value in new[] { new DirectoryInfo(root).Name, editorPath })
        {
            var version = ExtractVersion(value);
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
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

    private static string ExtractVersion(string value)
    {
        var match = Regex.Match(
            value,
            "(?<![0-9])(?<version>[0-9]+\\.[0-9]+(?:\\.[0-9]+)?)(?![0-9])",
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["version"].Value : string.Empty;
    }

    private static string CompatibilityVersion(string version)
    {
        var parsed = ExtractVersion(version);
        var pieces = parsed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return pieces.Length >= 2 ? $"{pieces[0]}.{pieces[1]}" : parsed;
    }

    private static Version VersionSortKey(string value)
    {
        var parsed = ExtractVersion(value);
        return Version.TryParse(parsed, out var version)
            ? version
            : new Version(0, 0, 0);
    }

    private static bool ContainsCryProject(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.cryproject", SearchOption.TopDirectoryOnly).Any();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsCryProjectName(string path) =>
        string.Equals(Path.GetExtension(path), ".cryproject", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrChild(string candidate, string root)
    {
        var candidatePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(candidatePath, rootPath, PathComparison()) ||
               candidatePath.StartsWith(rootPath + Path.DirectorySeparatorChar, PathComparison());
    }

    private static bool ShouldSkipDirectory(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Assets", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Code", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Engine", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Editor", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("Solutions", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("USER", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> DefaultLauncherRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            yield return Path.Combine(local, "Programs", "CRYENGINE Launcher", "Engines");
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            yield return Path.Combine(programFiles, "Crytek", "CRYENGINE");
        }
    }

    private static string NormalizeRegisteredPath(string path, string registryDirectory)
    {
        var trimmed = path.Trim().Trim('"');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            return uri.LocalPath;
        }

        return Path.IsPathRooted(trimmed)
            ? trimmed
            : Path.Combine(registryDirectory, trimmed);
    }

    private static JsonDocumentOptions JsonOptions() => new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
        MaxDepth = 64,
    };

    private static JsonElement? TryGetObject(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : null;

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeIdentity(string? identity) =>
        (identity ?? string.Empty).Trim().Trim('{', '}').ToLowerInvariant();

    private static bool IsSafeDirectory(string path)
    {
        try
        {
            return Directory.Exists(path) && !IsReparsePoint(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static StringComparer PathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison() =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record EngineCandidate(
        string EditorPath,
        string RootPath,
        string Source,
        string? Identity,
        string? Name,
        string? Version);

    private sealed record RegisteredEngine(string Path, string? Identity);

    private sealed record EngineDescriptorInfo(string? Identity, string? Name, string? Version)
    {
        public static EngineDescriptorInfo Empty { get; } = new(null, null, null);
    }
}
