using System.Text;

namespace CyEngineHub.Core.Processes;

public sealed record ProcessRequest
{
    public required string FileName { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? RawArguments { get; init; }

    public string? WorkingDirectory { get; init; }

    public IReadOnlyDictionary<string, string?> EnvironmentVariables { get; init; }
        = new Dictionary<string, string?>();

    public string? StandardInput { get; init; }

    public Encoding OutputEncoding { get; init; } = Encoding.UTF8;

    public int MaxCapturedLines { get; init; } = 5_000;
}

public sealed record ProcessOutputLine(
    DateTimeOffset Timestamp,
    ProcessOutputChannel Channel,
    string Text);

public enum ProcessOutputChannel
{
    StandardOutput,
    StandardError,
}

public sealed record ProcessResult(
    int ProcessId,
    int ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<ProcessOutputLine> CapturedOutput)
{
    public TimeSpan Duration => FinishedAt - StartedAt;

    public bool Succeeded => ExitCode == 0;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        ProcessRequest request,
        IProgress<ProcessOutputLine>? output = null,
        CancellationToken cancellationToken = default);
}

public interface IPlatformLauncher
{
    Task<int?> StartDetachedAsync(
        string fileName,
        IReadOnlyList<string>? arguments = null,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default);

    Task OpenFolderAsync(string path, CancellationToken cancellationToken = default);

    Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default);
}

public sealed class ProcessOperationException : Exception
{
    public ProcessOperationException(string message, string fileName, Exception? innerException = null)
        : base(message, innerException)
    {
        FileName = fileName;
    }

    public string FileName { get; }
}
