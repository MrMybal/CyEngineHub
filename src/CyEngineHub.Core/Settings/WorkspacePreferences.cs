using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyEngineHub.Core.Settings;

public enum WorkspaceItemKind
{
    Engine,
    Project,
    Plugin,
    Package,
    Asset,
}

public sealed record WorkspaceItemPreference(
    WorkspaceItemKind Kind,
    string Key,
    string Alias,
    string FolderPath,
    bool IsHidden,
    bool IsExplicit);

public sealed record WorkspacePreferences(
    int SchemaVersion,
    IReadOnlyDictionary<string, bool> EnabledProviders,
    IReadOnlyList<WorkspaceItemPreference> Items,
    bool AutoDiscoverProjects,
    bool ShowLogs)
{
    public const int CurrentSchemaVersion = 1;

    public static WorkspacePreferences Default { get; } = new(
        CurrentSchemaVersion,
        new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["unreal"] = true,
            ["unity"] = true,
            ["godot"] = true,
            ["cryengine"] = true,
        },
        [],
        AutoDiscoverProjects: true,
        ShowLogs: false);
}

public interface IWorkspacePreferencesService
{
    Task<WorkspacePreferences> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        WorkspacePreferences preferences,
        CancellationToken cancellationToken = default);
}

public sealed partial class WorkspacePreferencesService(ISettingsStore settings)
    : IWorkspacePreferencesService
{
    public const string SettingKey = "workspace.preferences.v1";
    private const int MaximumProviders = 64;
    private const int MaximumItems = 20_000;
    private const int MaximumKeyLength = 1_024;
    private const int MaximumAliasLength = 128;
    private const int MaximumFolderLength = 256;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public async Task<WorkspacePreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return WorkspacePreferences.Default;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<WorkspacePreferences>(json, JsonOptions);
            return Normalize(parsed);
        }
        catch (JsonException)
        {
            return WorkspacePreferences.Default;
        }
    }

    public async Task SaveAsync(
        WorkspacePreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var normalized = Normalize(preferences);
        await settings.SetAsync(
                SettingKey,
                JsonSerializer.Serialize(normalized, JsonOptions),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static string NormalizeFolderPath(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return string.Empty;
        }

        var segments = folderPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length > 16 ||
            segments.Any(segment =>
                segment is "." or ".." ||
                segment.Length > 64 ||
                segment.Any(char.IsControl)))
        {
            throw new ArgumentException("Le dossier logique est invalide.", nameof(folderPath));
        }

        var normalized = string.Join('/', segments);
        if (normalized.Length > MaximumFolderLength)
        {
            throw new ArgumentException("Le dossier logique est trop long.", nameof(folderPath));
        }

        return normalized;
    }

    public static string NormalizeAlias(string? alias)
    {
        var normalized = alias?.Trim() ?? string.Empty;
        if (normalized.Length > MaximumAliasLength || normalized.Any(char.IsControl))
        {
            throw new ArgumentException("Le libellé personnalisé est invalide.", nameof(alias));
        }

        return normalized;
    }

    private static WorkspacePreferences Normalize(WorkspacePreferences? preferences)
    {
        if (preferences is null || preferences.SchemaVersion != WorkspacePreferences.CurrentSchemaVersion)
        {
            return WorkspacePreferences.Default;
        }

        var providers = new Dictionary<string, bool>(
            WorkspacePreferences.Default.EnabledProviders,
            StringComparer.Ordinal);
        foreach (var pair in (preferences.EnabledProviders ?? new Dictionary<string, bool>())
                     .Take(MaximumProviders))
        {
            var id = pair.Key?.Trim().ToLowerInvariant() ?? string.Empty;
            if (ProviderIdPattern().IsMatch(id))
            {
                providers[id] = pair.Value;
            }
        }

        var items = new Dictionary<(WorkspaceItemKind Kind, string Key), WorkspaceItemPreference>();
        foreach (var item in (preferences.Items ?? []).Take(MaximumItems))
        {
            if (!Enum.IsDefined(item.Kind) ||
                string.IsNullOrWhiteSpace(item.Key) ||
                item.Key.Length > MaximumKeyLength ||
                item.Key.Any(char.IsControl))
            {
                continue;
            }

            try
            {
                var normalized = item with
                {
                    Key = item.Key.Trim(),
                    Alias = NormalizeAlias(item.Alias),
                    FolderPath = NormalizeFolderPath(item.FolderPath),
                };
                items[(normalized.Kind, normalized.Key)] = normalized;
            }
            catch (ArgumentException)
            {
                // Une préférence invalide est ignorée sans rendre le catalogue inutilisable.
            }
        }

        return new WorkspacePreferences(
            WorkspacePreferences.CurrentSchemaVersion,
            providers,
            items.Values
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .ToArray(),
            preferences.AutoDiscoverProjects,
            preferences.ShowLogs);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderIdPattern();
}
