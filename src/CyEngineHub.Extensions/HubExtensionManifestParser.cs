using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CyEngineHub.Extensions;

public interface IHubExtensionManifestParser
{
    Task<HubExtensionManifest> ParseAsync(
        string extensionRoot,
        CancellationToken cancellationToken = default);
}

public sealed partial class HubExtensionManifestParser : IHubExtensionManifestParser
{
    public const string ManifestFileName = "extension.json";
    private const long MaximumManifestBytes = 128 * 1024;
    private static readonly HashSet<string> AllowedPlaceholders =
    [
        "{Input}",
        "{Output}",
        "{SourceEngine}",
        "{TargetEngine}",
    ];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<HubExtensionManifest> ParseAsync(
        string extensionRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionRoot);
        var root = Path.GetFullPath(extensionRoot.Trim().Trim('"'));
        if (!Directory.Exists(root) || IsReparsePoint(root))
        {
            throw new HubExtensionException(
                "Le dossier de l’extension est absent ou constitue un lien symbolique.",
                "ParseManifest",
                root);
        }

        var manifestPath = Path.Combine(root, ManifestFileName);
        var info = new FileInfo(manifestPath);
        if (!info.Exists || info.Length is <= 0 or > MaximumManifestBytes || IsReparsePoint(manifestPath))
        {
            throw new HubExtensionException(
                $"Le fichier {ManifestFileName} est absent ou invalide.",
                "ParseManifest",
                manifestPath);
        }

        HubExtensionManifest? manifest;
        try
        {
            await using var stream = new FileStream(
                manifestPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                32 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            manifest = await JsonSerializer.DeserializeAsync<HubExtensionManifest>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new HubExtensionException(
                $"Le manifeste JSON est invalide : {exception.Message}",
                "ParseManifest",
                manifestPath,
                exception);
        }

        return Validate(manifest, root, manifestPath);
    }

    private static HubExtensionManifest Validate(
        HubExtensionManifest? manifest,
        string root,
        string manifestPath)
    {
        if (manifest is null ||
            manifest.SchemaVersion != 1 ||
            !ExtensionIdRegex().IsMatch(manifest.Id) ||
            !SemanticVersionRegex().IsMatch(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.DisplayName) ||
            manifest.DisplayName.Length > 100 ||
            string.IsNullOrWhiteSpace(manifest.Publisher) ||
            manifest.Publisher.Length > 100 ||
            string.IsNullOrWhiteSpace(manifest.Description) ||
            manifest.Description.Length > 1_000 ||
            manifest.Capabilities == HubExtensionCapability.None ||
            manifest.SupportedEngines is null or { Count: 0 } ||
            manifest.SupportedEngines.Count > 8 ||
            manifest.SupportedEngines.Distinct().Count() != manifest.SupportedEngines.Count ||
            manifest.SupportedFileExtensions is null ||
            manifest.SupportedFileExtensions.Count > 64 ||
            manifest.Arguments is null ||
            manifest.Arguments.Count > 64 ||
            manifest.UiContributions is null ||
            manifest.UiContributions.Count > 32 ||
            !Enum.IsDefined(manifest.Kind) ||
            !Enum.IsDefined(manifest.ExecutionKind))
        {
            throw InvalidManifest(manifestPath);
        }

        var contributionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var contribution in manifest.UiContributions)
        {
            if (contribution is null ||
                !ContributionIdRegex().IsMatch(contribution.Id) ||
                !contributionIds.Add(contribution.Id) ||
                string.IsNullOrWhiteSpace(contribution.Label) ||
                contribution.Label.Length > 80 ||
                contribution.Label.Any(char.IsControl) ||
                contribution.Order is < -1_000 or > 1_000 ||
                !Enum.IsDefined(contribution.Placement) ||
                !Enum.IsDefined(contribution.Action) ||
                contribution.Action == HubUiAction.OpenWebsite &&
                !TryGetHttpsUri(manifest.Website, out _) ||
                contribution.Action == HubUiAction.PrepareExecution &&
                manifest.ExecutionKind != HubExtensionExecutionKind.ExternalProcess)
            {
                throw InvalidManifest(manifestPath);
            }
        }

        foreach (var extension in manifest.SupportedFileExtensions)
        {
            if (!FileExtensionRegex().IsMatch(extension))
            {
                throw InvalidManifest(manifestPath);
            }
        }

        if (manifest.ExecutionKind == HubExtensionExecutionKind.WebLink)
        {
            if (!TryGetHttpsUri(manifest.Website, out _) ||
                !string.IsNullOrWhiteSpace(manifest.EntryPoint) ||
                manifest.Arguments.Count != 0 ||
                manifest.WritesToOutput)
            {
                throw InvalidManifest(manifestPath);
            }

            return manifest;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryPoint) ||
            Path.IsPathRooted(manifest.EntryPoint) ||
            !string.Equals(Path.GetExtension(manifest.EntryPoint), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidManifest(manifestPath);
        }

        var executablePath = CombineContained(root, manifest.EntryPoint);
        if (!File.Exists(executablePath) || IsReparsePoint(executablePath))
        {
            throw new HubExtensionException(
                "L’exécutable déclaré par l’extension est absent ou constitue un lien.",
                "ParseManifest",
                executablePath);
        }

        foreach (var argument in manifest.Arguments)
        {
            if (argument.Length > 1_000 ||
                PlaceholderRegex().Matches(argument)
                    .Select(match => match.Value)
                    .Any(value => !AllowedPlaceholders.Contains(value)))
            {
                throw InvalidManifest(manifestPath);
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Website) && !TryGetHttpsUri(manifest.Website, out _))
        {
            throw InvalidManifest(manifestPath);
        }

        return manifest;
    }

    internal static string CombineContained(string root, string relativePath)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var combined = Path.GetFullPath(Path.Combine(normalizedRoot, relativePath));
        if (!combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new HubExtensionException(
                "Un chemin de l’extension sort de son dossier autorisé.",
                "ValidatePath",
                combined);
        }

        return combined;
    }

    internal static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public static bool TryGetHttpsUri(string? value, out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrWhiteSpace(parsed.UserInfo))
        {
            uri = parsed;
            return true;
        }

        uri = null;
        return false;
    }

    private static HubExtensionException InvalidManifest(string manifestPath) =>
        new(
            "Le manifeste de l’extension est incomplet ou contient une valeur non autorisée.",
            "ParseManifest",
            manifestPath);

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{1,62}[a-z0-9])?$")]
    private static partial Regex ExtensionIdRegex();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9.-]{0,46}[a-z0-9])?$")]
    private static partial Regex ContributionIdRegex();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$")]
    private static partial Regex SemanticVersionRegex();

    [GeneratedRegex("^\\.[a-z0-9]{1,12}$", RegexOptions.IgnoreCase)]
    private static partial Regex FileExtensionRegex();

    [GeneratedRegex("\\{[^{}]+\\}")]
    private static partial Regex PlaceholderRegex();
}
