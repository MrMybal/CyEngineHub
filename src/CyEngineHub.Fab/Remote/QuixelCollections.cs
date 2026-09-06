using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Text.Json;

namespace CyEngineHub.Fab.Remote;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "A marketplace collection is a catalog entity, not a .NET collection container.")]
public sealed record QuixelCollection(
    string Id,
    string Name,
    string Category,
    string Description,
    Uri? ThumbnailUri,
    Uri ListingUri,
    IReadOnlyList<string>? AssetIds = null);

public interface IQuixelCollectionsClient
{
    Task<IReadOnlyList<QuixelCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default);
    Task<QuixelCollection> GetCollectionAsync(string id, CancellationToken cancellationToken = default);
}

public sealed partial class QuixelMegascansApiClient : IQuixelCollectionsClient
{
    private const int MaximumCollectionPages = 50;
    private const int MaximumCollectionResponseBytes = 8 * 1024 * 1024;
    private static readonly Uri CollectionImageBase = new("https://d3uwib8iif8w1p.cloudfront.net/");

    // Public metadata only. Acquisitions and downloads still use the authenticated client.
    public async Task<IReadOnlyList<QuixelCollection>> GetCollectionsAsync(CancellationToken cancellationToken = default)
    {
        var collections = new Dictionary<string, QuixelCollection>(StringComparer.OrdinalIgnoreCase);
        for (var page = 0; page < MaximumCollectionPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"https://{AlgoliaApplicationId}-2.algolianet.com/1/indexes/collections/query");
            request.Headers.Add("X-Algolia-Application-Id", AlgoliaApplicationId);
            request.Headers.Add("X-Algolia-API-Key", AlgoliaSearchKey);
            request.Content = JsonContent.Create(new { query = string.Empty, page, hitsPerPage = 100, attributesToHighlight = Array.Empty<string>() });
            using var document = await ReadCollectionResponseAsync(request, cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("hits", out var hits) || hits.ValueKind != JsonValueKind.Array ||
                !root.TryGetProperty("nbPages", out var pagesValue) || !pagesValue.TryGetInt32(out var pages) ||
                pages is < 0 or > MaximumCollectionPages)
                throw new QuixelException("Le catalogue des collections Megascans a un format inattendu.");
            foreach (var hit in hits.EnumerateArray())
            {
                var collection = ParseCollection(hit, includeAssets: false);
                if (collection is not null) collections.TryAdd(collection.Id, collection);
            }
            if (page + 1 >= pages) return collections.Values.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (hits.GetArrayLength() == 0) throw new QuixelException("Le catalogue des collections Megascans est incomplet.");
        }
        throw new QuixelException("Le catalogue des collections Megascans dépasse la limite prévue.");
    }

    public async Task<QuixelCollection> GetCollectionAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsSafeCollectionId(id)) throw new ArgumentException("Identifiant de collection invalide.", nameof(id));
        // Endpoint used by the official Quixel web client; no session/cookies are sent.
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{QuixelBase}/v1/packs/{Uri.EscapeDataString(id)}");
        using var document = await ReadCollectionResponseAsync(request, cancellationToken).ConfigureAwait(false);
        var collection = ParseCollection(document.RootElement, includeAssets: true);
        if (collection is null || !string.Equals(collection.Id, id, StringComparison.OrdinalIgnoreCase))
            throw new QuixelException("La collection Megascans demandée n’est pas disponible.");
        return collection;
    }

    private async Task<JsonDocument> ReadCollectionResponseAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "Les collections publiques Megascans sont indisponibles.", cancellationToken).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > MaximumCollectionResponseBytes)
            throw new QuixelException("La réponse des collections Megascans est trop volumineuse.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await input.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + read > MaximumCollectionResponseBytes)
                throw new QuixelException("La réponse des collections Megascans est trop volumineuse.");
            buffer.Write(chunk, 0, read);
        }
        buffer.Position = 0;
        return await JsonDocument.ParseAsync(buffer, new JsonDocumentOptions { MaxDepth = 32 }, cancellationToken).ConfigureAwait(false);
    }

    private static QuixelCollection? ParseCollection(JsonElement root, bool includeAssets)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        var id = ReadFirstString(root, "objectID", "id");
        var name = ReadString(root, "name");
        var category = ReadString(root, "collectionPath") ?? string.Empty;
        if (!IsSafeCollectionId(id) || string.IsNullOrWhiteSpace(name) ||
            name.StartsWith("x -", StringComparison.OrdinalIgnoreCase) ||
            category.StartsWith("x |", StringComparison.OrdinalIgnoreCase)) return null;
        var description = string.Empty;
        Uri? thumbnail = null;
        if (root.TryGetProperty("presentation", out var presentation) && presentation.ValueKind == JsonValueKind.Object)
        {
            description = ReadString(presentation, "subtitle") ?? string.Empty;
            var image = ReadString(presentation, "splashImage");
            if (presentation.TryGetProperty("coverImages", out var covers) && covers.ValueKind == JsonValueKind.Array)
                image = covers.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()).FirstOrDefault() ?? image;
            // Prefer the publisher's 644px cover variant when explicitly supplied.
            if (image is not null && presentation.TryGetProperty("coverImageRes", out var sizes) && sizes.ValueKind == JsonValueKind.Array &&
                sizes.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == "_644"))
            {
                var extension = image.LastIndexOf('.');
                if (extension > 0) image = image.Insert(extension, "_644");
            }
            thumbnail = CollectionImageUri(image);
        }
        string[]? assetIds = null;
        if (includeAssets)
        {
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array || assets.GetArrayLength() > MaximumAcquisitions)
                throw new QuixelException("Le contenu de la collection Megascans a un format inattendu.");
            assetIds = assets.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!).Where(value => SafeAssetIdRegex().IsMatch(value))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        var path = category.Split(" | ", StringSplitOptions.RemoveEmptyEntries).Select(part => part.ToLowerInvariant()).Append(id!);
        var query = string.Join("&", path.Select(part => "category=" + Uri.EscapeDataString(part)));
        return new QuixelCollection(id!, name, category, description, thumbnail,
            new Uri("https://quixel.com/megascans/collections?" + query), assetIds);
    }

    private static Uri? CollectionImageUri(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Replace("quixel-megascans-packs-images/", "megascans/", StringComparison.Ordinal);
        if (Uri.TryCreate(path, UriKind.Absolute, out var absolute))
            return absolute.Scheme == Uri.UriSchemeHttps && absolute.UserInfo.Length == 0 &&
                absolute.Host.Equals(CollectionImageBase.Host, StringComparison.OrdinalIgnoreCase) ? absolute : null;
        if (!path.StartsWith("megascans/", StringComparison.Ordinal) || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\')) return null;
        return new Uri(CollectionImageBase, path);
    }

    private static bool IsSafeCollectionId(string? id) => id is { Length: > 0 and <= 160 } &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
