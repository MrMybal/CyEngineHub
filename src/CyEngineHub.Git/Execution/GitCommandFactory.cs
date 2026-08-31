using System.Text;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Git.Execution;

public static class GitCommandFactory
{
    public static ProcessRequest CreateRead(string repositoryPath, params string[] arguments) =>
        Create(repositoryPath, arguments, maxCapturedLines: 500);

    public static ProcessRequest CreateFetch(string repositoryRoot, string remoteName)
    {
        ValidateRemoteName(remoteName);
        return Create(
            repositoryRoot,
            ["fetch", "--prune", "--progress", remoteName],
            maxCapturedLines: 5_000);
    }

    public static string FormatPreview(ProcessRequest request)
    {
        var builder = new StringBuilder(QuoteForDisplay(request.FileName));
        foreach (var argument in request.Arguments)
        {
            builder.Append(' ');
            builder.Append(QuoteForDisplay(argument));
        }

        return builder.ToString();
    }

    private static ProcessRequest Create(
        string repositoryPath,
        IReadOnlyList<string> arguments,
        int maxCapturedLines)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);
        return new ProcessRequest
        {
            FileName = "git",
            Arguments = [
                "-C",
                Path.GetFullPath(repositoryPath),
                "-c",
                "color.ui=false",
                "-c",
                "core.quotepath=false",
                .. arguments,
            ],
            WorkingDirectory = Path.GetFullPath(repositoryPath),
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["GIT_OPTIONAL_LOCKS"] = "0",
                ["GIT_TERMINAL_PROMPT"] = "0",
                ["LC_ALL"] = "C",
            },
            MaxCapturedLines = maxCapturedLines,
        };
    }

    private static void ValidateRemoteName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 200 ||
            value[0] == '-' ||
            value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character)))
        {
            throw new ArgumentException("Nom de remote Git invalide.", nameof(value));
        }
    }

    private static string QuoteForDisplay(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"" : value;
}
