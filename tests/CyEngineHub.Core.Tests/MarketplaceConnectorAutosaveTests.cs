using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Extensions;

namespace CyEngineHub.Core.Tests;

public sealed class MarketplaceConnectorAutosaveTests
{
    private static readonly string[] ExpectedPaths = ["first", "last", "last"];
    private static MarketplaceConnectorViewModel Create(Func<MarketplaceConnectorSettings, Task> save) => new(
        new HubExtensionSnapshot(new HubExtensionManifest
        {
            Id = "test", DisplayName = "Test",
            Marketplace = new HubMarketplaceDescriptor { InventoryUrl = "https://example.com/inventory" }
        }, "", HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null),
        true, "initial", _ => Task.CompletedTask, () => Task.CompletedTask, _ => { }, save);

    [Fact]
    public async Task InitializationDoesNotOverwriteSavedSettingsAndEditsKeepOrder()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<MarketplaceConnectorSettings>();
        var vm = Create(async snapshot =>
        {
            if (snapshot.DownloadDirectory == "first")
            {
                started.SetResult();
                await release.Task;
            }
            writes.Add(snapshot);
        });
        Assert.Empty(writes);
        vm.DownloadDirectory = "first";
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.DownloadDirectory = "last";
        vm.IsEnabled = false;
        release.SetResult();
        await vm.PendingWrite.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ExpectedPaths, writes.Select(item => item.DownloadDirectory));
        Assert.False(writes[^1].Enabled);
    }

    [Fact]
    public async Task AFailedDraftDoesNotPreventSavingCorrection()
    {
        MarketplaceConnectorSettings? saved = null;
        var vm = Create(snapshot =>
        {
            if (snapshot.DownloadDirectory == "") throw new ArgumentException("Invalid path");
            saved = snapshot;
            return Task.CompletedTask;
        });
        vm.DownloadDirectory = "";
        await Assert.ThrowsAsync<ArgumentException>(() => vm.SaveAsync());
        Assert.StartsWith("Non enregistré", vm.SaveStatus, StringComparison.Ordinal);
        vm.DownloadDirectory = "corrected";
        await vm.SaveAsync();
        Assert.Equal("corrected", saved!.DownloadDirectory);
        Assert.Equal("Enregistré automatiquement", vm.SaveStatus);
    }
}
