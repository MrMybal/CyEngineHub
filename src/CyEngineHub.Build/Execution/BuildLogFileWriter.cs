using System.Globalization;
using System.Text;
using CyEngineHub.Build.Models;

namespace CyEngineHub.Build.Execution;

public sealed class BuildLogFileWriter : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    private bool _disposed;

    private BuildLogFileWriter(string filePath, StreamWriter writer)
    {
        FilePath = filePath;
        _writer = writer;
    }

    public string FilePath { get; }

    public static BuildLogFileWriter Create(string directory, string operationTitle)
    {
        Directory.CreateDirectory(directory);
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeTitle = new string(operationTitle
            .Select(character => invalidCharacters.Contains(character) ? '_' : character)
            .Take(80)
            .ToArray())
            .Trim();
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            safeTitle = "UnrealOperation";
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
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return new BuildLogFileWriter(
            filePath,
            new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));
    }

    public void Write(BuildLogLine line)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            _writer.Write(line.Timestamp.ToString("O", CultureInfo.InvariantCulture));
            _writer.Write('\t');
            _writer.Write(line.Severity);
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

