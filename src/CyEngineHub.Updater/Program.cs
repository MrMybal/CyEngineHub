using System.Diagnostics;

namespace CyEngineHub.Updater;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var processId = ReadProcessId(args);
            var installerPath = ReadInstallerPath(args);
            await WaitForApplicationExitAsync(processId).ConfigureAwait(false);
            LaunchInstaller(installerPath);
            return 0;
        }
        catch (Exception exception) when (exception is
            ArgumentException or
            InvalidOperationException or
            IOException or
            System.ComponentModel.Win32Exception)
        {
            await WriteFailureLogAsync(exception).ConfigureAwait(false);
            return 1;
        }
    }

    private static int ReadProcessId(string[] args)
    {
        var value = ReadArgument(args, "--wait-pid");
        if (!int.TryParse(value, out var processId) || processId <= 0)
        {
            throw new ArgumentException("Identifiant du processus CyEngineHub invalide.");
        }

        return processId;
    }

    private static string ReadInstallerPath(string[] args)
    {
        var path = Path.GetFullPath(ReadArgument(args, "--installer"));
        if (!File.Exists(path) ||
            (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
             !path.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("Installateur Windows absent ou non pris en charge.");
        }

        return path;
    }

    private static string ReadArgument(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase)) return args[index + 1];
        }

        throw new ArgumentException($"Argument requis absent : {name}.");
    }

    private static async Task WaitForApplicationExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
            // Le processus est déjà terminé : l’installateur peut démarrer.
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException(
                "CyEngineHub ne s’est pas arrêté dans le délai autorisé. L’installateur n’a pas été lancé.",
                exception);
        }
    }

    private static void LaunchInstaller(string installerPath)
    {
        ProcessStartInfo startInfo;
        if (installerPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                UseShellExecute = true,
            };
            startInfo.ArgumentList.Add("/i");
            startInfo.ArgumentList.Add(installerPath);
        }
        else
        {
            startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true,
            };
        }

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("Windows n’a pas pu lancer l’installateur.");
        }
    }

    private static async Task WriteFailureLogAsync(Exception exception)
    {
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "CyEngineHub", "Updater");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "last-error.log");
            await File.WriteAllTextAsync(
                path,
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}").ConfigureAwait(false);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
