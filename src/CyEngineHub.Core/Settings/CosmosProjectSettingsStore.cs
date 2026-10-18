using System.Security.Cryptography;
using System.Text;

namespace CyEngineHub.Core.Settings;

public sealed class CosmosProjectSettingsStore(ISettingsStore settings)
{
    private static string Key(string project) => "cosmos.project.destination.v1." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(project)).ToUpperInvariant())));
    public async Task<string> LoadAsync(string project) => await settings.GetAsync(Key(project)).ConfigureAwait(false) ?? "Cosmos";
    public Task SaveAsync(string project, string destination) => settings.SetAsync(Key(project), Normalize(destination));
    public static string Normalize(string value)
    {
        var folder = value.Trim().Replace('\\', '/');
        if (folder.StartsWith("/Game/", StringComparison.Ordinal)) folder = folder[6..];
        if (folder.Length is 0 or > 240 || folder.Split('/').Any(part => part.Length == 0 || part.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_')))
            throw new ArgumentException("Utilisez un chemin sous /Game, avec lettres, chiffres, _ et / uniquement (ex. Assets/Cosmos).");
        return folder;
    }
}
