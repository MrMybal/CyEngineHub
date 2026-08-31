using System.Text.Json;
using System.Text.Json.Serialization;
using CyEngineHub.Core.Settings;
using CyEngineHub.Epic.Models;

namespace CyEngineHub.Epic.Persistence;

public sealed class EpicSnapshotCache
{
    public const string SettingsKey = "epic.local.snapshot.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ISettingsStore _settings;

    public EpicSnapshotCache(ISettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<EpicLocalSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EpicLocalSnapshot>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task SaveAsync(
        EpicLocalSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _settings.SetAsync(
            SettingsKey,
            JsonSerializer.Serialize(snapshot, SerializerOptions),
            cancellationToken);
    }
}
