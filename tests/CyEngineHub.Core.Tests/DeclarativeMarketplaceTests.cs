using CyEngineHub.Core.Settings;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Extensions;

namespace CyEngineHub.Core.Tests;

public sealed class DeclarativeMarketplaceTests
{
    [Theory]
    [InlineData("itchio", "itch.io")]
    [InlineData("blendkit", "BlenderKit / Blendkit")]
    [InlineData("artstation", "ArtStation")]
    [InlineData("gumroad", "Gumroad")]
    [InlineData("cgtrader", "CGTrader")]
    [InlineData("custom-no-host-code", "Custom")]
    public async Task PackageCapabilitiesAndUiAreDrivenByManifestNotPlatformNames(string slug, string name)
    {
        var writes = new List<MarketplaceConnectorSettings>();
        var opened = 0;
        var connector = new MarketplaceConnectorViewModel(new(new()
        {
            Id = "cyenginehub.marketplace-" + slug, DisplayName = name,
            Marketplace = new() { InventoryUrl = "https://example.invalid/inventory", PackageStorage = true,
                AcquisitionLabel = "Mes fichiers officiels", WorkflowNotice = "Alpha · session du site, pas de synchronisation API." }
        }, "", HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null), true, Path.GetTempPath(),
            _ => { opened++; return Task.CompletedTask; }, () => Task.CompletedTask, _ => { },
            value => { writes.Add(value); return Task.CompletedTask; });
        Assert.True(connector.HasPackageStorage);
        Assert.Contains(name.ToUpperInvariant(), connector.SettingsHeading, StringComparison.Ordinal);
        Assert.Contains("ALPHA", connector.SettingsHeading, StringComparison.Ordinal);
        Assert.DoesNotContain("COSMOS", connector.SettingsHeading, StringComparison.Ordinal);
        Assert.Equal("Mes fichiers officiels", connector.AcquisitionLabel);
        Assert.Contains("pas de synchronisation API", connector.WorkflowNotice!, StringComparison.Ordinal);
        Assert.Empty(writes);
        connector.Compression = "ZipUltra"; await connector.PendingWrite;
        Assert.Equal("ZipUltra", writes[^1].Compression);
        connector.IsEnabled = false; await connector.PendingWrite;
        Assert.False(connector.OpenInventoryCommand.CanExecute(null));
        connector.OpenInventoryCommand.Execute(null);
        Assert.Equal(0, opened);
    }
}
