using System.Text.Json;

namespace CyEngineHub.Core.Settings;

public sealed record MarketplaceConnectorSettings(bool Enabled, string DownloadDirectory);

/// <summary>One atomic settings value per connector; independent of all other marketplace forms.</summary>
public sealed class MarketplaceConnectorSettingsStore(ISettingsStore settings)
{
    private static readonly SemaphoreSlim WriteGate = new(1, 1);
    private static string Prefix(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '.' and not '-' and not '_'))
            throw new ArgumentException("Identifiant de connecteur invalide.", nameof(id));
        return $"marketplaces.connectors.{id}";
    }

    public async Task<MarketplaceConnectorSettings> LoadAsync(string id, string defaultDirectory)
    {
        var prefix = Prefix(id);
        var json = await settings.GetAsync(prefix + ".settings.v2").ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(json))
            return JsonSerializer.Deserialize<MarketplaceConnectorSettings>(json)
                ?? throw new InvalidDataException("Paramètres du connecteur illisibles ; aucune valeur écrasée.");
        var enabled = await settings.GetAsync(prefix + ".enabled.v1").ConfigureAwait(false);
        var directory = await settings.GetAsync(prefix + ".downloads.v1").ConfigureAwait(false);
        return new(!bool.TryParse(enabled, out var parsed) || parsed,
            string.IsNullOrWhiteSpace(directory) ? defaultDirectory : directory);
    }

    public async Task SaveAsync(string id, MarketplaceConnectorSettings snapshot)
    {
        var key = Prefix(id) + ".settings.v2";
        var path = snapshot.DownloadDirectory.Trim().Trim('"');
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Le dossier de téléchargement doit être un chemin absolu.");
        path = Path.GetFullPath(path);
        await WriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (File.Exists(path)) throw new IOException("Le chemin choisi désigne un fichier, pas un dossier.");
            await settings.SetAsync(key, JsonSerializer.Serialize(snapshot with { DownloadDirectory = path })).ConfigureAwait(false);
        }
        finally { WriteGate.Release(); }
    }
}
