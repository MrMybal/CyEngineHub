using System.Text.Json;
using CyEngineHub.Engines;

namespace CyEngineHub.CryEngine.Integration;

public sealed partial class CryEngineModule
{
    private const int MaximumCryEnginePlugins = 2_048;

    public Task<LocalEnginePackageInventorySnapshot> DiscoverPackagesAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var packages = new List<LocalEnginePackage>();
        var warnings = new List<string>();
        var partial = false;

        foreach (var project in projects
                     .Where(IsOwnedCryEngineProject)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (packages.Count >= MaximumCryEnginePlugins)
            {
                partial = true;
                break;
            }

            var result = ReadCryEnginePlugins(
                project,
                MaximumCryEnginePlugins - packages.Count,
                cancellationToken);
            packages.AddRange(result.Packages);
            warnings.AddRange(result.Warnings);
            partial |= result.IsPartial;
        }

        if (partial && packages.Count >= MaximumCryEnginePlugins)
        {
            warnings.Add($"Inventaire CRYENGINE limité à {MaximumCryEnginePlugins:N0} plugins.");
        }

        return Task.FromResult(new LocalEnginePackageInventorySnapshot(
            EngineFamily.CryEngine,
            packages,
            partial,
            warnings));
    }

    private static CryEngineInventoryResult ReadCryEnginePlugins(
        LocalEngineProject project,
        int remainingCapacity,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        try
        {
            var info = new FileInfo(project.MarkerPath);
            if (!info.Exists || info.Length > MaximumDescriptorBytes || IsReparsePoint(project.MarkerPath))
            {
                warnings.Add($"{project.Name} · descripteur .cryproject absent, trop volumineux ou non sûr.");
                return new CryEngineInventoryResult([], warnings, true);
            }

            using var stream = new FileStream(
                project.MarkerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream, JsonOptions());
            var required = TryGetObject(document.RootElement, "require");
            if (required is null ||
                !TryGetProperty(required.Value, "plugins", out var pluginArray) ||
                pluginArray.ValueKind != JsonValueKind.Array)
            {
                return new CryEngineInventoryResult([], warnings, false);
            }

            var entries = pluginArray.EnumerateArray().Take(remainingCapacity + 1).ToArray();
            var partial = entries.Length > remainingCapacity;
            var packages = new List<LocalEnginePackage>(Math.Min(entries.Length, remainingCapacity));
            var index = 0;
            foreach (var entry in entries.Take(remainingCapacity))
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    warnings.Add($"{project.Name} · entrée plugin CRYENGINE #{index} ignorée : objet attendu.");
                    partial = true;
                    continue;
                }

                var declaredPath = ReadString(entry, "path")?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(declaredPath))
                {
                    warnings.Add($"{project.Name} · entrée plugin CRYENGINE #{index} sans chemin.");
                    partial = true;
                    continue;
                }

                var type = ReadString(entry, "type")?.Trim() ?? string.Empty;
                var version = ReadString(entry, "version")?.Trim() ?? string.Empty;
                var issues = new List<string>();
                var localRoot = ResolveCryEnginePluginRoot(project.RootPath, declaredPath, issues);
                var dependencies = ReadCryEngineDependencies(entry);
                var kind = FormatCryEnginePluginType(type);
                packages.Add(new LocalEnginePackage(
                    EngineFamily.CryEngine,
                    "cryengine",
                    $"{project.Id}:cryplugin:{index}:{declaredPath}",
                    project.Id,
                    project.Name,
                    Path.GetFileNameWithoutExtension(declaredPath.TrimEnd('/', '\\')),
                    string.IsNullOrWhiteSpace(version) ? "Non déclarée" : version,
                    $"CRYENGINE · {kind}",
                    "Déclaré dans .cryproject",
                    localRoot,
                    project.MarkerPath,
                    $"Plugin {kind.ToLowerInvariant()} chargé depuis « {declaredPath} ».",
                    issues.Count == 0 ? LocalEnginePackageHealth.Ready : LocalEnginePackageHealth.Warning,
                    dependencies,
                    issues));
            }

            return new CryEngineInventoryResult(packages, warnings, partial);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            warnings.Add($"{project.Name} · descripteur .cryproject illisible : {exception.Message}");
            return new CryEngineInventoryResult([], warnings, true);
        }
    }

    private static string ResolveCryEnginePluginRoot(
        string projectRoot,
        string declaredPath,
        List<string> issues)
    {
        try
        {
            if (Path.IsPathRooted(declaredPath))
            {
                issues.Add("Chemin plugin absolu : résolution externe volontairement non suivie.");
                return projectRoot;
            }

            var candidate = Path.GetFullPath(Path.Combine(
                projectRoot,
                declaredPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsSameOrChild(candidate, projectRoot))
            {
                issues.Add("Le chemin plugin sort du dossier projet.");
                return projectRoot;
            }

            if (Directory.Exists(candidate) && !IsReparsePoint(candidate))
            {
                return candidate;
            }

            if (File.Exists(candidate) && !IsReparsePoint(candidate))
            {
                return Path.GetDirectoryName(candidate) ?? projectRoot;
            }

            // CRYENGINE searches its binary directory after the project directory.
            // A missing project-local path can therefore be a valid engine plugin.
            return projectRoot;
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            issues.Add("Chemin plugin invalide.");
            return projectRoot;
        }
    }

    private static string[] ReadCryEngineDependencies(JsonElement entry)
    {
        if (!TryGetProperty(entry, "dependencies", out var dependencies) ||
            dependencies.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return dependencies.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Cast<string>()
            .Take(256)
            .ToArray();
    }

    private static string FormatCryEnginePluginType(string type) => type switch
    {
        var value when value.Contains("Native", StringComparison.OrdinalIgnoreCase) => "natif C++",
        var value when value.Contains("Managed", StringComparison.OrdinalIgnoreCase) => "managé C#",
        var value when value.Contains("CSharp", StringComparison.OrdinalIgnoreCase) => "managé C#",
        "" => "type non déclaré",
        _ => type,
    };

    private static bool IsOwnedCryEngineProject(LocalEngineProject project) =>
        project.Family == EngineFamily.CryEngine &&
        string.Equals(project.ProviderId, "cryengine", StringComparison.Ordinal) &&
        File.Exists(project.MarkerPath) &&
        IsCryProjectName(project.MarkerPath);

    private sealed record CryEngineInventoryResult(
        IReadOnlyList<LocalEnginePackage> Packages,
        IReadOnlyList<string> Warnings,
        bool IsPartial);
}
