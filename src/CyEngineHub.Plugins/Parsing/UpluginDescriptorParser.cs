using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyEngineHub.Plugins.Models;

namespace CyEngineHub.Plugins.Parsing;

public interface IUpluginDescriptorParser
{
    Task<UnrealPluginDescriptor> ParseAsync(
        PluginCandidate candidate,
        CancellationToken cancellationToken = default);
}

public sealed class UpluginDescriptorParser : IUpluginDescriptorParser
{
    public async Task<UnrealPluginDescriptor> ParseAsync(
        PluginCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var descriptorPath = Path.GetFullPath(candidate.DescriptorPath);
        if (!File.Exists(descriptorPath))
        {
            throw new PluginOperationException(
                "Le descripteur .uplugin est introuvable.",
                "Parse",
                descriptorPath);
        }

        try
        {
            await using var stream = new FileStream(
                descriptorPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(
                    stream,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                        MaxDepth = 64,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            var root = document.RootElement;
            var pluginRoot = Path.GetDirectoryName(descriptorPath)!;
            var name = Path.GetFileNameWithoutExtension(descriptorPath);
            var issues = new List<string>();
            var fileVersion = GetInt(root, "FileVersion");
            if (fileVersion <= 0)
            {
                issues.Add("FileVersion est absent ou invalide.");
            }

            var modules = ParseModules(root, issues);
            var canContainContent = GetBool(root, "CanContainContent");
            if (modules.Count == 0 && !canContainContent)
            {
                issues.Add("Le plugin ne déclare ni module ni contenu.");
            }

            var health = issues.Count == 0 ? PluginHealth.Healthy : PluginHealth.Warning;
            return new UnrealPluginDescriptor(
                CreateStableId(descriptorPath),
                name,
                GetString(root, "FriendlyName") ?? name,
                GetString(root, "Description") ?? string.Empty,
                GetString(root, "Category") ?? "Other",
                descriptorPath,
                pluginRoot,
                candidate.SourceKind,
                candidate.SourceLabel,
                candidate.SourceOwnerId,
                fileVersion,
                GetInt(root, "Version"),
                GetString(root, "VersionName") ?? string.Empty,
                GetString(root, "EngineVersion"),
                canContainContent,
                GetBool(root, "Installed"),
                Directory.Exists(Path.Combine(pluginRoot, "Source")),
                modules,
                GetStringArray(root, "SupportedTargetPlatforms"),
                ParseDependencies(root),
                health,
                issues,
                DateTimeOffset.UtcNow,
                root.TryGetProperty("EnabledByDefault", out var enabledByDefault) &&
                enabledByDefault.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? enabledByDefault.GetBoolean() : null);
        }
        catch (JsonException exception)
        {
            throw new PluginOperationException(
                $"Le JSON du descripteur est invalide : {exception.Message}",
                "Parse",
                descriptorPath,
                innerException: exception);
        }
    }

    private static List<UnrealPluginModule> ParseModules(
        JsonElement root,
        List<string> issues)
    {
        var modules = new List<UnrealPluginModule>();
        if (!root.TryGetProperty("Modules", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return modules;
        }

        foreach (var item in array.EnumerateArray())
        {
            var name = GetString(item, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                issues.Add("Un module ne possède pas de nom.");
                continue;
            }

            var allowList = GetStringArray(item, "PlatformAllowList");
            if (allowList.Count == 0)
            {
                allowList = GetStringArray(item, "WhitelistPlatforms");
            }

            var denyList = GetStringArray(item, "PlatformDenyList");
            if (denyList.Count == 0)
            {
                denyList = GetStringArray(item, "BlacklistPlatforms");
            }

            modules.Add(new UnrealPluginModule(
                name,
                GetString(item, "Type") ?? "Runtime",
                GetString(item, "LoadingPhase") ?? "Default",
                allowList,
                denyList));
        }

        if (modules.Select(module => module.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() !=
            modules.Count)
        {
            issues.Add("Plusieurs modules portent le même nom.");
        }

        return modules;
    }

    private static List<UnrealPluginDependency> ParseDependencies(JsonElement root)
    {
        var dependencies = new List<UnrealPluginDependency>();
        if (!root.TryGetProperty("Plugins", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return dependencies;
        }

        foreach (var item in array.EnumerateArray())
        {
            var name = GetString(item, "Name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                dependencies.Add(new UnrealPluginDependency(
                    name,
                    !item.TryGetProperty("Enabled", out var enabled) || enabled.ValueKind != JsonValueKind.False,
                    GetBool(item, "Optional")));
            }
        }

        return dependencies;
    }

    private static List<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var result)
            ? result
            : 0;

    private static bool GetBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.True;

    internal static string CreateStableId(string descriptorPath)
    {
        var normalized = OperatingSystem.IsWindows()
            ? descriptorPath.ToUpperInvariant()
            : descriptorPath;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..16];
    }
}
