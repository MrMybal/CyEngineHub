using Microsoft.Win32;

namespace CyEngineHub.Desktop.Services;

public interface IWindowsStartupService
{
    bool IsSupported { get; }

    bool IsEnabled();

    void SetEnabled(bool enabled);
}

public sealed class WindowsStartupService : IWindowsStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CyEngineHub";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string command &&
               !string.IsNullOrWhiteSpace(command);
    }

    public void SetEnabled(bool enabled)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "Le démarrage automatique est uniquement disponible sous Windows.");
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                        ?? throw new InvalidOperationException(
                            "La clé de démarrage Windows n’est pas accessible.");
        if (enabled)
        {
            key.SetValue(ValueName, BuildLaunchCommand(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string BuildLaunchCommand()
    {
        var assemblyPath = typeof(App).Assembly.Location;
        var appHostPath = Path.ChangeExtension(assemblyPath, ".exe");
        if (File.Exists(appHostPath))
        {
            return $"\"{appHostPath}\" --background";
        }

        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) &&
            !Path.GetFileNameWithoutExtension(processPath)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return $"\"{processPath}\" --background";
        }
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(assemblyPath))
        {
            return $"\"{processPath}\" \"{assemblyPath}\" --background";
        }

        throw new InvalidOperationException(
            "L’exécutable CyEngineHub n’a pas pu être localisé pour le démarrage Windows.");
    }
}
