using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private RemoteFabAsset[]? _countedCatalog;
    private int _cachedFabCount;
    private int _cachedQuixelCount;

    public bool HasCachedSelectedRemoteCatalog
    {
        get
        {
            if (!ReferenceEquals(_countedCatalog, _remoteFabCatalog))
            {
                _cachedFabCount = 0; _cachedQuixelCount = 0;
                foreach (var asset in _remoteFabCatalog)
                    if (IsQuixelAsset(asset)) _cachedQuixelCount++; else if (IsLauncherFabAsset(asset)) _cachedFabCount++;
                _countedCatalog = _remoteFabCatalog;
            }
            return IsLibraryQuixelSelected ? _cachedQuixelCount > 0 : _cachedFabCount > 0;
        }
    }

    public bool IsHistoricalMegascansSearchAvailable => _quixelClient is not QuixelMegascansApiClient { IsCatalogSearchConfigured: false };
    public string RemoteCatalogNotice => IsLibraryQuixelSelected && !IsHistoricalMegascansSearchAvailable
        ? "Megascans historique : actualisation désactivée, aucune clé de recherche autorisée configurée. Le catalogue déjà en cache reste accessible. Les fiches acquises via Fab restent une source distincte."
        : (IsLibraryQuixelSelected ? !IsQuixelConnected : IsLibraryFabSelected && !IsDirectFabConnected) && HasCachedSelectedRemoteCatalog
            ? "Mode hors ligne : catalogue en cache. Reconnectez cette source pour actualiser ou télécharger."
            : string.Empty;
}
