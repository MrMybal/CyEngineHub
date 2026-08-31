using System.Text.Json;
using System.Text.RegularExpressions;
using CyEngineHub.Engines;

namespace CyEngineHub.Unity.Integration;

public sealed partial class UnityEngineModule
{
    private const int MaximumInventoryPackages = 4_096;
    private const long MaximumUnityManifestBytes = 2L * 1024 * 1024;
    private const long MaximumUnityLockBytes = 8L * 1024 * 1024;

    private static readonly Regex UnityPackageNamePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,199}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public Task<LocalEnginePackageInventorySnapshot> DiscoverPackagesAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var packages = new List<LocalEnginePackage>();
        var warnings = new List<string>();
        var partial = false;

        foreach (var project in projects
                     .Where(IsOwnedUnityProject)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (packages.Count >= MaximumInventoryPackages)
            {
                partial = true;
                break;
            }

            var result = ReadUnityPackages(project, MaximumInventoryPackages - packages.Count);
            packages.AddRange(result.Packages);
            warnings.AddRange(result.Warnings);
            partial |= result.IsPartial;
        }

        if (partial && packages.Count >= MaximumInventoryPackages)
        {
            warnings.Add($"Inventaire Unity limité à {MaximumInventoryPackages:N0} packages.");
        }

        return Task.FromResult(new LocalEnginePackageInventorySnapshot(
            EngineFamily.Unity,
            packages,
            partial,
            warnings));
    }

    private static UnityInventoryResult ReadUnityPackages(
        LocalEngineProject project,
        int remainingCapacity)
    {
        var warnings = new List<string>();
        var manifestPath = Path.Combine(project.RootPath, "Packages", "manifest.json");
        if (!TryOpenJson(
                manifestPath,
                MaximumUnityManifestBytes,
                out var manifest,
                out var manifestError))
        {
            warnings.Add($"{project.Name} · manifest UPM ignoré : {manifestError}");
            return new UnityInventoryResult([], warnings, true);
        }

        using (manifest)
        {
            if (!TryReadStringMap(manifest.RootElement, "dependencies", out var direct))
            {
                warnings.Add($"{project.Name} · le manifest UPM ne contient pas d’objet dependencies valide.");
                return new UnityInventoryResult([], warnings, true);
            }

            var lockPath = Path.Combine(project.RootPath, "Packages", "packages-lock.json");
            var lockEntries = new Dictionary<string, UnityLockEntry>(StringComparer.OrdinalIgnoreCase);
            var hasLockFile = File.Exists(lockPath);
            if (hasLockFile)
            {
                if (TryOpenJson(lockPath, MaximumUnityLockBytes, out var lockDocument, out var lockError))
                {
                    using (lockDocument)
                    {
                        ReadUnityLockEntries(lockDocument.RootElement, lockEntries);
                    }
                }
                else
                {
                    warnings.Add($"{project.Name} · verrou UPM ignoré : {lockError}");
                }
            }

            var embeddedNames = ReadUnityEmbeddedPackageNames(project, warnings);
            var names = direct.Keys
                .Concat(lockEntries.Keys)
                .Concat(embeddedNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Take(remainingCapacity + 1)
                .ToArray();
            var partial = names.Length > remainingCapacity;
            var packages = new List<LocalEnginePackage>(Math.Min(names.Length, remainingCapacity));
            foreach (var name in names.Take(remainingCapacity))
            {
                if (!UnityPackageNamePattern.IsMatch(name))
                {
                    warnings.Add($"{project.Name} · identifiant UPM non sûr ignoré : {name}");
                    partial = true;
                    continue;
                }

                direct.TryGetValue(name, out var requestedVersion);
                lockEntries.TryGetValue(name, out var locked);
                var embeddedRoot = Path.Combine(project.RootPath, "Packages", name);
                var hasEmbeddedRoot = Directory.Exists(embeddedRoot) && !IsReparsePoint(embeddedRoot);
                var packageManifest = hasEmbeddedRoot
                    ? Path.Combine(embeddedRoot, "package.json")
                    : manifestPath;
                var metadata = hasEmbeddedRoot
                    ? ReadUnityPackageMetadata(packageManifest)
                    : UnityPackageMetadata.Empty;
                var issues = new List<string>();
                if (hasLockFile && direct.ContainsKey(name) && locked is null)
                {
                    issues.Add("Dépendance directe absente du verrou UPM résolu.");
                }

                if (metadata.Error is not null)
                {
                    issues.Add(metadata.Error);
                }

                var sourceCode = locked?.Source ?? InferUnitySource(requestedVersion, hasEmbeddedRoot);
                var source = FormatUnitySource(sourceCode);
                var version = FirstNotEmpty(locked?.Version, metadata.Version, requestedVersion);
                var isDirect = direct.ContainsKey(name) || locked?.Depth == 0;
                var state = hasEmbeddedRoot && !isDirect && locked is null
                    ? "Package embarqué"
                    : isDirect
                        ? "Dépendance directe"
                        : "Dépendance transitive";
                packages.Add(new LocalEnginePackage(
                    EngineFamily.Unity,
                    "unity",
                    $"{project.Id}:upm:{name}",
                    project.Id,
                    project.Name,
                    FirstNotEmpty(metadata.DisplayName, name),
                    version,
                    source,
                    state,
                    hasEmbeddedRoot ? embeddedRoot : project.RootPath,
                    packageManifest,
                    metadata.Description,
                    issues.Count == 0 ? LocalEnginePackageHealth.Ready : LocalEnginePackageHealth.Warning,
                    locked?.Dependencies ?? [],
                    issues));
            }

            return new UnityInventoryResult(packages, warnings, partial);
        }
    }

    private static void ReadUnityLockEntries(
        JsonElement root,
        IDictionary<string, UnityLockEntry> entries)
    {
        if (!root.TryGetProperty("dependencies", out var dependencies) ||
            dependencies.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in dependencies.EnumerateObject().Take(MaximumInventoryPackages + 1))
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var item = property.Value;
            var version = ReadJsonString(item, "version");
            var source = ReadJsonString(item, "source");
            var depth = item.TryGetProperty("depth", out var depthElement) &&
                        depthElement.TryGetInt32(out var parsedDepth)
                ? parsedDepth
                : -1;
            var dependencyNames = TryReadStringMap(item, "dependencies", out var nested)
                ? nested.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            entries[property.Name] = new UnityLockEntry(version, source, depth, dependencyNames);
        }
    }

    private static string[] ReadUnityEmbeddedPackageNames(
        LocalEngineProject project,
        List<string> warnings)
    {
        var packagesRoot = Path.Combine(project.RootPath, "Packages");
        try
        {
            if (!Directory.Exists(packagesRoot) || IsReparsePoint(packagesRoot))
            {
                return [];
            }

            return Directory
                .EnumerateDirectories(packagesRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !IsReparsePoint(path) && File.Exists(Path.Combine(path, "package.json")))
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name) && UnityPackageNamePattern.IsMatch(name))
                .Cast<string>()
                .Take(MaximumInventoryPackages + 1)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{project.Name} · packages embarqués illisibles : {exception.Message}");
            return [];
        }
    }

    private static UnityPackageMetadata ReadUnityPackageMetadata(string path)
    {
        if (!File.Exists(path))
        {
            return UnityPackageMetadata.Empty;
        }

        if (!TryOpenJson(path, MaximumUnityManifestBytes, out var document, out var error))
        {
            return new UnityPackageMetadata(string.Empty, string.Empty, string.Empty, error);
        }

        using (document)
        {
            return new UnityPackageMetadata(
                ReadJsonString(document.RootElement, "displayName"),
                ReadJsonString(document.RootElement, "version"),
                ReadJsonString(document.RootElement, "description"),
                null);
        }
    }

    private static bool TryOpenJson(
        string path,
        long maximumBytes,
        out JsonDocument document,
        out string error)
    {
        document = null!;
        error = string.Empty;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                error = $"fichier absent ({path})";
                return false;
            }

            if (info.Length > maximumBytes)
            {
                error = $"fichier trop volumineux ({info.Length:N0} octets)";
                return false;
            }

            if (IsReparsePoint(path))
            {
                error = "lien/reparse point refusé";
                return false;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 64,
            });
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool TryReadStringMap(
        JsonElement parent,
        string propertyName,
        out Dictionary<string, string> values)
    {
        values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!parent.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject().Take(MaximumInventoryPackages + 1))
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                values[property.Name] = property.Value.GetString() ?? string.Empty;
            }
        }

        return true;
    }

    private static string ReadJsonString(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static string InferUnitySource(string? reference, bool embedded) =>
        embedded ? "embedded" : reference switch
        {
            not null when reference.StartsWith("file:", StringComparison.OrdinalIgnoreCase) => "local",
            not null when reference.Contains("git", StringComparison.OrdinalIgnoreCase) ||
                          reference.StartsWith("ssh:", StringComparison.OrdinalIgnoreCase) => "git",
            _ => "registry",
        };

    private static string FormatUnitySource(string source) => source.ToLowerInvariant() switch
    {
        "embedded" => "UPM · embarqué",
        "local" => "UPM · dossier local",
        "git" => "UPM · Git",
        "builtin" => "UPM · intégré",
        "registry" => "UPM · registre",
        _ => $"UPM · {source}",
    };

    private static string FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Non déclarée";

    private static bool IsOwnedUnityProject(LocalEngineProject project) =>
        project.Family == EngineFamily.Unity &&
        string.Equals(project.ProviderId, "unity", StringComparison.Ordinal) &&
        Directory.Exists(project.RootPath);

    private sealed record UnityLockEntry(
        string Version,
        string Source,
        int Depth,
        IReadOnlyList<string> Dependencies);

    private sealed record UnityPackageMetadata(
        string DisplayName,
        string Version,
        string Description,
        string? Error)
    {
        public static UnityPackageMetadata Empty { get; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            null);
    }

    private sealed record UnityInventoryResult(
        IReadOnlyList<LocalEnginePackage> Packages,
        IReadOnlyList<string> Warnings,
        bool IsPartial);
}
