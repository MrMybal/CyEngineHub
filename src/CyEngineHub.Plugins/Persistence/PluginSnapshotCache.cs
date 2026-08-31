using System.Text.Json;
using System.Text.Json.Serialization;
using CyEngineHub.Core.Settings;
using CyEngineHub.Plugins.Models;

namespace CyEngineHub.Plugins.Persistence;

public sealed class PluginSnapshotCache
{
    public const string SettingsKey = "plugins.snapshots.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ISettingsStore _settings;

    public PluginSnapshotCache(ISettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<UnrealPluginDescriptor>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<UnrealPluginDescriptor>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public Task SaveAsync(
        IReadOnlyList<UnrealPluginDescriptor> plugins,
        CancellationToken cancellationToken = default) =>
        _settings.SetAsync(
            SettingsKey,
            JsonSerializer.Serialize(plugins, SerializerOptions),
            cancellationToken);
}
