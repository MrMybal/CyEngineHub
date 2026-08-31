using System.Globalization;
using CyEngineHub.Git.Models;

namespace CyEngineHub.Git.Parsing;

public sealed class GitStatusParser
{
    private string _branch = "(inconnue)";
    private string _headCommit = string.Empty;
    private string? _upstream;
    private int _ahead;
    private int _behind;
    private int _staged;
    private int _modified;
    private int _untracked;
    private int _conflicted;

    public void Accept(string line)
    {
        if (line.StartsWith("# branch.oid ", StringComparison.Ordinal))
        {
            _headCommit = line[13..].Trim();
            return;
        }

        if (line.StartsWith("# branch.head ", StringComparison.Ordinal))
        {
            _branch = line[14..].Trim();
            return;
        }

        if (line.StartsWith("# branch.upstream ", StringComparison.Ordinal))
        {
            _upstream = line[18..].Trim();
            return;
        }

        if (line.StartsWith("# branch.ab ", StringComparison.Ordinal))
        {
            ParseAheadBehind(line[12..]);
            return;
        }

        if (line.StartsWith("? ", StringComparison.Ordinal))
        {
            _untracked++;
            return;
        }

        if (line.StartsWith("u ", StringComparison.Ordinal))
        {
            _conflicted++;
            return;
        }

        if ((line.StartsWith("1 ", StringComparison.Ordinal) ||
             line.StartsWith("2 ", StringComparison.Ordinal)) &&
            line.Length >= 4)
        {
            var indexStatus = line[2];
            var workTreeStatus = line[3];
            if (indexStatus != '.')
            {
                _staged++;
            }

            if (workTreeStatus != '.')
            {
                _modified++;
            }

            if (indexStatus == 'U' || workTreeStatus == 'U')
            {
                _conflicted++;
            }
        }
    }

    public GitParsedStatus Build() => new(
        _branch,
        _headCommit,
        _upstream,
        _ahead,
        _behind,
        new GitWorkingTreeSummary(_staged, _modified, _untracked, _conflicted));

    private void ParseAheadBehind(string value)
    {
        foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length < 2 ||
                !int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                continue;
            }

            if (token[0] == '+')
            {
                _ahead = count;
            }
            else if (token[0] == '-')
            {
                _behind = count;
            }
        }
    }
}

public sealed record GitParsedStatus(
    string Branch,
    string HeadCommit,
    string? Upstream,
    int Ahead,
    int Behind,
    GitWorkingTreeSummary WorkingTree);
