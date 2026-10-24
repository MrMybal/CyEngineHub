using System.Text;

namespace CyEngineHub.Fab.Remote;

public sealed record MarketplaceTextRun(string Text, bool Bold = false, bool Italic = false, bool Code = false, Uri? Link = null);
public sealed record MarketplaceTextBlock(IReadOnlyList<MarketplaceTextRun> Runs, int Heading = 0, string? Marker = null,
    int Indent = 0, bool Quote = false, bool Code = false, bool Separator = false);
public sealed record MarketplaceDescriptionDocument(IReadOnlyList<MarketplaceTextBlock> Blocks)
{
    public static MarketplaceDescriptionDocument Parse(string? source)
    {
        var markdown = MarketplaceRichText.ToMarkdown(source).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var blocks = new List<MarketplaceTextBlock>(); var paragraph = new StringBuilder();
        var code = new StringBuilder(); var fence = string.Empty;
        void Flush()
        {
            if (paragraph.Length == 0) return;
            blocks.Add(new(ParseInline(paragraph.ToString()))); paragraph.Clear();
        }
        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                Flush();
                if (fence.Length == 0) { fence = line[..3]; continue; }
                if (line.StartsWith(fence, StringComparison.Ordinal))
                { blocks.Add(new([new(code.ToString().TrimEnd(), Code: true)], Code: true)); code.Clear(); fence = ""; continue; }
            }
            if (fence.Length > 0) { code.AppendLine(raw); continue; }
            if (line.Length == 0) { Flush(); continue; }
            var heading = 0; while (heading < line.Length && line[heading] == '#') heading++;
            if (heading is > 0 and <= 6 && heading < line.Length && char.IsWhiteSpace(line[heading]))
            { Flush(); blocks.Add(new(ParseInline(line[(heading + 1)..].Trim().TrimEnd('#').TrimEnd()), Heading: heading)); continue; }
            if (line.Length >= 3 && line[0] is '-' or '_' or '*' && line.All(character => character == line[0] || char.IsWhiteSpace(character)))
            { Flush(); blocks.Add(new([], Separator: true)); continue; }
            var indent = Math.Min(6, (raw.Length - raw.TrimStart().Length) / 2);
            if (line.Length > 2 && line[0] is '-' or '*' or '+' or '•' && char.IsWhiteSpace(line[1]))
            { Flush(); blocks.Add(new(ParseInline(line[2..].Trim()), Marker: "•", Indent: indent)); continue; }
            var digits = 0; while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
            if (digits is > 0 and <= 6 && digits + 1 < line.Length && line[digits] is '.' or ')' && char.IsWhiteSpace(line[digits + 1]))
            { Flush(); blocks.Add(new(ParseInline(line[(digits + 2)..].Trim()), Marker: line[..(digits + 1)], Indent: indent)); continue; }
            if (line.StartsWith("> ", StringComparison.Ordinal))
            { Flush(); blocks.Add(new(ParseInline(line[2..]), Quote: true)); continue; }
            if (paragraph.Length > 0) paragraph.Append(' ');
            paragraph.Append(line);
        }
        Flush();
        if (code.Length > 0) blocks.Add(new([new(code.ToString().TrimEnd(), Code: true)], Code: true));
        return new(blocks);
    }

    private static List<MarketplaceTextRun> ParseInline(string text, bool bold = false, bool italic = false, bool code = false, Uri? link = null, int depth = 0)
    {
        if (depth > 12) return [new(text, bold, italic, code, link)];
        var runs = new List<MarketplaceTextRun>(); var plain = new StringBuilder();
        void Flush()
        {
            if (plain.Length == 0) return;
            runs.Add(new(plain.ToString(), bold, italic, code, link)); plain.Clear();
        }
        for (var index = 0; index < text.Length;)
        {
            if (text[index] == '\\' && index + 1 < text.Length && "\\`*_{}[]()#+-.!>".Contains(text[index + 1], StringComparison.Ordinal))
            { plain.Append(text[index + 1]); index += 2; continue; }
            var image = text[index] == '!' && index + 1 < text.Length && text[index + 1] == '[';
            var labelStart = image ? index + 1 : index;
            if (text[labelStart] == '[' && text.IndexOf("](", labelStart + 1, StringComparison.Ordinal) is var labelEnd && labelEnd > labelStart)
            {
                var urlEnd = ClosingParenthesis(text, labelEnd + 2);
                if (urlEnd >= 0)
                {
                    Flush(); var destination = text[(labelEnd + 2)..urlEnd].Trim();
                    var title = destination.IndexOf(" \"", StringComparison.Ordinal);
                    if (title >= 0) destination = destination[..title];
                    var target = image ? null : Link(destination.Trim('<', '>'));
                    runs.AddRange(ParseInline(text[(labelStart + 1)..labelEnd], bold, italic, code, target, depth + 1));
                    index = urlEnd + 1; continue;
                }
            }
            if (text[index] == '`' && text.IndexOf('`', index + 1) is var codeEnd && codeEnd > index + 1)
            { Flush(); runs.Add(new(text[(index + 1)..codeEnd], bold, italic, Code: true, Link: link)); index = codeEnd + 1; continue; }
            if (text[index] == '<' && text.IndexOf('>', index + 1) is var autoEnd && autoEnd > index && Link(text[(index + 1)..autoEnd]) is { } autoTarget)
            { Flush(); runs.Add(new(text[(index + 1)..autoEnd], bold, italic, code, autoTarget)); index = autoEnd + 1; continue; }
            var marker = text.AsSpan(index).StartsWith("***", StringComparison.Ordinal) ? "***" :
                text.AsSpan(index).StartsWith("**", StringComparison.Ordinal) ? "**" :
                text.AsSpan(index).StartsWith("__", StringComparison.Ordinal) ? "__" : text[index] is '*' or '_' ? text[index].ToString() : "";
            if (marker.Length > 0 && index + marker.Length < text.Length && !char.IsWhiteSpace(text[index + marker.Length]) &&
                !(text[index] == '_' && index > 0 && char.IsLetterOrDigit(text[index - 1])))
            {
                var end = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal);
                if (end > index + marker.Length && !char.IsWhiteSpace(text[end - 1]))
                {
                    Flush(); runs.AddRange(ParseInline(text[(index + marker.Length)..end], bold || marker.Length >= 2,
                        italic || marker.Length == 1 || marker.Length == 3, code, link, depth + 1));
                    index = end + marker.Length; continue;
                }
            }
            if (link is null && (text.AsSpan(index).StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.AsSpan(index).StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            {
                var end = index;
                while (end < text.Length && !char.IsWhiteSpace(text[end]) && text[end] is not ('<' or '>' or '"')) end++;
                while (end > index && ".,;:!?]".Contains(text[end - 1], StringComparison.Ordinal)) end--;
                while (end > index && text[end - 1] == ')' && text[index..end].Count(character => character == ')') > text[index..end].Count(character => character == '(')) end--;
                if (Link(text[index..end]) is { } target)
                { Flush(); runs.Add(new(text[index..end], bold, italic, code, target)); index = end; continue; }
            }
            plain.Append(text[index++]);
        }
        Flush(); return runs;
    }
    private static int ClosingParenthesis(string text, int start)
    {
        var depth = 0;
        for (var index = start; index < text.Length; index++)
        {
            if (text[index] == '\\') { index++; continue; }
            if (text[index] == '(') depth++;
            if (text[index] == ')' && depth-- == 0) return index;
        }
        return -1;
    }
    private static Uri? Link(string value) => MarketplaceRichText.SafeLink(value) is { } safe ? new Uri(safe) : null;
}
