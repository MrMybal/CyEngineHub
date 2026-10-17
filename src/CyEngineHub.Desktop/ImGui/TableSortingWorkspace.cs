using System.Globalization;
using System.Text.RegularExpressions;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed partial class ImGuiWorkspace
{
    private readonly Dictionary<string, (int Column, bool Descending)> _tableSorts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TableSortCache> _tableSortCache = new(StringComparer.Ordinal);
    private sealed record TableSortCache(object[] Items, object?[] Keys, int Column, bool Descending, object[] Sorted);

    private (int Column, bool Descending) SortableHeaders(string id, string[] labels, int unsortable = -1)
    {
        var sort = _tableSorts.GetValueOrDefault(id, (-1, false));
        Gui.TableNextRow(ImGuiTableRowFlags.Headers);
        for (var column = 0; column < labels.Length; column++)
        {
            Gui.TableSetColumnIndex(column);
            var arrow = sort.Item1 == column ? sort.Item2 ? " ↓" : " ↑" : "";
            Gui.TableHeader(labels[column] + arrow + "###header-" + column);
            Record("table-header/" + id + "/" + column);
            if (column == unsortable) continue;
            if (Gui.IsItemHovered()) Gui.SetTooltip("Cliquer pour trier · cliquer à nouveau pour inverser");
            if (Gui.IsItemClicked())
            {
                sort = (column, sort.Item1 == column && !sort.Item2);
                _tableSorts[id] = sort;
            }
        }
        return sort;
    }
    private T[] SortTableItems<T>(string id, T[] items, Func<T, int, object?> key)
    {
        if (!_tableSorts.TryGetValue(id, out var sort)) return items;
        var values = items.Select(item => key(item, sort.Column)).ToArray();
        var objects = items.Cast<object>().ToArray();
        if (_tableSortCache.TryGetValue(id, out var cache) && cache.Column == sort.Column && cache.Descending == sort.Descending &&
            cache.Items.SequenceEqual(objects) && cache.Keys.SequenceEqual(values)) return cache.Sorted.Cast<T>().ToArray();
        var indices = Enumerable.Range(0, items.Length).ToArray();
        Array.Sort(indices, (a, b) =>
        {
            var left = values[a]; var right = values[b];
            // Unknown measurements stay last in either direction, never masquerading as zero.
            if (left is null || right is null) return left is null ? right is null ? a.CompareTo(b) : 1 : -1;
            var comparison = left is string text && right is string other ? CompareNatural(text, other) :
                left is IComparable comparable ? comparable.CompareTo(right) : CompareNatural(left.ToString() ?? "", right.ToString() ?? "");
            return comparison == 0 ? a.CompareTo(b) : sort.Descending ? -Math.Sign(comparison) : Math.Sign(comparison);
        });
        var sorted = indices.Select(index => items[index]).ToArray();
        _tableSortCache[id] = new(objects, values, sort.Column, sort.Descending, sorted.Cast<object>().ToArray());
        return sorted;
    }
    private static int CompareNatural(string left, string right)
    {
        var a = 0; var b = 0;
        while (a < left.Length && b < right.Length)
        {
            if (char.IsAsciiDigit(left[a]) && char.IsAsciiDigit(right[b]))
            {
                var startA = a; var startB = b;
                while (a < left.Length && char.IsAsciiDigit(left[a])) a++;
                while (b < right.Length && char.IsAsciiDigit(right[b])) b++;
                while (startA < a - 1 && left[startA] == '0') startA++;
                while (startB < b - 1 && right[startB] == '0') startB++;
                var lengths = (a - startA).CompareTo(b - startB);
                if (lengths != 0) return lengths;
                var digits = string.Compare(left, startA, right, startB, a - startA, StringComparison.Ordinal);
                if (digits != 0) return digits;
            }
            else
            {
                var chars = char.ToUpperInvariant(left[a++]).CompareTo(char.ToUpperInvariant(right[b++]));
                if (chars != 0) return chars;
            }
        }
        return (left.Length - a).CompareTo(right.Length - b);
    }
    private static double? SizeSortValue(string label)
    {
        var match = Regex.Match(label, @"([\d\s\u00a0\u202f]+(?:[.,]\d+)?)\s*([KMGT]?)(?:i?[Bo]|octets?)\b", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
        if (!match.Success || !double.TryParse(Regex.Replace(match.Groups[1].Value, @"\s", "").Replace(',', '.'),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var size)) return null;
        var exponent = "KMGT".IndexOf(match.Groups[2].Value.ToUpperInvariant(), StringComparison.Ordinal) + 1;
        return size * Math.Pow(1024, match.Groups[2].Length == 0 ? 0 : exponent);
    }
}
