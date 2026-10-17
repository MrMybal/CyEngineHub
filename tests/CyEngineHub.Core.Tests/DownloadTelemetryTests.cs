using CyEngineHub.Core.Marketplaces;
namespace CyEngineHub.Core.Tests;

public sealed class DownloadTelemetryTests
{
    [Fact]
    public void ReusedCacheDoesNotCountAsNetworkThroughput()
    {
        var meter = new DownloadProgressText();
        meter.Format("Cache", 1024, 16384, cachedBytes: 1024);
        var text = meter.Format("Cache", 8192, 16384, cachedBytes: 8192);
        Assert.DoesNotContain("Kio/s", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Mio/s", text, StringComparison.Ordinal);
        Assert.Contains("50,0", text.Replace("50.0", "50,0", StringComparison.Ordinal), StringComparison.Ordinal);
    }
    [Fact]
    public void AllCachedDownloadStillShowsCompletionButNoNetworkEta()
    {
        var text = new DownloadProgressText().Format("Cache vérifié", 4096, 4096, cachedBytes: 4096);
        Assert.Contains("100", text, StringComparison.Ordinal);
        Assert.Contains("Vitesse : —", text, StringComparison.Ordinal);
        Assert.Contains("Restant estimé : —", text, StringComparison.Ordinal);
    }
    [Fact]
    public void TextShowsAllMetricsAndDoesNotInventPostDownloadEta()
    {
        var sample = new DownloadTelemetrySnapshot("pack", 2048, 4096, 50, 1024, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2), "Téléchargement");
        Assert.Contains("%", DownloadProgressText.Summary(sample), StringComparison.Ordinal);
        Assert.Contains("Kio", DownloadProgressText.Summary(sample), StringComparison.Ordinal);
        Assert.Contains("/s", DownloadProgressText.Timing(sample), StringComparison.Ordinal);
        Assert.Contains("Restant estimé", DownloadProgressText.Timing(sample), StringComparison.Ordinal);
        Assert.Contains("Écoulé", DownloadProgressText.Timing(sample), StringComparison.Ordinal);
        var text = new DownloadProgressText().Format("Reconstruction", 4096, 4096);
        Assert.Contains("Restant estimé : —", text, StringComparison.Ordinal);
        Assert.Contains("Vitesse : —", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ComputesRatePercentAndEtaFromObservedBytes()
    {
        var meter = new DownloadTelemetry();
        meter.Update("pack.zip", 0, 10000, TimeSpan.Zero, "Téléchargement");
        var sample = meter.Update("pack.zip", 2000, 10000, TimeSpan.FromSeconds(2), "Téléchargement");
        Assert.Equal(1000d, sample.BytesPerSecond);
        Assert.Equal(20d, sample.Percent);
        Assert.Equal(TimeSpan.FromSeconds(8), sample.Remaining);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UnknownTotalDoesNotInventProgressOrEta(long total)
    {
        var sample = new DownloadTelemetry().Update("pack", 1000, total, TimeSpan.Zero, "Téléchargement");
        Assert.Null(sample.TotalBytes); Assert.Null(sample.Percent); Assert.Null(sample.Remaining);
    }

    [Fact]
    public void PauseResumeAndRestartDoNotProduceArtificialRates()
    {
        var meter = new DownloadTelemetry();
        meter.Update("pack", 0, 100, TimeSpan.Zero, "Téléchargement");
        Assert.Null(meter.Update("pack", 10, 100, TimeSpan.FromSeconds(1), "En pause / interrompu").BytesPerSecond);
        Assert.Null(meter.Update("pack", 10, 100, TimeSpan.FromSeconds(100), "Téléchargement").Remaining);
        Assert.Equal(10d, meter.Update("pack", 20, 100, TimeSpan.FromSeconds(101), "Téléchargement").BytesPerSecond);
        Assert.Null(meter.Update("pack", 0, 100, TimeSpan.FromSeconds(102), "Téléchargement").BytesPerSecond);
    }

    [Fact]
    public void StallDropsRateAndCompletionClearsEta()
    {
        var meter = new DownloadTelemetry();
        meter.Update("pack", 0, 1000, TimeSpan.Zero, "Téléchargement");
        for (var i = 1; i <= 10; i++) meter.Update("pack", 100, 1000, TimeSpan.FromSeconds(i), "Téléchargement");
        var stalled = meter.Update("pack", 100, 1000, TimeSpan.FromSeconds(11), "Téléchargement");
        Assert.Equal(0d, stalled.BytesPerSecond); Assert.Null(stalled.Remaining);
        var completed = meter.Update("pack", 1001, 1000, TimeSpan.FromSeconds(12), "Terminé");
        Assert.Equal(100d, completed.Percent); Assert.Null(completed.Remaining); Assert.Null(completed.BytesPerSecond);
    }
}
