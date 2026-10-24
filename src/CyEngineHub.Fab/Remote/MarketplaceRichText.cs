using System.Net;
using System.Text;

namespace CyEngineHub.Fab.Remote;

/// <summary>Display-only conversion of marketplace HTML. Never executes markup or fetches links/images.</summary>
public static class MarketplaceRichText
{
    private static readonly HashSet<string> InlineTags = new(StringComparer.Ordinal)
        { "strong", "b", "em", "i", "u", "s", "strike", "span", "font", "small", "big", "sup", "sub", "code", "mark", "del", "ins", "abbr" };
    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
        { "p", "div", "section", "article", "header", "footer", "blockquote", "pre", "h1", "h2", "h3", "h4", "h5", "h6", "table", "tr", "dl", "dt", "dd", "hr" };
    private static readonly HashSet<string> HiddenTags = new(StringComparer.Ordinal)
        { "script", "style", "iframe", "object", "head", "noscript", "svg" };

    public static string ToPlainText(string? value) => Convert(value, markdown: false);
    public static string ToMarkdown(string? value) => Convert(value, markdown: true);

    private static string Convert(string? value, bool markdown)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        // Some older caches hold entity-encoded HTML, sometimes encoded twice.
        for (var pass = 0; pass < 3; pass++)
        {
            var decoded = WebUtility.HtmlDecode(value);
            if (decoded == value) break;
            value = decoded;
        }
        if (markdown && !HasHtml(value)) return value.Trim();
        var output = new StringBuilder(value.Length);
        var lists = new Stack<int>(); // -1 = unordered; otherwise next item number.
        var anchors = new Stack<(int Start, string? Url)>();
        var awaitingListText = false;
        for (var index = 0; index < value.Length;)
        {
            if (value[index] != '<')
            {
                var end = value.IndexOf('<', index);
                if (end < 0) end = value.Length;
                var text = value.AsSpan(index, end - index);
                output.Append(text);
                if (!text.IsWhiteSpace()) awaitingListText = false;
                index = end; continue;
            }
            if (value.AsSpan(index).StartsWith("<!--", StringComparison.Ordinal))
            {
                var endComment = value.IndexOf("-->", index + 4, StringComparison.Ordinal);
                index = endComment < 0 ? value.Length : endComment + 3; continue;
            }
            var endTag = TagEnd(value, index + 1);
            if (endTag < 0) { output.Append(value.AsSpan(index)); break; }
            var token = value.AsSpan(index + 1, endTag - index - 1).Trim();
            var closing = token.StartsWith("/", StringComparison.Ordinal);
            if (closing) token = token[1..].TrimStart();
            var nameEnd = 0;
            while (nameEnd < token.Length && char.IsAsciiLetterOrDigit(token[nameEnd])) nameEnd++;
            var name = token[..nameEnd].ToString().ToLowerInvariant();
            var attributes = token[nameEnd..];
            if (HiddenTags.Contains(name) && !closing)
            {
                var close = value.IndexOf("</" + name, endTag + 1, StringComparison.OrdinalIgnoreCase);
                var closeEnd = close < 0 ? -1 : TagEnd(value, close + 2);
                index = closeEnd < 0 ? value.Length : closeEnd + 1; continue;
            }
            if (name == "a")
            {
                if (!closing && anchors.Count < 64) anchors.Push((output.Length, SafeLink(Attribute(attributes, "href"))));
                else if (closing && anchors.TryPop(out var anchor) && anchor.Url is { } url)
                {
                    var start = Math.Min(anchor.Start, output.Length);
                    var label = output.ToString(start, output.Length - start).Trim();
                    if (markdown)
                    {
                        output.Insert(start, '[');
                        output.Append("](").Append(url).Append(')');
                    }
                    else if (!label.Equals(url, StringComparison.OrdinalIgnoreCase)) output.Append(" (").Append(url).Append(')');
                }
            }
            else if (name is "ul" or "ol")
            {
                Break(output, 1);
                if (closing) { if (lists.Count > 0) lists.Pop(); }
                else if (lists.Count < 64) lists.Push(name == "ol" ? 1 : -1);
            }
            else if (name == "li")
            {
                Break(output, 1);
                if (!closing)
                {
                    var number = lists.Count > 0 ? lists.Pop() : -1;
                    output.Append(number < 0 ? "• " : number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ". ");
                    if (lists.Count < 64) lists.Push(number < 0 ? -1 : number + 1);
                    awaitingListText = true;
                }
            }
            else if (name == "br" || name == "wbr") Break(output, 1);
            else if (BlockTags.Contains(name))
            {
                if (!awaitingListText || closing) Break(output, name is "p" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" ? 2 : 1);
                if (markdown && !closing && name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6')
                    output.Append('#', name[1] - '0').Append(' ');
                if (markdown && name == "hr") { output.Append("---"); Break(output, 2); }
            }
            else if (name is "td" or "th") { if (closing) output.Append(' '); }
            else if (name == "img") { if (!closing) output.Append(Attribute(attributes, "alt")); }
            else if (markdown && name is "strong" or "b") output.Append("**");
            else if (markdown && name is "em" or "i") output.Append('*');
            else if (markdown && name == "code") output.Append('`');
            else if (!InlineTags.Contains(name) && !HiddenTags.Contains(name) && !token.StartsWith("!", StringComparison.Ordinal))
                output.Append(value.AsSpan(index, endTag - index + 1)); // Preserve C++ types and ordinary comparisons.
            index = endTag + 1;
        }
        return NormalizeWhitespace(output.ToString());
    }

    private static int TagEnd(string text, int start)
    {
        var quote = '\0';
        for (var index = start; index < text.Length; index++)
        {
            var character = text[index];
            if (quote != '\0') { if (character == quote) quote = '\0'; }
            else if (character is '\'' or '"') quote = character;
            else if (character == '>') return index;
        }
        return -1;
    }

    private static string? Attribute(ReadOnlySpan<char> attributes, string requested)
    {
        while (!attributes.IsEmpty)
        {
            attributes = attributes.TrimStart();
            var length = 0;
            while (length < attributes.Length && (char.IsAsciiLetterOrDigit(attributes[length]) || attributes[length] is '-' or '_' or ':')) length++;
            if (length == 0) { attributes = attributes[1..]; continue; }
            var name = attributes[..length]; attributes = attributes[length..].TrimStart();
            if (attributes.IsEmpty || attributes[0] != '=') continue;
            attributes = attributes[1..].TrimStart();
            if (attributes.IsEmpty) break;
            var quote = attributes[0] is '\'' or '"' ? attributes[0] : '\0';
            if (quote != '\0') attributes = attributes[1..];
            length = 0;
            while (length < attributes.Length && (quote != '\0' ? attributes[length] != quote : !char.IsWhiteSpace(attributes[length]))) length++;
            var content = attributes[..length].ToString();
            attributes = attributes[Math.Min(attributes.Length, length + (quote == '\0' ? 0 : 1))..];
            if (name.Equals(requested, StringComparison.OrdinalIgnoreCase)) return content;
        }
        return null;
    }

    internal static string? SafeLink(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)) value = "https://www.fab.com" + value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo) ? uri.AbsoluteUri : null;
    }
    private static bool HasHtml(string value)
    {
        for (var start = value.IndexOf('<'); start >= 0; start = value.IndexOf('<', start + 1))
        {
            var index = start + 1;
            if (index < value.Length && value[index] == '/') index++;
            var nameStart = index;
            while (index < value.Length && char.IsAsciiLetterOrDigit(value[index])) index++;
            var name = value[nameStart..index].ToLowerInvariant();
            if (BlockTags.Contains(name) || InlineTags.Contains(name) || HiddenTags.Contains(name) || name is "a" or "br" or "ul" or "ol" or "li" or "img" or "td" or "th" or "wbr") return true;
        }
        return false;
    }
    private static void Break(StringBuilder output, int count)
    {
        while (output.Length > 0 && output[^1] is ' ' or '\t') output.Length--;
        if (output.Length == 0) return;
        var existing = 0;
        for (var index = output.Length - 1; index >= 0 && output[index] == '\n'; index--) existing++;
        if (existing < count) output.Append('\n', count - existing);
    }
    private static string NormalizeWhitespace(string text)
    {
        var result = new StringBuilder(text.Length); var blank = false;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            var clean = new StringBuilder(); var space = false;
            foreach (var character in line.Trim())
            {
                if (char.IsWhiteSpace(character)) { space = clean.Length > 0; continue; }
                if (space) clean.Append(' ');
                clean.Append(character); space = false;
            }
            if (clean.Length == 0) { if (result.Length > 0) blank = true; continue; }
            if (result.Length > 0) result.Append(blank ? "\n\n" : "\n");
            result.Append(clean); blank = false;
        }
        return result.ToString();
    }
}
