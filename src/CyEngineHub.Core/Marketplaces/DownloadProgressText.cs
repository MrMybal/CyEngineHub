using System.Diagnostics;

namespace CyEngineHub.Core.Marketplaces;

/// <summary>One instance per transfer; never estimates unpacking or installation time.</summary>
public sealed class DownloadProgressText
{
    private readonly DownloadTelemetry _telemetry = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();

    public string Format(string message, long received, long? total)
    {
        lock (_gate)
        {
            var complete = total > 0 && received >= total;
            var snapshot = _telemetry.Update(string.Empty, received, total, _clock.Elapsed,
                complete ? "Traitement" : "Téléchargement");
            return message + "\n" + Summary(snapshot) + " · " + Timing(snapshot);
        }
    }

    public static string Summary(DownloadTelemetrySnapshot value) =>
        $"{(value.Percent is { } percent ? $"{percent:N1} %" : "pourcentage inconnu")} · " +
        $"{Size(value.ReceivedBytes)} / {(value.TotalBytes is { } total ? Size(total) : "taille inconnue")}";

    public static string Timing(DownloadTelemetrySnapshot value) =>
        $"Vitesse : {(value.BytesPerSecond is { } speed ? Size(speed) + "/s" : "—")} · " +
        $"Restant estimé : {(value.Remaining is { } remaining ? Duration(remaining) : "—")} · Écoulé : {Duration(value.Elapsed)}";

    private static string Size(double bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824:N2} Gio" : bytes >= 1048576 ? $"{bytes / 1048576:N1} Mio" : bytes >= 1024 ? $"{bytes / 1024:N1} Kio" : $"{bytes:N0} o";
    private static string Duration(TimeSpan time) => time.TotalHours >= 1 ? $"{(int)time.TotalHours} h {time.Minutes:00} min" : $"{(int)time.TotalMinutes} min {time.Seconds:00} s";
}
