using CyEngineHub.Core.Processes;
using CyEngineHub.Toolchains.Compatibility;
using CyEngineHub.Toolchains.Devices;
using CyEngineHub.Toolchains.Models;
using CyEngineHub.Unreal.Models;
using static CyEngineHub.Toolchains.Detection.ToolchainDetectionHelpers;

namespace CyEngineHub.Toolchains.Detection;

public interface IToolchainInventoryService
{
    Task<ToolchainSnapshot> RefreshAsync(
        IReadOnlyList<UnrealEngineInstallation> engines,
        IProgress<ToolchainRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ToolchainInventoryService : IToolchainInventoryService
{
    private static readonly string[] EnvironmentVariableNames =
    [
        "ANDROID_HOME",
        "ANDROID_SDK_ROOT",
        "JAVA_HOME",
        "NDKROOT",
        "UE_SDKS_ROOT",
        "LINUX_MULTIARCH_ROOT",
    ];

    private readonly IProcessRunner _processRunner;
    private readonly IUnrealToolchainRequirementParser _requirementParser;
    private readonly IToolchainCompatibilityService _compatibilityService;

    public ToolchainInventoryService(
        IProcessRunner processRunner,
        IUnrealToolchainRequirementParser requirementParser,
        IToolchainCompatibilityService compatibilityService)
    {
        _processRunner = processRunner;
        _requirementParser = requirementParser;
        _compatibilityService = compatibilityService;
    }

    public async Task<ToolchainSnapshot> RefreshAsync(
        IReadOnlyList<UnrealEngineInstallation> engines,
        IProgress<ToolchainRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engines);
        var diagnostics = new List<string>();
        var installations = new List<ToolchainInstallation>();
        var variables = EnvironmentVariableNames.Select(ReadEnvironmentVariable).ToArray();

        progress?.Report(new ToolchainRefreshProgress(0, 6, "Visual Studio, MSVC, Windows SDK et .NET"));
        await new WindowsToolchainDetector(_processRunner)
            .DetectAsync(installations, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new ToolchainRefreshProgress(1, 6, "Android SDK, NDK, Java et ADB"));
        var adbPaths = AndroidToolchainDetector.Detect(variables, installations, diagnostics);

        progress?.Report(new ToolchainRefreshProgress(2, 6, "Outils fournis par Unreal et Linux"));
        EngineToolchainDetector.Detect(engines, variables, installations, diagnostics);
        var normalizedInstallations = installations
            .GroupBy(
                item => item.Kind == ToolchainKind.DotNet
                    ? $"{item.Kind}|{item.Version}|{NormalizePath(item.Path)}"
                    : $"{item.Kind}|{NormalizePath(item.Path)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Kind)
            .ThenByDescending(
                item => item.Version,
                Comparer<string>.Create(ToolchainVersion.Compare))
            .ToArray();

        progress?.Report(new ToolchainRefreshProgress(3, 6, "Contraintes des moteurs Unreal"));
        var engineSnapshots = new List<EngineToolchainSnapshot>();
        foreach (var engine in engines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ToolchainRefreshProgress(
                3,
                6,
                $"Compatibilité · {engine.DisplayName}"));
            var requirements = await _requirementParser.ParseAsync(engine, cancellationToken)
                .ConfigureAwait(false);
            engineSnapshots.Add(_compatibilityService.Evaluate(requirements, normalizedInstallations));
        }

        progress?.Report(new ToolchainRefreshProgress(5, 6, "Appareils Android / Quest"));
        var devices = await DetectAndroidDevicesAsync(
                adbPaths.Concat(normalizedInstallations
                    .Where(item => item.Kind == ToolchainKind.Adb)
                    .Select(item => item.Path)),
                diagnostics,
                cancellationToken)
            .ConfigureAwait(false);

        progress?.Report(new ToolchainRefreshProgress(6, 6, "Inventaire Toolchains actualisé"));
        return new ToolchainSnapshot(
            normalizedInstallations,
            engineSnapshots,
            variables,
            devices,
            diagnostics,
            DateTimeOffset.UtcNow);
    }

    private async Task<IReadOnlyList<AndroidDeviceSnapshot>> DetectAndroidDevicesAsync(
        IEnumerable<string> candidateAdbPaths,
        List<string> diagnostics,
        CancellationToken cancellationToken)
    {
        var adb = candidateAdbPaths.Where(File.Exists).Distinct(PathComparer).FirstOrDefault();
        if (adb is null)
        {
            return [];
        }

        try
        {
            var result = await _processRunner.RunAsync(
                    new ProcessRequest
                    {
                        FileName = adb,
                        Arguments = ["devices", "-l"],
                        EnvironmentVariables = new Dictionary<string, string?>
                        {
                            ["ADB_MDNS_OPENSCREEN"] = "0",
                        },
                        MaxCapturedLines = 2_000,
                    },
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!result.Succeeded)
            {
                diagnostics.Add($"ADB a retourné le code {result.ExitCode}.");
            }

            return AdbDeviceParser.Parse(result.CapturedOutput.Select(line => line.Text));
        }
        catch (ProcessOperationException exception)
        {
            diagnostics.Add($"ADB : {exception.Message}");
            return [];
        }
    }
}
