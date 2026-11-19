using CyEngineHub.Core.Marketplaces;

namespace CyEngineHub.Fab.Remote;

public sealed record FabDownloadPresentation(string Stage, double? Fraction, string Message);

/// <summary>One meter per artifact. Download and local processing have independent byte totals and rate windows.</summary>
public sealed class FabDownloadProgressText(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly long _start = (timeProvider ?? TimeProvider.System).GetTimestamp();
    private readonly object _gate = new();
    private DownloadTelemetry _meter = new();
    private DirectFabDownloadPhase? _lastCounter;

    public FabDownloadPresentation Format(string label, DirectFabDownloadProgress value)
    {
        lock (_gate)
        {
            var phase = value.Phase;
            var network = phase == DirectFabDownloadPhase.Transfer;
            var stage = phase switch
            {
                DirectFabDownloadPhase.Reconstruction => "Reconstruction",
                DirectFabDownloadPhase.Verification => "Vérification",
                DirectFabDownloadPhase.Packaging => "Conditionnement",
                DirectFabDownloadPhase.Placement => "Stockage",
                DirectFabDownloadPhase.Finalization => "Finalisation",
                DirectFabDownloadPhase.Completed => "Transfert terminé",
                _ => "Téléchargement",
            };
            // Resume checks and reconstruction advance the same cumulative processing counter.
            var counter = phase == DirectFabDownloadPhase.Verification ? DirectFabDownloadPhase.Reconstruction : phase;
            if (counter != _lastCounter)
            {
                _meter = new DownloadTelemetry();
                _lastCounter = counter;
            }
            var elapsed = _clock.GetElapsedTime(_start);
            var bytes = Math.Max(0, network ? value.DownloadedBytes : value.ProcessedBytes);
            var total = network ? value.TotalBytes : value.TotalProcessingBytes;
            var cached = network ? Math.Clamp(value.CachedBytes, 0, bytes) : 0;
            var hasCounters = bytes > 0 || total > 0;
            var sample = _meter.Update(string.Empty, bytes - cached, total - cached, elapsed, stage,
                measureThroughput: hasCounters && (total is null || bytes < total));
            double? fraction = total is > 0 ? Math.Clamp((double)bytes / total.Value, 0, 1) : null;
            var lines = new List<string> { $"{label} · {value.Message}" };
            if (hasCounters)
            {
                var scale = fraction is { } known
                    ? $"{(bytes < total ? Math.Min(99.9, 100 * known) : 100):N1} % · " : "";
                var amount = total is > 0 ? $"{DownloadProgressText.Size(bytes)} / {DownloadProgressText.Size(total.Value)}" : DownloadProgressText.Size(bytes);
                var cache = cached > 0 ? $" · dont {DownloadProgressText.Size(cached)} en cache" : "";
                lines.Add($"{(network ? "Données disponibles" : "Traitement disque")} · {scale}{amount}{cache}");
            }
            else if (!network && phase != DirectFabDownloadPhase.Completed)
                lines.Add("Traitement local sur le disque en cours.");

            var timing = new List<string>();
            if (sample.BytesPerSecond is > 0)
                timing.Add($"{(network ? "Débit réseau" : "Débit de traitement")} : {DownloadProgressText.Size(sample.BytesPerSecond.Value)}/s");
            if (sample.Remaining is { } eta) timing.Add($"Restant estimé pour cette étape : {DownloadProgressText.Duration(eta)}");
            timing.Add($"Écoulé : {DownloadProgressText.Duration(elapsed)}");
            lines.Add(string.Join(" · ", timing));
            return new(stage, fraction, string.Join('\n', lines));
        }
    }
}
