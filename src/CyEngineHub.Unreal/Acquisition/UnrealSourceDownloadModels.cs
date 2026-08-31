using CyEngineHub.Core.Processes;

namespace CyEngineHub.Unreal.Acquisition;

public sealed record UnrealSourceDownloadRequest(
    Uri RepositoryUri,
    string Branch,
    string TargetDirectory,
    bool ShallowClone);

public sealed record UnrealSourceDownloadPlan(
    Guid Id,
    UnrealSourceDownloadRequest Download,
    ProcessRequest Process,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

public sealed record UnrealSourceDownloadProgress(double? Progress, string Message);

public sealed record UnrealSourceDownloadResult(
    string EngineRoot,
    string Branch,
    bool ShallowClone,
    TimeSpan Duration);

public sealed class UnrealSourceDownloadException : Exception
{
    public UnrealSourceDownloadException(
        string message,
        string operation,
        string targetDirectory,
        int? exitCode = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Operation = operation;
        TargetDirectory = targetDirectory;
        ExitCode = exitCode;
    }

    public string Operation { get; }

    public string TargetDirectory { get; }

    public int? ExitCode { get; }
}
