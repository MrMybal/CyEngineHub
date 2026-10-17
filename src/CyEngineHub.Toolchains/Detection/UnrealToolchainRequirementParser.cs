using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyEngineHub.Toolchains.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Toolchains.Detection;

public interface IUnrealToolchainRequirementParser
{
    Task<UnrealEngineToolchainRequirements> ParseAsync(
        UnrealEngineInstallation engine,
        CancellationToken cancellationToken = default);
}

public sealed partial class UnrealToolchainRequirementParser : IUnrealToolchainRequirementParser
{
    private const int MaximumMetadataLength = 2 * 1024 * 1024;

    public async Task<UnrealEngineToolchainRequirements> ParseAsync(
        UnrealEngineInstallation engine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var diagnostics = new List<string>();
        var windows = await ParseWindowsAsync(engine.RootPath, diagnostics, cancellationToken)
            .ConfigureAwait(false);
        var android = await ParseAndroidAsync(engine.RootPath, diagnostics, cancellationToken)
            .ConfigureAwait(false);
        var linux = await ParseLinuxAsync(engine.RootPath, diagnostics, cancellationToken)
            .ConfigureAwait(false);
        return new UnrealEngineToolchainRequirements(
            engine.Id,
            engine.DisplayName,
            engine.Version.ToString(),
            engine.RootPath,
            windows,
            android,
            linux,
            diagnostics);
    }

    private static async Task<WindowsToolchainRequirement?> ParseWindowsAsync(
        string engineRoot,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(engineRoot, "Engine", "Config", "Windows", "Windows_SDK.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                using var document = await ReadJsonAsync(jsonPath, cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                return new WindowsToolchainRequirement(
                    GetString(root, "MainVersion"),
                    GetString(root, "MinSoftwareVersion") ?? GetString(root, "MinVersion"),
                    GetString(root, "MaxVersion"),
                    GetString(root, "MinimumVisualCppVersion"),
                    GetStringArray(root, "PreferredVisualCppVersions"),
                    GetStringArray(root, "BannedVisualCppVersions"),
                    GetString(root, "MinimumVisualStudio2022Version"),
                    jsonPath);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                diagnostics.Add($"Windows SDK : {exception.Message}");
            }
        }

        var sourcePath = Path.Combine(
            engineRoot,
            "Engine",
            "Source",
            "Programs",
            "UnrealBuildTool",
            "Platform",
            "Windows",
            "MicrosoftPlatformSDK.Versions.cs");
        if (!File.Exists(sourcePath))
        {
            diagnostics.Add("Contraintes Windows SDK introuvables pour ce moteur.");
            return null;
        }

        try
        {
            var text = await ReadBoundedTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            var preferredSection = Slice(text, "PreferredVisualCppVersions", "MinimumRequiredClangVersion");
            var bannedSection = Slice(text, "BannedVisualCppVersions", "MinimumVisualCppVersion");
            var mainSdkSection = Slice(text, "PreferredWindowsSdkVersions", "MinimumWindowsSDKVersion");
            return new WindowsToolchainRequirement(
                MatchFirst(mainSdkSection, "VersionNumber\\.Parse\\(\"([^\"]+)\"\\)"),
                MatchVersionConstructor(text, "MinimumWindowsSDKVersion"),
                MatchVersionConstructor(text, "MaximumWindowsSDKVersion"),
                MatchVersionConstructor(text, "MinimumVisualCppVersion"),
                MatchRanges(preferredSection),
                MatchRanges(bannedSection),
                null,
                sourcePath);
        }
        catch (IOException exception)
        {
            diagnostics.Add($"Windows SDK : {exception.Message}");
            return null;
        }
    }

    private static async Task<AndroidToolchainRequirement?> ParseAndroidAsync(
        string engineRoot,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(engineRoot, "Engine", "Config", "Android", "Android_SDK.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                using var document = await ReadJsonAsync(jsonPath, cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                return new AndroidToolchainRequirement(
                    GetString(root, "MainVersion"),
                    GetString(root, "MinVersion"),
                    GetString(root, "MaxVersion"),
                    GetString(root, "platforms"),
                    GetString(root, "build-tools"),
                    GetString(root, "cmake"),
                    GetString(root, "ndk"),
                    jsonPath);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                diagnostics.Add($"Android SDK : {exception.Message}");
            }
        }

        var sourcePath = Path.Combine(
            engineRoot,
            "Engine",
            "Source",
            "Programs",
            "UnrealBuildTool",
            "Platform",
            "Android",
            "AndroidPlatformSDK.Versions.cs");
        if (File.Exists(sourcePath))
        {
            try
            {
                var text = await ReadBoundedTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
                return new AndroidToolchainRequirement(
                    MatchMethodReturn(text, "GetMainVersionInternal"),
                    MatchAssignment(text, "MinVersion"),
                    MatchAssignment(text, "MaxVersion"),
                    MatchCaseReturn(text, "platforms"),
                    MatchCaseReturn(text, "build-tools"),
                    MatchCaseReturn(text, "cmake"),
                    MatchCaseReturn(text, "ndk"),
                    sourcePath);
            }
            catch (IOException exception)
            {
                diagnostics.Add($"Android SDK : {exception.Message}");
            }
        }

        var setupPath = Path.Combine(engineRoot, "Engine", "Extras", "Android", "SetupAndroid.bat");
        if (!File.Exists(setupPath))
        {
            diagnostics.Add("Contraintes Android SDK introuvables pour ce moteur.");
            return null;
        }

        try
        {
            var text = await ReadBoundedTextAsync(setupPath, cancellationToken).ConfigureAwait(false);
            return new AndroidToolchainRequirement(
                null,
                null,
                null,
                MatchBatchDefault(text, "PLATFORMS_VERSION"),
                MatchBatchDefault(text, "BUILDTOOLS_VERSION"),
                MatchBatchDefault(text, "CMAKE_VERSION"),
                MatchBatchDefault(text, "NDK_VERSION"),
                setupPath);
        }
        catch (IOException exception)
        {
            diagnostics.Add($"Android SDK : {exception.Message}");
            return null;
        }
    }

    private static async Task<LinuxToolchainRequirement?> ParseLinuxAsync(
        string engineRoot,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var jsonPath = Path.Combine(engineRoot, "Engine", "Config", "Linux", "Linux_SDK.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                using var document = await ReadJsonAsync(jsonPath, cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                return new LinuxToolchainRequirement(
                    GetString(root, "MainVersion"),
                    GetString(root, "MinVersion"),
                    GetString(root, "MaxVersion"),
                    jsonPath);
            }
            catch (Exception exception) when (exception is IOException or JsonException)
            {
                diagnostics.Add($"Linux SDK : {exception.Message}");
            }
        }

        var sourcePath = Path.Combine(
            engineRoot,
            "Engine",
            "Source",
            "Programs",
            "UnrealBuildTool",
            "Platform",
            "Linux",
            "LinuxPlatformSDK.Versions.cs");
        if (!File.Exists(sourcePath))
        {
            diagnostics.Add("Contraintes Linux SDK introuvables pour ce moteur.");
            return null;
        }

        try
        {
            var text = await ReadBoundedTextAsync(sourcePath, cancellationToken).ConfigureAwait(false);
            return new LinuxToolchainRequirement(
                MatchMethodReturn(text, "GetMainVersionInternal") ?? MatchMethodReturn(text, "GetMainVersion"),
                MatchAssignment(text, "MinVersion"),
                MatchAssignment(text, "MaxVersion"),
                sourcePath);
        }
        catch (IOException exception)
        {
            diagnostics.Add($"Linux SDK : {exception.Message}");
            return null;
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var text = await ReadBoundedTextAsync(path, cancellationToken).ConfigureAwait(false);
        return JsonDocument.Parse(text, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 32,
        });
    }

    private static async Task<string> ReadBoundedTextAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (info.Length > MaximumMetadataLength)
        {
            throw new ToolchainException(
                "Le fichier de métadonnées Toolchain dépasse la limite de sécurité.",
                "ParseRequirements",
                path);
        }

        return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[] GetStringArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item!)
                .ToArray()
            : [];

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        if (startIndex < 0)
        {
            return string.Empty;
        }

        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        return endIndex < 0 ? text[startIndex..] : text[startIndex..endIndex];
    }

    private static string? MatchVersionConstructor(string text, string variable)
    {
        var match = Match(
            text,
            $@"{Regex.Escape(variable)}\s*=\s*new\s+VersionNumber\(([^)]+)\)");
        if (!match.Success)
        {
            return null;
        }

        var numbers = match.Groups[1].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return numbers.All(number => int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            ? string.Join('.', numbers)
            : null;
    }

    private static string[] MatchRanges(string text) =>
        Matches(text, @"VersionNumberRange\.Parse\(""([^""]+)""\s*,\s*""([^""]+)""\)")
            .Select(match => $"{match.Groups[1].Value}-{match.Groups[2].Value}")
            .ToArray();

    private static string? MatchMethodReturn(string text, string methodName)
    {
        var section = Slice(text, methodName, "}");
        return MatchFirst(section, @"return\s+""([^""]+)""");
    }

    private static string? MatchAssignment(string text, string name) =>
        MatchFirst(text, $@"{Regex.Escape(name)}\s*=\s*""([^""]+)""");

    private static string? MatchCaseReturn(string text, string name) =>
        MatchFirst(
            text,
            $@"case\s+""{Regex.Escape(name)}""\s*:\s*return\s+""([^""]+)""");

    private static string? MatchBatchDefault(string text, string name) =>
        MatchFirst(
            text,
            $@"if\s+""%{Regex.Escape(name)}%""\s*==\s*""""\s+SET\s+{Regex.Escape(name)}=([^\r\n]+)",
            RegexOptions.IgnoreCase)?.Trim();

    private static string? MatchFirst(
        string text,
        string pattern,
        RegexOptions options = RegexOptions.None)
    {
        var match = Match(text, pattern, options);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static Match Match(
        string text,
        string pattern,
        RegexOptions options = RegexOptions.None) =>
        Regex.Match(text, pattern, options, TimeSpan.FromSeconds(1));

    private static MatchCollection Matches(string text, string pattern) =>
        Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
}
