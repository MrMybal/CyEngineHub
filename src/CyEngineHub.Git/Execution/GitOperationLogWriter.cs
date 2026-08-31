using System.Globalization;
using System.Text;
using CyEngineHub.Core.Processes;

namespace CyEngineHub.Git.Execution;

public sealed class GitOperationLogWriter : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    private GitOperationLogWriter(string filePath, StreamWriter writer)
    {
        FilePath = filePath;
        _writer = writer;
    }

    public string FilePath { get; }

    public static GitOperationLogWriter Create(string directory, string operationTitle)
    {
        Directory.CreateDirectory(directory);
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeTitle = new string(operationTitle
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .Take(80)
            .ToArray())
            .Trim();
        if (safeTitle.Length == 0)
        {
            safeTitle = "GitOperation";
        }

        var timestamp = DateTimeOffset.Now.ToString(
            "yyyyMMdd-HHmmss-fff",
            CultureInfo.InvariantCulture);
        var filePath = Path.Combine(directory, $"{timestamp}-{safeTitle}.log");
        var stream = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            32 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new GitOperationLogWriter(
            filePath,
            new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));
    }

    public void Write(ProcessOutputLine line)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _writer.Write(line.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            _writer.Write('\t');
            _writer.Write(line.Channel);
            _writer.Write('\t');
            _writer.WriteLine(line.Text);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _writer.FlushAsync().ConfigureAwait(false);
        await _writer.DisposeAsync().ConfigureAwait(false);
    }
}
