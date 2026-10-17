using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Unreal.Detection;

public sealed class EngineInspector : IEngineInspector
{
    private const int MaximumParentTraversal = 8;

    public async Task<UnrealEngineInstallation?> InspectAsync(
        EngineCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        var rootPath = FindEngineRoot(candidate.Path);
        if (rootPath is null)
        {
            return null;
        }

        var buildVersionPath = Path.Combine(rootPath, "Engine", "Build", "Build.version");
        var build = await ReadBuildVersionAsync(buildVersionPath, cancellationToken).ConfigureAwait(false);
        var editorPath = FindEditor(rootPath);
        var issues = InspectHealth(rootPath, editorPath, build is not null);
        var health = editorPath is null || build is null
            ? EngineHealth.Broken
            : issues.Count > 0
                ? EngineHealth.Warning
                : EngineHealth.Healthy;

        var version = build is null
            ? EngineVersion.Unknown
            : new EngineVersion(build.MajorVersion, build.MinorVersion, build.PatchVersion);
        var type = DetectType(rootPath, candidate.Source);
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var associationId = string.IsNullOrWhiteSpace(candidate.Identifier)
            ? version.ToString()
            : candidate.Identifier;
        var displayName = string.IsNullOrWhiteSpace(candidate.DisplayName)
            ? version.IsKnown ? $"Unreal Engine {version.Major}.{version.Minor}" : Path.GetFileName(normalizedRoot)
            : candidate.DisplayName;

        return new UnrealEngineInstallation(
            CreateStableId(normalizedRoot),
            associationId,
            displayName,
            normalizedRoot,
            editorPath ?? ExpectedEditorPath(normalizedRoot),
            version,
            type,
            build?.BranchName,
            build?.Changelist,
            build?.BuildId,
            candidate.Source,
            health,
            issues,
            DateTimeOffset.UtcNow);
    }

    private static string? FindEngineRoot(string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return null;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(candidatePath.Trim().Trim('"'));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return null;
        }

        var current = File.Exists(fullPath) ? Path.GetDirectoryName(fullPath) : fullPath;
        for (var index = 0; index < MaximumParentTraversal && current is not null; index++)
        {
            if (File.Exists(Path.Combine(current, "Engine", "Build", "Build.version")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        return null;
    }

    private static async Task<BuildVersionDocument?> ReadBuildVersionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<BuildVersionDocument>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
    }

    private static string? FindEditor(string rootPath)
    {
        var candidates = new[]
        {
            Path.Combine(rootPath, "Engine", "Binaries", "Win64", "UnrealEditor.exe"),
            Path.Combine(rootPath, "Engine", "Binaries", "Win64", "UE4Editor.exe"),
            Path.Combine(rootPath, "Engine", "Binaries", "Linux", "UnrealEditor"),
            Path.Combine(rootPath, "Engine", "Binaries", "Mac", "UnrealEditor.app"),
        };

        return candidates.FirstOrDefault(path => File.Exists(path) || Directory.Exists(path));
    }

    private static string ExpectedEditorPath(string rootPath) =>
        Path.Combine(rootPath, "Engine", "Binaries", "Win64", "UnrealEditor.exe");

    private static List<string> InspectHealth(string rootPath, string? editorPath, bool hasBuildVersion)
    {
        var issues = new List<string>();
        if (!hasBuildVersion)
        {
            issues.Add("Engine/Build/Build.version is missing or invalid.");
        }

        if (editorPath is null)
        {
            issues.Add("UnrealEditor executable is missing.");
        }

        var ubtDll = Path.Combine(
            rootPath,
            "Engine",
            "Binaries",
            "DotNET",
            "UnrealBuildTool",
            "UnrealBuildTool.dll");
        var ubtExe = Path.Combine(
            rootPath,
            "Engine",
            "Binaries",
            "DotNET",
            "UnrealBuildTool.exe");
        if (!File.Exists(ubtDll) && !File.Exists(ubtExe))
        {
            issues.Add("UnrealBuildTool is missing.");
        }

        var runUat = Path.Combine(rootPath, "Engine", "Build", "BatchFiles", "RunUAT.bat");
        if (!File.Exists(runUat))
        {
            issues.Add("AutomationTool launcher is missing.");
        }

        return issues;
    }

    private static EngineInstallationType DetectType(string rootPath, string discoverySource)
    {
        var isInstalled = File.Exists(Path.Combine(rootPath, "Engine", "Build", "InstalledBuild.txt"));
        if (isInstalled)
        {
            return discoverySource.StartsWith("Epic", StringComparison.OrdinalIgnoreCase)
                ? EngineInstallationType.EpicBinary
                : EngineInstallationType.InstalledBuild;
        }

        var hasSource = Directory.Exists(Path.Combine(rootPath, "Engine", "Source"));
        if (hasSource)
        {
            return Directory.Exists(Path.Combine(rootPath, ".git"))
                ? EngineInstallationType.SourceFork
                : EngineInstallationType.Source;
        }

        return EngineInstallationType.Custom;
    }

    private static string CreateStableId(string rootPath)
    {
        var normalized = OperatingSystem.IsWindows() ? rootPath.ToUpperInvariant() : rootPath;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(hash)[..16];
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private sealed record BuildVersionDocument(
        int MajorVersion,
        int MinorVersion,
        int PatchVersion,
        long Changelist,
        string? BranchName,
        string? BuildId);
}

