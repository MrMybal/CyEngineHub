using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net;

namespace CyEngineHub.Fab.Remote;

/// <summary>Read-only acquired library. Transport stays in the official authenticated browser.</summary>
public sealed partial class FabWebLibraryClient(Func<string, CancellationToken, Task<string>> read)
{
    public const string Source = "FabWeb";
    public const string FirstPage = "/i/library/search?sort_by=-createdAt&source=acquired";

    public async Task<IReadOnlyList<RemoteFabAsset>> SynchronizeAsync(
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        var assets = new Dictionary<string, RemoteFabAsset>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var path = FirstPage;
        for (var page = 0; page < 3000; page++)
        {
            var json = await read(path, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Fab n’a pas renvoyé une bibliothèque. Reconnectez-vous dans la fenêtre Fab.");
            foreach (var item in results.EnumerateArray())
            {
                var asset = ParseAcquiredItem(item);
                if (asset is not null) assets[asset.AssetId] = asset;
            }
            progress?.Report(assets.Count);
            var next = document.RootElement.TryGetProperty("cursors", out var cursor) ? Text(cursor, "next") : "";
            if (next.Length == 0) return assets.Values.ToArray();
            if (!cursors.Add(next)) throw new InvalidDataException("Pagination Fab répétée. Le cache précédent est conservé.");
            path = FirstPage + "&cursor=" + Uri.EscapeDataString(next);
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        }
        throw new InvalidDataException("Limite de pagination atteinte. Le cache précédent est conservé.");
    }

    public static bool IsAllowedRequest(string path) =>
        path == FirstPage || (path.StartsWith(FirstPage + "&cursor=", StringComparison.Ordinal)
        && path[(FirstPage.Length + 8)..].All(c => char.IsAsciiLetterOrDigit(c) || c is '%' or '-' or '_' or '.' or '~'));

    public static RemoteFabAsset? ParseAcquiredItem(JsonElement item)
    {
        if (Text(item, "source") != "acquired" || !item.TryGetProperty("entitlement", out var entitlement)
            || entitlement.ValueKind != JsonValueKind.Object || !item.TryGetProperty("listing", out var listing)
            || listing.ValueKind != JsonValueKind.Object) return null;
        var publisher = listing.TryGetProperty("publisher", out var p) ? Text(p, "sellerName") : "";
        if (!publisher.Equals("Quixel Megascans", StringComparison.OrdinalIgnoreCase)) return null;
        var id = Text(listing, "uid");
        var libraryId = Text(item, "uid");
        var title = Text(listing, "title");
        if (!Guid.TryParse(id, out _) || !Guid.TryParse(libraryId, out _) || title.Length == 0) return null;
        var categories = new List<string> { "Megascans", Text(listing, "listingType") };
        var description = new List<string>();
        if (listing.TryGetProperty("assetFormats", out var formats) && formats.ValueKind == JsonValueKind.Array)
            foreach (var format in formats.EnumerateArray())
            {
                if (format.TryGetProperty("assetFormatType", out var type)) categories.Add(Text(type, "name"));
                if (format.TryGetProperty("technicalSpecs", out var specs))
                    description.Add(WebUtility.HtmlDecode(HtmlTags().Replace(Text(specs, "technicalDetails"), " ")));
            }
        var images = new List<Uri>();
        if (listing.TryGetProperty("thumbnails", out var thumbnails) && thumbnails.ValueKind == JsonValueKind.Array)
            foreach (var thumbnail in thumbnails.EnumerateArray())
            {
                if (!thumbnail.TryGetProperty("images", out var sizes) || sizes.ValueKind != JsonValueKind.Array) continue;
                var best = sizes.EnumerateArray().OrderBy(s => Math.Abs((s.TryGetProperty("width", out var w) && w.TryGetInt32(out var width) ? width : 0) - 640)).FirstOrDefault();
                if (best.ValueKind != JsonValueKind.Object) continue;
                if (Uri.TryCreate(Text(best, "url"), UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "media.fab.com")
                    images.Add(uri);
            }
        var canDownload = item.TryGetProperty("canRequestDownloadUrl", out var allowed) && allowed.ValueKind == JsonValueKind.True;
        var active = listing.TryGetProperty("isActiveInLive", out var live) && live.ValueKind == JsonValueKind.True;
        var uriPath = active ? "listings/" + id : "library/assets/" + libraryId;
        return new RemoteFabAsset(id, "fab-web", title, string.Join("\n", description.Where(s => s.Length > 0)),
            Source, "Fab web · acquis", new Uri("https://www.fab.com/" + uriPath), images.FirstOrDefault(),
            categories.Where(s => s.Length > 0).Distinct().ToArray(), [], images,
            DateTimeOffset.TryParse(Text(item, "createdAt"), out var date) ? date : null,
            libraryId, canDownload);
    }

    private static string Text(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString()! : "";
    [GeneratedRegex("<[^>]+>", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HtmlTags();
}
