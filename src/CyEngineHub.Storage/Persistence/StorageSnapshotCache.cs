using System.Text.Json;
using System.Text.Json.Serialization;
using CyEngineHub.Core.Settings;
using CyEngineHub.Storage.Models;

namespace CyEngineHub.Storage.Persistence;

public sealed class StorageSnapshotCache
{
    public const string SettingsKey = "storage.snapshot.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ISettingsStore _settings;

    public StorageSnapshotCache(ISettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<StorageSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<StorageSnapshot>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task SaveAsync(StorageSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _settings.SetAsync(
            SettingsKey,
            JsonSerializer.Serialize(snapshot, SerializerOptions),
            cancellationToken);
    }
}
