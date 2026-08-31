using CyEngineHub.Git.Models;

namespace CyEngineHub.Git.Parsing;

public static class GitRemoteParser
{
    private const string FetchSuffix = " (fetch)";

    public static IReadOnlyList<GitRemote> Parse(IEnumerable<string> lines)
    {
        var remotes = new Dictionary<string, GitRemote>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (!line.EndsWith(FetchSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            var content = line[..^FetchSuffix.Length];
            var separator = content.IndexOfAny(['\t', ' ']);
            if (separator <= 0)
            {
                continue;
            }

            var name = content[..separator].Trim();
            var url = content[(separator + 1)..].Trim();
            if (name.Length == 0 || url.Length == 0)
            {
                continue;
            }

            remotes[name] = new GitRemote(name, SanitizeUrl(url));
        }

        return remotes.Values.OrderBy(remote => remote.Name, StringComparer.Ordinal).ToArray();
    }

    public static string SanitizeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (string.IsNullOrEmpty(uri.UserInfo) &&
             string.IsNullOrEmpty(uri.Query) &&
             string.IsNullOrEmpty(uri.Fragment)))
        {
            return value;
        }

        var builder = new UriBuilder(uri)
        {
            UserName = string.Empty,
            Password = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.AbsoluteUri;
    }
}
