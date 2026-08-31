using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyEngineHub.Projects.Models;

namespace CyEngineHub.Projects.Parsing;

public sealed class UprojectDescriptorParser
{
    private readonly JsonDocumentOptions _documentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public async Task<UnrealProject> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var normalizedPath = Path.GetFullPath(filePath);
        if (!File.Exists(normalizedPath))
        {
            throw new ProjectDescriptorException("The .uproject file does not exist.", normalizedPath);
        }

        try
        {
            await using var stream = new FileStream(
                normalizedPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                stream,
                _documentOptions,
                cancellationToken).ConfigureAwait(false);

            var root = document.RootElement;
            var modules = ReadNames(root, "Modules");
            var plugins = ReadPlugins(root);
            var association = GetString(root, "EngineAssociation");
            var issues = new List<string>();
            if (string.IsNullOrWhiteSpace(association))
            {
                issues.Add("No EngineAssociation is declared.");
            }

            var projectRoot = Path.GetDirectoryName(normalizedPath)
                ?? throw new ProjectDescriptorException("The project has no parent directory.", normalizedPath);
            return new UnrealProject(
                CreateStableId(normalizedPath),
                Path.GetFileNameWithoutExtension(normalizedPath),
                normalizedPath,
                projectRoot,
                association,
                GetString(root, "Description"),
                GetString(root, "Category"),
                modules,
                plugins,
                issues.Count == 0 ? ProjectHealth.Healthy : ProjectHealth.Warning,
                issues,
                DateTimeOffset.UtcNow);
        }
        catch (JsonException exception)
        {
            throw new ProjectDescriptorException(
                "The .uproject descriptor contains invalid JSON.",
                normalizedPath,
                exception);
        }
        catch (IOException exception)
        {
            throw new ProjectDescriptorException(
                "The .uproject descriptor could not be read.",
                normalizedPath,
                exception);
        }
    }

    private static string[] ReadNames(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return items.EnumerateArray()
            .Select(item => GetString(item, "Name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();
    }

    private static List<ProjectPluginReference> ReadPlugins(JsonElement root)
    {
        if (!root.TryGetProperty("Plugins", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var plugins = new List<ProjectPluginReference>();
        foreach (var item in items.EnumerateArray())
        {
            var name = GetString(item, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var enabled = !item.TryGetProperty("Enabled", out var enabledValue) ||
                          enabledValue.ValueKind != JsonValueKind.False;
            plugins.Add(new ProjectPluginReference(name, enabled));
        }

        return plugins;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string CreateStableId(string path)
    {
        var normalized = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..16];
    }
}

public sealed class ProjectDescriptorException : Exception
{
    public ProjectDescriptorException(string message, string projectFile, Exception? innerException = null)
        : base(message, innerException)
    {
        ProjectFile = projectFile;
    }

    public string ProjectFile { get; }
}
