using System.Text.Json;
using System.Runtime.Versioning;
using CyEngineHub.Core.Processes;
using CyEngineHub.Toolchains.Models;
using Microsoft.Win32;
using static CyEngineHub.Toolchains.Detection.ToolchainDetectionHelpers;

namespace CyEngineHub.Toolchains.Detection;

internal sealed class WindowsToolchainDetector
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IProcessRunner _processRunner;

    public WindowsToolchainDetector(IProcessRunner processRunner)
    {
        _processRunner = processRunner;
    }

    public async Task DetectAsync(
        List<ToolchainInstallation> installations,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var visualStudioRoots = await DetectVisualStudioAsync(
                installations,
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);
        DetectMsvcAndBundledTools(visualStudioRoots, installations, diagnostics);
        DetectWindowsSdks(installations, diagnostics);
        await DetectDotNetAsync(installations, diagnostics, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> DetectVisualStudioAsync(
        List<ToolchainInstallation> installations,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var roots = new List<string>();
        var vsWhere = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio",
            "Installer",
            "vswhere.exe");
        if (File.Exists(vsWhere))
        {
            try
            {
                var result = await _processRunner.RunAsync(
                        new ProcessRequest
                        {
                            FileName = vsWhere,
                            Arguments = ["-all", "-products", "*", "-prerelease", "-format", "json", "-utf8"],
                            MaxCapturedLines = 5_000,
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var json = string.Join(
                    Environment.NewLine,
                    result.CapturedOutput
                        .Where(line => line.Channel == ProcessOutputChannel.StandardOutput)
                        .Select(line => line.Text));
                var instances = result.Succeeded
                    ? JsonSerializer.Deserialize<VisualStudioInstance[]>(json, JsonOptions) ?? []
                    : [];
                foreach (var instance in instances.Where(instance =>
                             !string.IsNullOrWhiteSpace(instance.InstallationPath)))
                {
                    var root = Path.GetFullPath(instance.InstallationPath!);
                    roots.Add(root);
                    var issues = new List<string>();
                    if (!instance.IsComplete)
                    {
                        issues.Add("Installation Visual Studio incomplète.");
                    }

                    if (!instance.IsLaunchable)
                    {
                        issues.Add("Visual Studio n’est pas marqué comme lançable.");
                    }

                    AddInstallation(
                        installations,
                        ToolchainKind.VisualStudio,
                        instance.DisplayName ?? "Visual Studio",
                        instance.InstallationVersion ?? "Inconnue",
                        root,
                        "vswhere",
                        issues.Count == 0 ? ToolchainHealth.Ready : ToolchainHealth.Warning,
                        issues,
                        new Dictionary<string, string>
                        {
                            ["InstanceId"] = instance.InstanceId ?? string.Empty,
                            ["ProductId"] = instance.ProductId ?? string.Empty,
                        });
                }
            }
            catch (Exception exception) when (
                exception is ProcessOperationException or JsonException or IOException)
            {
                diagnostics.Add($"Visual Studio : {exception.Message}");
            }
        }

        if (roots.Count == 0)
        {
            var baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Microsoft Visual Studio",
                "2022");
            foreach (var root in GetDirectoriesSafe(baseDirectory))
            {
                var editor = Path.Combine(root, "Common7", "IDE", "devenv.exe");
                if (!File.Exists(editor))
                {
                    continue;
                }

                roots.Add(root);
                AddInstallation(
                    installations,
                    ToolchainKind.VisualStudio,
                    $"Visual Studio 2022 {Path.GetFileName(root)}",
                    ReadFileVersion(editor),
                    root,
                    "Filesystem",
                    ToolchainHealth.Ready);
            }
        }

        return roots.Distinct(PathComparer).ToArray();
    }

    private static void DetectMsvcAndBundledTools(
        IReadOnlyList<string> visualStudioRoots,
        List<ToolchainInstallation> installations,
        List<string> diagnostics)
    {
        foreach (var root in visualStudioRoots)
        {
            foreach (var toolchainRoot in GetDirectoriesSafe(Path.Combine(root, "VC", "Tools", "MSVC")))
            {
                var compiler = Path.Combine(toolchainRoot, "bin", "Hostx64", "x64", "cl.exe");
                var issues = File.Exists(compiler) ? Array.Empty<string>() : ["cl.exe x64 absent."];
                AddInstallation(
                    installations,
                    ToolchainKind.Msvc,
                    $"MSVC {Path.GetFileName(toolchainRoot)}",
                    Path.GetFileName(toolchainRoot),
                    toolchainRoot,
                    Path.GetFileName(root),
                    issues.Length == 0 ? ToolchainHealth.Ready : ToolchainHealth.Warning,
                    issues);
            }

            AddKnownExecutable(
                installations,
                ToolchainKind.CMake,
                "CMake Visual Studio",
                Path.Combine(root, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "CMake", "bin", "cmake.exe"),
                "Visual Studio");
            AddKnownExecutable(
                installations,
                ToolchainKind.Ninja,
                "Ninja Visual Studio",
                Path.Combine(root, "Common7", "IDE", "CommonExtensions", "Microsoft", "CMake", "Ninja", "ninja.exe"),
                "Visual Studio");
            AddKnownExecutable(
                installations,
                ToolchainKind.Clang,
                "Clang Visual Studio",
                Path.Combine(root, "VC", "Tools", "Llvm", "x64", "bin", "clang.exe"),
                "Visual Studio");
        }

        if (visualStudioRoots.Count == 0)
        {
            diagnostics.Add("Visual Studio n’a pas été détecté.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DetectWindowsSdks(
        List<ToolchainInstallation> installations,
        List<string> diagnostics)
    {
        var roots = new HashSet<string>(PathComparer);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows Kits\Installed Roots",
                writable: false);
            if (key?.GetValue("KitsRoot10") is string root && Directory.Exists(root))
            {
                roots.Add(Path.GetFullPath(root));
            }
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            diagnostics.Add($"Windows SDK registre : {exception.Message}");
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Windows Kits",
            "10");
        if (Directory.Exists(fallback))
        {
            roots.Add(fallback);
        }

        foreach (var root in roots)
        {
            foreach (var libraryDirectory in GetDirectoriesSafe(Path.Combine(root, "Lib")))
            {
                var version = Path.GetFileName(libraryDirectory);
                var issues = new List<string>();
                if (!File.Exists(Path.Combine(libraryDirectory, "um", "x64", "kernel32.lib")))
                {
                    issues.Add("Bibliothèque x64 kernel32.lib absente.");
                }

                if (!Directory.Exists(Path.Combine(root, "Include", version, "um")))
                {
                    issues.Add("Headers UM absents.");
                }

                AddInstallation(
                    installations,
                    ToolchainKind.WindowsSdk,
                    $"Windows SDK {version}",
                    version,
                    libraryDirectory,
                    "Windows Kits",
                    issues.Count == 0 ? ToolchainHealth.Ready : ToolchainHealth.Warning,
                    issues);
            }
        }
    }

    private async Task DetectDotNetAsync(
        List<ToolchainInstallation> installations,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var executable = ExecutableLocator.Find("dotnet.exe") ?? ExecutableLocator.Find("dotnet");
        if (executable is null)
        {
            diagnostics.Add("SDK .NET global non détecté dans PATH.");
            return;
        }

        try
        {
            var result = await _processRunner.RunAsync(
                    new ProcessRequest
                    {
                        FileName = executable,
                        Arguments = ["--list-sdks"],
                        MaxCapturedLines = 1_000,
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            foreach (var line in result.CapturedOutput.Where(line =>
                         line.Channel == ProcessOutputChannel.StandardOutput))
            {
                var separator = line.Text.IndexOf(' ', StringComparison.Ordinal);
                if (separator > 0)
                {
                    AddInstallation(
                        installations,
                        ToolchainKind.DotNet,
                        $".NET SDK {line.Text[..separator]}",
                        line.Text[..separator],
                        executable,
                        "PATH",
                        ToolchainHealth.Ready);
                }
            }
        }
        catch (ProcessOperationException exception)
        {
            diagnostics.Add($".NET : {exception.Message}");
        }
    }

    private sealed record VisualStudioInstance
    {
        public string? InstanceId { get; init; }

        public string? InstallationPath { get; init; }

        public string? InstallationVersion { get; init; }

        public string? ProductId { get; init; }

        public string? DisplayName { get; init; }

        public bool IsComplete { get; init; }

        public bool IsLaunchable { get; init; }
    }
}
