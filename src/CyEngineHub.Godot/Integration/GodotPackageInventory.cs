using CyEngineHub.Engines;

namespace CyEngineHub.Godot.Integration;

public sealed partial class GodotEngineModule
{
    private const int MaximumGodotAddons = 1_024;
    private const int MaximumNativeDescriptorDirectories = 256;
    private const int MaximumNativeDescriptorDepth = 4;
    private const long MaximumPluginConfigurationBytes = 256 * 1024;

    public Task<LocalEnginePackageInventorySnapshot> DiscoverPackagesAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(projects);
        var packages = new List<LocalEnginePackage>();
        var warnings = new List<string>();
        var partial = false;

        foreach (var project in projects
                     .Where(IsOwnedGodotProject)
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (packages.Count >= MaximumGodotAddons)
            {
                partial = true;
                break;
            }

            var result = ReadGodotAddons(
                project,
                MaximumGodotAddons - packages.Count,
                cancellationToken);
            packages.AddRange(result.Packages);
            warnings.AddRange(result.Warnings);
            partial |= result.IsPartial;
        }

        if (partial && packages.Count >= MaximumGodotAddons)
        {
            warnings.Add($"Inventaire Godot limité à {MaximumGodotAddons:N0} addons.");
        }

        return Task.FromResult(new LocalEnginePackageInventorySnapshot(
            EngineFamily.Godot,
            packages,
            partial,
            warnings));
    }

    private static GodotInventoryResult ReadGodotAddons(
        LocalEngineProject project,
        int remainingCapacity,
        CancellationToken cancellationToken)
    {
        var addonsRoot = Path.Combine(project.RootPath, "addons");
        if (!Directory.Exists(addonsRoot) || IsReparsePoint(addonsRoot))
        {
            return new GodotInventoryResult([], [], false);
        }

        var warnings = new List<string>();
        var packages = new List<LocalEnginePackage>();
        var projectConfiguration = ReadGodotText(
            Path.Combine(project.RootPath, "project.godot"),
            MaximumProjectFileBytes,
            out var projectError);
        if (projectError is not null)
        {
            warnings.Add($"{project.Name} · état d’activation des plugins indisponible : {projectError}");
        }

        string[] addonDirectories;
        try
        {
            addonDirectories = Directory
                .EnumerateDirectories(addonsRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(path => !IsReparsePoint(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Take(remainingCapacity + 1)
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"{project.Name} · dossier addons illisible : {exception.Message}");
            return new GodotInventoryResult([], warnings, true);
        }

        var partial = addonDirectories.Length > remainingCapacity;
        foreach (var addonRoot in addonDirectories.Take(remainingCapacity))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pluginConfigurationPath = Path.Combine(addonRoot, "plugin.cfg");
            var nativeSearch = FindGodotNativeDescriptors(addonRoot, cancellationToken);
            partial |= nativeSearch.IsPartial;
            var hasPluginConfiguration = File.Exists(pluginConfigurationPath);
            if (!hasPluginConfiguration && nativeSearch.Paths.Count == 0)
            {
                continue;
            }

            var folderName = Path.GetFileName(addonRoot);
            var metadata = hasPluginConfiguration
                ? ReadGodotPluginMetadata(pluginConfigurationPath)
                : GodotPluginMetadata.Empty;
            var issues = new List<string>();
            if (metadata.Error is not null)
            {
                issues.Add(metadata.Error);
            }

            if (hasPluginConfiguration && string.IsNullOrWhiteSpace(metadata.Script))
            {
                issues.Add("Le champ script est absent du plugin.cfg.");
            }
            else if (hasPluginConfiguration &&
                     !TryResolveGodotScript(project.RootPath, addonRoot, metadata.Script, out var scriptPath))
            {
                issues.Add("Le script déclaré sort du projet ou n’existe plus.");
            }

            var enabledMarker = $"res://addons/{folderName}/plugin.cfg";
            var state = hasPluginConfiguration
                ? projectConfiguration is null
                    ? "Plugin éditeur · état inconnu"
                    : projectConfiguration.Contains(enabledMarker, StringComparison.OrdinalIgnoreCase)
                        ? "Plugin éditeur · activé"
                        : "Plugin éditeur · désactivé"
                : "Extension native détectée";
            var source = hasPluginConfiguration && nativeSearch.Paths.Count > 0
                ? "Godot · addon + GDExtension"
                : hasPluginConfiguration
                    ? "Godot · addon éditeur"
                    : "Godot · GDExtension";
            var manifestPath = hasPluginConfiguration
                ? pluginConfigurationPath
                : nativeSearch.Paths[0];
            packages.Add(new LocalEnginePackage(
                EngineFamily.Godot,
                "godot",
                $"{project.Id}:addon:{folderName}",
                project.Id,
                project.Name,
                FirstGodotValue(metadata.Name, folderName),
                FirstGodotValue(metadata.Version, "Non déclarée"),
                source,
                state,
                addonRoot,
                manifestPath,
                metadata.Description,
                issues.Count == 0 ? LocalEnginePackageHealth.Ready : LocalEnginePackageHealth.Warning,
                [],
                issues));
        }

        return new GodotInventoryResult(packages, warnings, partial);
    }

    private static GodotPluginMetadata ReadGodotPluginMetadata(string path)
    {
        var content = ReadGodotText(path, MaximumPluginConfigurationBytes, out var error);
        if (content is null)
        {
            return new GodotPluginMetadata(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                error ?? "plugin.cfg illisible");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inPluginSection = false;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inPluginSection = string.Equals(
                    line[1..^1].Trim(),
                    "plugin",
                    StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inPluginSection)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1]
                    .Replace("\\\"", "\"")
                    .Replace("\\n", "\n");
            }

            values[key] = value;
        }

        values.TryGetValue("name", out var name);
        values.TryGetValue("version", out var version);
        values.TryGetValue("description", out var description);
        values.TryGetValue("script", out var script);
        var metadataError = values.Count == 0
            ? "La section [plugin] du plugin.cfg est absente ou vide."
            : null;
        return new GodotPluginMetadata(
            name ?? string.Empty,
            version ?? string.Empty,
            description ?? string.Empty,
            script ?? string.Empty,
            metadataError);
    }

    private static string? ReadGodotText(string path, long maximumBytes, out string? error)
    {
        error = null;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                error = $"fichier absent ({path})";
                return null;
            }

            if (info.Length > maximumBytes)
            {
                error = $"fichier trop volumineux ({info.Length:N0} octets)";
                return null;
            }

            if (IsReparsePoint(path))
            {
                error = "lien/reparse point refusé";
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = exception.Message;
            return null;
        }
    }

    private static GodotNativeDescriptorSearch FindGodotNativeDescriptors(
        string root,
        CancellationToken cancellationToken)
    {
        var paths = new List<string>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var inspected = 0;
        var partial = false;
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            inspected++;
            if (inspected > MaximumNativeDescriptorDirectories)
            {
                partial = true;
                break;
            }

            try
            {
                paths.AddRange(Directory
                    .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                    .Where(path =>
                        path.EndsWith(".gdextension", StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".gdnlib", StringComparison.OrdinalIgnoreCase))
                    .Where(path => !IsReparsePoint(path))
                    .Take(32 - paths.Count));
                if (paths.Count >= 32)
                {
                    partial = true;
                    break;
                }

                if (depth >= MaximumNativeDescriptorDepth)
                {
                    continue;
                }

                foreach (var child in Directory.EnumerateDirectories(
                             directory,
                             "*",
                             SearchOption.TopDirectoryOnly))
                {
                    if (!IsReparsePoint(child))
                    {
                        pending.Push((child, depth + 1));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                partial = true;
            }
        }

        return new GodotNativeDescriptorSearch(paths, partial);
    }

    private static bool TryResolveGodotScript(
        string projectRoot,
        string addonRoot,
        string script,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        try
        {
            var candidate = script.StartsWith("res://", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(projectRoot, script[6..].Replace('/', Path.DirectorySeparatorChar))
                : Path.Combine(addonRoot, script.Replace('/', Path.DirectorySeparatorChar));
            resolvedPath = Path.GetFullPath(candidate);
            var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectRoot));
            var relative = Path.GetRelativePath(normalizedRoot, resolvedPath);
            return !Path.IsPathRooted(relative) &&
                   !relative.Equals("..", StringComparison.Ordinal) &&
                   !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
                   File.Exists(resolvedPath) &&
                   !IsReparsePoint(resolvedPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string FirstGodotValue(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static bool IsOwnedGodotProject(LocalEngineProject project) =>
        project.Family == EngineFamily.Godot &&
        string.Equals(project.ProviderId, "godot", StringComparison.Ordinal) &&
        File.Exists(Path.Combine(project.RootPath, "project.godot"));

    private sealed record GodotPluginMetadata(
        string Name,
        string Version,
        string Description,
        string Script,
        string? Error)
    {
        public static GodotPluginMetadata Empty { get; } = new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            null);
    }

    private sealed record GodotNativeDescriptorSearch(
        IReadOnlyList<string> Paths,
        bool IsPartial);

    private sealed record GodotInventoryResult(
        IReadOnlyList<LocalEnginePackage> Packages,
        IReadOnlyList<string> Warnings,
        bool IsPartial);
}
