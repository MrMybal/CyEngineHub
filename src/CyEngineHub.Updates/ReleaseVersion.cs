using System.Globalization;

namespace CyEngineHub.Updates;

// Preserve prerelease precedence: alpha.10 > alpha.2, stable > alpha, ignore build metadata.
internal sealed record ReleaseVersion(Version Core, string[] Prerelease)
{
    public bool IsPrerelease => Prerelease.Length != 0;

    public static ReleaseVersion Parse(string tag)
    {
        var value = tag.Trim();
        if (value.StartsWith('v') || value.StartsWith('V')) value = value[1..];
        var metadata = value.IndexOf('+');
        if (metadata >= 0) value = value[..metadata];
        var separator = value.IndexOf('-');
        var core = separator < 0 ? value : value[..separator];
        var prerelease = separator < 0 ? [] : value[(separator + 1)..].Split('.');
        var parts = core.Split('.');
        if (parts.Length is < 2 or > 4 || parts.Any(part =>
                !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)) ||
            prerelease.Any(part => part.Length == 0 ||
                part.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')) ||
            !Version.TryParse(core, out var version))
        {
            throw new ApplicationUpdateException($"Version non reconnue : « {tag} ».");
        }

        return new ReleaseVersion(new Version(version.Major, version.Minor,
            Math.Max(0, version.Build), Math.Max(0, version.Revision)), prerelease);
    }

    public int CompareTo(ReleaseVersion other)
    {
        var result = Core.CompareTo(other.Core);
        if (result != 0) return result;
        if (!IsPrerelease) return other.IsPrerelease ? 1 : 0;
        if (!other.IsPrerelease) return -1;
        for (var index = 0; index < Math.Min(Prerelease.Length, other.Prerelease.Length); index++)
        {
            var left = Prerelease[index];
            var right = other.Prerelease[index];
            var leftNumeric = left.All(char.IsAsciiDigit);
            var rightNumeric = right.All(char.IsAsciiDigit);
            if (leftNumeric && rightNumeric)
            {
                left = left.TrimStart('0');
                right = right.TrimStart('0');
                result = left.Length.CompareTo(right.Length);
                if (result == 0) result = string.CompareOrdinal(left, right);
            }
            else result = leftNumeric != rightNumeric ? (leftNumeric ? -1 : 1) : string.CompareOrdinal(left, right);
            if (result != 0) return result;
        }

        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }
}
