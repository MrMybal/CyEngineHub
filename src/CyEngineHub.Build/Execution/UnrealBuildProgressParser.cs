using System.Globalization;
using System.Text.RegularExpressions;

namespace CyEngineHub.Build.Execution;

public static partial class UnrealBuildProgressParser
{
    public static double? Parse(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var percent = ProgressPercent().Match(line);
        if (percent.Success && double.TryParse(percent.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value is >= 0 and <= 100)
            return value / 100;
        var actions = ActionCount().Match(line);
        if (actions.Success && long.TryParse(actions.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var done) &&
            long.TryParse(actions.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var total) && total > 0 && done <= total)
            return (double)done / total;
        return null;
    }

    [GeneratedRegex(@"@progress\s+.*?\s(\d+(?:\.\d+)?)%", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ProgressPercent();
    [GeneratedRegex(@"^\s*\[(\d+)/(\d+)\]\s", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ActionCount();
}
