using System.Text.Json;
using System.Text.Json.Serialization;
using CyEngineHub.Core.Settings;
using CyEngineHub.Git.Models;

namespace CyEngineHub.Git.Persistence;

public sealed class GitSnapshotCache
{
    public const string SettingsKey = "git.snapshots.v1";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ISettingsStore _settings;

    public GitSnapshotCache(ISettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<GitRepositorySnapshot>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<GitRepositorySnapshot>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public Task SaveAsync(
        IReadOnlyList<GitRepositorySnapshot> snapshots,
        CancellationToken cancellationToken = default) =>
        _settings.SetAsync(
            SettingsKey,
            JsonSerializer.Serialize(snapshots, SerializerOptions),
            cancellationToken);
}
