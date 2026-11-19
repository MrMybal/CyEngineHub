using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Epic.Tests;

public sealed class FabDownloadProgressTextTests
{
    [Fact]
    public void ReconstructionUsesLocalCountersAndStageEtaAfterNetworkCompletes()
    {
        var clock = new ManualClock();
        var meter = new FabDownloadProgressText(clock);
        const long network = 91L * 1024 * 1024 * 1024;
        var transfer = new DirectFabDownloadProgress(.72, "Chunks disponibles", network, network);
        Assert.Equal(1d, meter.Format("City Sample", transfer).Fraction);
        var reconstruct = transfer with
        {
            Phase = DirectFabDownloadPhase.Reconstruction, Message = "Reconstruction · 144 437/147 434 fichiers",
            Fraction = .98, ProcessedBytes = 1000, TotalProcessingBytes = 10000,
        };
        var first = meter.Format("City Sample", reconstruct);
        Assert.Equal("Reconstruction", first.Stage);
        Assert.Equal(.1d, first.Fraction);
        Assert.DoesNotContain("Gio", first.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Restant estimé", first.Message, StringComparison.Ordinal);
        clock.Advance(TimeSpan.FromSeconds(2));
        var second = meter.Format("City Sample", reconstruct with { ProcessedBytes = 3000 });
        Assert.Equal(.3d, second.Fraction);
        Assert.Contains("Traitement disque", second.Message, StringComparison.Ordinal);
        Assert.Contains("Débit de traitement", second.Message, StringComparison.Ordinal);
        Assert.Contains("0 min 07 s", second.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Débit réseau", second.Message, StringComparison.Ordinal);
        var cleanup = meter.Format("City Sample", transfer with { Phase = DirectFabDownloadPhase.Finalization });
        Assert.Null(cleanup.Fraction);
        Assert.DoesNotContain("Gio", cleanup.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Restant estimé", cleanup.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DirectFabDownloadPhase.Packaging, "Conditionnement")]
    [InlineData(DirectFabDownloadPhase.Placement, "Stockage")]
    [InlineData(DirectFabDownloadPhase.Verification, "Vérification")]
    public void LocalPhasesReportOnlyMeasuredWork(DirectFabDownloadPhase phase, string label)
    {
        var progress = new DirectFabDownloadProgress(1, "Travail local", 8000, 8000) { Phase = phase };
        var meter = new FabDownloadProgressText();
        var unknown = meter.Format("Fab", progress);
        Assert.Equal(label, unknown.Stage);
        Assert.Null(unknown.Fraction);
        Assert.Contains("disque", unknown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("%", unknown.Message, StringComparison.Ordinal);
        var measured = meter.Format("Fab", progress with { ProcessedBytes = 250, TotalProcessingBytes = 1000 });
        Assert.Equal(.25d, measured.Fraction);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }
}
