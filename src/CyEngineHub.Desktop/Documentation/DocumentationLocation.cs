using System.Text.Json;
using System.Text.RegularExpressions;

namespace CyEngineHub.Desktop.Documentation;

public sealed partial class DocumentationLocation
{
    private readonly string _root;
    private readonly JsonElement _manifest;
    public DocumentationLocation(string root)
    {
        _root = Path.GetFullPath(root);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "manifest.json")));
        _manifest = manifest.RootElement.Clone();
        if (!File.Exists(Path.Combine(_root, "index.html"))) throw new FileNotFoundException("The bundled CyDoc reader is missing.");
    }
    public static string Language(string? language) => language == "fr" ? "fr" : "en";
    public Uri CreateUri(string pageId, string language = "en", string? anchor = null, string? media = null)
    {
        if (pageId.StartsWith("help:", StringComparison.Ordinal)) pageId = pageId[5..];
        var parts = pageId.Split('#', 2); pageId = parts[0]; anchor ??= parts.Length > 1 ? parts[1] : null;
        if (!Identifier().IsMatch(pageId)) throw new ArgumentException("Invalid documentation identifier.", nameof(pageId));
        if (_manifest.TryGetProperty("aliases", out var aliases) && aliases.TryGetProperty(pageId, out var alias))
        {
            var canonical = alias.GetString()!.Split('#', 2); pageId = canonical[0]; anchor ??= canonical.Length > 1 ? canonical[1] : null;
        }
        language = Language(language);
        var pages = _manifest.GetProperty("pages").EnumerateArray().Where(p => p.GetProperty("id").GetString() == pageId).ToArray();
        var page = pages.FirstOrDefault(p => p.GetProperty("locale").GetString() == language);
        if (page.ValueKind == JsonValueKind.Undefined) page = pages.FirstOrDefault(p => p.GetProperty("locale").GetString() == "en");
        if (page.ValueKind == JsonValueKind.Undefined) throw new ArgumentException("Unknown documentation page.", nameof(pageId));
        if (anchor is not null && !page.GetProperty("headings").EnumerateArray().Any(h => h.GetProperty("id").GetString() == anchor) &&
            !(page.TryGetProperty("anchors", out var anchors) && anchors.EnumerateArray().Any(a => a.GetString() == anchor)))
            throw new ArgumentException("Unknown documentation section.", nameof(anchor));
        if (media is not null && (!Identifier().IsMatch(media) || !page.GetProperty("html").GetString()!.Contains("data-cydoc-id=\"" + media + "\"", StringComparison.Ordinal)))
            throw new ArgumentException("Unknown documentation media.", nameof(media));
        return new Uri(new Uri(Path.Combine(_root, "index.html")).AbsoluteUri + "#/" + language + "/" + Uri.EscapeDataString(pageId) +
            (anchor is null ? "" : "/" + Uri.EscapeDataString(anchor)) + (media is null ? "" : "?media=" + Uri.EscapeDataString(media)));
    }
    public bool AllowsResource(Uri uri)
    {
        if (!uri.IsFile || uri.IsUnc || uri.UserInfo.Length > 0 || uri.Query.Length > 0) return false;
        var path = Path.GetFullPath(uri.LocalPath);
        return path.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,200}$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}
