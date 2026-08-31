using System.Text.Json;

namespace CyEngineHub.Unreal.Detection;

public sealed class EpicManifestEngineDiscoverySource : IEngineDiscoverySource
{
    private readonly string _manifestDirectory;

    public EpicManifestEngineDiscoverySource(string? commonApplicationData = null)
    {
        var programData = commonApplicationData
            ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        _manifestDirectory = Path.Combine(
            programData,
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests");
    }

    public string Name => "Epic manifests";

    public async Task<IReadOnlyList<EngineCandidate>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_manifestDirectory))
        {
            return [];
        }

        var candidates = new List<EngineCandidate>();
        foreach (var manifestPath in Directory.EnumerateFiles(
                     _manifestDirectory,
                     "*.item",
                     SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var stream = new FileStream(
                    manifestPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var document = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                var installLocation = GetString(root, "InstallLocation");
                var appName = GetString(root, "AppName");
                if (string.IsNullOrWhiteSpace(installLocation) ||
                    (appName?.StartsWith("UE_", StringComparison.OrdinalIgnoreCase) != true &&
                     !File.Exists(Path.Combine(installLocation, "Engine", "Build", "Build.version"))))
                {
                    continue;
                }

                candidates.Add(new EngineCandidate(
                    installLocation,
                    Name,
                    appName,
                    GetString(root, "DisplayName")));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // A partially-written launcher manifest must not abort the whole refresh.
            }
        }

        return candidates;
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

