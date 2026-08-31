using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Epic.Authentication;
using CyEngineHub.Epic.Models;
using CyEngineHub.Fab;

namespace CyEngineHub.Epic.Tests;

public sealed class OfficialConnectionBoundaryTests
{
    [Fact]
    public async Task EpicConnectionUsesOfficialExternalBrowserBoundary()
    {
        var service = new OfficialEpicWebAuthenticationService();

        var status = await service.GetStatusAsync();

        Assert.Equal("https", service.SignInUri.Scheme);
        Assert.Equal("www.epicgames.com", service.SignInUri.Host);
        Assert.Equal(EpicAuthenticationState.ExternalBrowserRequired, status.State);
        Assert.False(status.CanVerifyIdentity);
        Assert.False(status.CanQueryGlobalEntitlements);
    }

    [Fact]
    public async Task FabBackendDoesNotPretendToExposeRemoteLibrary()
    {
        var backend = new OfficialFabWebBackend();

        var status = await backend.GetStatusAsync();

        Assert.Equal("https", backend.LibraryUri.Scheme);
        Assert.Equal("www.fab.com", backend.LibraryUri.Host);
        Assert.Equal("dev.epicgames.com", backend.ExportGuideUri.Host);
        Assert.Equal(FabBackendState.WebOnly, status.State);
        Assert.False(status.SupportsRemoteLibrary);
        Assert.False(status.SupportsDownloads);
    }

    [Fact]
    public async Task BinaryEngineBackendRequiresOfficialExternalLauncher()
    {
        var backend = new OfficialEpicGamesLauncherEngineBackend();

        var status = await backend.GetStatusAsync();

        Assert.Equal("dev.epicgames.com", backend.InstallationGuideUri.Host);
        Assert.Equal(EpicBinaryEngineBackendState.ExternalLauncherRequired, status.State);
        Assert.False(status.SupportsInAppDownloads);
    }
}
