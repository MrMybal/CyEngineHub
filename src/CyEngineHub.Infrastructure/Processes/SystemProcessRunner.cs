using System.Diagnostics;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Infrastructure.Processes;

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        ProcessRequest request,
        IProgress<ProcessOutputLine>? output = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("A process file name is required.", nameof(request));
        }

        if (request.MaxCapturedLines < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "MaxCapturedLines cannot be negative.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.StandardInput is not null,
            StandardOutputEncoding = request.OutputEncoding,
            StandardErrorEncoding = request.OutputEncoding,
        };
        if (request.RawArguments is not null && request.Arguments.Count > 0)
        {
            throw new ArgumentException(
                "Structured Arguments and RawArguments cannot be used together.",
                nameof(request));
        }

        if (request.RawArguments is not null)
        {
            startInfo.Arguments = request.RawArguments;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var variable in request.EnvironmentVariables)
        {
            if (variable.Value is null)
            {
                startInfo.Environment.Remove(variable.Key);
            }
            else
            {
                startInfo.Environment[variable.Key] = variable.Value;
            }
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            if (!process.Start())
            {
                throw new ProcessOperationException(
                    $"The process '{request.FileName}' did not start.",
                    request.FileName);
            }
        }
        catch (ProcessOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ProcessOperationException(
                $"Could not start '{request.FileName}'.",
                request.FileName,
                exception);
        }

        var processId = process.Id;
        var captured = new Queue<ProcessOutputLine>();
        var captureGate = new object();

        void Publish(ProcessOutputLine line)
        {
            if (request.MaxCapturedLines > 0)
            {
                lock (captureGate)
                {
                    captured.Enqueue(line);
                    while (captured.Count > request.MaxCapturedLines)
                    {
                        captured.Dequeue();
                    }
                }
            }

            output?.Report(line);
        }

        var standardOutputTask = ReadLinesAsync(
            process.StandardOutput,
            ProcessOutputChannel.StandardOutput,
            Publish);
        var standardErrorTask = ReadLinesAsync(
            process.StandardError,
            ProcessOutputChannel.StandardError,
            Publish);

        if (request.StandardInput is not null)
        {
            await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        using var cancellationRegistration = cancellationToken.Register(
            static state => TryKillProcessTree((Process)state!),
            process);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }

        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        IReadOnlyList<ProcessOutputLine> capturedSnapshot;
        lock (captureGate)
        {
            capturedSnapshot = captured.ToArray();
        }

        return new ProcessResult(
            processId,
            process.ExitCode,
            startedAt,
            DateTimeOffset.UtcNow,
            capturedSnapshot);
    }

    private static async Task ReadLinesAsync(
        StreamReader reader,
        ProcessOutputChannel channel,
        Action<ProcessOutputLine> publish)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            publish(new ProcessOutputLine(DateTimeOffset.UtcNow, channel, line));
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
