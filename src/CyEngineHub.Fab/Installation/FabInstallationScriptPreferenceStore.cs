using System.Text.Json;
using CyEngineHub.Core.Settings;

namespace CyEngineHub.Fab.Installation;

public sealed record FabInstallationScriptPreference(
    string TargetId,
    string ContentKind,
    string ScriptId,
    IReadOnlyDictionary<string, string> Configuration);

public interface IFabInstallationScriptPreferenceStore
{
    Task<IReadOnlyList<FabInstallationScriptPreference>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyList<FabInstallationScriptPreference> preferences,
        CancellationToken cancellationToken = default);
}

public sealed class FabInstallationScriptPreferenceStore(ISettingsStore settings)
    : IFabInstallationScriptPreferenceStore
{
    public const string SettingKey = "fab.installation-scripts.v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<FabInstallationScriptPreference>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await settings.GetAsync(SettingKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var preferences = JsonSerializer.Deserialize<FabInstallationScriptPreference[]>(json, JsonOptions)
                              ?? [];
            return Normalize(preferences);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public Task SaveAsync(
        IReadOnlyList<FabInstallationScriptPreference> preferences,
        CancellationToken cancellationToken = default) => settings.SetAsync(
        SettingKey,
        JsonSerializer.Serialize(Normalize(preferences), JsonOptions),
        cancellationToken);

    private static FabInstallationScriptPreference[] Normalize(
        IEnumerable<FabInstallationScriptPreference> preferences) => preferences
        .Where(preference =>
            !string.IsNullOrWhiteSpace(preference.TargetId) &&
            preference.TargetId.Length <= 1024 &&
            !string.IsNullOrWhiteSpace(preference.ContentKind) &&
            !string.IsNullOrWhiteSpace(preference.ScriptId) &&
            preference.ScriptId.Length <= 128)
        .Take(20_000)
        .Select(preference => preference with
        {
            TargetId = preference.TargetId.Trim(),
            ContentKind = preference.ContentKind.Trim(),
            ScriptId = preference.ScriptId.Trim(),
            Configuration = (preference.Configuration ?? new Dictionary<string, string>())
                .Where(pair => pair.Key.Length is > 0 and <= 64 && pair.Value.Length <= 4096)
                .Take(64)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
        })
        .GroupBy(
            preference => (preference.TargetId, preference.ContentKind),
            new TargetPreferenceKeyComparer())
        .Select(group => group.Last())
        .ToArray();

    private sealed class TargetPreferenceKeyComparer
        : IEqualityComparer<(string TargetId, string ContentKind)>
    {
        public bool Equals(
            (string TargetId, string ContentKind) x,
            (string TargetId, string ContentKind) y) =>
            string.Equals(x.TargetId, y.TargetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ContentKind, y.ContentKind, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string TargetId, string ContentKind) obj) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.TargetId),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ContentKind));
    }
}
