namespace CyEngineHub.Core.Diagnostics;

public interface IAppLogger
{
    ValueTask WriteAsync(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default);
}

public enum AppLogLevel
{
    Trace,
    Information,
    Warning,
    Error,
    Critical,
}

public sealed class NullAppLogger : IAppLogger
{
    public static NullAppLogger Instance { get; } = new();

    private NullAppLogger()
    {
    }

    public ValueTask WriteAsync(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

