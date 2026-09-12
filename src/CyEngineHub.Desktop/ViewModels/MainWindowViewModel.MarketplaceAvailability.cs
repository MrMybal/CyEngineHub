namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private bool _fabMarketplaceEnabled = true;
    private bool _quixelMarketplaceEnabled = true;
    private bool _unityMarketplaceEnabled = true;

    public bool FabMarketplaceEnabled
    {
        get => _fabMarketplaceEnabled;
        set { if (SetProperty(ref _fabMarketplaceEnabled, value)) UpdateMarketplaceAvailability(); }
    }
    public bool QuixelMarketplaceEnabled
    {
        get => _quixelMarketplaceEnabled;
        set { if (SetProperty(ref _quixelMarketplaceEnabled, value)) UpdateMarketplaceAvailability(); }
    }
    public bool UnityMarketplaceEnabled
    {
        get => _unityMarketplaceEnabled;
        set { if (SetProperty(ref _unityMarketplaceEnabled, value)) UpdateMarketplaceAvailability(); }
    }
    public bool IsFabLibrarySourceAvailable => FabMarketplaceEnabled && IsFabConnectionEnabled;

    private bool IsMarketplaceAvailable(string source) => source.ToLowerInvariant() switch
    {
        "fab" => IsFabLibrarySourceAvailable,
        "fabweb" => MarketplaceConnectors.Any(item => item.Id == MegascansFabConnectorId && item.IsEnabled),
        "quixel" or "megascans" => IsQuixelLibrarySourceAvailable,
        "unity" => IsUnityLibrarySourceAvailable,
        _ => true,
    };

    private void UpdateMarketplaceAvailability()
    {
        OnPropertyChanged(nameof(IsFabLibrarySourceAvailable));
        OnPropertyChanged(nameof(IsQuixelLibrarySourceAvailable));
        OnPropertyChanged(nameof(IsUnityLibrarySourceAvailable));
        _refreshDirectFabLibraryCommand?.NotifyCanExecuteChanged();
        _refreshQuixelLibraryCommand?.NotifyCanExecuteChanged();
        _downloadSelectedFabAssetCommand?.NotifyCanExecuteChanged();
        _downloadSelectedFabVersionsCommand?.NotifyCanExecuteChanged();
        if (!IsMarketplaceAvailable(LibrarySource))
        {
            CloseRemoteFabProduct(forgetSelection: false);
            LibrarySource = IsFabLibrarySourceAvailable ? "Fab"
                : IsQuixelLibrarySourceAvailable ? "Quixel"
                : IsUnityLibrarySourceAvailable ? "Unity" : "Local";
        }
    }

    private async Task LoadMarketplaceAvailabilityAsync()
    {
        FabMarketplaceEnabled = ParseEnabledSetting(await _settings.GetAsync("marketplaces.fab.enabled.v1").ConfigureAwait(true));
        QuixelMarketplaceEnabled = ParseEnabledSetting(await _settings.GetAsync("marketplaces.quixel.enabled.v1").ConfigureAwait(true));
        UnityMarketplaceEnabled = ParseEnabledSetting(await _settings.GetAsync("marketplaces.unity.enabled.v1").ConfigureAwait(true));
        UpdateMarketplaceAvailability();
    }

    private async Task SaveMarketplaceAvailabilityAsync()
    {
        await _settings.SetAsync("marketplaces.fab.enabled.v1", FabMarketplaceEnabled.ToString()).ConfigureAwait(true);
        await _settings.SetAsync("marketplaces.quixel.enabled.v1", QuixelMarketplaceEnabled.ToString()).ConfigureAwait(true);
        await _settings.SetAsync("marketplaces.unity.enabled.v1", UnityMarketplaceEnabled.ToString()).ConfigureAwait(true);
        foreach (var connector in MarketplaceConnectors)
        {
            await connector.SaveAsync().ConfigureAwait(true);
        }
    }
}
