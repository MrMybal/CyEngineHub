using CyEngineHub.Core.Tasks;
using CyEngineHub.Desktop.ViewModels;

namespace CyEngineHub.Epic.Tests;

public sealed class EngineDownloadCardTests
{
    [Theory]
    [InlineData(HubTaskStatus.Queued, "En attente", true)]
    [InlineData(HubTaskStatus.Running, "En téléchargement", true)]
    [InlineData(HubTaskStatus.Canceled, "Annulé", false)]
    [InlineData(HubTaskStatus.Failed, "Échec", false)]
    [InlineData(HubTaskStatus.Completed, "Installation terminée", false)]
    public void DownloadRowHasTaskStateAndIsNeverAnInstalledEngine(HubTaskStatus state, string label, bool cancelable)
    {
        var snapshot = Snapshot(state);
        var row = new EngineDownloadCardViewModel(new("qa", "UE_QA", "5.6", "QA Unreal"), "QA-path", snapshot, _ => { }, () => { });
        Assert.Equal(label, row.StatusLabel);
        Assert.Equal(cancelable, row.CancelCommand.CanExecute(null));
        Assert.False(row.IsInstallationAvailable);
        Assert.Equal(25, row.ProgressPercent);
        Assert.Equal(snapshot.Message, row.Message);
    }

    [Fact]
    public void CancelUsesTaskIdAndShowsPendingCancellationUntilTaskAcknowledges()
    {
        var snapshot = Snapshot(HubTaskStatus.Running);
        Guid? canceled = null;
        var row = new EngineDownloadCardViewModel(new("qa", "UE_QA", "5.6", "QA"), "QA-path", snapshot, id => canceled = id, () => { });
        row.CancelCommand.Execute(null);
        Assert.Equal(snapshot.Id, canceled);
        Assert.Equal("Annulation…", row.StatusLabel);
        Assert.False(row.CancelCommand.CanExecute(null));
        Assert.True(row.CanCancel); // Remains an active destination until cancellation is acknowledged.
        row.ApplySnapshot(snapshot with { Status = HubTaskStatus.Canceled });
        Assert.False(row.CanCancel);
        Assert.Equal("Annulé", row.StatusLabel);
    }

    [Fact]
    public void FailedRegistrationKeepsVisibleErrorRatherThanClaimingInstalled()
    {
        var snapshot = Snapshot(HubTaskStatus.Completed);
        var row = new EngineDownloadCardViewModel(new("qa", "UE_QA", "5.6", "QA"), "QA-path", snapshot, _ => { }, () => { });
        row.SetRegistrationFailure("Synthetic invalid engine directory");
        Assert.False(row.IsCompleted);
        Assert.Equal("Échec de l’ajout", row.StatusLabel);
        Assert.Contains("Synthetic", row.Message, StringComparison.Ordinal);
    }
    private static HubTaskSnapshot Snapshot(HubTaskStatus state) => new(Guid.NewGuid(), "Epic", "QA", state,
        .25, "Téléchargement simulé · Vitesse : —", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null);

    [Theory]
    [InlineData(HubTaskStatus.Queued, false)]
    [InlineData(HubTaskStatus.Running, false)]
    [InlineData(HubTaskStatus.Completed, false)]
    [InlineData(HubTaskStatus.Failed, true)]
    [InlineData(HubTaskStatus.Canceled, true)]
    public void RetryIsAvailableOnlyForInterruptedDownloads(HubTaskStatus state, bool retryable)
    {
        var calls = 0;
        var row = new EngineDownloadCardViewModel(new("qa", "UE_QA", "5.6", "QA"), "QA-path", Snapshot(state),
            _ => { }, () => { }, () => { calls++; return Task.CompletedTask; });
        Assert.Equal(retryable, row.CanRetry);
        row.RetryCommand.Execute(null);
        Assert.Equal(retryable ? 1 : 0, calls);
        row.SetRegistrationFailure("registration fixture");
        Assert.False(row.RetryCommand.CanExecute(null));
    }
}
