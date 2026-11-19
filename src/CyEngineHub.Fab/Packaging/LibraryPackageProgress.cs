using System.Diagnostics;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Fab.Packaging;

// Archive creation and cross-volume copies can touch thousands of small files a second.
internal sealed class LibraryPackageProgress(IProgress<DirectFabDownloadProgress>? progress, DirectFabDownloadPhase phase)
{
    private long _lastReport;

    public void Stage(string message, bool finished = false) =>
        progress?.Report(new(finished ? 1d : null, message, 0, null) { Phase = phase });

    public void Bytes(string message, long processed, long total, bool force = false)
    {
        if (progress is null || (!force && Stopwatch.GetElapsedTime(_lastReport).TotalMilliseconds < 250)) return;
        _lastReport = Stopwatch.GetTimestamp();
        progress.Report(new(total == 0 ? null : Math.Clamp((double)processed / total, 0, 1), message, processed, total)
        {
            Phase = phase,
            ProcessedBytes = processed,
            TotalProcessingBytes = total,
        });
    }
}
