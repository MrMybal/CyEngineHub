using System.Text.Json;
using System.Text.Json.Serialization;
using CyEngineHub.Core.Settings;
using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Persistence;

public sealed class ToolchainSnapshotCache
{
    public const string SettingsKey = "toolchains.snapshot.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ISettingsStore _settings;

    public ToolchainSnapshotCache(ISettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<ToolchainSnapshot?> LoadAsync(CancellationToken cancellationToken = default)
    {
        var json = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ToolchainSnapshot>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public Task SaveAsync(
        ToolchainSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return _settings.SetAsync(
            SettingsKey,
            JsonSerializer.Serialize(snapshot, SerializerOptions),
            cancellationToken);
    }
}
