using CyEngineHub.Toolchains.Models;

namespace CyEngineHub.Toolchains.Compatibility;

public interface IToolchainCompatibilityService
{
    EngineToolchainSnapshot Evaluate(
        UnrealEngineToolchainRequirements requirements,
        IReadOnlyList<ToolchainInstallation> installations);
}

public sealed class ToolchainCompatibilityService : IToolchainCompatibilityService
{
    public EngineToolchainSnapshot Evaluate(
        UnrealEngineToolchainRequirements requirements,
        IReadOnlyList<ToolchainInstallation> installations)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(installations);
        var checks = new List<ToolchainCompatibilityCheck>();
        if (requirements.Windows is { } windows)
        {
            checks.Add(EvaluateVisualStudio(windows, installations));
            checks.Add(EvaluateMsvc(windows, installations));
            checks.Add(EvaluateWindowsSdk(windows, installations));
            var bundled = installations.Where(item => item.Kind == ToolchainKind.DotNet &&
                item.Details.TryGetValue("EngineId", out var owner) && owner == requirements.EngineId).ToArray();
            var dotnet = EvaluatePresence(ToolchainKind.DotNet, ".NET", "Runtime fourni avec ce moteur ou SDK compatible",
                bundled.Length > 0 ? bundled : installations.Where(item => !item.Details.ContainsKey("EngineId")).ToArray(),
                ToolchainHealth.Missing);
            checks.Add(bundled.Length == 0 && dotnet.Health == ToolchainHealth.Ready
                ? dotnet with { Health = ToolchainHealth.Warning, Diagnostic = "SDK système détecté ; compatibilité avec ce moteur à vérifier. Le runtime embarqué est préférable." }
                : dotnet);
        }

        if (requirements.Android is { } android)
        {
            checks.Add(EvaluateAndroidSdk(android, installations));
            checks.Add(EvaluateExactVersion(
                ToolchainKind.AndroidNdk,
                "Android NDK",
                android.NdkVersion,
                installations,
                ToolchainHealth.Missing));
            checks.Add(EvaluateExactVersion(
                ToolchainKind.CMake,
                "CMake Android",
                android.CMakeVersion,
                installations,
                ToolchainHealth.Missing));
            checks.Add(EvaluatePresence(
                ToolchainKind.Java,
                "Java / JDK",
                "JDK disponible",
                installations,
                ToolchainHealth.Missing));
            checks.Add(EvaluateEngineGradle(requirements.EngineId, installations));
            checks.Add(EvaluatePresence(
                ToolchainKind.Adb,
                "ADB",
                "Optionnel pour packaging; requis pour appareils",
                installations,
                ToolchainHealth.Warning));
        }

        if (requirements.Linux is { } linux)
        {
            checks.Add(EvaluateExactVersion(
                ToolchainKind.LinuxToolchain,
                "Linux cross-toolchain",
                linux.MainVersion,
                installations,
                ToolchainHealth.Warning));
        }

        return new EngineToolchainSnapshot(
            requirements.EngineId,
            requirements.EngineDisplayName,
            requirements.EngineVersion,
            checks,
            requirements.Diagnostics);
    }

    private static ToolchainCompatibilityCheck EvaluateVisualStudio(
        WindowsToolchainRequirement requirement,
        IReadOnlyList<ToolchainInstallation> installations)
    {
        var candidates = OfKind(installations, ToolchainKind.VisualStudio);
        var best = Latest(candidates);
        var required = string.IsNullOrWhiteSpace(requirement.MinimumVisualStudioVersion)
            ? "Visual Studio avec C++"
            : $">= {requirement.MinimumVisualStudioVersion}";
        if (best is null)
        {
            return Missing(ToolchainKind.VisualStudio, "Visual Studio", required);
        }

        var meetsMinimum = string.IsNullOrWhiteSpace(requirement.MinimumVisualStudioVersion) ||
                           ToolchainVersion.Compare(best.Version, requirement.MinimumVisualStudioVersion) >= 0;
        return new ToolchainCompatibilityCheck(
            ToolchainKind.VisualStudio,
            "Visual Studio",
            meetsMinimum ? best.Health : ToolchainHealth.Missing,
            required,
            best.Version,
            meetsMinimum
                ? best.DisplayName
                : "Visual Studio est installé mais trop ancien pour ce moteur.");
    }

    private static ToolchainCompatibilityCheck EvaluateMsvc(
        WindowsToolchainRequirement requirement,
        IReadOnlyList<ToolchainInstallation> installations)
    {
        var candidates = OfKind(installations, ToolchainKind.Msvc)
            .Where(item => requirement.BannedMsvcRanges.All(range =>
                !ToolchainVersion.IsInRange(item.Version, range)))
            .ToArray();
        foreach (var range in requirement.PreferredMsvcRanges)
        {
            var preferred = Latest(candidates.Where(item =>
                ToolchainVersion.IsInRange(item.Version, range)).ToArray());
            if (preferred is not null)
            {
                return new ToolchainCompatibilityCheck(
                    ToolchainKind.Msvc,
                    "MSVC",
                    ToolchainHealth.Ready,
                    $"Préféré {range}",
                    preferred.Version,
                    "Toolchain préférée par Unreal disponible.");
            }
        }

        var supported = Latest(candidates.Where(item =>
            string.IsNullOrWhiteSpace(requirement.MinimumMsvcVersion) ||
            ToolchainVersion.Compare(item.Version, requirement.MinimumMsvcVersion) >= 0).ToArray());
        if (supported is not null)
        {
            return new ToolchainCompatibilityCheck(
                ToolchainKind.Msvc,
                "MSVC",
                ToolchainHealth.Warning,
                $">= {requirement.MinimumMsvcVersion ?? "version UE"}",
                supported.Version,
                "Version supportée mais hors plage préférée/testée par ce moteur.");
        }

        var installed = Latest(OfKind(installations, ToolchainKind.Msvc));
        return new ToolchainCompatibilityCheck(
            ToolchainKind.Msvc,
            "MSVC",
            ToolchainHealth.Missing,
            $">= {requirement.MinimumMsvcVersion ?? "version UE"}",
            installed?.Version ?? "Absent",
            installed is null
                ? "Aucun compilateur MSVC x64 valide détecté."
                : "Les versions installées sont trop anciennes ou bannies par Unreal.");
    }

    private static ToolchainCompatibilityCheck EvaluateWindowsSdk(
        WindowsToolchainRequirement requirement,
        IReadOnlyList<ToolchainInstallation> installations)
    {
        var candidates = OfKind(installations, ToolchainKind.WindowsSdk);
        var main = candidates.FirstOrDefault(item =>
            ToolchainVersion.Compare(item.Version, requirement.MainWindowsSdkVersion) == 0);
        if (main is not null)
        {
            return new ToolchainCompatibilityCheck(
                ToolchainKind.WindowsSdk,
                "Windows SDK",
                ToolchainHealth.Ready,
                requirement.MainWindowsSdkVersion ?? "Version UE",
                main.Version,
                "Version principale recommandée par Unreal disponible.");
        }

        var compatible = Latest(candidates.Where(item =>
            (string.IsNullOrWhiteSpace(requirement.MinimumWindowsSdkVersion) ||
             ToolchainVersion.Compare(item.Version, requirement.MinimumWindowsSdkVersion) >= 0) &&
            (string.IsNullOrWhiteSpace(requirement.MaximumWindowsSdkVersion) ||
             ToolchainVersion.Compare(item.Version, requirement.MaximumWindowsSdkVersion) <= 0)).ToArray());
        if (compatible is not null)
        {
            return new ToolchainCompatibilityCheck(
                ToolchainKind.WindowsSdk,
                "Windows SDK",
                ToolchainHealth.Warning,
                requirement.MainWindowsSdkVersion ?? requirement.MinimumWindowsSdkVersion ?? "Version UE",
                compatible.Version,
                "SDK valide, mais la version principale recommandée n’est pas installée.");
        }

        return Missing(
            ToolchainKind.WindowsSdk,
            "Windows SDK",
            requirement.MinimumWindowsSdkVersion ?? requirement.MainWindowsSdkVersion ?? "Version UE");
    }

    private static ToolchainCompatibilityCheck EvaluateAndroidSdk(
        AndroidToolchainRequirement requirement,
        IReadOnlyList<ToolchainInstallation> installations)
    {
        var candidates = OfKind(installations, ToolchainKind.AndroidSdk);
        if (candidates.Count == 0)
        {
            return Missing(
                ToolchainKind.AndroidSdk,
                "Android SDK",
                $"{requirement.Platform ?? "plateforme UE"} · build-tools {requirement.BuildToolsVersion ?? "UE"}");
        }

        var match = candidates.FirstOrDefault(item =>
            ContainsDetail(item, "Platforms", requirement.Platform) &&
            ContainsDetail(item, "BuildTools", requirement.BuildToolsVersion));
        if (match is not null)
        {
            return new ToolchainCompatibilityCheck(
                ToolchainKind.AndroidSdk,
                "Android SDK",
                ToolchainHealth.Ready,
                $"{requirement.Platform} · build-tools {requirement.BuildToolsVersion}",
                match.Version,
                "Plateforme et build-tools requis disponibles.");
        }

        var installed = candidates[0];
        return new ToolchainCompatibilityCheck(
            ToolchainKind.AndroidSdk,
            "Android SDK",
            ToolchainHealth.Missing,
            $"{requirement.Platform} · build-tools {requirement.BuildToolsVersion}",
            installed.Version,
            "Le SDK existe, mais une plateforme ou les build-tools requis manquent.");
    }

    private static ToolchainCompatibilityCheck EvaluateExactVersion(
        ToolchainKind kind,
        string label,
        string? requiredVersion,
        IReadOnlyList<ToolchainInstallation> installations,
        ToolchainHealth missingHealth)
    {
        var candidates = OfKind(installations, kind);
        if (string.IsNullOrWhiteSpace(requiredVersion))
        {
            return EvaluatePresence(kind, label, "Version non précisée", installations, missingHealth);
        }

        var exact = candidates.FirstOrDefault(item =>
            string.Equals(item.Version, requiredVersion, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(item.Path)), requiredVersion, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new ToolchainCompatibilityCheck(
                kind,
                label,
                ToolchainHealth.Ready,
                requiredVersion,
                exact.Version,
                "Version exacte requise par Unreal disponible.");
        }

        var installed = Latest(candidates);
        return new ToolchainCompatibilityCheck(
            kind,
            label,
            missingHealth,
            requiredVersion,
            installed?.Version ?? "Absent",
            installed is null
                ? $"{label} introuvable."
                : "Une autre version est installée; Unreal attend la version indiquée.");
    }

    private static ToolchainCompatibilityCheck EvaluateEngineGradle(
        string engineId,
        IReadOnlyList<ToolchainInstallation> installations)
    {
        var match = OfKind(installations, ToolchainKind.Gradle).FirstOrDefault(item =>
            item.Details.TryGetValue("EngineId", out var value) &&
            string.Equals(value, engineId, StringComparison.Ordinal));
        return match is null
            ? Missing(ToolchainKind.Gradle, "Gradle", "Wrapper fourni avec le moteur")
            : new ToolchainCompatibilityCheck(
                ToolchainKind.Gradle,
                "Gradle",
                ToolchainHealth.Ready,
                "Wrapper fourni avec le moteur",
                match.Version,
                "Wrapper Gradle Unreal disponible.");
    }

    private static ToolchainCompatibilityCheck EvaluatePresence(
        ToolchainKind kind,
        string label,
        string required,
        IReadOnlyList<ToolchainInstallation> installations,
        ToolchainHealth missingHealth)
    {
        var candidate = Latest(OfKind(installations, kind));
        return candidate is null
            ? new ToolchainCompatibilityCheck(
                kind,
                label,
                missingHealth,
                required,
                "Absent",
                $"{label} introuvable.")
            : new ToolchainCompatibilityCheck(
                kind,
                label,
                candidate.Health,
                required,
                candidate.Version,
                candidate.DisplayName);
    }

    private static ToolchainCompatibilityCheck Missing(
        ToolchainKind kind,
        string label,
        string required) => new(
        kind,
        label,
        ToolchainHealth.Missing,
        required,
        "Absent",
        $"{label} requis mais introuvable.");

    private static List<ToolchainInstallation> OfKind(
        IReadOnlyList<ToolchainInstallation> installations,
        ToolchainKind kind) => installations.Where(item => item.Kind == kind).ToList();

    private static ToolchainInstallation? Latest(IReadOnlyList<ToolchainInstallation> items) =>
        items.OrderByDescending(item => item.Version, Comparer<string>.Create(ToolchainVersion.Compare))
            .FirstOrDefault();

    private static bool ContainsDetail(
        ToolchainInstallation installation,
        string key,
        string? required)
    {
        if (string.IsNullOrWhiteSpace(required))
        {
            return true;
        }

        return installation.Details.TryGetValue(key, out var values) &&
               values.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                   .Contains(required, StringComparer.OrdinalIgnoreCase);
    }
}
