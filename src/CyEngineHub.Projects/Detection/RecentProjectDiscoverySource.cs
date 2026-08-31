using System.Text.RegularExpressions;

namespace CyEngineHub.Projects.Detection;

public sealed partial class RecentProjectDiscoverySource : IProjectDiscoverySource
{
    private readonly string _unrealLocalDataRoot;

    public RecentProjectDiscoverySource(string? localApplicationData = null)
    {
        var localData = localApplicationData
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _unrealLocalDataRoot = Path.Combine(localData, "UnrealEngine");
    }

    public string Name => "Unreal recent projects";

    public async Task<IReadOnlyList<string>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_unrealLocalDataRoot))
        {
            return [];
        }

        var projects = new HashSet<string>(
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var versionDirectory in Directory.EnumerateDirectories(_unrealLocalDataRoot))
        {
            foreach (var relativePath in CandidateSettingsPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settingsPath = Path.Combine(versionDirectory, relativePath);
                if (!File.Exists(settingsPath))
                {
                    continue;
                }

                string content;
                try
                {
                    content = await File.ReadAllTextAsync(settingsPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                foreach (Match match in ProjectPathPattern().Matches(content))
                {
                    var path = match.Groups["path"].Value.Replace('/', Path.DirectorySeparatorChar);
                    if (File.Exists(path))
                    {
                        projects.Add(Path.GetFullPath(path));
                    }
                }
            }
        }

        return projects.ToArray();
    }

    private static readonly string[] CandidateSettingsPaths =
    [
        Path.Combine("Saved", "Config", "WindowsEditor", "EditorSettings.ini"),
        Path.Combine("Saved", "Config", "WindowsEditor", "EditorPerProjectUserSettings.ini"),
        Path.Combine("Saved", "Config", "Windows", "EditorSettings.ini"),
    ];

    [GeneratedRegex(
        @"(?<path>[A-Za-z]:[\\/][^\r\n""]*?\.uproject)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        1_000)]
    private static partial Regex ProjectPathPattern();
}

