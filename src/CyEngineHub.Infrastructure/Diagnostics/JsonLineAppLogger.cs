using System.Text.Json;
using CyEngineHub.Core.Diagnostics;

namespace CyEngineHub.Infrastructure.Diagnostics;

public sealed class JsonLineAppLogger : IAppLogger, IDisposable
{
    private readonly string _logsDirectory;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _disposed;

    public JsonLineAppLogger(string logsDirectory)
    {
        _logsDirectory = logsDirectory;
        Directory.CreateDirectory(logsDirectory);
    }

    public async ValueTask WriteAsync(
        AppLogLevel level,
        string category,
        string message,
        Exception? exception = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var timestamp = DateTimeOffset.UtcNow;
        var entry = new LogEntry(
            timestamp,
            level.ToString(),
            category,
            message,
            exception?.GetType().FullName,
            exception?.Message,
            exception?.StackTrace);
        var line = JsonSerializer.Serialize(entry);
        var path = Path.Combine(_logsDirectory, $"cyenginehub-{timestamp:yyyy-MM-dd}.jsonl");

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await File.AppendAllTextAsync(
                path,
                line + Environment.NewLine,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeLock.Dispose();
    }

    private sealed record LogEntry(
        DateTimeOffset Timestamp,
        string Level,
        string Category,
        string Message,
        string? ExceptionType,
        string? ExceptionMessage,
        string? StackTrace);
}

