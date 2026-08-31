using System.Diagnostics;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Infrastructure.Processes;

public sealed class PlatformLauncher : IPlatformLauncher
{
    public Task<int?> StartDetachedAsync(
        string fileName,
        IReadOnlyList<string>? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(fileName))
        {
            throw new ProcessOperationException($"Executable not found: {fileName}", fileName);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(fileName) ?? string.Empty,
            UseShellExecute = false,
        };
        foreach (var argument in arguments ?? [])
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            var process = Process.Start(startInfo)
                ?? throw new ProcessOperationException($"Could not launch '{fileName}'.", fileName);
            return Task.FromResult<int?>(process.Id);
        }
        catch (ProcessOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProcessOperationException(
                $"Could not launch '{fileName}'.",
                fileName,
                exception);
        }
    }

    public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
        return Task.CompletedTask;
    }

    public Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Only absolute HTTP(S) addresses can be opened.", nameof(uri));
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            });
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            throw new ProcessOperationException(
                $"Could not open '{uri.Host}'.",
                uri.AbsoluteUri,
                exception);
        }
    }
}
