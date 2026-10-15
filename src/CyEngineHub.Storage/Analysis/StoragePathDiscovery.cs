using System.Text.RegularExpressions;
using CyEngineHub.Storage.Models;

namespace CyEngineHub.Storage.Analysis;

internal sealed record StoragePathCandidate(
    StorageCategory Category,
    StorageEntryRole Role,
    string DisplayName,
    string OwnerName,
    string OwnerRoot,
    string Path,
    bool IsSafeToClean);

internal static partial class StoragePathDiscovery
{
    public static IReadOnlyList<StoragePathCandidate> Discover(StorageAnalysisRequest request)
    {
        var candidates = new List<StoragePathCandidate>();
        foreach (var engine in request.Engines)
        {
            Add(candidates, new StoragePathCandidate(
                StorageCategory.EngineInstallation,
                StorageEntryRole.Footprint,
                "Installation moteur",
                engine.DisplayName,
                engine.RootPath,
                engine.RootPath,
                false));
            Add(candidates, new StoragePathCandidate(
                StorageCategory.EngineDerivedDataCache,
                StorageEntryRole.Cache,
                "Derived Data Cache moteur",
                engine.DisplayName,
                engine.RootPath,
                Path.Combine(engine.RootPath, "Engine", "DerivedDataCache"),
                true));
        }

        foreach (var project in request.Projects)
        {
            Add(candidates, new StoragePathCandidate(
                StorageCategory.Project,
                StorageEntryRole.Footprint,
                "Projet complet",
                project.Name,
                project.RootPath,
                project.RootPath,
                false));
            AddProjectGenerated(candidates, project.Name, project.RootPath);
        }

        foreach (var root in request.AdditionalRoots)
        {
            if (string.IsNullOrWhiteSpace(root.Path) || !Path.IsPathFullyQualified(root.Path)) continue;
            var role = root.Category switch
            {
                StorageCategory.EngineInstallation or StorageCategory.Project or StorageCategory.MarketplaceLibrary => StorageEntryRole.Footprint,
                StorageCategory.MarketplaceCache => StorageEntryRole.Cache,
                _ => StorageEntryRole.External,
            };
            Add(candidates, new StoragePathCandidate(root.Category, role, root.Owner,
                root.Owner, root.Path, root.Path, false));
        }

        AddGlobalDdc(candidates);
        AddVaultCache(candidates);
        return candidates;
    }

    private static void AddProjectGenerated(
        List<StoragePathCandidate> candidates,
        string projectName,
        string projectRoot)
    {
        Add(candidates, ProjectCandidate(
            StorageCategory.ProjectIntermediate,
            "Intermediate",
            projectName,
            projectRoot,
            "Intermediate"));
        Add(candidates, ProjectCandidate(
            StorageCategory.ProjectBinaries,
            "Binaries",
            projectName,
            projectRoot,
            "Binaries"));
        Add(candidates, ProjectCandidate(
            StorageCategory.ProjectDerivedDataCache,
            "Derived Data Cache projet",
            projectName,
            projectRoot,
            "DerivedDataCache"));
        Add(candidates, ProjectCandidate(
            StorageCategory.ProjectLogs,
            "Logs",
            projectName,
            projectRoot,
            "Saved",
            "Logs"));
        Add(candidates, ProjectCandidate(
            StorageCategory.ProjectCooked,
            "Cooked",
            projectName,
            projectRoot,
            "Saved",
            "Cooked"));
        Add(candidates, ProjectCandidate(
            StorageCategory.ProjectStagedBuilds,
            "Staged Builds",
            projectName,
            projectRoot,
            "Saved",
            "StagedBuilds"));
    }

    private static StoragePathCandidate ProjectCandidate(
        StorageCategory category,
        string displayName,
        string projectName,
        string projectRoot,
        params string[] segments) => new(
        category,
        StorageEntryRole.Generated,
        displayName,
        projectName,
        projectRoot,
        Combine(projectRoot, segments),
        true);

    private static void AddGlobalDdc(List<StoragePathCandidate> candidates)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Add(candidates, CacheCandidate(
            StorageCategory.GlobalDerivedDataCache,
            "DDC global Unreal",
            Path.Combine(localAppData, "UnrealEngine", "Common", "DerivedDataCache")));
        Add(candidates, CacheCandidate(
            StorageCategory.GlobalDerivedDataCache,
            "Zen local data",
            Path.Combine(localAppData, "UnrealEngine", "Common", "Zen", "Data")));

        AddEnvironmentCache(candidates, "UE-LocalDataCachePath", StorageCategory.GlobalDerivedDataCache);
        AddEnvironmentCache(candidates, "UE_DDC_PATH", StorageCategory.GlobalDerivedDataCache);
        AddEnvironmentCache(candidates, "UE-SharedDataCachePath", StorageCategory.SharedDerivedDataCache);
        AddEnvironmentCache(candidates, "UE_SharedDataCachePath", StorageCategory.SharedDerivedDataCache);
    }

    private static StoragePathCandidate CacheCandidate(
        StorageCategory category,
        string displayName,
        string path) => new(
        category,
        StorageEntryRole.Cache,
        displayName,
        "Unreal Engine",
        path,
        path,
        false);

    private static void AddEnvironmentCache(
        List<StoragePathCandidate> candidates,
        string variable,
        StorageCategory category)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value) ||
            string.Equals(value, "None", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var expanded = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(expanded))
        {
            return;
        }

        Add(candidates, new StoragePathCandidate(
            category,
            StorageEntryRole.Cache,
            variable,
            "Variable d’environnement",
            expanded,
            expanded,
            false));
    }

    private static void AddVaultCache(List<StoragePathCandidate> candidates)
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Add(candidates, new StoragePathCandidate(
            StorageCategory.VaultCache,
            StorageEntryRole.External,
            "Epic Vault Cache",
            "Epic Games Launcher",
            Path.Combine(programData, "Epic", "EpicGamesLauncher", "VaultCache"),
            Path.Combine(programData, "Epic", "EpicGamesLauncher", "VaultCache"),
            false));

        var configPaths = new[]
        {
            Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Config", "Windows", "GameUserSettings.ini"),
            Path.Combine(localAppData, "EpicGamesLauncher", "Saved", "Config", "WindowsEditor", "GameUserSettings.ini"),
        };
        foreach (var configPath in configPaths.Where(File.Exists))
        {
            try
            {
                foreach (var line in File.ReadLines(configPath).Take(10_000))
                {
                    if (!line.Contains("VaultCache", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    foreach (Match match in QuotedPathRegex().Matches(line))
                    {
                        var path = match.Groups["path"].Value.Replace("\\\\", "\\", StringComparison.Ordinal);
                        if (Path.IsPathFullyQualified(path))
                        {
                            Add(candidates, new StoragePathCandidate(
                                StorageCategory.VaultCache,
                                StorageEntryRole.External,
                                "Epic Vault Cache configuré",
                                "Epic Games Launcher",
                                path,
                                path,
                                false));
                        }
                    }
                }
            }
            catch (IOException)
            {
                // The standard path remains available even if Launcher config is locked.
            }
        }
    }

    private static void Add(
        List<StoragePathCandidate> candidates,
        StoragePathCandidate candidate)
    {
        if (!Directory.Exists(candidate.Path))
        {
            return;
        }

        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate.Path));
        if (candidates.Any(existing =>
                existing.Category == candidate.Category &&
                string.Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(existing.Path)),
                    normalized,
                    StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add(candidate with { Path = normalized });
    }

    private static string Combine(string root, IReadOnlyList<string> segments)
    {
        var path = root;
        foreach (var segment in segments)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }

    [GeneratedRegex("(?:Path\\s*=\\s*)?\"(?<path>[A-Za-z]:\\\\[^\"]+)\"", RegexOptions.IgnoreCase, 1000)]
    private static partial Regex QuotedPathRegex();
}
