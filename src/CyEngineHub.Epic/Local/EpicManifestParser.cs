using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyEngineHub.Epic.Models;

namespace CyEngineHub.Epic.Local;

public interface IEpicManifestParser
{
    Task<EpicLocalArtifact?> ParseAsync(
        string manifestPath,
        CancellationToken cancellationToken = default);
}

public sealed class EpicManifestParser : IEpicManifestParser
{
    private const long MaximumManifestBytes = 2 * 1024 * 1024;

    public async Task<EpicLocalArtifact?> ParseAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var info = new FileInfo(manifestPath);
        if (!info.Exists)
        {
            return null;
        }

        if (info.Length > MaximumManifestBytes)
        {
            throw new EpicOperationException(
                "Le manifeste Epic dépasse la limite de 2 MB.",
                "ParseManifest",
                manifestPath);
        }

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
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var root = document.RootElement;
            var appName = GetString(root, "AppName");
            var artifactId = GetString(root, "ArtifactId");
            if (string.IsNullOrWhiteSpace(appName) && string.IsNullOrWhiteSpace(artifactId))
            {
                return null;
            }

            var displayName = GetString(root, "DisplayName");
            var installLocation = GetString(root, "InstallLocation");
            var catalogNamespace = GetString(root, "CatalogNamespace");
            var mainGameAppName = GetString(root, "MainGameAppName");
            var identity = artifactId ?? appName!;
            return new EpicLocalArtifact(
                StableId(identity),
                appName ?? identity,
                displayName ?? appName ?? identity,
                GetString(root, "AppVersionString") ?? "—",
                installLocation ?? string.Empty,
                GetString(root, "CatalogItemId") ?? string.Empty,
                catalogNamespace ?? string.Empty,
                Classify(appName, mainGameAppName, catalogNamespace),
                !string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation),
                Path.GetFileName(manifestPath));
        }
        catch (EpicOperationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new EpicOperationException(
                $"Manifeste Epic illisible : {exception.Message}",
                "ParseManifest",
                manifestPath,
                exception);
        }
    }

    internal static EpicArtifactKind Classify(
        string? appName,
        string? mainGameAppName,
        string? catalogNamespace)
    {
        if (appName?.StartsWith("UE_", StringComparison.OrdinalIgnoreCase) == true)
        {
            return EpicArtifactKind.UnrealEngine;
        }

        if (appName?.StartsWith("FabPlugin_", StringComparison.OrdinalIgnoreCase) == true)
        {
            return EpicArtifactKind.FabIntegration;
        }

        if (mainGameAppName?.StartsWith("UE_", StringComparison.OrdinalIgnoreCase) == true)
        {
            return EpicArtifactKind.UnrealPlugin;
        }

        if (string.Equals(catalogNamespace, "ue", StringComparison.OrdinalIgnoreCase))
        {
            return EpicArtifactKind.UnrealContent;
        }

        return string.IsNullOrWhiteSpace(catalogNamespace)
            ? EpicArtifactKind.Unknown
            : EpicArtifactKind.EpicApplication;
    }

    internal static string StableId(string identity)
    {
        var bytes = Encoding.UTF8.GetBytes(identity.ToUpperInvariant());
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
