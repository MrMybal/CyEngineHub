using System.Text.RegularExpressions;
using CyEngineHub.Build.Models;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Build.Execution;

public static partial class BuildLogClassifier
{
    public static BuildLogLine Classify(ProcessOutputLine line)
    {
        var severity = line.Channel == ProcessOutputChannel.StandardError
            ? BuildLogSeverity.Error
            : ClassifyText(line.Text);
        return new BuildLogLine(line.Timestamp, severity, line.Text);
    }

    public static string? Diagnose(string text)
    {
        if (text.Contains("Android SDK", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("NDK", StringComparison.OrdinalIgnoreCase))
        {
            return "Vérifiez Android SDK, NDK et la version attendue par ce moteur.";
        }

        if (text.Contains("Git LFS", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("pointer file", StringComparison.OrdinalIgnoreCase))
        {
            return "Des objets Git LFS semblent absents. Vérifiez git lfs install/fetch/checkout.";
        }

        if (text.Contains("Visual Studio", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("toolchain", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("compiler", StringComparison.OrdinalIgnoreCase))
        {
            return "Vérifiez Visual Studio, MSVC et le Windows SDK requis par Unreal.";
        }

        if (text.Contains("plugin", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("missing", StringComparison.OrdinalIgnoreCase))
        {
            return "Un plugin requis est manquant ou incompatible avec le moteur sélectionné.";
        }

        return null;
    }

    private static BuildLogSeverity ClassifyText(string text)
    {
        if (FatalPattern().IsMatch(text))
        {
            return BuildLogSeverity.Fatal;
        }

        if (ErrorPattern().IsMatch(text))
        {
            return BuildLogSeverity.Error;
        }

        if (WarningPattern().IsMatch(text))
        {
            return BuildLogSeverity.Warning;
        }

        return text.StartsWith("Log", StringComparison.Ordinal)
            ? BuildLogSeverity.Trace
            : BuildLogSeverity.Information;
    }

    [GeneratedRegex(
        @"\b(fatal error|BUILD FAILED|AutomationTool exiting with ExitCode=[1-9]|Unhandled Exception)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        1_000)]
    private static partial Regex FatalPattern();

    [GeneratedRegex(
        @"(^|\s)(error\s+[A-Z]*\d+|error:|Log[A-Za-z]+:\s*Error:|\berror\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        1_000)]
    private static partial Regex ErrorPattern();

    [GeneratedRegex(
        @"(^|\s)(warning\s+[A-Z]*\d+|warning:|Log[A-Za-z]+:\s*Warning:|\bwarning\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        1_000)]
    private static partial Regex WarningPattern();
}
