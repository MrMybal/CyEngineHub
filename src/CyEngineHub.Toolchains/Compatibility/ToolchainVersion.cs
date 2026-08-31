using System.Globalization;

namespace CyEngineHub.Toolchains.Compatibility;

public static class ToolchainVersion
{
    public static int Compare(string? left, string? right)
    {
        var leftParts = ExtractNumericParts(left);
        var rightParts = ExtractNumericParts(right);
        var length = Math.Max(leftParts.Count, rightParts.Count);
        for (var index = 0; index < length; index++)
        {
            var leftValue = index < leftParts.Count ? leftParts[index] : 0;
            var rightValue = index < rightParts.Count ? rightParts[index] : 0;
            var comparison = leftValue.CompareTo(rightValue);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    public static bool IsInRange(string version, string range)
    {
        var separator = range.IndexOf('-', StringComparison.Ordinal);
        if (separator <= 0 || separator >= range.Length - 1)
        {
            return Compare(version, range) == 0;
        }

        var minimum = range[..separator];
        var maximum = range[(separator + 1)..];
        return Compare(version, minimum) >= 0 && Compare(version, maximum) <= 0;
    }

    private static List<long> ExtractNumericParts(string? value)
    {
        var parts = new List<long>();
        if (string.IsNullOrWhiteSpace(value))
        {
            return parts;
        }

        var start = -1;
        for (var index = 0; index <= value.Length; index++)
        {
            if (index < value.Length && char.IsAsciiDigit(value[index]))
            {
                start = start < 0 ? index : start;
                continue;
            }

            if (start < 0)
            {
                continue;
            }

            var segment = value.AsSpan(start, index - start);
            parts.Add(long.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                ? number
                : long.MaxValue);
            start = -1;
        }

        return parts;
    }
}
