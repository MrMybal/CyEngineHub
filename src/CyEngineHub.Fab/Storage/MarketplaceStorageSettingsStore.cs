using System.Text.Json;
using CyEngineHub.Core.Settings;

namespace CyEngineHub.Fab.Storage;

public sealed class MarketplaceStorageSettingsStore(ISettingsStore settings)
{
    public static string NormalizePath(string value)
    {
        var path = value.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Indiquez un chemin absolu complet.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    public Task SaveAsync(string key, MarketplaceStorageProfile snapshot)
    {
        var libraries = snapshot.Libraries.Select(item => item with { Path = NormalizePath(item.Path) }).ToArray();
        if (!libraries.Any(item => item.Enabled)) throw new ArgumentException("Activez au moins un dossier de bibliothèque.");
        if (libraries.Any(item => item.MaximumBytes < 0)) throw new ArgumentException("La limite de stockage ne peut pas être négative.");
        if (libraries.Select(item => item.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != libraries.Length)
            throw new ArgumentException("Un dossier de bibliothèque est présent plusieurs fois.");
        var normalized = snapshot with { CacheDirectory = NormalizePath(snapshot.CacheDirectory),
            DownloadDirectory = NormalizePath(snapshot.DownloadDirectory), Libraries = libraries };
        // Persist all paths, quotas and ordering together. Never create/move folders while typing.
        return settings.SetAsync(key, JsonSerializer.Serialize(normalized));
    }
}
