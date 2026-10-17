using System.Globalization;
using System.Text.RegularExpressions;

namespace CyEngineHub.Epic.Acquisition;

public sealed record EpicEngineBuild(string BuildVersion, Version? Version, long? Changelist)
{
    public static EpicEngineBuild Parse(string build)
    {
        var match = Regex.Match(build, @"(?:^|[^0-9])(\d+)\.(\d+)\.(\d+)(?:-(\d+))?", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success || !System.Version.TryParse($"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}", out var version))
            return new(build, null, null);
        var changelist = long.TryParse(match.Groups[4].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : (long?)null;
        return new(build, version, changelist);
    }
    public bool? IsNewerThan(Version installed, long? installedChangelist)
    {
        if (Version is null || installed.Major != Version.Major || installed.Minor != Version.Minor) return null;
        if (Version.Build != installed.Build) return Version.Build > installed.Build;
        if (Changelist is { } remote && installedChangelist is { } local) return remote > local;
        if (Changelist.HasValue != installedChangelist.HasValue) return null;
        return false;
    }
}
