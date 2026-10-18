using System.Windows.Input;
using CyEngineHub.Fab.Storage;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string _interfaceTheme = "Gris";
    private string _appearanceStatus = "Les thèmes s’appliquent immédiatement. Enregistrez pour les retrouver au lancement.";
    public static IReadOnlyList<string> InterfaceThemes { get; } = ["Gris", "Sombre", "Contraste"];
    public string InterfaceTheme
    {
        get => _interfaceTheme;
        set { if (InterfaceThemes.Contains(value)) SetProperty(ref _interfaceTheme, value); }
    }
    public string AppearanceStatus { get => _appearanceStatus; private set => SetProperty(ref _appearanceStatus, value); }
    public bool IsSettingsAppearanceSelected => SelectedSettingsCategory?.Id == "appearance";
    private ICommand? _saveFabMarketplacePanelCommand, _saveQuixelMarketplacePanelCommand, _saveUnityMarketplacePanelCommand;
    public ICommand SaveFabMarketplacePanelCommand => _saveFabMarketplacePanelCommand ??= new AsyncRelayCommand(() => SaveMarketplacePanelAsync("Fab"), onError: ShowError);
    public ICommand SaveQuixelMarketplacePanelCommand => _saveQuixelMarketplacePanelCommand ??= new AsyncRelayCommand(() => SaveMarketplacePanelAsync("Megascans"), onError: ShowError);
    public ICommand SaveUnityMarketplacePanelCommand => _saveUnityMarketplacePanelCommand ??= new AsyncRelayCommand(() => SaveMarketplacePanelAsync("Unity"), onError: ShowError);
    private async Task SaveMarketplacePanelAsync(string market)
    {
        var fab = market == "Fab"; var quixel = market == "Megascans";
        var locations = fab ? FabLibraryLocations : quixel ? QuixelLibraryLocations : UnityLibraryLocations;
        var profile = BuildMarketplaceStorageProfile(market, fab ? FabCacheDirectory : quixel ? QuixelCacheDirectory : UnityCacheDirectory,
            fab ? FabDownloadDirectory : quixel ? QuixelDownloadDirectory : UnityDownloadDirectory, locations);
        var cacheBytes = fab ? ParseMaximumLibraryBytes(FabNativeCacheMaximumGigabytes, "cache Vault Epic") : 0;
        if (_storageWrites.TryGetValue(market, out var pending)) { try { await pending.ConfigureAwait(true); } catch (Exception) { } }
        await new MarketplaceStorageSettingsStore(_settings).SaveAsync(fab ? FabStorageProfileSetting : quixel ? QuixelStorageProfileSetting : UnityStorageProfileSetting, profile).ConfigureAwait(true);
        await _settings.SetAsync(fab ? FabAutoRefreshOnStartupSetting : quixel ? QuixelAutoRefreshOnStartupSetting : UnityAutoRefreshOnStartupSetting,
            (fab ? AutoRefreshFabOnStartup : quixel ? AutoRefreshQuixelOnStartup : AutoRefreshUnityOnStartup).ToString()).ConfigureAwait(true);
        await _settings.SetAsync(fab ? FabPackageCompressionSetting : quixel ? QuixelPackageCompressionSetting : UnityPackageCompressionSetting,
            GetCompressionMode(fab ? SelectedFabCompressionOption : quixel ? SelectedQuixelCompressionOption : SelectedUnityCompressionOption).ToString()).ConfigureAwait(true);
        await _settings.SetAsync(fab ? "marketplaces.fab.enabled.v1" : quixel ? "marketplaces.quixel.enabled.v1" : "marketplaces.unity.enabled.v1",
            (fab ? FabMarketplaceEnabled : quixel ? QuixelMarketplaceEnabled : UnityMarketplaceEnabled).ToString()).ConfigureAwait(true);
        if (fab)
        {
            await _settings.SetAsync(FabNativeCacheMaximumGigabytesSetting, FabNativeCacheMaximumGigabytes.Trim()).ConfigureAwait(true);
            EpicVaultNativeCacheStatus = cacheBytes > 0 ? $"Cache de reconstruction prêt · limite {FormatBytes(cacheBytes)}." : "Cache de reconstruction prêt · taille illimitée.";
        }
        StatusMessage = $"Paramètres {market} enregistrés. Les autres marketplaces ne sont pas modifiées.";
    }
    private ICommand? _saveAppearanceSettingsCommand;
    public ICommand SaveAppearanceSettingsCommand => _saveAppearanceSettingsCommand ??= new AsyncRelayCommand(async () =>
    {
        await _settings.SetAsync("interface.theme.v1", InterfaceTheme).ConfigureAwait(true);
        await _interfaceWrite.ConfigureAwait(true);
        AppearanceStatus = "Thème et préférences d’affichage enregistrés.";
    }, onError: ShowError);
}
