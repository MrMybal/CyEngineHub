using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows.Input;
using Avalonia.Threading;
using Avalonia.Media;
using CyEngineHub.Build.Commands;
using CyEngineHub.Build.Execution;
using CyEngineHub.Build.Models;
using CyEngineHub.Build.Presets;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Settings;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Desktop.Services;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Epic.Authentication;
using CyEngineHub.Epic.Local;
using CyEngineHub.Epic.Models;
using CyEngineHub.Epic.Persistence;
using CyEngineHub.Engines;
using CyEngineHub.Extensions;
using CyEngineHub.Fab;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Library;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;
using CyEngineHub.Git.Analysis;
using CyEngineHub.Git.Execution;
using CyEngineHub.Git.Models;
using CyEngineHub.Git.Persistence;
using CyEngineHub.Infrastructure.AppData;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Plugins.Discovery;
using CyEngineHub.Plugins.Installation;
using CyEngineHub.Plugins.Models;
using CyEngineHub.Plugins.Persistence;
using CyEngineHub.Projects.Detection;
using CyEngineHub.Projects.Creation;
using CyEngineHub.Projects.Models;
using CyEngineHub.Storage.Analysis;
using CyEngineHub.Storage.Cleanup;
using CyEngineHub.Storage.Models;
using CyEngineHub.Storage.Persistence;
using CyEngineHub.Toolchains.Detection;
using CyEngineHub.Toolchains.Models;
using CyEngineHub.Toolchains.Persistence;
using CyEngineHub.Updates;
using CyEngineHub.Unreal.Detection;
using CyEngineHub.Unreal.Acquisition;
using CyEngineHub.Unreal.Models;
using CyEngineHub.Unreal.Registration;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    public const string EngineRootsSetting = "discovery.engineRoots";
    public const string ProjectRootsSetting = "discovery.projectRoots";
    public const string PluginLibraryRootsSetting = "plugins.libraryRoots";
    public const string FabLocalLibraryRootsSetting = "fab.localLibraryRoots";
    public const string FabLocalLibrarySnapshotSetting = "fab.localLibrarySnapshot";
    public const string LibraryDownloadRootSetting = "library.downloadRoot";
    public const string LibraryPackageCompressionSetting = "library.packageCompression.v1";
    public const string FabCollectionsSetting = "library.fab.collections.v1";
    public const string FabStoredDownloadsSetting = "library.fab.downloads.v1";
    public const string QuixelLibraryRootSetting = "library.quixel.root.v1";
    public const string FabAutoRefreshOnStartupSetting = "marketplaces.fab.autoRefreshOnStartup.v1";
    public const string QuixelAutoRefreshOnStartupSetting = "marketplaces.quixel.autoRefreshOnStartup.v1";
    public const string UnityAutoRefreshOnStartupSetting = "marketplaces.unity.autoRefreshOnStartup.v1";
    public const string FabPackageCompressionSetting = "marketplaces.fab.packageCompression.v1";
    public const string QuixelPackageCompressionSetting = "marketplaces.quixel.packageCompression.v1";
    public const string UnityPackageCompressionSetting = "marketplaces.unity.packageCompression.v1";
    public const string FabStorageProfileSetting = "marketplaces.fab.storageProfile.v1";
    public const string QuixelStorageProfileSetting = "marketplaces.quixel.storageProfile.v1";
    public const string UnityStorageProfileSetting = "marketplaces.unity.storageProfile.v1";
    public const string MarketplaceNativeStoresSetting = "marketplaces.nativeStores.v1";
    public const string FabNativeCacheMaximumGigabytesSetting = "marketplaces.fab.nativeCacheMaximumGigabytes.v1";
    public const string ProjectCreationProfilesSetting = "projects.creation.profiles.v1";
    public const string ConnectionModulesSetting = "connections.modules.v1";
    public const string UpdateRepositorySetting = "updates.githubRepository.v1";
    public const string AutoCheckStableUpdatesSetting = "updates.autoCheckStable.v1";
    public const string IncludePrereleaseUpdatesSetting = "updates.includePrereleases.v1";
    public const string OfficialUpdateRepository = "MrMybal/CyEngineHub";
    private const int LibraryPageSize = 120;

    private readonly SqliteCatalogStore _database;
    private readonly ISettingsStore _settings;
    private readonly WorkspacePreferencesService _workspacePreferencesService;
    private readonly IEngineCatalog _engineCatalog;
    private readonly IEngineModuleRegistry _engineModules;
    private readonly IHubExtensionCatalogService _hubExtensionCatalog;
    private readonly IHubExtensionInstallationService _hubExtensionInstallation;
    private readonly IHubExtensionExecutionService _hubExtensionExecution;
    private readonly IProjectCatalog _projectCatalog;
    private readonly EngineDiscoveryService _engineDiscovery;
    private readonly ProjectDiscoveryService _projectDiscovery;
    private readonly HubTaskScheduler _taskScheduler;
    private readonly UnrealBuildExecutor _buildExecutor;
    private readonly UnrealBuildPresetCatalog _buildPresetCatalog;
    private readonly IUnrealEngineRegistrationService _engineRegistration;
    private readonly GitRepositoryDiscoveryService _gitDiscovery;
    private readonly GitSnapshotCache _gitSnapshotCache;
    private readonly PluginDiscoveryService _pluginDiscovery;
    private readonly PluginSnapshotCache _pluginSnapshotCache;
    private readonly IPluginInstallationService _pluginInstallation;
    private readonly IToolchainInventoryService _toolchainInventory;
    private readonly ToolchainSnapshotCache _toolchainSnapshotCache;
    private readonly IStorageAnalysisService _storageAnalysis;
    private readonly StorageSnapshotCache _storageSnapshotCache;
    private readonly IStorageCleanupService _storageCleanup;
    private readonly IEpicLocalCatalogService _epicLocalCatalog;
    private readonly EpicSnapshotCache _epicSnapshotCache;
    private readonly IEpicAuthenticationService _epicAuthentication;
    private readonly IFabBackend _fabBackend;
    private readonly IDirectFabClient _directFabClient;
    private readonly IQuixelMegascansClient _quixelClient;
    private readonly IEpicBinaryEngineBackend _epicBinaryEngineBackend;
    private readonly IFabProjectContentImportService _fabProjectContentImport;
    private readonly FabInstallationScriptRegistry _fabInstallationScripts;
    private readonly FabInstallationScriptPreferenceStore _fabInstallationScriptPreferenceStore;
    private readonly IUnityFabAssetImportService _unityFabAssetImport;
    private readonly IGodotFabAssetImportService _godotFabAssetImport;
    private readonly ICryEngineFabAssetImportService _cryEngineFabAssetImport;
    private readonly IFabLocalContentLibraryService _fabLocalContentLibrary;
    private readonly RemoteImageCache _fabRemoteImageCache;
    private readonly RemoteImageCache _quixelRemoteImageCache;
    private readonly QuixelLibraryCacheStore _quixelLibraryCache;
    private readonly MarketplaceLibraryCacheStore _marketplaceLibraryCache;
    private readonly MarketplaceLibraryCacheStore _legacyMarketplaceLibraryCache;
    private readonly IUnrealSourceDownloadService _unrealSourceDownload;
    private readonly IProcessRunner _processRunner;
    private readonly IPlatformLauncher _platformLauncher;
    private readonly WindowsStartupService _windowsStartupService;
    private readonly IApplicationUpdateService _applicationUpdateService;
    private readonly ProjectPostCreationScriptRegistry _projectPostCreationScripts;
    private readonly ProjectCreationService _projectCreation;
    private readonly string _operationLogsDirectory;
    private readonly string _gitOperationLogsDirectory;
    private readonly string _pluginPackagesDirectory;
    private readonly string _builtInExtensionsDirectory;
    private readonly string _userExtensionsDirectory;
    private readonly string _installationScriptsDirectory;
    private readonly string _projectCreationScriptsDirectory;
    private readonly string _applicationUpdateDirectory;
    private readonly string _rawLogPath;
    private readonly object _rawLogSync = new();
    private int _fabInstallationPreferenceSaveVersion;
    private IReadOnlyList<FabInstallationScriptPreference> _fabInstallationScriptPreferences = [];
    private readonly AsyncRelayCommand _refreshCommand;
    private readonly AsyncRelayCommand _refreshGitCommand;
    private readonly AsyncRelayCommand _refreshPluginsCommand;
    private readonly AsyncRelayCommand _confirmPluginInstallCommand;
    private readonly AsyncRelayCommand _prepareLocalEnginePackageInstallCommand;
    private readonly AsyncRelayCommand _confirmLocalEnginePackageInstallCommand;
    private readonly AsyncRelayCommand _confirmLocalEnginePackageRollbackCommand;
    private readonly AsyncRelayCommand _confirmLocalEnginePackageRemovalCommand;
    private readonly AsyncRelayCommand _confirmLocalEnginePackageRestoreCommand;
    private readonly AsyncRelayCommand _refreshHubExtensionsCommand;
    private readonly AsyncRelayCommand _prepareHubExtensionInstallCommand;
    private readonly AsyncRelayCommand _confirmHubExtensionInstallCommand;
    private readonly AsyncRelayCommand _prepareHubExtensionExecutionCommand;
    private readonly AsyncRelayCommand _confirmHubExtensionExecutionCommand;
    private readonly AsyncRelayCommand _openHubExtensionWebsiteCommand;
    private readonly RelayCommand _openHubExtensionWindowCommand;
    private readonly AsyncRelayCommand _refreshToolchainsCommand;
    private readonly AsyncRelayCommand _refreshStorageCommand;
    private readonly AsyncRelayCommand _confirmStorageCleanupCommand;
    private readonly AsyncRelayCommand _refreshEpicCommand;
    private readonly AsyncRelayCommand _launchEpicLauncherCommand;
    private readonly AsyncRelayCommand _launchFabProjectCommand;
    private readonly AsyncRelayCommand _confirmFabImportCommand;
    private readonly AsyncRelayCommand _confirmFabRollbackCommand;
    private readonly AsyncRelayCommand _launchUnityFabProjectCommand;
    private readonly AsyncRelayCommand _confirmUnityFabImportCommand;
    private readonly AsyncRelayCommand _confirmUnityFabRollbackCommand;
    private readonly AsyncRelayCommand _launchGodotFabProjectCommand;
    private readonly AsyncRelayCommand _confirmGodotFabImportCommand;
    private readonly AsyncRelayCommand _confirmGodotFabRollbackCommand;
    private readonly AsyncRelayCommand _launchCryEngineFabProjectCommand;
    private readonly AsyncRelayCommand _confirmCryEngineFabImportCommand;
    private readonly AsyncRelayCommand _confirmCryEngineFabRollbackCommand;
    private readonly AsyncRelayCommand _refreshFabLocalLibraryCommand;
    private readonly AsyncRelayCommand _addFabLocalLibraryRootCommand;
    private readonly AsyncRelayCommand _refreshDirectFabLibraryCommand;
    private readonly AsyncRelayCommand _refreshQuixelLibraryCommand;
    private readonly AsyncRelayCommand _downloadSelectedFabAssetCommand;
    private readonly AsyncRelayCommand _downloadSelectedFabVersionsCommand;
    private readonly AsyncRelayCommand _installSelectedFabVersionsInProjectCommand;
    private readonly AsyncRelayCommand _installSelectedFabVersionsInEngineCommand;
    private readonly AsyncRelayCommand _confirmSourceEngineDownloadCommand;
    private readonly AsyncRelayCommand _refreshEpicEngineReleasesCommand;
    private readonly AsyncRelayCommand _downloadEpicEngineCommand;
    private readonly AsyncRelayCommand _updateSelectedEngineComponentsCommand;
    private readonly AsyncRelayCommand _refreshQuixelLocalLibraryCommand;
    private IReadOnlyList<UnrealEngineInstallation> _currentEngines = [];
    private IReadOnlyList<UnrealProject> _currentUnrealProjects = [];
    private IReadOnlyList<LocalEngineDiscoverySnapshot> _currentLocalSnapshots = [];
    private IReadOnlyList<LocalEngineInstallation> _currentLocalEngineInstallations = [];
    private IReadOnlyList<UnrealPluginDescriptor> _currentPlugins = [];
    private UnrealPluginDescriptor[] _projectCreationEnginePlugins = [];
    private LocalEnginePackage[] _currentLocalEnginePackages = [];
    private IReadOnlyList<LocalEnginePackageInventorySnapshot> _currentLocalPackageSnapshots = [];
    private WorkspacePreferences _workspacePreferences = WorkspacePreferences.Default;
    private EngineFamilyOptionViewModel? _selectedEngineTab;
    private EngineFamilyOptionViewModel? _selectedProjectTab;
    private EngineFamilyOptionViewModel? _selectedPluginTab;
    private bool _forceProjectDiscoveryOnce;
    private bool _isProjectCreatorVisible;
    private bool _isEngineDiagnosticsVisible;
    private bool _isEngineImporterVisible;
    private bool _isEngineInstallationEditing;
    private bool _isEngineRemovalConfirmationVisible;
    private bool _isEngineUninstallConfirmationVisible;
    private bool _isEngineUninstallActive;
    private string _engineManagementStatus = "Le moteur est géré par CyEngineHub.";
    private string _engineComponentUpdateStatus = "Sélectionnez Modifier pour ajouter des composants à cette installation.";
    private bool _isManualProjectAddVisible;
    private string _projectSearchQuery = string.Empty;
    private ProjectCreationPresetOptionViewModel? _selectedProjectCreationPreset;
    private ProjectCreationConfigurationOptionViewModel? _selectedProjectCreationConfiguration;
    private ProjectCreationEngineOptionViewModel? _selectedProjectCreationEngine;
    private readonly List<ProjectConfigurationProfile> _customProjectCreationProfiles = [];
    private bool _isProjectProfileEditorVisible;
    private string? _editingProjectProfileId;
    private string _projectProfileName = string.Empty;
    private string _projectProfileDescription = string.Empty;
    private string _projectProfilePluginSearchQuery = string.Empty;
    private string _projectCreationPluginSearchQuery = string.Empty;
    private ProjectCardViewModel? _selectedProjectProfileSourceProject;
    private string _projectCreationPluginCompatibilitySummary =
        "Sélectionnez un moteur Unreal pour inventorier ses plugins.";
    private int _projectCreationEnginePluginRefreshVersion;
    private string _newProjectName = string.Empty;
    private string _newProjectParentDirectory = string.Empty;
    private string _newProjectEngineAssociation = string.Empty;
    private string _newProjectPlugins = string.Empty;
    private string _newProjectDisabledPlugins = string.Empty;
    private PageSection _section = PageSection.Home;
    private PageSection _settingsReturnSection = PageSection.Home;
    private readonly Stack<PageSection> _backNavigation = [];
    private readonly Stack<PageSection> _forwardNavigation = [];
    private readonly RelayCommand _navigateBackCommand;
    private readonly RelayCommand _navigateForwardCommand;
    private bool _isHistoryNavigation;
    private bool _isEngineCardView = true;
    private bool _isProjectCardView = true;
    private EngineCardViewModel? _selectedEngineDetails;
    private LocalEngineInstallationCardViewModel? _selectedLocalEngineDetails;
    private ProjectCardViewModel? _selectedProjectDetails;
    private LocalEngineProjectCardViewModel? _selectedLocalProjectDetails;
    private SettingsCategoryOptionViewModel? _selectedSettingsCategory;
    private string _selectedConnectionPage = "Epic";
    private bool _isBusy;
    private string _statusMessage = "Initialisation…";
    private string _latestRawLogLine = "Initialisation du journal…";
    private string _localEngineDiscoverySummary =
        "Unity, Godot et CryEngine seront analysés avec les emplacements configurés.";
    private string _manualEnginePath = string.Empty;
    private string _manualProjectRoot = string.Empty;
    private string _roadmapTitle = string.Empty;
    private string _roadmapDescription = string.Empty;
    private string _activeBuildTitle = "Aucune opération";
    private string _buildSummary = "Les logs UBT/UAT apparaîtront ici.";
    private ProjectCardViewModel? _selectedPresetProject;
    private string? _editingPresetId;
    private string _presetName = string.Empty;
    private string _presetKind = nameof(UnrealBuildPresetKind.Target);
    private string _presetTarget = "{Project}Editor";
    private string _presetPlatform = "Win64";
    private string _presetConfiguration = "Development";
    private string _presetAction = nameof(UnrealBuildAction.Build);
    private string _presetMaximumParallelActions = string.Empty;
    private string _presetCookFlavor = string.Empty;
    private bool _presetAttachProject = true;
    private bool _presetCook = true;
    private bool _presetStage = true;
    private bool _presetPackage = true;
    private bool _presetArchive = true;
    private bool _presetPak = true;
    private bool _presetIoStore = true;
    private bool _presetCompressed;
    private bool _presetDistribution;
    private bool _presetIncludePrerequisites = true;
    private bool _isGitBusy;
    private string _gitStatusSummary = "Chargez le cache ou lancez une analyse manuelle.";
    private string _activeGitOperation = "Aucune opération Git";
    private bool _isPluginBusy;
    private string _pluginStatusSummary = "Chargez le cache ou lancez une analyse manuelle.";
    private string _manualPluginLibraryRoot = string.Empty;
    private PluginInstallTargetViewModel? _selectedPluginTarget;
    private PluginInstallPlan? _pendingPluginInstall;
    private string _pendingPluginInstallSummary = string.Empty;
    private string _manualLocalEnginePackageSource = string.Empty;
    private LocalEngineProjectCardViewModel? _selectedLocalPackageInstallProject;
    private LocalEnginePackageInstallPlan? _pendingLocalEnginePackageInstall;
    private string _pendingLocalEnginePackageInstallSummary = string.Empty;
    private LocalEnginePackageRollbackPlan? _pendingLocalEnginePackageRollback;
    private string _pendingLocalEnginePackageRollbackSummary = string.Empty;
    private LocalEnginePackageRemovalPlan? _pendingLocalEnginePackageRemoval;
    private string _pendingLocalEnginePackageRemovalSummary = string.Empty;
    private LocalEnginePackageRestorePlan? _pendingLocalEnginePackageRestore;
    private string _pendingLocalEnginePackageRestoreSummary = string.Empty;
    private bool _isHubExtensionBusy;
    private string _hubExtensionStatus = "Chargement du catalogue d’extensions…";
    private string _manualHubExtensionPackage = string.Empty;
    private string _hubExtensionInputPath = string.Empty;
    private string _hubExtensionOutputPath = string.Empty;
    private string _hubExtensionSourceEngine = nameof(EngineFamily.Unreal);
    private string _hubExtensionTargetEngine = nameof(EngineFamily.Unity);
    private HubExtensionCardViewModel? _selectedHubExtension;
    private string _unrealVersionBridgeStorageRoot = string.Empty;
    private string _unrealVersionBridgeProfile = "safe";
    private string _unrealVersionBridgeAssetProfileFile = string.Empty;
    private bool _unrealVersionBridgeKeepStaging;
    private bool _unrealVersionBridgeValidateOutput = true;
    private HubExtensionInstallPlan? _pendingHubExtensionInstall;
    private string _pendingHubExtensionInstallSummary = string.Empty;
    private HubExtensionExecutionPlan? _pendingHubExtensionExecution;
    private string _pendingHubExtensionExecutionSummary = string.Empty;
    private ToolchainSnapshot? _currentToolchainSnapshot;
    private bool _isToolchainBusy;
    private string _toolchainStatusSummary = "Chargez le cache ou lancez une analyse manuelle.";
    private StorageSnapshot? _currentStorageSnapshot;
    private bool _isStorageBusy;
    private string _storageStatusSummary = "Chargez le cache ou lancez une analyse manuelle.";
    private StorageCleanupPlan? _pendingStorageCleanup;
    private string _pendingStorageCleanupSummary = string.Empty;
    private EpicLocalSnapshot? _currentEpicSnapshot;
    private bool _isEpicBusy;
    private string _epicStatusSummary = "Chargez le cache ou lancez un inventaire local manuel.";
    private string _epicAuthenticationSummary = "Connexion Epic via le navigateur officiel.";
    private string _fabStatusSummary = "My Library s’ouvre sur le site officiel de Fab.";
    private string _engineBinaryDownloadSummary = "Connectez Epic pour charger les versions officielles.";
    private EpicBinaryEngineRelease? _selectedEpicEngineRelease;
    private string _epicEngineTargetDirectory = string.Empty;
    private double _epicEngineDownloadProgress;
    private bool _isEpicEngineDownloadActive;
    private string _sourceEngineDownloadStatus = "Préparez un clone GitHub officiel ou un fork UnrealEngine autorisé.";
    private string _sourceEngineRepository = "https://github.com/EpicGames/UnrealEngine.git";
    private string _sourceEngineBranch = "release";
    private string? _selectedSourceEngineVersion = "release";
    private string _sourceEngineVersionsStatus = "La liste officielle sera chargée à l’ouverture.";
    private string _sourceEngineTargetDirectory = string.Empty;
    private bool _sourceEngineShallowClone = true;
    private UnrealSourceDownloadPlan? _pendingSourceEngineDownload;
    private string _pendingSourceEngineDownloadSummary = string.Empty;
    private ProjectCardViewModel? _selectedFabProject;
    private string _manualFabContentSource = string.Empty;
    private FabProjectImportPlan? _pendingFabImport;
    private string _pendingFabImportSummary = string.Empty;
    private FabImportRollbackPlan? _pendingFabRollback;
    private string _pendingFabRollbackSummary = string.Empty;
    private string _fabProjectImportStatus = "Sélectionnez un projet puis utilisez Fab officiel ou un pack Unreal local extrait.";
    private LocalEngineProjectCardViewModel? _selectedUnityFabProject;
    private string _manualUnityFabContentSource = string.Empty;
    private UnityFabAssetImportPlan? _pendingUnityFabImport;
    private string _pendingUnityFabImportSummary = string.Empty;
    private UnityFabAssetRollbackPlan? _pendingUnityFabRollback;
    private string _pendingUnityFabRollbackSummary = string.Empty;
    private string _unityFabImportStatus = "Sélectionnez un projet Unity et un pack Fab/Unity local extrait.";
    private LocalEngineProjectCardViewModel? _selectedGodotFabProject;
    private string _manualGodotFabContentSource = string.Empty;
    private GodotFabAssetImportPlan? _pendingGodotFabImport;
    private string _pendingGodotFabImportSummary = string.Empty;
    private GodotFabAssetRollbackPlan? _pendingGodotFabRollback;
    private string _pendingGodotFabRollbackSummary = string.Empty;
    private string _godotFabImportStatus = "Sélectionnez un projet Godot et un pack de données Fab local extrait.";
    private LocalEngineProjectCardViewModel? _selectedCryEngineFabProject;
    private string _manualCryEngineFabContentSource = string.Empty;
    private CryEngineFabAssetImportPlan? _pendingCryEngineFabImport;
    private string _pendingCryEngineFabImportSummary = string.Empty;
    private CryEngineFabAssetRollbackPlan? _pendingCryEngineFabRollback;
    private string _pendingCryEngineFabRollbackSummary = string.Empty;
    private string _cryEngineFabImportStatus = "Sélectionnez un projet CryEngine et un pack de données Fab local extrait.";
    private string _manualFabLibraryRoot = string.Empty;
    private string _fabLocalLibraryStatus = "Ajoutez un dossier de packs extraits, puis lancez une analyse manuelle.";
    private DirectFabConnectionState _directFabState = DirectFabConnectionState.Disconnected;
    private string _directFabStatusSummary = "Compte Epic/Fab non connecté.";
    private string _directFabAccount = string.Empty;
    private string _fabAuthorizationCode = string.Empty;
    private bool _isFabLibraryRefreshing;
    private double _fabRefreshSpinnerAngle;
    private DispatcherTimer? _fabRefreshSpinnerTimer;
    private DateTimeOffset? _fabCatalogCachedAt;
    private QuixelConnectionState _quixelState = QuixelConnectionState.Disconnected;
    private string _quixelStatusSummary = "Compte Epic/Quixel non connecté.";
    private string _quixelAccount = string.Empty;
    private string _quixelAuthorizationCode = string.Empty;
    private bool _isQuixelLibraryRefreshing;
    private double _quixelRefreshSpinnerAngle;
    private DispatcherTimer? _quixelRefreshSpinnerTimer;
    private DateTimeOffset? _quixelCatalogCachedAt;
    private string _libraryDownloadRoot = string.Empty;
    private string _fabCacheDirectory = string.Empty;
    private string _fabDownloadDirectory = string.Empty;
    private string _quixelCacheDirectory = string.Empty;
    private string _quixelDownloadDirectory = string.Empty;
    private string _unityCacheDirectory = string.Empty;
    private string _unityDownloadDirectory = string.Empty;
    private string _fabNativeCacheMaximumGigabytes = "50";
    private string _epicVaultNativeCacheStatus = "Cache de reconstruction prêt · limite 50 Gio.";
    private MarketplaceCompressionOptionViewModel? _selectedFabCompressionOption;
    private MarketplaceCompressionOptionViewModel? _selectedQuixelCompressionOption;
    private MarketplaceCompressionOptionViewModel? _selectedUnityCompressionOption;
    private bool _isNativeStoreSourceEditorVisible;
    private MarketplaceNativeStoreKind _pendingNativeStoreKind = MarketplaceNativeStoreKind.EpicGamesStore;
    private string _pendingNativeStorePath = string.Empty;
    private bool _pendingNativeStoreConvertOnUse;
    private bool _autoRefreshFabOnStartup = true;
    private bool _autoRefreshQuixelOnStartup = true;
    private bool _autoRefreshUnityOnStartup = true;
    private bool _launchAtWindowsStartup;
    private string _windowsStartupStatus =
        "CyEngineHub reste disponible dans la zone de notification après fermeture.";
    private string _quixelLibraryRoot = string.Empty;
    private string _quixelLibraryStatus = "Bibliothèque Quixel Bridge non analysée.";
    private string _librarySearchQuery = string.Empty;
    private string _selectedLibrarySort = "Acquisition · récent";
    private string _selectedLibraryCategory = "Toutes les catégories";
    private string _selectedLibraryFolder = "Toutes les collections";
    private string _selectedLibraryEngineVersion = "Tous les moteurs";
    private string _selectedLibraryTag = "Tous les tags";
    private bool _isAdvancedLibraryFilterVisible;
    private bool _isLibraryInfiniteMode;
    private int _libraryInfiniteItemCount;
    private bool _isLibraryCompactCards;
    private bool _showHiddenFabAssets;
    private bool _isFabCollectionEditorVisible;
    private bool _isDeleteAllFabDownloadsConfirmationVisible;
    private string _fabCollectionSearchQuery = string.Empty;
    private string _newFabCollectionName = string.Empty;
    private string? _collectionEditorItemId;
    private string _collectionEditorItemName = string.Empty;
    private Action<string[]>? _collectionEditorApply;
    private string _fabInstallTargetSearchQuery = string.Empty;
    private bool _showOnlyCompatibleFabTargets = true;
    private readonly Dictionary<string, string[]> _fabCollections = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FabStoredDownloadRecord> _fabStoredDownloads = [];
    private readonly List<FabVersionDownloadViewModel> _fabVersionDownloads = [];
    private readonly Dictionary<Guid, FabVersionDownloadViewModel> _fabVersionDownloadsByTaskId = [];
    private readonly Dictionary<string, string> _libraryProductAssetIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, bool> _connectionModules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["epic"] = true,
        ["fab"] = true,
        ["git"] = true,
        ["unity"] = true,
        ["quixel"] = true,
    };
    private CancellationTokenSource? _libraryPresentationCancellation;
    private CancellationTokenSource? _remoteLibraryViewCancellation;
    private int _libraryPageIndex;
    private string _librarySource = "Fab";
    private RemoteFabAsset[] _remoteFabCatalog = [];
    private RemoteFabAsset[] _visibleRemoteFabCatalog = [];
    private int _librarySourceAssetCount;
    private MarketplaceLocalAssetIndex _libraryLocalIndex = MarketplaceLocalAssetIndex.Empty;
    private DateTimeOffset _libraryLocalIndexCreatedAt = DateTimeOffset.MinValue;

    private sealed record RemoteLibraryIndexEntry(
        RemoteFabAsset Asset,
        string Name,
        string Folder,
        string[] Collections,
        string[] Categories,
        string[] Engines,
        string AssetType,
        bool IsHidden,
        bool IsDownloaded);

    private sealed record RemoteLibraryViewResult(
        RemoteFabAsset[] VisibleAssets,
        string[] EngineFacets,
        string[] AssetTypeFacets,
        string[] CategoryFacets,
        string[] CollectionFacets,
        IReadOnlyDictionary<string, int> EngineCounts,
        IReadOnlyDictionary<string, int> AssetTypeCounts,
        IReadOnlyDictionary<string, int> CategoryCounts,
        IReadOnlyDictionary<string, int> CollectionCounts);
    private RemoteFabAsset? _selectedRemoteFabAsset;
    private RemoteFabAssetCardViewModel? _selectedRemoteFabAssetCard;
    private bool _isRemoteFabProductVisible;
    private bool _isRemoteFabProductLoading;
    private bool _isFabInstallOptionsVisible;
    private bool _isFabInstallationScriptConfigurationVisible;
    private bool _isFabDownloadOptionsVisible;
    private bool _isFabImagePreviewVisible;
    private bool _isFabDownloadActive;
    private double _fabDownloadProgress;
    private string _fabDownloadStatus = "Prêt";
    private ProjectCardViewModel? _selectedFabInstallProject;
    private EngineCardViewModel? _selectedFabInstallEngine;
    private FabInstallTargetOptionViewModel? _selectedFabInstallationScriptTarget;
    private bool _showFabProjectInstallTargets = true;
    private bool _showFabEngineInstallTargets = true;
    private string _rawLogText = string.Empty;
    private string _rawLogSearchQuery = string.Empty;
    private readonly List<RawLogEntryViewModel> _rawLogEntries = [];
    private string _updateRepository = OfficialUpdateRepository;
    private bool _autoCheckStableUpdates = true;
    private bool _includePrereleaseUpdates = ResolveApplicationVersion().Contains('-');
    private string _updateStatus = "Dépôt officiel · MrMybal/CyEngineHub";
    private bool _isUpdateCheckActive;
    private bool _isUpdateDownloadActive;
    private bool _isUpdateAvailablePopupVisible;
    private bool _isUpdateDownloadProgressVisible;
    private double _updateProgressPercent;
    private string _updateProgressStatus = "Préparation…";
    private string _updateProgressDetails = string.Empty;
    private ApplicationRelease? _availableUpdate;

    public MainWindowViewModel(
        SqliteCatalogStore database,
        ISettingsStore settings,
        IEngineCatalog engineCatalog,
        IEngineModuleRegistry engineModules,
        IHubExtensionCatalogService hubExtensionCatalog,
        IHubExtensionInstallationService hubExtensionInstallation,
        IHubExtensionExecutionService hubExtensionExecution,
        EngineDiscoveryService engineDiscovery,
        ProjectDiscoveryService projectDiscovery,
        HubTaskScheduler taskScheduler,
        UnrealBuildExecutor buildExecutor,
        UnrealBuildPresetCatalog buildPresetCatalog,
        IUnrealEngineRegistrationService engineRegistration,
        GitRepositoryDiscoveryService gitDiscovery,
        GitSnapshotCache gitSnapshotCache,
        PluginDiscoveryService pluginDiscovery,
        PluginSnapshotCache pluginSnapshotCache,
        IPluginInstallationService pluginInstallation,
        IToolchainInventoryService toolchainInventory,
        ToolchainSnapshotCache toolchainSnapshotCache,
        IStorageAnalysisService storageAnalysis,
        StorageSnapshotCache storageSnapshotCache,
        IStorageCleanupService storageCleanup,
        IEpicLocalCatalogService epicLocalCatalog,
        EpicSnapshotCache epicSnapshotCache,
        IEpicAuthenticationService epicAuthentication,
        IFabBackend fabBackend,
        IDirectFabClient directFabClient,
        IQuixelMegascansClient quixelClient,
        IEpicBinaryEngineBackend epicBinaryEngineBackend,
        IFabProjectContentImportService fabProjectContentImport,
        IUnityFabAssetImportService unityFabAssetImport,
        IGodotFabAssetImportService godotFabAssetImport,
        ICryEngineFabAssetImportService cryEngineFabAssetImport,
        IFabLocalContentLibraryService fabLocalContentLibrary,
        IUnrealSourceDownloadService unrealSourceDownload,
        IProcessRunner processRunner,
        IPlatformLauncher platformLauncher,
        IApplicationUpdateService applicationUpdateService,
        AppDataPaths appData)
        : this(
            database,
            settings,
            engineCatalog,
            (IProjectCatalog)database,
            engineModules,
            hubExtensionCatalog,
            hubExtensionInstallation,
            hubExtensionExecution,
            engineDiscovery,
            projectDiscovery,
            taskScheduler,
            buildExecutor,
            buildPresetCatalog,
            engineRegistration,
            gitDiscovery,
            gitSnapshotCache,
            pluginDiscovery,
            pluginSnapshotCache,
            pluginInstallation,
            toolchainInventory,
            toolchainSnapshotCache,
            storageAnalysis,
            storageSnapshotCache,
            storageCleanup,
            epicLocalCatalog,
            epicSnapshotCache,
            epicAuthentication,
            fabBackend,
            directFabClient,
            quixelClient,
            epicBinaryEngineBackend,
            fabProjectContentImport,
            unityFabAssetImport,
            godotFabAssetImport,
            cryEngineFabAssetImport,
            fabLocalContentLibrary,
            unrealSourceDownload,
            processRunner,
            platformLauncher,
            applicationUpdateService,
            appData)
    {
    }

    private MainWindowViewModel(
        SqliteCatalogStore database,
        ISettingsStore settings,
        IEngineCatalog engineCatalog,
        IProjectCatalog projectCatalog,
        IEngineModuleRegistry engineModules,
        IHubExtensionCatalogService hubExtensionCatalog,
        IHubExtensionInstallationService hubExtensionInstallation,
        IHubExtensionExecutionService hubExtensionExecution,
        EngineDiscoveryService engineDiscovery,
        ProjectDiscoveryService projectDiscovery,
        HubTaskScheduler taskScheduler,
        UnrealBuildExecutor buildExecutor,
        UnrealBuildPresetCatalog buildPresetCatalog,
        IUnrealEngineRegistrationService engineRegistration,
        GitRepositoryDiscoveryService gitDiscovery,
        GitSnapshotCache gitSnapshotCache,
        PluginDiscoveryService pluginDiscovery,
        PluginSnapshotCache pluginSnapshotCache,
        IPluginInstallationService pluginInstallation,
        IToolchainInventoryService toolchainInventory,
        ToolchainSnapshotCache toolchainSnapshotCache,
        IStorageAnalysisService storageAnalysis,
        StorageSnapshotCache storageSnapshotCache,
        IStorageCleanupService storageCleanup,
        IEpicLocalCatalogService epicLocalCatalog,
        EpicSnapshotCache epicSnapshotCache,
        IEpicAuthenticationService epicAuthentication,
        IFabBackend fabBackend,
        IDirectFabClient directFabClient,
        IQuixelMegascansClient quixelClient,
        IEpicBinaryEngineBackend epicBinaryEngineBackend,
        IFabProjectContentImportService fabProjectContentImport,
        IUnityFabAssetImportService unityFabAssetImport,
        IGodotFabAssetImportService godotFabAssetImport,
        ICryEngineFabAssetImportService cryEngineFabAssetImport,
        IFabLocalContentLibraryService fabLocalContentLibrary,
        IUnrealSourceDownloadService unrealSourceDownload,
        IProcessRunner processRunner,
        IPlatformLauncher platformLauncher,
        IApplicationUpdateService applicationUpdateService,
        AppDataPaths appData)
    {
        _database = database;
        _settings = settings;
        _workspacePreferencesService = new WorkspacePreferencesService(settings);
        _engineCatalog = engineCatalog;
        _projectCatalog = projectCatalog;
        _engineModules = engineModules;
        _hubExtensionCatalog = hubExtensionCatalog;
        _hubExtensionInstallation = hubExtensionInstallation;
        _hubExtensionExecution = hubExtensionExecution;
        _engineDiscovery = engineDiscovery;
        _projectDiscovery = projectDiscovery;
        _taskScheduler = taskScheduler;
        _buildExecutor = buildExecutor;
        _buildPresetCatalog = buildPresetCatalog;
        _engineRegistration = engineRegistration;
        _gitDiscovery = gitDiscovery;
        _gitSnapshotCache = gitSnapshotCache;
        _pluginDiscovery = pluginDiscovery;
        _pluginSnapshotCache = pluginSnapshotCache;
        _pluginInstallation = pluginInstallation;
        _toolchainInventory = toolchainInventory;
        _toolchainSnapshotCache = toolchainSnapshotCache;
        _storageAnalysis = storageAnalysis;
        _storageSnapshotCache = storageSnapshotCache;
        _storageCleanup = storageCleanup;
        _epicLocalCatalog = epicLocalCatalog;
        _epicSnapshotCache = epicSnapshotCache;
        _epicAuthentication = epicAuthentication;
        _fabBackend = fabBackend;
        _directFabClient = directFabClient;
        _quixelClient = quixelClient;
        _epicBinaryEngineBackend = epicBinaryEngineBackend;
        _fabProjectContentImport = fabProjectContentImport;
        _fabInstallationScripts = new FabInstallationScriptRegistry(
            [
                new UnrealContentPackInstallationScript(fabProjectContentImport),
                new UnrealPluginInstallationScript(pluginDiscovery, pluginInstallation),
                new UnrealProjectPluginInstallationScript(pluginDiscovery, pluginInstallation),
                new UnrealMegascansInstallationScript(
                    processRunner,
                    fabProjectContentImport,
                    Path.Combine(appData.RootDirectory, "Megascans", "ImportedVariants")),
                new UnrealRelocatedContentInstallationScript(
                    processRunner,
                    fabProjectContentImport,
                    Path.Combine(appData.RootDirectory, "RelocatedImports")),
                new UnrealVersionBridgeInstallationScript(
                    engineCatalog,
                    settings,
                    processRunner,
                    fabProjectContentImport,
                    Path.Combine(appData.RootDirectory, "VersionBridge", "Variants")),
            ],
            processRunner);
        _fabInstallationScriptPreferenceStore = new FabInstallationScriptPreferenceStore(settings);
        _unityFabAssetImport = unityFabAssetImport;
        _godotFabAssetImport = godotFabAssetImport;
        _cryEngineFabAssetImport = cryEngineFabAssetImport;
        _fabLocalContentLibrary = fabLocalContentLibrary;
        _unrealSourceDownload = unrealSourceDownload;
        _processRunner = processRunner;
        _projectPostCreationScripts = new ProjectPostCreationScriptRegistry(processRunner);
        _projectCreation = new ProjectCreationService(_projectPostCreationScripts);
        _platformLauncher = platformLauncher;
        _windowsStartupService = new WindowsStartupService();
        _applicationUpdateService = applicationUpdateService;
        DataDirectory = appData.RootDirectory;
        StorageMode = appData.IsPortable ? "Portable" : "Profil utilisateur";
        _operationLogsDirectory = Path.Combine(appData.LogsDirectory, "Operations");
        _gitOperationLogsDirectory = Path.Combine(appData.LogsDirectory, "Git");
        _pluginPackagesDirectory = Path.Combine(appData.RootDirectory, "PluginPackages");
        _builtInExtensionsDirectory = Path.Combine(AppContext.BaseDirectory, "Extensions");
        _userExtensionsDirectory = Path.Combine(appData.RootDirectory, "Extensions");
        _installationScriptsDirectory = Path.Combine(appData.RootDirectory, "InstallationScripts");
        _projectCreationScriptsDirectory = Path.Combine(appData.RootDirectory, "ProjectCreationScripts");
        _applicationUpdateDirectory = Path.Combine(appData.RootDirectory, "Updates");
        _rawLogPath = Path.Combine(appData.LogsDirectory, $"cyenginehub-raw-{DateTime.UtcNow:yyyy-MM-dd}.log");
        _libraryDownloadRoot = Path.Combine(appData.RootDirectory, "Library");
        _unrealVersionBridgeStorageRoot = Path.Combine(appData.RootDirectory, "VersionBridge", "Variants");
        _quixelLibraryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Megascans Library");
        _fabCacheDirectory = Path.Combine(appData.RootDirectory, "Cache", "Fab");
        _quixelCacheDirectory = Path.Combine(appData.RootDirectory, "Cache", "Megascans");
        _unityCacheDirectory = Path.Combine(appData.RootDirectory, "Cache", "Unity");
        _fabDownloadDirectory = Path.Combine(appData.RootDirectory, "Downloads", "Fab");
        _quixelDownloadDirectory = Path.Combine(appData.RootDirectory, "Downloads", "Megascans");
        _unityDownloadDirectory = Path.Combine(appData.RootDirectory, "Downloads", "Unity");
        _fabRemoteImageCache = new RemoteImageCache(() => Path.Combine(FabCacheDirectory, "Images"));
        _quixelRemoteImageCache = new RemoteImageCache(() => Path.Combine(QuixelCacheDirectory, "Images"));
        InitializeQuixelCollections();
        InitializeDerivedWorkspaces();
        _quixelLibraryCache = new QuixelLibraryCacheStore(
            Path.Combine(appData.RootDirectory, "Cache", "Quixel", "library-v1.json.gz"));
        _marketplaceLibraryCache = new MarketplaceLibraryCacheStore(source =>
            source.Equals("quixel", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(QuixelCacheDirectory, "Catalog")
                : source.Equals("unity", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(UnityCacheDirectory, "Catalog")
                    : Path.Combine(FabCacheDirectory, "Catalog"));
        _legacyMarketplaceLibraryCache = new MarketplaceLibraryCacheStore(
            Path.Combine(appData.RootDirectory, "Cache", "Marketplaces"));
        _selectedFabCompressionOption = FindCompressionOption(LibraryPackageStorageMode.ZipFastest);
        _selectedQuixelCompressionOption = FindCompressionOption(LibraryPackageStorageMode.ZipFastest);
        _selectedUnityCompressionOption = FindCompressionOption(LibraryPackageStorageMode.ZipFastest);
        FabLibraryLocations.Add(CreateMarketplaceLibraryLocation(FabLibraryLocations, _libraryDownloadRoot));
        QuixelLibraryLocations.Add(CreateMarketplaceLibraryLocation(QuixelLibraryLocations, _libraryDownloadRoot));
        UnityLibraryLocations.Add(CreateMarketplaceLibraryLocation(UnityLibraryLocations, _libraryDownloadRoot));
        LoadRawLog();

        RebuildEngineModuleCards();
        RebuildConnectionModuleCards();
        RebuildEngineTabs();
        foreach (var preset in _projectCreation.Presets)
        {
            ProjectCreationPresets.Add(new ProjectCreationPresetOptionViewModel(preset));
        }
        SelectedProjectCreationPreset = ProjectCreationPresets.FirstOrDefault();
        RebuildProjectCreationConfigurationOptions();
        RebuildProjectPostCreationScriptOptions();
        InitializeProjectComposition();
        InitializeProjectProfileManager();

        SettingsCategories.Add(new SettingsCategoryOptionViewModel(
            "connections",
            "Connexions",
            "Epic, Fab, Git et Unity"));
        SettingsCategories.Add(new SettingsCategoryOptionViewModel(
            "engines",
            "Moteurs",
            "Providers et emplacements"));
        SettingsCategories.Add(new SettingsCategoryOptionViewModel(
            "project-profiles",
            "Profils de projet",
            "Plugins et configuration Unreal"));
        SettingsCategories.Add(new SettingsCategoryOptionViewModel(
            "library",
            "Bibliothèque",
            "Téléchargements et catalogue"));
        SettingsCategories.Add(new SettingsCategoryOptionViewModel(
            "marketplaces",
            "Marketplaces",
            "Fab, Megascans et Unity"));
        SettingsCategories.Add(new SettingsCategoryOptionViewModel(
            "storage",
            "Stockage",
            "Données locales"));
        SettingsCategories.Add(new SettingsCategoryOptionViewModel(
            "advanced",
            "Avancé",
            "Logs et diagnostic"));
        SettingsCategories.Add(new SettingsCategoryOptionViewModel("ai", "IA / MCP", "Codex, Claude et OpenCode"));
        InitializeAiCommands();
        SelectedSettingsCategory = SettingsCategories.FirstOrDefault();

        ShowHomeCommand = new RelayCommand(() => ShowSection(PageSection.Home));
        NavigateBackCommand = _navigateBackCommand = new RelayCommand(
            NavigateBack,
            () => _backNavigation.Count > 0);
        NavigateForwardCommand = _navigateForwardCommand = new RelayCommand(
            NavigateForward,
            () => _forwardNavigation.Count > 0);
        ShowEnginesCommand = new RelayCommand(() => ShowSection(PageSection.Engines));
        ShowProjectsCommand = new RelayCommand(() => ShowSection(PageSection.Projects));
        ShowLibraryCommand = new RelayCommand(() => ShowSection(PageSection.Epic));
        ShowGitCommand = new RelayCommand(() => ShowSection(PageSection.Git));
        ShowTasksCommand = new RelayCommand(() => ShowSection(PageSection.Tasks));
        ShowSettingsCommand = new RelayCommand(OpenSettings);
        ConfigureMarketplaceCachesCommand = new RelayCommand(() =>
        {
            OpenSettings();
            SelectedSettingsCategory = SettingsCategories.First(category => category.Id == "marketplaces");
        });
        ShowEpicConnectionSettingsCommand = new RelayCommand(() => OpenConnectionSettings("Epic"));
        ShowFabConnectionSettingsCommand = new RelayCommand(() => OpenConnectionSettings("Fab"));
        ShowGitConnectionSettingsCommand = new RelayCommand(() => OpenConnectionSettings("Git"));
        ShowUnityConnectionSettingsCommand = new RelayCommand(() => OpenConnectionSettings("Unity"));
        ShowQuixelConnectionSettingsCommand = new RelayCommand(() => OpenConnectionSettings("Quixel"));
        CloseSettingsCommand = new RelayCommand(CloseSettings);
        ShowEngineCardsCommand = new RelayCommand(() => IsEngineCardView = true);
        ShowEngineListCommand = new RelayCommand(() => IsEngineCardView = false);
        ToggleEngineImporterCommand = new AsyncRelayCommand(
            ToggleEngineImporterAsync,
            onError: ShowError);
        ShowProjectCardsCommand = new RelayCommand(() => IsProjectCardView = true);
        ShowProjectListCommand = new RelayCommand(() => IsProjectCardView = false);
        BackFromEngineDetailsCommand = new RelayCommand(() =>
        {
            SetEngineInstallationEditing(false);
            ShowSection(PageSection.Engines);
        });
        BackFromProjectDetailsCommand = new RelayCommand(() => ShowSection(PageSection.Projects));
        ShowSelectedEnginePluginsCommand = new RelayCommand(OpenSelectedEnginePlugins);
        ShowSelectedEngineBuildsCommand = new RelayCommand(() => ShowSection(PageSection.Builds));
        ShowSelectedEngineToolchainsCommand = new RelayCommand(() => ShowSection(PageSection.Toolchains));
        RequestRemoveSelectedEngineCommand = new RelayCommand(() => IsEngineRemovalConfirmationVisible = true);
        CancelRemoveSelectedEngineCommand = new RelayCommand(() => IsEngineRemovalConfirmationVisible = false);
        ConfirmRemoveSelectedEngineCommand = new AsyncRelayCommand(RemoveSelectedEngineFromHubAsync, onError: ShowError);
        RequestUninstallSelectedEngineCommand = new RelayCommand(() => IsEngineUninstallConfirmationVisible = true);
        CancelUninstallSelectedEngineCommand = new RelayCommand(() => IsEngineUninstallConfirmationVisible = false);
        ConfirmUninstallSelectedEngineCommand = new AsyncRelayCommand(
            UninstallSelectedEngineAsync,
            () => !IsEngineUninstallActive,
            ShowError);
        ShowSelectedProjectPluginsCommand = new RelayCommand(OpenSelectedProjectPlugins);
        ShowSelectedProjectBuildsCommand = new RelayCommand(() => ShowSection(PageSection.Builds));
        ShowLogsCommand = new RelayCommand(() => ShowSection(PageSection.Logs));
        ShowPluginsCommand = new RelayCommand(() => ShowSection(PageSection.Plugins));
        ShowHubExtensionsCommand = new RelayCommand(() => ShowSection(PageSection.HubExtensions));
        ShowBuildsCommand = new RelayCommand(() => ShowSection(PageSection.Builds));
        ShowStorageCommand = new RelayCommand(() => ShowSection(PageSection.Storage));
        ShowToolchainsCommand = new RelayCommand(() => ShowSection(PageSection.Toolchains));
        ShowEpicCommand = ShowLibraryCommand;
        ShowLibraryFabCommand = new RelayCommand(() =>
        {
            var alreadySelected = IsLibraryFabSelected;
            SelectLibrarySource("Fab");
            if (alreadySelected)
            {
                ShowCachedRemoteFabPreview();
                QueueRemoteFabViewRefresh();
            }
            ShowSection(PageSection.Epic);
        });
        ShowLibraryUnityCommand = new RelayCommand(() =>
        {
            SelectLibrarySource("Unity");
            ShowSection(PageSection.Epic);
        });
        ShowLibraryQuixelCommand = new AsyncRelayCommand(async () =>
        {
            var alreadySelected = IsLibraryQuixelSelected;
            SelectLibrarySource("Quixel");
            if (alreadySelected)
            {
                ShowCachedRemoteFabPreview();
                QueueRemoteFabViewRefresh();
            }
            ShowSection(PageSection.Epic);
            if (IsQuixelConnected && !_remoteFabCatalog.Any(IsQuixelAsset))
            {
                await RefreshQuixelLibraryAsync().ConfigureAwait(true);
            }
        }, onError: ShowError);
        ShowLibraryLocalCommand = new RelayCommand(() =>
        {
            SelectLibrarySource("Local");
            ShowSection(PageSection.Epic);
        });
        _refreshCommand = new AsyncRelayCommand(
            RefreshCatalogAsync,
            () => !IsBusy,
            ShowError);
        RefreshCommand = _refreshCommand;
        _refreshGitCommand = new AsyncRelayCommand(
            RefreshGitAsync,
            () => !IsGitBusy,
            ShowError);
        RefreshGitCommand = _refreshGitCommand;
        _refreshPluginsCommand = new AsyncRelayCommand(
            RefreshPluginsAsync,
            () => !IsPluginBusy,
            ShowError);
        RefreshPluginsCommand = _refreshPluginsCommand;
        _confirmPluginInstallCommand = new AsyncRelayCommand(
            ConfirmPluginInstallAsync,
            () => PendingPluginInstall is not null && !IsPluginBusy,
            ShowError);
        ConfirmPluginInstallCommand = _confirmPluginInstallCommand;
        CancelPluginInstallCommand = new RelayCommand(CancelPendingPluginInstall);
        _prepareLocalEnginePackageInstallCommand = new AsyncRelayCommand(
            PrepareLocalEnginePackageInstallAsync,
            () => SelectedLocalPackageInstallProject is not null && !IsPluginBusy,
            ShowError);
        PrepareLocalEnginePackageInstallCommand = _prepareLocalEnginePackageInstallCommand;
        _confirmLocalEnginePackageInstallCommand = new AsyncRelayCommand(
            ConfirmLocalEnginePackageInstallAsync,
            () => PendingLocalEnginePackageInstall is not null && !IsPluginBusy,
            ShowError);
        ConfirmLocalEnginePackageInstallCommand = _confirmLocalEnginePackageInstallCommand;
        CancelLocalEnginePackageInstallCommand = new RelayCommand(CancelPendingLocalEnginePackageInstall);
        _confirmLocalEnginePackageRollbackCommand = new AsyncRelayCommand(
            ConfirmLocalEnginePackageRollbackAsync,
            () => PendingLocalEnginePackageRollback is not null && !IsPluginBusy,
            ShowError);
        ConfirmLocalEnginePackageRollbackCommand = _confirmLocalEnginePackageRollbackCommand;
        CancelLocalEnginePackageRollbackCommand = new RelayCommand(CancelPendingLocalEnginePackageRollback);
        _confirmLocalEnginePackageRemovalCommand = new AsyncRelayCommand(
            ConfirmLocalEnginePackageRemovalAsync,
            () => PendingLocalEnginePackageRemoval is not null && !IsPluginBusy,
            ShowError);
        ConfirmLocalEnginePackageRemovalCommand = _confirmLocalEnginePackageRemovalCommand;
        CancelLocalEnginePackageRemovalCommand = new RelayCommand(CancelPendingLocalEnginePackageRemoval);
        _confirmLocalEnginePackageRestoreCommand = new AsyncRelayCommand(
            ConfirmLocalEnginePackageRestoreAsync,
            () => PendingLocalEnginePackageRestore is not null && !IsPluginBusy,
            ShowError);
        ConfirmLocalEnginePackageRestoreCommand = _confirmLocalEnginePackageRestoreCommand;
        CancelLocalEnginePackageRestoreCommand = new RelayCommand(CancelPendingLocalEnginePackageRestore);
        _refreshHubExtensionsCommand = new AsyncRelayCommand(
            RefreshHubExtensionsAsync,
            () => !IsHubExtensionBusy,
            ShowError);
        RefreshHubExtensionsCommand = _refreshHubExtensionsCommand;
        _prepareHubExtensionInstallCommand = new AsyncRelayCommand(
            PrepareHubExtensionInstallAsync,
            () => !IsHubExtensionBusy,
            ShowError);
        PrepareHubExtensionInstallCommand = _prepareHubExtensionInstallCommand;
        _confirmHubExtensionInstallCommand = new AsyncRelayCommand(
            ConfirmHubExtensionInstallAsync,
            () => PendingHubExtensionInstall is not null && !IsHubExtensionBusy,
            ShowError);
        ConfirmHubExtensionInstallCommand = _confirmHubExtensionInstallCommand;
        CancelHubExtensionInstallCommand = new RelayCommand(CancelPendingHubExtensionInstall);
        _prepareHubExtensionExecutionCommand = new AsyncRelayCommand(
            PrepareHubExtensionExecutionAsync,
            () => SelectedHubExtension?.CanRun == true && !IsHubExtensionBusy,
            ShowError);
        PrepareHubExtensionExecutionCommand = _prepareHubExtensionExecutionCommand;
        _confirmHubExtensionExecutionCommand = new AsyncRelayCommand(
            ConfirmHubExtensionExecutionAsync,
            () => PendingHubExtensionExecution is not null && !IsHubExtensionBusy,
            ShowError);
        ConfirmHubExtensionExecutionCommand = _confirmHubExtensionExecutionCommand;
        CancelHubExtensionExecutionCommand = new RelayCommand(CancelPendingHubExtensionExecution);
        _openHubExtensionWebsiteCommand = new AsyncRelayCommand(
            OpenSelectedHubExtensionWebsiteAsync,
            () => SelectedHubExtension?.CanOpenWebsite == true && !IsHubExtensionBusy,
            ShowError);
        OpenHubExtensionWebsiteCommand = _openHubExtensionWebsiteCommand;
        _openHubExtensionWindowCommand = new RelayCommand(
            RequestSelectedHubExtensionWindow,
            () => SelectedHubExtension?.HasDedicatedWindow == true && !IsHubExtensionBusy);
        OpenHubExtensionWindowCommand = _openHubExtensionWindowCommand;
        _refreshToolchainsCommand = new AsyncRelayCommand(
            RefreshToolchainsAsync,
            () => !IsToolchainBusy,
            ShowError);
        RefreshToolchainsCommand = _refreshToolchainsCommand;
        _refreshStorageCommand = new AsyncRelayCommand(
            RefreshStorageAsync,
            () => !IsStorageBusy,
            ShowError);
        RefreshStorageCommand = _refreshStorageCommand;
        _confirmStorageCleanupCommand = new AsyncRelayCommand(
            ConfirmStorageCleanupAsync,
            () => PendingStorageCleanup is not null && !IsStorageBusy,
            ShowError);
        ConfirmStorageCleanupCommand = _confirmStorageCleanupCommand;
        CancelStorageCleanupCommand = new RelayCommand(CancelPendingStorageCleanup);
        _refreshEpicCommand = new AsyncRelayCommand(
            RefreshEpicAsync,
            () => !IsEpicBusy,
            ShowError);
        RefreshEpicCommand = _refreshEpicCommand;
        _launchEpicLauncherCommand = new AsyncRelayCommand(
            LaunchEpicLauncherAsync,
            () => CanLaunchEpicLauncher,
            ShowError);
        LaunchEpicLauncherCommand = _launchEpicLauncherCommand;
        ConnectEpicCommand = new AsyncRelayCommand(ConnectEpicLauncherAsync, onError: ShowError);
        ConnectFabCommand = new AsyncRelayCommand(ConnectFabViaLauncherAsync, onError: ShowError);
        OpenEpicSignInCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(_epicAuthentication.SignInUri, "Connexion Epic"),
            onError: ShowError);
        OpenEpicAccountCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(_epicAuthentication.AccountUri, "Compte Epic"),
            onError: ShowError);
        OpenEpicDeveloperPortalCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(_epicAuthentication.DeveloperPortalUri, "Portail Epic"),
            onError: ShowError);
        OpenFabLibraryCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(_fabBackend.LibraryUri, "Fab My Library"),
            onError: ShowError);
        OpenFabHomeCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(_fabBackend.HomeUri, "Fab"),
            onError: ShowError);
        OpenUnityAssetStoreCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(new Uri("https://assetstore.unity.com/"), "Unity Asset Store"),
            onError: ShowError);
        OpenQuixelWebsiteCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(new Uri("https://quixel.com/"), "Quixel"),
            onError: ShowError);
        _refreshQuixelLocalLibraryCommand = new AsyncRelayCommand(
            RefreshQuixelLocalLibraryAsync,
            onError: ShowError);
        RefreshQuixelLocalLibraryCommand = _refreshQuixelLocalLibraryCommand;
        OpenFabExportGuideCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(_fabBackend.ExportGuideUri, "Guide d’export Fab"),
            onError: ShowError);
        OpenEngineInstallationGuideCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(
                _epicBinaryEngineBackend.InstallationGuideUri,
                "Guide d’installation Unreal"),
            onError: ShowError);
        OpenLinuxToolchainGuideCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(
                new Uri("https://dev.epicgames.com/documentation/unreal-engine/linux-development-requirements-for-unreal-engine"),
                "Toolchains Linux Unreal Engine"),
            onError: ShowError);
        OpenUnrealSourceAccessGuideCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(
                _unrealSourceDownload.AccessGuideUri,
                "Accès aux sources Unreal"),
            onError: ShowError);
        OpenUnrealSourceRepositoryCommand = new AsyncRelayCommand(
            () => OpenOfficialUriAsync(
                _unrealSourceDownload.OfficialRepositoryUri,
                "Dépôt Unreal Engine"),
            onError: ShowError);
        _launchFabProjectCommand = new AsyncRelayCommand(
            LaunchSelectedFabProjectAsync,
            () => SelectedFabProject is not null && !IsEpicBusy,
            ShowError);
        LaunchFabProjectCommand = _launchFabProjectCommand;
        PrepareFabImportCommand = new AsyncRelayCommand(
            PrepareFabImportAsync,
            () => SelectedFabProject is not null && !IsEpicBusy,
            ShowError);
        _confirmFabImportCommand = new AsyncRelayCommand(
            ConfirmFabImportAsync,
            () => PendingFabImport is not null && !IsEpicBusy,
            ShowError);
        ConfirmFabImportCommand = _confirmFabImportCommand;
        CancelFabImportCommand = new RelayCommand(CancelPendingFabImport);
        _confirmFabRollbackCommand = new AsyncRelayCommand(
            ConfirmFabRollbackAsync,
            () => PendingFabRollback is not null && !IsEpicBusy,
            ShowError);
        ConfirmFabRollbackCommand = _confirmFabRollbackCommand;
        CancelFabRollbackCommand = new RelayCommand(CancelPendingFabRollback);
        _launchUnityFabProjectCommand = new AsyncRelayCommand(
            LaunchSelectedUnityFabProjectAsync,
            () => SelectedUnityFabProject is not null && !IsEpicBusy,
            ShowError);
        LaunchUnityFabProjectCommand = _launchUnityFabProjectCommand;
        PrepareUnityFabImportCommand = new AsyncRelayCommand(
            PrepareUnityFabImportAsync,
            () => SelectedUnityFabProject is not null && !IsEpicBusy,
            ShowError);
        _confirmUnityFabImportCommand = new AsyncRelayCommand(
            ConfirmUnityFabImportAsync,
            () => PendingUnityFabImport is not null && !IsEpicBusy,
            ShowError);
        ConfirmUnityFabImportCommand = _confirmUnityFabImportCommand;
        CancelUnityFabImportCommand = new RelayCommand(CancelPendingUnityFabImport);
        _confirmUnityFabRollbackCommand = new AsyncRelayCommand(
            ConfirmUnityFabRollbackAsync,
            () => PendingUnityFabRollback is not null && !IsEpicBusy,
            ShowError);
        ConfirmUnityFabRollbackCommand = _confirmUnityFabRollbackCommand;
        CancelUnityFabRollbackCommand = new RelayCommand(CancelPendingUnityFabRollback);
        _launchGodotFabProjectCommand = new AsyncRelayCommand(
            LaunchSelectedGodotFabProjectAsync,
            () => SelectedGodotFabProject is not null && !IsEpicBusy,
            ShowError);
        LaunchGodotFabProjectCommand = _launchGodotFabProjectCommand;
        PrepareGodotFabImportCommand = new AsyncRelayCommand(
            PrepareGodotFabImportAsync,
            () => SelectedGodotFabProject is not null && !IsEpicBusy,
            ShowError);
        _confirmGodotFabImportCommand = new AsyncRelayCommand(
            ConfirmGodotFabImportAsync,
            () => PendingGodotFabImport is not null && !IsEpicBusy,
            ShowError);
        ConfirmGodotFabImportCommand = _confirmGodotFabImportCommand;
        CancelGodotFabImportCommand = new RelayCommand(CancelPendingGodotFabImport);
        _confirmGodotFabRollbackCommand = new AsyncRelayCommand(
            ConfirmGodotFabRollbackAsync,
            () => PendingGodotFabRollback is not null && !IsEpicBusy,
            ShowError);
        ConfirmGodotFabRollbackCommand = _confirmGodotFabRollbackCommand;
        CancelGodotFabRollbackCommand = new RelayCommand(CancelPendingGodotFabRollback);
        _launchCryEngineFabProjectCommand = new AsyncRelayCommand(
            LaunchSelectedCryEngineFabProjectAsync,
            () => SelectedCryEngineFabProject is not null && !IsEpicBusy,
            ShowError);
        LaunchCryEngineFabProjectCommand = _launchCryEngineFabProjectCommand;
        PrepareCryEngineFabImportCommand = new AsyncRelayCommand(
            PrepareCryEngineFabImportAsync,
            () => SelectedCryEngineFabProject is not null && !IsEpicBusy,
            ShowError);
        _confirmCryEngineFabImportCommand = new AsyncRelayCommand(
            ConfirmCryEngineFabImportAsync,
            () => PendingCryEngineFabImport is not null && !IsEpicBusy,
            ShowError);
        ConfirmCryEngineFabImportCommand = _confirmCryEngineFabImportCommand;
        CancelCryEngineFabImportCommand = new RelayCommand(CancelPendingCryEngineFabImport);
        _confirmCryEngineFabRollbackCommand = new AsyncRelayCommand(
            ConfirmCryEngineFabRollbackAsync,
            () => PendingCryEngineFabRollback is not null && !IsEpicBusy,
            ShowError);
        ConfirmCryEngineFabRollbackCommand = _confirmCryEngineFabRollbackCommand;
        CancelCryEngineFabRollbackCommand = new RelayCommand(CancelPendingCryEngineFabRollback);
        _refreshFabLocalLibraryCommand = new AsyncRelayCommand(
            RefreshFabLocalLibraryAsync,
            () => !IsEpicBusy,
            ShowError);
        RefreshFabLocalLibraryCommand = _refreshFabLocalLibraryCommand;
        _addFabLocalLibraryRootCommand = new AsyncRelayCommand(
            AddFabLocalLibraryRootAsync,
            () => !IsEpicBusy,
            ShowError);
        AddFabLocalLibraryRootCommand = _addFabLocalLibraryRootCommand;
        OpenEmbeddedFabAuthorizationCommand = new AsyncRelayCommand(
            OpenEmbeddedFabAuthorizationAsync,
            () => !IsEpicBusy && !IsFabWebLoginActive && !IsDirectFabConnected);
        OpenDirectFabAuthorizationCommand = new AsyncRelayCommand(
            OpenDirectFabAuthorizationAsync,
            () => !IsFabWebLoginActive,
            onError: ShowError);
        ConnectDirectFabCommand = new AsyncRelayCommand(ConnectDirectFabAsync, onError: ShowError);
        DisconnectDirectFabCommand = new AsyncRelayCommand(DisconnectDirectFabAsync, onError: ShowError);
        _refreshDirectFabLibraryCommand = new AsyncRelayCommand(
            RefreshDirectFabLibraryAsync,
            () => IsDirectFabConnected && IsFabLibrarySourceAvailable && !IsFabLibraryRefreshing,
            ShowError);
        RefreshDirectFabLibraryCommand = _refreshDirectFabLibraryCommand;
        OpenQuixelAuthorizationCommand = new AsyncRelayCommand(
            OpenQuixelAuthorizationAsync,
            onError: ShowError);
        ConnectQuixelCommand = new AsyncRelayCommand(ConnectQuixelAsync, onError: ShowError);
        ConnectQuixelFromEpicCommand = new AsyncRelayCommand(
            ConnectQuixelFromEpicAsync,
            onError: ShowError);
        DisconnectQuixelCommand = new AsyncRelayCommand(DisconnectQuixelAsync, onError: ShowError);
        _refreshQuixelLibraryCommand = new AsyncRelayCommand(
            RefreshQuixelLibraryAsync,
            () => IsQuixelConnected && IsQuixelLibrarySourceAvailable && !IsQuixelLibraryRefreshing,
            ShowError);
        RefreshQuixelLibraryCommand = _refreshQuixelLibraryCommand;
        _downloadSelectedFabAssetCommand = new AsyncRelayCommand(
            DownloadSelectedFabAssetAsync,
            () => SelectedRemoteFabAsset?.PreferredProjectVersion is not null && IsMarketplaceAvailable(NormalizeAssetSource(SelectedRemoteFabAsset)),
            ShowError,
            allowConcurrentExecutions: true);
        DownloadSelectedFabAssetCommand = _downloadSelectedFabAssetCommand;
        OpenFabDownloadOptionsCommand = new RelayCommand(() =>
        {
            IsFabInstallOptionsVisible = false;
            IsFabDownloadOptionsVisible = true;
        });
        CloseFabDownloadOptionsCommand = new RelayCommand(() => IsFabDownloadOptionsVisible = false);
        OpenFabInstallOptionsCommand = new RelayCommand(() =>
        {
            _activeDerivedInstallArtifactId = null;
            if (SelectedRemoteFabAssetCard is not null)
            {
                RestoreSupplierInstallVersions(SelectedRemoteFabAssetCard);
            }
            IsFabDownloadOptionsVisible = false;
            IsFabInstallOptionsVisible = true;
            OnPropertyChanged(nameof(HasActiveDerivedInstall));
            OnPropertyChanged(nameof(FabInstallContentKindLabel));
            OnPropertyChanged(nameof(FabInstallContentKindReason));
            OnPropertyChanged(nameof(FabInstallModeGuidance));
        });
        CloseFabInstallOptionsCommand = new RelayCommand(() => IsFabInstallOptionsVisible = false);
        CloseFabInstallationScriptConfigurationCommand = new RelayCommand(() =>
            IsFabInstallationScriptConfigurationVisible = false);
        OpenFabImagePreviewCommand = new RelayCommand(() =>
        {
            if (SelectedRemoteFabAssetCard?.HasPreviewImage != true)
            {
                return;
            }
            IsFabInstallOptionsVisible = false;
            IsFabDownloadOptionsVisible = false;
            IsFabImagePreviewVisible = true;
        });
        CloseFabImagePreviewCommand = new RelayCommand(() => IsFabImagePreviewVisible = false);
        RefreshFabInstallationScriptsCommand = new AsyncRelayCommand(
            RefreshFabInstallationScriptsAsync,
            onError: ShowError);
        OpenFabInstallationScriptsFolderCommand = new AsyncRelayCommand(
            () => OpenFolderAsync(_installationScriptsDirectory),
            onError: ShowError);
        _downloadSelectedFabVersionsCommand = new AsyncRelayCommand(
            () => DownloadSelectedFabVersionsAsync(FabInstallDestination.Library),
            () => SelectedRemoteFabAsset is not null && IsMarketplaceAvailable(NormalizeAssetSource(SelectedRemoteFabAsset)),
            ShowError,
            allowConcurrentExecutions: true);
        DownloadSelectedFabVersionsCommand = _downloadSelectedFabVersionsCommand;
        InstallSelectedFabVersionsCommand = new AsyncRelayCommand(
            InstallSelectedFabVersionsIntoTargetsAsync,
            () => SelectedRemoteFabAsset is not null && !IsEpicBusy,
            ShowError);
        _installSelectedFabVersionsInProjectCommand = new AsyncRelayCommand(
            () => DownloadSelectedFabVersionsAsync(FabInstallDestination.Project),
            () => SelectedRemoteFabAsset is not null && SelectedFabInstallProject is not null && !IsEpicBusy,
            ShowError);
        InstallSelectedFabVersionsInProjectCommand = _installSelectedFabVersionsInProjectCommand;
        _installSelectedFabVersionsInEngineCommand = new AsyncRelayCommand(
            () => DownloadSelectedFabVersionsAsync(FabInstallDestination.Engine),
            () => SelectedRemoteFabAsset is not null && SelectedFabInstallEngine is not null && !IsEpicBusy,
            ShowError);
        InstallSelectedFabVersionsInEngineCommand = _installSelectedFabVersionsInEngineCommand;
        CloseRemoteFabProductCommand = new RelayCommand(CloseRemoteFabProduct);
        OpenSelectedRemoteFabListingCommand = new AsyncRelayCommand(
            OpenSelectedRemoteFabListingAsync,
            onError: ShowError);
        HideSelectedFabAssetCommand = new AsyncRelayCommand(
            ToggleSelectedFabAssetHiddenAsync,
            onError: ShowError);
        RequestDeleteAllFabDownloadsCommand = new RelayCommand(
            () => IsDeleteAllFabDownloadsConfirmationVisible = true);
        CancelDeleteAllFabDownloadsCommand = new RelayCommand(
            () => IsDeleteAllFabDownloadsConfirmationVisible = false);
        ConfirmDeleteAllFabDownloadsCommand = new AsyncRelayCommand(
            DeleteAllSelectedFabDownloadsAsync,
            () => HasSelectedFabStoredDownloads,
            ShowError);
        CloseFabCollectionEditorCommand = new RelayCommand(() => IsFabCollectionEditorVisible = false);
        SaveFabCollectionsCommand = new AsyncRelayCommand(SaveFabCollectionEditorAsync, onError: ShowError);
        AddFabCollectionCommand = new RelayCommand(AddFabCollectionFromEditor);
        SaveLibrarySettingsCommand = new AsyncRelayCommand(
            SaveLibrarySettingsAsync,
            onError: ShowError);
        SaveMarketplaceSettingsCommand = new AsyncRelayCommand(
            SaveMarketplaceSettingsAsync,
            onError: ShowError);
        SaveWindowsStartupSettingsCommand = new AsyncRelayCommand(
            SaveWindowsStartupSettingsAsync,
            onError: ShowError);
        SaveUpdateSettingsCommand = new AsyncRelayCommand(
            SaveUpdateSettingsAsync,
            onError: ShowError);
        CheckForUpdatesCommand = new AsyncRelayCommand(
            () => CheckForUpdatesAsync(showUpToDateStatus: true),
            onError: ShowUpdateError);
        PostponeUpdateCommand = new RelayCommand(() => IsUpdateAvailablePopupVisible = false);
        OpenAvailableUpdateReleaseCommand = new AsyncRelayCommand(
            OpenAvailableUpdateReleaseAsync,
            onError: ShowError);
        DownloadAndInstallUpdateCommand = new AsyncRelayCommand(
            DownloadAndInstallUpdateAsync,
            onError: ShowUpdateError);
        AddFabLibraryLocationCommand = new RelayCommand(() =>
            AddMarketplaceLibraryLocation(FabLibraryLocations));
        AddQuixelLibraryLocationCommand = new RelayCommand(() =>
            AddMarketplaceLibraryLocation(QuixelLibraryLocations));
        AddUnityLibraryLocationCommand = new RelayCommand(() =>
            AddMarketplaceLibraryLocation(UnityLibraryLocations));
        AddEpicNativeStoreSourceCommand = new RelayCommand(() =>
            OpenNativeStoreSourceEditor(MarketplaceNativeStoreKind.EpicGamesStore));
        AddMegascansNativeStoreSourceCommand = new RelayCommand(() =>
            OpenNativeStoreSourceEditor(MarketplaceNativeStoreKind.Megascans));
        CloseNativeStoreSourceEditorCommand = new RelayCommand(() =>
            IsNativeStoreSourceEditorVisible = false);
        ConfirmNativeStoreSourceCommand = new AsyncRelayCommand(
            ConfirmNativeStoreSourceAsync,
            onError: ShowError);
        TrimEpicVaultNativeCacheCommand = new AsyncRelayCommand(
            TrimEpicVaultNativeCacheAsync,
            onError: ShowError);
        SaveUnrealVersionBridgeSettingsCommand = new AsyncRelayCommand(
            SaveUnrealVersionBridgeSettingsAsync,
            onError: ShowError);
        PreviousLibraryPageCommand = new RelayCommand(ShowPreviousLibraryPage);
        NextLibraryPageCommand = new RelayCommand(ShowNextLibraryPage);
        ToggleAdvancedLibraryFilterCommand = new RelayCommand(() =>
        {
            IsAdvancedLibraryFilterVisible = !IsAdvancedLibraryFilterVisible;
        });
        ClearAdvancedLibraryFiltersCommand = new RelayCommand(() =>
        {
            ClearLibraryFacetSelections();
        });
        SelectAllLibraryEngineFacetsCommand = new RelayCommand(() => SetAllFacets(LibraryEngineFacets, true));
        SelectAllLibraryAssetTypeFacetsCommand = new RelayCommand(() => SetAllFacets(LibraryAssetTypeFacets, true));
        SelectAllLibraryCategoryFacetsCommand = new RelayCommand(() => SetAllFacets(LibraryCategoryFacets, true));
        SelectAllLibraryCollectionFacetsCommand = new RelayCommand(() => SetAllFacets(LibraryCollectionFacets, true));
        ShowLargeLibraryCardsCommand = new RelayCommand(() => IsLibraryCompactCards = false);
        ShowCompactLibraryCardsCommand = new RelayCommand(() => IsLibraryCompactCards = true);
        ShowPagedLibraryCommand = new RelayCommand(() => IsLibraryInfiniteMode = false);
        ShowInfiniteLibraryCommand = new RelayCommand(() => IsLibraryInfiniteMode = true);
        PrepareSourceEngineDownloadCommand = new AsyncRelayCommand(
            PrepareSourceEngineDownloadAsync,
            () => !IsEpicBusy,
            ShowError);
        _confirmSourceEngineDownloadCommand = new AsyncRelayCommand(
            ConfirmSourceEngineDownloadAsync,
            () => PendingSourceEngineDownload is not null && !IsEpicBusy,
            ShowError);
        ConfirmSourceEngineDownloadCommand = _confirmSourceEngineDownloadCommand;
        CancelSourceEngineDownloadCommand = new RelayCommand(CancelPendingSourceEngineDownload);
        _refreshEpicEngineReleasesCommand = new AsyncRelayCommand(
            RefreshEpicEngineReleasesAsync,
            () => !IsEpicBusy,
            ShowError);
        RefreshEpicEngineReleasesCommand = _refreshEpicEngineReleasesCommand;
        _downloadEpicEngineCommand = new AsyncRelayCommand(
            DownloadEpicEngineAsync,
            () => SelectedEpicEngineRelease is not null && !IsEpicBusy,
            ShowError);
        DownloadEpicEngineCommand = _downloadEpicEngineCommand;
        ToggleEngineInstallationEditCommand = new RelayCommand(() =>
            SetEngineInstallationEditing(!IsEngineInstallationEditing));
        _updateSelectedEngineComponentsCommand = new AsyncRelayCommand(
            UpdateSelectedEngineComponentsAsync,
            () => SelectedEngineDetails?.IsOfficialEpic == true &&
                  IsEngineInstallationEditing &&
                  !IsEpicBusy,
            ShowError);
        UpdateSelectedEngineComponentsCommand = _updateSelectedEngineComponentsCommand;
        AddEnginePathCommand = new AsyncRelayCommand(AddEnginePathAsync, onError: ShowError);
        AddProjectRootCommand = new AsyncRelayCommand(AddProjectRootAsync, onError: ShowError);
        AddPluginLibraryRootCommand = new AsyncRelayCommand(
            AddPluginLibraryRootAsync,
            onError: ShowError);
        ToggleAutoDiscoverProjectsCommand = new AsyncRelayCommand(
            ToggleAutoDiscoverProjectsAsync,
            onError: ShowError);
        ToggleLogsCommand = new AsyncRelayCommand(ToggleLogsAsync, onError: ShowError);
        RestoreHiddenProjectsCommand = new AsyncRelayCommand(
            RestoreHiddenProjectsAsync,
            onError: ShowError);
        RestoreHiddenEnginesCommand = new AsyncRelayCommand(
            RestoreHiddenEnginesAsync,
            onError: ShowError);
        ToggleProjectCreatorCommand = new RelayCommand(() =>
        {
            IsProjectCreatorVisible = !IsProjectCreatorVisible;
            if (IsProjectCreatorVisible)
            {
                RebuildProjectCreationCollections();
                PopulateProjectCreationPluginsFromOverrides();
            }
        });
        CloseProjectCreatorCommand = new RelayCommand(() =>
            IsProjectCreatorVisible = false);
        CreateProjectCommand = new AsyncRelayCommand(CreateProjectAsync, onError: ShowError);
        RefreshProjectCreationScriptsCommand = new AsyncRelayCommand(
            RefreshProjectCreationScriptsAsync,
            onError: ShowError);
        OpenProjectCreationScriptsFolderCommand = new AsyncRelayCommand(
            () => OpenFolderAsync(_projectCreationScriptsDirectory),
            onError: ShowError);
        NewProjectProfileCommand = new RelayCommand(OpenNewProjectProfile);
        EditProjectProfileCommand = new RelayCommand(OpenSelectedProjectProfile);
        CloseProjectProfileEditorCommand = new RelayCommand(() =>
            IsProjectProfileEditorVisible = false);
        SaveProjectProfileCommand = new AsyncRelayCommand(
            SaveProjectProfileAsync,
            onError: ShowError);
        DeleteProjectProfileCommand = new AsyncRelayCommand(
            DeleteProjectProfileAsync,
            onError: ShowError);
        ImportProjectProfileCommand = new AsyncRelayCommand(
            ImportProjectProfileAsync,
            onError: ShowError);
        AddProjectProfileIniSettingCommand = new RelayCommand(() =>
            ProjectProfileIniSettings.Add(new ProjectProfileIniSettingViewModel(
                new ProjectIniSetting(
                    "DefaultEngine.ini",
                    "/Script/Engine.RendererSettings",
                    "r.Setting",
                    "True"),
                RemoveProjectProfileIniSetting)));
        ToggleManualProjectAddCommand = new RelayCommand(() =>
        {
            IsManualProjectAddVisible = !IsManualProjectAddVisible;
        });
        ScanProjectImportCandidatesCommand = new AsyncRelayCommand(
            ScanProjectImportCandidatesAsync,
            onError: ShowError);
        ConfirmProjectImportCandidatesCommand = new AsyncRelayCommand(
            ConfirmProjectImportCandidatesAsync,
            onError: ShowError);
        RefreshRawLogCommand = new RelayCommand(LoadRawLog);
        OpenRawLogFolderCommand = new AsyncRelayCommand(
            () => OpenFolderAsync(Path.GetDirectoryName(_rawLogPath)!),
            onError: ShowError);
        ToggleEngineDiagnosticsCommand = new RelayCommand(() =>
        {
            IsEngineDiagnosticsVisible = !IsEngineDiagnosticsVisible;
        });
        NewPresetCommand = new RelayCommand(ResetPresetEditor);
        SavePresetCommand = new AsyncRelayCommand(SavePresetAsync, onError: ShowError);

        _taskScheduler.TaskChanged += OnTaskChanged;
    }

    public ObservableCollection<EngineCardViewModel> Engines { get; } = [];

    public ObservableCollection<FabInstallVersionOptionViewModel> FabInstallVersions { get; } = [];

    public BulkObservableCollection<FabInstallVersionOptionViewModel> CompatibleFabInstallVersions { get; } = [];

    public ObservableCollection<FabInstallTargetOptionViewModel> FabInstallTargets { get; } = [];

    public BulkObservableCollection<FabInstallTargetOptionViewModel> FilteredFabInstallTargets { get; } = [];

    public string FabInstallTargetResultsLabel =>
        $"{FilteredFabInstallTargets.Count:N0} résultat(s)";

    public string FabInstallVersionResultsLabel =>
        $"{CompatibleFabInstallVersions.Count:N0} variante(s) correspondant aux cibles";

    public bool HasSelectedFabInstallTargets => FabInstallTargets.Any(item => item.IsSelected);

    public bool HasCompatibleFabInstallVersions => CompatibleFabInstallVersions.Count > 0;

    public bool HasNoCompatibleFabInstallVersions =>
        HasSelectedFabInstallTargets && !HasCompatibleFabInstallVersions;

    public ObservableCollection<FabCollectionOptionViewModel> FabCollectionOptions { get; } = [];

    public ObservableCollection<FabStoredDownloadViewModel> SelectedFabStoredDownloads { get; } = [];

    public ObservableCollection<FabVersionDownloadViewModel> SelectedFabVersionDownloads { get; } = [];

    public ObservableCollection<string> AvailableSourceEngineVersions { get; } = new() { "release" };

    public ObservableCollection<EpicBinaryEngineRelease> AvailableEpicEngineReleases { get; } = [];

    public ObservableCollection<EngineModuleCardViewModel> EngineModules { get; } = [];

    public ObservableCollection<ConnectionModuleCardViewModel> ConnectionModules { get; } = [];

    public ObservableCollection<EngineFamilyOptionViewModel> EngineTabs { get; } = [];

    public ObservableCollection<LocalEngineInstallationCardViewModel> LocalEngineInstallations { get; } = [];

    public ObservableCollection<EngineCardViewModel> VisibleEngines { get; } = [];

    public ObservableCollection<LocalEngineInstallationCardViewModel> VisibleLocalEngineInstallations { get; } = [];

    public ObservableCollection<ProjectCardViewModel> Projects { get; } = [];

    public ObservableCollection<LocalEngineProjectCardViewModel> LocalEngineProjects { get; } = [];

    public ObservableCollection<ProjectCardViewModel> VisibleProjects { get; } = [];

    public ObservableCollection<LocalEngineProjectCardViewModel> VisibleLocalEngineProjects { get; } = [];

    public ObservableCollection<ProjectCreationPresetOptionViewModel> ProjectCreationPresets { get; } = [];

    public ObservableCollection<ProjectCreationConfigurationOptionViewModel> ProjectCreationConfigurations { get; } = [];

    public ObservableCollection<ProjectCreationEngineOptionViewModel> ProjectCreationEngines { get; } = [];

    public ObservableCollection<ProjectCreationCollectionOptionViewModel> ProjectCreationCollections { get; } = [];

    public ObservableCollection<ProjectPostCreationScriptOptionViewModel> ProjectPostCreationScripts { get; } = [];

    public ObservableCollection<ProjectProfilePluginOptionViewModel> ProjectProfilePlugins { get; } = [];

    public ObservableCollection<ProjectProfilePluginOptionViewModel> ProjectCreationPlugins { get; } = [];

    public ObservableCollection<ProjectProfileIniSettingViewModel> ProjectProfileIniSettings { get; } = [];

    public ObservableCollection<MarketplaceNativeStoreSourceViewModel> EpicNativeStoreSources { get; } = [];

    public ObservableCollection<MarketplaceNativeStoreSourceViewModel> MegascansNativeStoreSources { get; } = [];

    public ObservableCollection<ProjectImportCandidateViewModel> ProjectImportCandidates { get; } = [];

    public ObservableCollection<SettingsCategoryOptionViewModel> SettingsCategories { get; } = [];

    public ObservableCollection<MarketplaceCompressionOptionViewModel> MarketplaceCompressionOptions { get; } =
    [
        new(LibraryPackageStorageMode.ZipStore, "Stockage", "ZIP sans recompression"),
        new(LibraryPackageStorageMode.ZipFastest, "Rapide", "Priorité à la vitesse"),
        new(LibraryPackageStorageMode.ZipBalanced, "Moyen", "Équilibre vitesse et taille"),
        new(LibraryPackageStorageMode.ZipHigh, "Élevé", "Réduction de taille renforcée"),
        new(LibraryPackageStorageMode.ZipUltra, "Ultra", "Taille minimale, plus lent"),
    ];

    public ObservableCollection<MarketplaceLibraryLocationViewModel> FabLibraryLocations { get; } = [];

    public ObservableCollection<MarketplaceLibraryLocationViewModel> QuixelLibraryLocations { get; } = [];

    public ObservableCollection<MarketplaceLibraryLocationViewModel> UnityLibraryLocations { get; } = [];

    public ObservableCollection<string> UnrealVersionBridgeProfiles { get; } = ["safe", "extended"];

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

    public ObservableCollection<BuildLogItemViewModel> BuildLogs { get; } = [];

    public ObservableCollection<BuildPresetCardViewModel> BuildPresets { get; } = [];

    public ObservableCollection<GitRepositoryCardViewModel> GitRepositories { get; } = [];

    public ObservableCollection<GitLogItemViewModel> GitLogs { get; } = [];

    public ObservableCollection<PluginCardViewModel> Plugins { get; } = [];

    public ObservableCollection<LocalEnginePackageCardViewModel> LocalEnginePackages { get; } = [];

    public ObservableCollection<PluginCardViewModel> VisiblePlugins { get; } = [];

    public ObservableCollection<LocalEnginePackageCardViewModel> VisibleLocalEnginePackages { get; } = [];

    public ObservableCollection<LocalEngineProjectCardViewModel> LocalPackageInstallProjects { get; } = [];

    public ObservableCollection<LocalEnginePackageInstallHistoryCardViewModel> LocalEnginePackageInstallHistory { get; } = [];

    public ObservableCollection<LocalEnginePackageRemovalHistoryCardViewModel> LocalEnginePackageRemovalHistory { get; } = [];

    public ObservableCollection<HubExtensionCardViewModel> HubExtensions { get; } = [];

    public ObservableCollection<HubUiContributionButtonViewModel> HubGlobalToolbarContributions { get; } = [];

    public ObservableCollection<HubUiContributionButtonViewModel> HubEngineToolbarContributions { get; } = [];

    public ObservableCollection<HubUiContributionButtonViewModel> HubProjectToolbarContributions { get; } = [];

    public ObservableCollection<HubUiContributionButtonViewModel> HubPluginToolbarContributions { get; } = [];

    public ObservableCollection<PluginInstallTargetViewModel> PluginTargets { get; } = [];

    public ObservableCollection<ToolchainInstallationCardViewModel> ToolchainInstallations { get; } = [];

    public ObservableCollection<EngineToolchainCardViewModel> EngineToolchains { get; } = [];

    public ObservableCollection<EngineToolchainCardViewModel> SelectedEngineToolchains { get; } = [];

    public ObservableCollection<EnvironmentVariableCardViewModel> ToolchainEnvironmentVariables { get; } = [];

    public ObservableCollection<AndroidDeviceCardViewModel> AndroidDevices { get; } = [];

    public ObservableCollection<StorageEntryCardViewModel> StorageEntries { get; } = [];

    public ObservableCollection<StorageDriveCardViewModel> StorageDrives { get; } = [];

    public ObservableCollection<EpicArtifactCardViewModel> EpicArtifacts { get; } = [];

    public ObservableCollection<EpicCapabilityCardViewModel> EpicCapabilities { get; } = [];

    public ObservableCollection<FabImportHistoryCardViewModel> FabImportHistory { get; } = [];

    public ObservableCollection<LocalEngineProjectCardViewModel> UnityFabProjects { get; } = [];

    public ObservableCollection<UnityFabImportHistoryCardViewModel> UnityFabImportHistory { get; } = [];

    public ObservableCollection<LocalEngineProjectCardViewModel> GodotFabProjects { get; } = [];

    public ObservableCollection<GodotFabImportHistoryCardViewModel> GodotFabImportHistory { get; } = [];

    public ObservableCollection<LocalEngineProjectCardViewModel> CryEngineFabProjects { get; } = [];

    public ObservableCollection<CryEngineFabImportHistoryCardViewModel> CryEngineFabImportHistory { get; } = [];

    public BulkObservableCollection<FabLocalContentPackCardViewModel> FabLocalContentPacks { get; } = [];

    public BulkObservableCollection<RemoteFabAssetRowViewModel> PagedRemoteFabRows { get; } = [];
    public BulkObservableCollection<RawLogEntryViewModel> FilteredRawLogEntries { get; } = [];

    public ObservableCollection<QuixelLocalAssetCardViewModel> QuixelLocalAssets { get; } = [];

    public ObservableCollection<string> LibraryCategories { get; } = ["Toutes les catégories"];

    public ObservableCollection<string> LibraryFolders { get; } = ["Toutes les collections"];

    public ObservableCollection<string> LibraryEngineVersions { get; } = ["Tous les moteurs"];

    public ObservableCollection<string> LibraryTags { get; } = ["Tous les tags"];

    public BulkObservableCollection<LibraryFacetOptionViewModel> LibraryEngineFacets { get; } = [];

    public BulkObservableCollection<LibraryFacetOptionViewModel> LibraryAssetTypeFacets { get; } = [];

    public BulkObservableCollection<LibraryFacetOptionViewModel> LibraryCategoryFacets { get; } = [];

    public BulkObservableCollection<LibraryFacetOptionViewModel> LibraryCollectionFacets { get; } = [];

    public IReadOnlyList<string> LibrarySortOptions { get; } =
        ["Acquisition · récent", "Acquisition · ancien", "Nom · A → Z", "Nom · Z → A", "Catégorie", "Dossier / collection", "Versions moteur", "Téléchargeables d’abord"];

    public IReadOnlyList<string> PresetKinds { get; } = Enum.GetNames<UnrealBuildPresetKind>();

    public IReadOnlyList<string> PresetActions { get; } = Enum.GetNames<UnrealBuildAction>();

    public IReadOnlyList<string> HubExtensionEngineFamilies { get; } =
        [nameof(EngineFamily.Unreal), nameof(EngineFamily.Unity), nameof(EngineFamily.Godot), nameof(EngineFamily.CryEngine)];

    public ICommand ShowHomeCommand { get; }

    public ICommand NavigateBackCommand { get; }

    public ICommand NavigateForwardCommand { get; }

    public ICommand ShowEnginesCommand { get; }

    public ICommand ShowProjectsCommand { get; }

    public ICommand ShowLibraryCommand { get; }

    public ICommand ShowGitCommand { get; }

    public ICommand ShowPluginsCommand { get; }

    public ICommand ShowHubExtensionsCommand { get; }

    public ICommand ShowBuildsCommand { get; }

    public ICommand ShowStorageCommand { get; }

    public ICommand ShowToolchainsCommand { get; }

    public ICommand ShowEpicCommand { get; }

    public ICommand ShowLibraryFabCommand { get; }

    public ICommand ShowLibraryUnityCommand { get; }

    public ICommand ShowLibraryQuixelCommand { get; }

    public ICommand ShowLibraryLocalCommand { get; }

    public ICommand ShowTasksCommand { get; }

    public ICommand ShowSettingsCommand { get; }
    public ICommand ConfigureMarketplaceCachesCommand { get; }

    public ICommand ShowEpicConnectionSettingsCommand { get; }

    public ICommand ShowFabConnectionSettingsCommand { get; }

    public ICommand ShowGitConnectionSettingsCommand { get; }

    public ICommand ShowUnityConnectionSettingsCommand { get; }

    public ICommand ShowQuixelConnectionSettingsCommand { get; }

    public ICommand CloseSettingsCommand { get; }

    public ICommand CloseRemoteFabProductCommand { get; }

    public ICommand OpenSelectedRemoteFabListingCommand { get; }

    public ICommand SaveLibrarySettingsCommand { get; }

    public ICommand SaveMarketplaceSettingsCommand { get; }

    public ICommand SaveWindowsStartupSettingsCommand { get; }

    public ICommand SaveUpdateSettingsCommand { get; }

    public ICommand CheckForUpdatesCommand { get; }

    public ICommand PostponeUpdateCommand { get; }

    public ICommand OpenAvailableUpdateReleaseCommand { get; }

    public ICommand DownloadAndInstallUpdateCommand { get; }

    public ICommand AddFabLibraryLocationCommand { get; }

    public ICommand AddQuixelLibraryLocationCommand { get; }

    public ICommand AddUnityLibraryLocationCommand { get; }

    public ICommand AddEpicNativeStoreSourceCommand { get; }

    public ICommand AddMegascansNativeStoreSourceCommand { get; }

    public ICommand CloseNativeStoreSourceEditorCommand { get; }

    public ICommand ConfirmNativeStoreSourceCommand { get; }

    public ICommand TrimEpicVaultNativeCacheCommand { get; }

    public ICommand SaveUnrealVersionBridgeSettingsCommand { get; }

    public ICommand PreviousLibraryPageCommand { get; }

    public ICommand NextLibraryPageCommand { get; }

    public ICommand ToggleAdvancedLibraryFilterCommand { get; }

    public ICommand ClearAdvancedLibraryFiltersCommand { get; }

    public ICommand ShowLargeLibraryCardsCommand { get; }

    public ICommand ShowCompactLibraryCardsCommand { get; }

    public ICommand ShowPagedLibraryCommand { get; }

    public ICommand ShowInfiniteLibraryCommand { get; }

    public ICommand ShowEngineCardsCommand { get; }

    public ICommand ShowEngineListCommand { get; }

    public ICommand ToggleEngineImporterCommand { get; }

    public ICommand ShowProjectCardsCommand { get; }

    public ICommand ShowProjectListCommand { get; }

    public ICommand BackFromEngineDetailsCommand { get; }

    public ICommand BackFromProjectDetailsCommand { get; }

    public ICommand ShowSelectedEnginePluginsCommand { get; }

    public ICommand ShowSelectedEngineBuildsCommand { get; }

    public ICommand ShowSelectedEngineToolchainsCommand { get; }

    public ICommand RequestRemoveSelectedEngineCommand { get; }

    public ICommand CancelRemoveSelectedEngineCommand { get; }

    public ICommand ConfirmRemoveSelectedEngineCommand { get; }

    public ICommand RequestUninstallSelectedEngineCommand { get; }

    public ICommand CancelUninstallSelectedEngineCommand { get; }

    public ICommand ConfirmUninstallSelectedEngineCommand { get; }

    public ICommand ShowSelectedProjectPluginsCommand { get; }

    public ICommand ShowSelectedProjectBuildsCommand { get; }

    public ICommand ShowLogsCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand RefreshGitCommand { get; }

    public ICommand RefreshPluginsCommand { get; }

    public ICommand RefreshHubExtensionsCommand { get; }

    public ICommand PrepareHubExtensionInstallCommand { get; }

    public ICommand ConfirmHubExtensionInstallCommand { get; }

    public ICommand CancelHubExtensionInstallCommand { get; }

    public ICommand PrepareHubExtensionExecutionCommand { get; }

    public ICommand ConfirmHubExtensionExecutionCommand { get; }

    public ICommand CancelHubExtensionExecutionCommand { get; }

    public ICommand OpenHubExtensionWebsiteCommand { get; }

    public ICommand OpenHubExtensionWindowCommand { get; }

    public event EventHandler? ExtensionWindowRequested;

    public event EventHandler? FabAuthorizationCaptureRequested;

    public event EventHandler? RawLogScrollRequested;

    public ICommand RefreshToolchainsCommand { get; }

    public ICommand RefreshStorageCommand { get; }

    public ICommand RefreshEpicCommand { get; }

    public ICommand LaunchEpicLauncherCommand { get; }

    public ICommand ConnectEpicCommand { get; }

    public ICommand ConnectFabCommand { get; }

    public ICommand OpenEpicSignInCommand { get; }

    public ICommand OpenEpicAccountCommand { get; }

    public ICommand OpenEpicDeveloperPortalCommand { get; }

    public ICommand OpenFabLibraryCommand { get; }

    public ICommand OpenFabHomeCommand { get; }

    public ICommand OpenUnityAssetStoreCommand { get; }

    public ICommand OpenQuixelWebsiteCommand { get; }

    public ICommand RefreshQuixelLocalLibraryCommand { get; }

    public ICommand OpenFabExportGuideCommand { get; }

    public ICommand OpenEngineInstallationGuideCommand { get; }

    public ICommand OpenLinuxToolchainGuideCommand { get; }

    public ICommand OpenUnrealSourceAccessGuideCommand { get; }

    public ICommand OpenUnrealSourceRepositoryCommand { get; }

    public ICommand LaunchFabProjectCommand { get; }

    public ICommand PrepareFabImportCommand { get; }

    public ICommand ConfirmFabImportCommand { get; }

    public ICommand CancelFabImportCommand { get; }

    public ICommand ConfirmFabRollbackCommand { get; }

    public ICommand CancelFabRollbackCommand { get; }

    public ICommand LaunchUnityFabProjectCommand { get; }

    public ICommand PrepareUnityFabImportCommand { get; }

    public ICommand ConfirmUnityFabImportCommand { get; }

    public ICommand CancelUnityFabImportCommand { get; }

    public ICommand ConfirmUnityFabRollbackCommand { get; }

    public ICommand CancelUnityFabRollbackCommand { get; }

    public ICommand LaunchGodotFabProjectCommand { get; }

    public ICommand PrepareGodotFabImportCommand { get; }

    public ICommand ConfirmGodotFabImportCommand { get; }

    public ICommand CancelGodotFabImportCommand { get; }

    public ICommand ConfirmGodotFabRollbackCommand { get; }

    public ICommand CancelGodotFabRollbackCommand { get; }

    public ICommand LaunchCryEngineFabProjectCommand { get; }

    public ICommand PrepareCryEngineFabImportCommand { get; }

    public ICommand ConfirmCryEngineFabImportCommand { get; }

    public ICommand CancelCryEngineFabImportCommand { get; }

    public ICommand ConfirmCryEngineFabRollbackCommand { get; }

    public ICommand CancelCryEngineFabRollbackCommand { get; }

    public ICommand RefreshFabLocalLibraryCommand { get; }

    public ICommand AddFabLocalLibraryRootCommand { get; }

    public ICommand OpenDirectFabAuthorizationCommand { get; }

    public ICommand ConnectDirectFabCommand { get; }

    public ICommand DisconnectDirectFabCommand { get; }

    public ICommand RefreshDirectFabLibraryCommand { get; }

    public ICommand OpenQuixelAuthorizationCommand { get; }

    public ICommand ConnectQuixelCommand { get; }

    public ICommand ConnectQuixelFromEpicCommand { get; }

    public ICommand DisconnectQuixelCommand { get; }

    public ICommand RefreshQuixelLibraryCommand { get; }

    public ICommand DownloadSelectedFabAssetCommand { get; }

    public ICommand OpenFabInstallOptionsCommand { get; }

    public ICommand CloseFabInstallOptionsCommand { get; }

    public ICommand CloseFabInstallationScriptConfigurationCommand { get; }

    public ICommand OpenFabDownloadOptionsCommand { get; }

    public ICommand CloseFabDownloadOptionsCommand { get; }

    public ICommand OpenFabImagePreviewCommand { get; }

    public ICommand CloseFabImagePreviewCommand { get; }

    public ICommand RefreshFabInstallationScriptsCommand { get; }

    public ICommand OpenFabInstallationScriptsFolderCommand { get; }

    public ICommand DownloadSelectedFabVersionsCommand { get; }

    public ICommand InstallSelectedFabVersionsCommand { get; }

    public ICommand InstallSelectedFabVersionsInProjectCommand { get; }

    public ICommand InstallSelectedFabVersionsInEngineCommand { get; }

    public ICommand HideSelectedFabAssetCommand { get; }

    public ICommand RequestDeleteAllFabDownloadsCommand { get; }

    public ICommand CancelDeleteAllFabDownloadsCommand { get; }

    public ICommand ConfirmDeleteAllFabDownloadsCommand { get; }

    public ICommand CloseFabCollectionEditorCommand { get; }

    public ICommand SaveFabCollectionsCommand { get; }

    public ICommand AddFabCollectionCommand { get; }

    public ICommand SelectAllLibraryEngineFacetsCommand { get; }

    public ICommand SelectAllLibraryAssetTypeFacetsCommand { get; }

    public ICommand SelectAllLibraryCategoryFacetsCommand { get; }

    public ICommand SelectAllLibraryCollectionFacetsCommand { get; }

    public ICommand PrepareSourceEngineDownloadCommand { get; }

    public ICommand ConfirmSourceEngineDownloadCommand { get; }

    public ICommand CancelSourceEngineDownloadCommand { get; }

    public ICommand RefreshEpicEngineReleasesCommand { get; }

    public ICommand DownloadEpicEngineCommand { get; }

    public ICommand ToggleEngineInstallationEditCommand { get; }

    public ICommand UpdateSelectedEngineComponentsCommand { get; }

    public ICommand ConfirmStorageCleanupCommand { get; }

    public ICommand CancelStorageCleanupCommand { get; }

    public ICommand AddEnginePathCommand { get; }

    public ICommand AddProjectRootCommand { get; }

    public ICommand AddPluginLibraryRootCommand { get; }

    public ICommand ToggleAutoDiscoverProjectsCommand { get; }

    public ICommand ToggleLogsCommand { get; }

    public ICommand RestoreHiddenProjectsCommand { get; }

    public ICommand RestoreHiddenEnginesCommand { get; }

    public ICommand ToggleProjectCreatorCommand { get; }

    public ICommand CloseProjectCreatorCommand { get; }

    public ICommand CreateProjectCommand { get; }

    public ICommand RefreshProjectCreationScriptsCommand { get; }

    public ICommand OpenProjectCreationScriptsFolderCommand { get; }

    public ICommand NewProjectProfileCommand { get; }

    public ICommand EditProjectProfileCommand { get; }

    public ICommand CloseProjectProfileEditorCommand { get; }

    public ICommand SaveProjectProfileCommand { get; }

    public ICommand DeleteProjectProfileCommand { get; }

    public ICommand ImportProjectProfileCommand { get; }

    public ICommand AddProjectProfileIniSettingCommand { get; }

    public ICommand ToggleEngineDiagnosticsCommand { get; }

    public ICommand ToggleManualProjectAddCommand { get; }

    public ICommand ScanProjectImportCandidatesCommand { get; }

    public ICommand ConfirmProjectImportCandidatesCommand { get; }

    public ICommand RefreshRawLogCommand { get; }

    public ICommand OpenRawLogFolderCommand { get; }

    public ICommand ConfirmPluginInstallCommand { get; }

    public ICommand CancelPluginInstallCommand { get; }

    public ICommand PrepareLocalEnginePackageInstallCommand { get; }

    public ICommand ConfirmLocalEnginePackageInstallCommand { get; }

    public ICommand CancelLocalEnginePackageInstallCommand { get; }

    public ICommand ConfirmLocalEnginePackageRollbackCommand { get; }

    public ICommand CancelLocalEnginePackageRollbackCommand { get; }

    public ICommand ConfirmLocalEnginePackageRemovalCommand { get; }

    public ICommand CancelLocalEnginePackageRemovalCommand { get; }

    public ICommand ConfirmLocalEnginePackageRestoreCommand { get; }

    public ICommand CancelLocalEnginePackageRestoreCommand { get; }

    public ICommand NewPresetCommand { get; }

    public ICommand SavePresetCommand { get; }

    public string DataDirectory { get; }

    public string StorageMode { get; }

    public string ApplicationVersion { get; } = ResolveApplicationVersion();

    private static string ResolveApplicationVersion()
    {
        var informational = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(MainWindowViewModel).Assembly)
            ?.InformationalVersion.Split('+')[0];
        if (!string.IsNullOrWhiteSpace(informational)) return $"v{informational}";
        var version = typeof(MainWindowViewModel).Assembly.GetName().Version;
        return version is null
            ? "v0.0.0"
            : $"v{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }

    public bool IsHomeVisible => _section == PageSection.Home;

    public bool IsHomeNavigationActive => _section == PageSection.Home;

    public bool IsEnginesNavigationActive =>
        _section is PageSection.Engines or PageSection.EngineDetails;

    public bool IsProjectsNavigationActive =>
        _section is PageSection.Projects or PageSection.ProjectDetails;

    public bool IsLibraryNavigationActive => _section == PageSection.Epic;

    public bool IsGitNavigationActive => _section == PageSection.Git;

    public bool IsPluginsNavigationActive => _section == PageSection.Plugins;

    public bool IsExtensionsNavigationActive => _section == PageSection.HubExtensions;

    public bool IsStorageNavigationActive => _section == PageSection.Storage;

    public bool IsTasksNavigationActive => _section == PageSection.Tasks;

    public bool IsLogsNavigationActive => _section == PageSection.Logs;

    public bool IsSettingsNavigationActive => _section == PageSection.Settings;

    public bool IsEnginesVisible => _section == PageSection.Engines;

    public bool IsEngineDetailsVisible => _section == PageSection.EngineDetails;

    public bool IsProjectDetailsVisible => _section == PageSection.ProjectDetails;

    public bool IsProjectsVisible => _section == PageSection.Projects;

    public bool IsGitVisible => _section == PageSection.Git;

    public bool IsPluginsVisible => _section == PageSection.Plugins;

    public bool IsHubExtensionsVisible => _section == PageSection.HubExtensions;

    public bool IsToolchainsVisible => _section == PageSection.Toolchains;

    public bool IsStorageVisible => _section == PageSection.Storage;

    public bool IsLibraryVisible => _section == PageSection.Epic;

    public bool IsMainNavigationVisible => !IsLibraryVisible;

    public bool IsEpicVisible => _section == PageSection.Epic && !IsLibraryVisible;

    public bool IsTasksVisible => _section == PageSection.Tasks;

    public bool IsBuildsVisible => _section == PageSection.Builds;

    public bool IsSettingsVisible => _section == PageSection.Settings;

    public bool IsLogsVisible => _section == PageSection.Logs;

    public bool IsLogsNavigationVisible => !string.IsNullOrWhiteSpace(_rawLogPath);

    public bool IsRoadmapVisible => _section == PageSection.Roadmap;

    public bool IsEngineCardView
    {
        get => _isEngineCardView;
        private set
        {
            if (SetProperty(ref _isEngineCardView, value))
            {
                OnPropertyChanged(nameof(IsEngineListView));
                OnPropertyChanged(nameof(IsUnrealEngineCardViewVisible));
                OnPropertyChanged(nameof(IsLocalEngineCardViewVisible));
                OnPropertyChanged(nameof(IsUnrealEngineListViewVisible));
                OnPropertyChanged(nameof(IsLocalEngineListViewVisible));
            }
        }
    }

    public bool IsEngineListView => !IsEngineCardView;

    public bool IsUnrealEngineCardViewVisible => IsEngineCardView && IsUnrealEngineTabSelected;

    public bool IsLocalEngineCardViewVisible => IsEngineCardView && IsLocalEngineTabSelected;

    public bool IsUnrealEngineListViewVisible => IsEngineListView && IsUnrealEngineTabSelected;

    public bool IsLocalEngineListViewVisible => IsEngineListView && IsLocalEngineTabSelected;

    public bool IsProjectCardView
    {
        get => _isProjectCardView;
        private set
        {
            if (SetProperty(ref _isProjectCardView, value))
            {
                OnPropertyChanged(nameof(IsProjectListView));
                OnPropertyChanged(nameof(IsUnrealProjectCardViewVisible));
                OnPropertyChanged(nameof(IsLocalProjectCardViewVisible));
                OnPropertyChanged(nameof(IsUnrealProjectListViewVisible));
                OnPropertyChanged(nameof(IsLocalProjectListViewVisible));
            }
        }
    }

    public bool IsProjectListView => !IsProjectCardView;

    public bool IsUnrealProjectCardViewVisible => IsProjectCardView && IsUnrealProjectTabSelected;

    public bool IsLocalProjectCardViewVisible => IsProjectCardView && IsLocalProjectTabSelected;

    public bool IsUnrealProjectListViewVisible => IsProjectListView && IsUnrealProjectTabSelected;

    public bool IsLocalProjectListViewVisible => IsProjectListView && IsLocalProjectTabSelected;

    public EngineCardViewModel? SelectedEngineDetails
    {
        get => _selectedEngineDetails;
        private set
        {
            if (SetProperty(ref _selectedEngineDetails, value))
            {
                OnPropertyChanged(nameof(IsUnrealEngineDetailsSelected));
                NotifySelectedEngineManagementProperties();
            }
        }
    }

    public LocalEngineInstallationCardViewModel? SelectedLocalEngineDetails
    {
        get => _selectedLocalEngineDetails;
        private set
        {
            if (SetProperty(ref _selectedLocalEngineDetails, value))
            {
                OnPropertyChanged(nameof(IsLocalEngineDetailsSelected));
                NotifySelectedEngineManagementProperties();
            }
        }
    }

    public bool IsUnrealEngineDetailsSelected => SelectedEngineDetails is not null;

    public bool IsLocalEngineDetailsSelected => SelectedLocalEngineDetails is not null;

    public string SelectedEngineManagementName =>
        SelectedEngineDetails?.DisplayName ?? SelectedLocalEngineDetails?.Name ?? "Moteur";

    public string SelectedEngineManagementPath =>
        SelectedEngineDetails?.Engine.RootPath ?? SelectedLocalEngineDetails?.Installation.RootPath ?? string.Empty;

    public bool IsEngineRemovalConfirmationVisible
    {
        get => _isEngineRemovalConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isEngineRemovalConfirmationVisible, value))
            {
                NotifyFabOptionsModalState();
            }
        }
    }

    public bool IsEngineUninstallConfirmationVisible
    {
        get => _isEngineUninstallConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isEngineUninstallConfirmationVisible, value))
            {
                NotifyFabOptionsModalState();
            }
        }
    }

    public bool IsEngineUninstallActive
    {
        get => _isEngineUninstallActive;
        private set => SetProperty(ref _isEngineUninstallActive, value);
    }

    public string EngineManagementStatus
    {
        get => _engineManagementStatus;
        private set => SetProperty(ref _engineManagementStatus, value);
    }

    public bool IsEngineInstallationEditing
    {
        get => _isEngineInstallationEditing;
        private set
        {
            if (SetProperty(ref _isEngineInstallationEditing, value))
            {
                OnPropertyChanged(nameof(EngineInstallationEditLabel));
                _updateSelectedEngineComponentsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string EngineInstallationEditLabel => IsEngineInstallationEditing ? "Annuler" : "Modifier";

    public string EngineComponentUpdateStatus
    {
        get => _engineComponentUpdateStatus;
        private set => SetProperty(ref _engineComponentUpdateStatus, value);
    }

    public ProjectCardViewModel? SelectedProjectDetails
    {
        get => _selectedProjectDetails;
        private set
        {
            if (SetProperty(ref _selectedProjectDetails, value))
            {
                OnPropertyChanged(nameof(IsUnrealProjectDetailsSelected));
            }
        }
    }

    public LocalEngineProjectCardViewModel? SelectedLocalProjectDetails
    {
        get => _selectedLocalProjectDetails;
        private set
        {
            if (SetProperty(ref _selectedLocalProjectDetails, value))
            {
                OnPropertyChanged(nameof(IsLocalProjectDetailsSelected));
            }
        }
    }

    public bool IsUnrealProjectDetailsSelected => SelectedProjectDetails is not null;

    public bool IsLocalProjectDetailsSelected => SelectedLocalProjectDetails is not null;

    public SettingsCategoryOptionViewModel? SelectedSettingsCategory
    {
        get => _selectedSettingsCategory;
        set
        {
            if (SetProperty(ref _selectedSettingsCategory, value))
            {
                OnPropertyChanged(nameof(IsSettingsConnectionsSelected));
                OnPropertyChanged(nameof(IsSettingsEnginesSelected));
                OnPropertyChanged(nameof(IsSettingsProjectProfilesSelected));
                OnPropertyChanged(nameof(IsSettingsLibrarySelected));
                OnPropertyChanged(nameof(IsSettingsMarketplacesSelected));
                OnPropertyChanged(nameof(IsSettingsStorageSelected));
                OnPropertyChanged(nameof(IsSettingsAdvancedSelected));
                OnPropertyChanged(nameof(IsSettingsAiSelected));
            }
        }
    }

    public bool IsSettingsConnectionsSelected => SelectedSettingsCategory?.Id == "connections";

    public bool IsEpicConnectionPageSelected => _selectedConnectionPage == "Epic";

    public bool IsFabConnectionPageSelected => _selectedConnectionPage == "Fab";

    public bool IsGitConnectionPageSelected => _selectedConnectionPage == "Git";

    public bool IsUnityConnectionPageSelected => _selectedConnectionPage == "Unity";

    public bool IsQuixelConnectionPageSelected => _selectedConnectionPage == "Quixel";

    public bool IsSettingsEnginesSelected => SelectedSettingsCategory?.Id == "engines";

    public bool IsSettingsLibrarySelected => SelectedSettingsCategory?.Id == "library";

    public bool IsSettingsMarketplacesSelected => SelectedSettingsCategory?.Id == "marketplaces";

    public bool IsSettingsStorageSelected => SelectedSettingsCategory?.Id == "storage";

    public bool IsSettingsAdvancedSelected => SelectedSettingsCategory?.Id == "advanced";

    public bool HasNoEngines => Engines.Count == 0 && LocalEngineInstallations.Count == 0;

    public bool HasNoProjects => Projects.Count == 0 && LocalEngineProjects.Count == 0;

    public bool HasNoLocalEngineInstallations => LocalEngineInstallations.Count == 0;

    public bool HasNoLocalEngineProjects => LocalEngineProjects.Count == 0;

    public bool HasNoTasks => Tasks.Count == 0;

    public double FooterProgressPercent =>
        Tasks.FirstOrDefault(task => task.CanCancel)?.ProgressPercent ?? 0;

    public string FooterActivityText
    {
        get
        {
            var task = Tasks.FirstOrDefault(item => item.CanCancel);
            if (task is null)
            {
                return _latestRawLogLine;
            }

            var details = string.IsNullOrWhiteSpace(task.Message)
                ? task.Status
                : task.Message;
            return $"[{task.Category}] {task.Title} · {details}";
        }
    }

    public bool HasNoBuildLogs => BuildLogs.Count == 0;

    public bool HasNoBuildPresets => BuildPresets.Count == 0;

    public bool HasNoGitRepositories => GitRepositories.Count == 0;

    public bool HasNoGitLogs => GitLogs.Count == 0;

    public bool HasNoPlugins => Plugins.Count == 0;

    public bool HasNoLocalEnginePackages => LocalEnginePackages.Count == 0;

    public bool HasLocalEnginePackages => LocalEnginePackages.Count > 0;

    public bool HasNoHubExtensions => HubExtensions.Count == 0;

    public bool HasPendingHubExtensionInstall => PendingHubExtensionInstall is not null;

    public bool HasPendingHubExtensionExecution => PendingHubExtensionExecution is not null;

    public bool HasPluginTargets => PluginTargets.Count > 0;

    public bool HasPendingPluginInstall => PendingPluginInstall is not null;

    public bool HasLocalPackageInstallTargets => LocalPackageInstallProjects.Count > 0;

    public bool HasPendingLocalEnginePackageInstall => PendingLocalEnginePackageInstall is not null;

    public bool HasLocalEnginePackageInstallHistory => LocalEnginePackageInstallHistory.Count > 0;

    public bool HasNoLocalEnginePackageInstallHistory => LocalEnginePackageInstallHistory.Count == 0;

    public bool HasPendingLocalEnginePackageRollback => PendingLocalEnginePackageRollback is not null;

    public bool HasPendingLocalEnginePackageRemoval => PendingLocalEnginePackageRemoval is not null;

    public bool HasPendingLocalEnginePackageRestore => PendingLocalEnginePackageRestore is not null;

    public bool HasLocalEnginePackageRemovalHistory => LocalEnginePackageRemovalHistory.Count > 0;

    public bool HasNoLocalEnginePackageRemovalHistory => LocalEnginePackageRemovalHistory.Count == 0;

    public bool HasNoToolchainInstallations => ToolchainInstallations.Count == 0;

    public bool HasNoEngineToolchains => EngineToolchains.Count == 0;

    public bool HasNoSelectedEngineToolchains => SelectedEngineToolchains.Count == 0;

    public bool HasNoAndroidDevices => AndroidDevices.Count == 0;

    public bool HasNoStorageEntries => StorageEntries.Count == 0;

    public bool HasNoStorageDrives => StorageDrives.Count == 0;

    public bool HasPendingStorageCleanup => PendingStorageCleanup is not null;

    public bool HasNoEpicArtifacts => EpicArtifacts.Count == 0;

    public bool HasPendingFabImport => PendingFabImport is not null;

    public bool HasPendingFabRollback => PendingFabRollback is not null;

    public bool HasNoFabImportHistory => FabImportHistory.Count == 0;

    public bool HasPendingUnityFabImport => PendingUnityFabImport is not null;

    public bool HasPendingUnityFabRollback => PendingUnityFabRollback is not null;

    public bool HasNoUnityFabImportHistory => UnityFabImportHistory.Count == 0;

    public bool HasPendingGodotFabImport => PendingGodotFabImport is not null;

    public bool HasPendingGodotFabRollback => PendingGodotFabRollback is not null;

    public bool HasNoGodotFabImportHistory => GodotFabImportHistory.Count == 0;

    public bool HasPendingCryEngineFabImport => PendingCryEngineFabImport is not null;

    public bool HasPendingCryEngineFabRollback => PendingCryEngineFabRollback is not null;

    public bool HasNoCryEngineFabImportHistory => CryEngineFabImportHistory.Count == 0;

    public bool HasNoFabLocalContentPacks => FabLocalContentPacks.Count == 0;

    public bool HasPendingSourceEngineDownload => PendingSourceEngineDownload is not null;

    public bool CanLaunchEpicLauncher =>
        _currentEpicSnapshot?.LauncherPath is { } path && File.Exists(path);

    public int EngineCount => Engines.Count + LocalEngineInstallations.Count;

    public int ActiveEngineModuleCount => EngineModules.Count(module => module.IsActive);

    public bool IsProjectCreatorVisible
    {
        get => _isProjectCreatorVisible;
        set { if (SetProperty(ref _isProjectCreatorVisible, value)) NotifyFabOptionsModalState(); }
    }

    public bool IsEngineDiagnosticsVisible
    {
        get => _isEngineDiagnosticsVisible;
        set => SetProperty(ref _isEngineDiagnosticsVisible, value);
    }

    public bool IsEngineImporterVisible
    {
        get => _isEngineImporterVisible;
        set => SetProperty(ref _isEngineImporterVisible, value);
    }

    public bool IsManualProjectAddVisible
    {
        get => _isManualProjectAddVisible;
        set => SetProperty(ref _isManualProjectAddVisible, value);
    }

    public string ProjectSearchQuery
    {
        get => _projectSearchQuery;
        set
        {
            if (SetProperty(ref _projectSearchQuery, value))
            {
                ApplyEngineFamilyFilters();
            }
        }
    }

    public ProjectCreationPresetOptionViewModel? SelectedProjectCreationPreset
    {
        get => _selectedProjectCreationPreset;
        set
        {
            if (SetProperty(ref _selectedProjectCreationPreset, value) && value is not null)
            {
                if (SelectedProjectCreationConfiguration?.Profile.Engine != value.Preset.Engine)
                {
                    SelectedProjectCreationConfiguration = ProjectCreationConfigurations.FirstOrDefault(item =>
                        item.Profile.Engine == value.Preset.Engine);
                }
                ApplyProjectCreationPluginDefaults();
                OnPropertyChanged(nameof(IsUnrealProjectCreation));
                OnPropertyChanged(nameof(CanCreateProjectConfiguration));
            }
        }
    }

    public ProjectCreationConfigurationOptionViewModel? SelectedProjectCreationConfiguration
    {
        get => _selectedProjectCreationConfiguration;
        set
        {
            if (_isRebuildingProjectProfileOptions) return;
            if (SetProperty(ref _selectedProjectCreationConfiguration, value))
            {
                ApplyProjectCreationPluginDefaults();
                UpdateProjectCreationPluginCompatibilitySummary();
                OnPropertyChanged(nameof(CanDeleteSelectedProjectProfile));
            }
        }
    }

    public ProjectCreationEngineOptionViewModel? SelectedProjectCreationEngine
    {
        get => _selectedProjectCreationEngine;
        set
        {
            if (SetProperty(ref _selectedProjectCreationEngine, value) && value is not null)
            {
                NewProjectEngineAssociation = value.Association;
                _ = RefreshProjectCreationEnginePluginsAsync(value.Engine);
            }
        }
    }

    public bool IsUnrealProjectCreation =>
        SelectedProjectCreationPreset?.Preset.Engine == EngineFamily.Unreal;

    public bool HasProjectCreationCollections => ProjectCreationCollections.Count > 0;

    public bool HasProjectPostCreationScripts => ProjectPostCreationScripts.Count > 0;

    public bool IsProjectProfileEditorVisible
    {
        get => _isProjectProfileEditorVisible;
        private set { if (SetProperty(ref _isProjectProfileEditorVisible, value)) NotifyFabOptionsModalState(); }
    }

    public string ProjectProfileName
    {
        get => _projectProfileName;
        set => SetProperty(ref _projectProfileName, value);
    }

    public string ProjectProfileDescription
    {
        get => _projectProfileDescription;
        set => SetProperty(ref _projectProfileDescription, value);
    }

    public string ProjectProfilePluginSearchQuery
    {
        get => _projectProfilePluginSearchQuery;
        set
        {
            if (SetProperty(ref _projectProfilePluginSearchQuery, value))
            {
                ApplyProjectProfilePluginFilter();
            }
        }
    }

    public ProjectCardViewModel? SelectedProjectProfileSourceProject
    {
        get => _selectedProjectProfileSourceProject;
        set => SetProperty(ref _selectedProjectProfileSourceProject, value);
    }

    public string ProjectCreationPluginCompatibilitySummary
    {
        get => _projectCreationPluginCompatibilitySummary;
        private set => SetProperty(ref _projectCreationPluginCompatibilitySummary, value);
    }

    public string ProjectCreationPluginSearchQuery
    {
        get => _projectCreationPluginSearchQuery;
        set
        {
            if (SetProperty(ref _projectCreationPluginSearchQuery, value))
            {
                ApplyProjectCreationPluginFilter();
            }
        }
    }

    public int ProjectCreationPluginCount => ProjectCreationPlugins.Count;
    public string ProjectCreationPluginActivationSummary =>
        $"{ProjectCreationPlugins.Count(plugin => plugin.IsActive)} actifs / {ProjectCreationPluginCount} plugins · {VisibleProjectCreationPlugins.Count} affichés";
    public BulkObservableCollection<ProjectProfilePluginOptionViewModel> VisibleProjectCreationPlugins { get; } = [];
    public BulkObservableCollection<string> ProjectCreationPluginCategories { get; } = [];
    private string _selectedProjectCreationPluginCategory = "Toutes les catégories";
    public string SelectedProjectCreationPluginCategory
    {
        get => _selectedProjectCreationPluginCategory;
        set { if (SetProperty(ref _selectedProjectCreationPluginCategory, value)) ApplyProjectCreationPluginFilter(); }
    }
    private bool _showActiveProjectCreationPluginsOnly;
    public bool ShowActiveProjectCreationPluginsOnly
    {
        get => _showActiveProjectCreationPluginsOnly;
        set { if (SetProperty(ref _showActiveProjectCreationPluginsOnly, value)) ApplyProjectCreationPluginFilter(); }
    }

    public int ProjectCreationOverriddenPluginCount =>
        ProjectCreationPlugins.Count(plugin => plugin.State.HasValue);

    public bool CanDeleteSelectedProjectProfile =>
        IsEditingProjectFeaturePack ? _editingFeaturePackId is not null : !string.IsNullOrWhiteSpace(_editingProjectProfileId);

    public int ProjectProfilePluginCount => ProjectProfilePlugins.Count;

    public int ProjectProfileOverriddenPluginCount =>
        ProjectProfilePlugins.Count(plugin => plugin.State.HasValue);

    public string NewProjectName
    {
        get => _newProjectName;
        set
        {
            if (SetProperty(ref _newProjectName, value)) RebuildProjectIniPreview();
        }
    }

    public string NewProjectParentDirectory
    {
        get => _newProjectParentDirectory;
        set => SetProperty(ref _newProjectParentDirectory, value);
    }

    public string NewProjectEngineAssociation
    {
        get => _newProjectEngineAssociation;
        set => SetProperty(ref _newProjectEngineAssociation, value);
    }

    public string NewProjectPlugins
    {
        get => _newProjectPlugins;
        set => SetProperty(ref _newProjectPlugins, value);
    }

    public string NewProjectDisabledPlugins
    {
        get => _newProjectDisabledPlugins;
        set => SetProperty(ref _newProjectDisabledPlugins, value);
    }

    public bool AutoDiscoverProjects => _workspacePreferences.AutoDiscoverProjects;

    public string AutoDiscoverProjectsLabel => AutoDiscoverProjects
        ? "Détection automatique activée"
        : "Détection automatique désactivée";

    public bool ShowLogs => _workspacePreferences.ShowLogs;

    public string ShowLogsLabel => ShowLogs
        ? "Onglet Logs visible"
        : "Onglet Logs masqué";

    public string EpicConnectionStatus => _directFabState switch
    {
        DirectFabConnectionState.Connected => "EPIC · ✓ CONNECTÉ",
        DirectFabConnectionState.Unavailable => "EPIC · ! INDISPONIBLE",
        DirectFabConnectionState.Expired => "EPIC · ! SESSION EXPIRÉE",
        _ => "EPIC · × NON CONNECTÉ",
    };

    public string FabConnectionStatus => _directFabState switch
    {
        DirectFabConnectionState.Connected => "FAB · ✓ BIBLIOTHÈQUE DIRECTE",
        DirectFabConnectionState.Unavailable => "FAB · ! SERVICE INDISPONIBLE",
        DirectFabConnectionState.Expired => "FAB · ! SESSION EXPIRÉE",
        _ => "FAB · × NON CONNECTÉ",
    };

    public string GitConnectionStatus => GitRepositories.Count > 0
        ? "GIT · ✓ LOCAL"
        : "GIT · × À CONFIGURER";

    public string UnityStoreConnectionStatus => IsProviderEnabled("unity")
        ? "UNITY STORE · × À CONFIGURER"
        : "UNITY STORE · PROVIDER DÉSACTIVÉ";

    public IBrush EpicConnectionIndicator => _directFabState switch
    {
        DirectFabConnectionState.Connected => Brushes.LimeGreen,
        DirectFabConnectionState.Unavailable or DirectFabConnectionState.Expired => Brushes.Orange,
        _ => Brushes.IndianRed,
    };

    public IBrush FabConnectionIndicator => _directFabState switch
    {
        DirectFabConnectionState.Connected => Brushes.LimeGreen,
        DirectFabConnectionState.Unavailable or DirectFabConnectionState.Expired => Brushes.Orange,
        _ => Brushes.IndianRed,
    };

    public bool HasLocalFabIntegration =>
        FabLocalContentPackCount > 0 ||
        EpicVaultItemCount > 0 ||
        FabPluginPackageCount > 0;

    public IBrush GitConnectionIndicator => GitRepositories.Count > 0
        ? Brushes.LimeGreen
        : Brushes.IndianRed;

    public IBrush UnityConnectionIndicator => IsProviderEnabled("unity")
        ? Brushes.Orange
        : Brushes.IndianRed;

    public EngineFamilyOptionViewModel? SelectedEngineTab
    {
        get => _selectedEngineTab;
        set
        {
            if (SetProperty(ref _selectedEngineTab, value))
            {
                ApplyEngineFamilyFilters();
            }
        }
    }

    public EngineFamilyOptionViewModel? SelectedProjectTab
    {
        get => _selectedProjectTab;
        set
        {
            if (SetProperty(ref _selectedProjectTab, value))
            {
                ApplyEngineFamilyFilters();
            }
        }
    }

    public EngineFamilyOptionViewModel? SelectedPluginTab
    {
        get => _selectedPluginTab;
        set
        {
            if (SetProperty(ref _selectedPluginTab, value))
            {
                ApplyEngineFamilyFilters();
            }
        }
    }

    public bool IsUnrealEngineTabSelected => SelectedEngineTab?.Family == EngineFamily.Unreal;

    public bool IsLocalEngineTabSelected =>
        SelectedEngineTab is not null && SelectedEngineTab.Family != EngineFamily.Unreal;

    public bool IsUnrealProjectTabSelected => SelectedProjectTab?.Family == EngineFamily.Unreal;

    public bool IsLocalProjectTabSelected =>
        SelectedProjectTab is not null && SelectedProjectTab.Family != EngineFamily.Unreal;

    public bool IsUnrealPluginTabSelected => SelectedPluginTab?.Family == EngineFamily.Unreal;

    public bool IsLocalPackageTabSelected =>
        SelectedPluginTab is not null && SelectedPluginTab.Family != EngineFamily.Unreal;

    public int ProjectCount => Projects.Count + LocalEngineProjects.Count;

    public int VisibleProjectCount => VisibleProjects.Count + VisibleLocalEngineProjects.Count;

    public int LocalEngineInstallationCount => LocalEngineInstallations.Count;

    public int LocalEngineProjectCount => LocalEngineProjects.Count;

    public int GitRepositoryCount => GitRepositories.Count;

    public int PluginCount => Plugins.Count + LocalEnginePackages.Count;

    public int ToolchainProblemCount => _currentToolchainSnapshot?.Engines.Sum(engine =>
        engine.Checks.Count(check => check.Health != ToolchainHealth.Ready)) ?? 0;

    public int StorageWarningCount =>
        (_currentStorageSnapshot?.Entries.Count(entry => entry.Health != StorageHealth.Ready) ?? 0) +
        (_currentStorageSnapshot?.Drives.Count(drive => drive.IsLowSpace) ?? 0);

    public int EpicArtifactCount => EpicArtifacts.Count;

    public int EpicVaultItemCount => _currentEpicSnapshot?.VaultItemCount ?? 0;

    public int FabPluginPackageCount => _currentEpicSnapshot?.FabPluginPackageCount ?? 0;

    public int FabLocalContentPackCount => FabLocalContentPacks.Count;

    public string EpicLauncherStatus => _currentEpicSnapshot switch
    {
        { IsLauncherInstalled: true, IsLauncherRunning: true } => "Installé · en cours d’exécution",
        { IsLauncherInstalled: true } => "Installé · arrêté",
        _ => "Non détecté",
    };

    public string EpicLauncherPath => _currentEpicSnapshot?.LauncherPath ?? "Aucun exécutable détecté";

    public string StorageEngineSize => FormatBytes(SumStorage(StorageCategory.EngineInstallation));

    public string StorageProjectSize => FormatBytes(SumStorage(StorageCategory.Project));

    public string StorageDdcSize => FormatBytes(_currentStorageSnapshot?.Entries
        .Where(entry => entry.Category is
            StorageCategory.EngineDerivedDataCache or
            StorageCategory.ProjectDerivedDataCache or
            StorageCategory.GlobalDerivedDataCache or
            StorageCategory.SharedDerivedDataCache)
        .Sum(entry => entry.SizeBytes) ?? 0);

    public string StorageVaultSize => FormatBytes(SumStorage(StorageCategory.VaultCache));

    public string StorageLfsSize => FormatBytes(SumStorage(StorageCategory.GitLfs));

    public string StorageReclaimableSize => FormatBytes(_currentStorageSnapshot?.Entries
        .Where(entry => entry.IsSafeToClean)
        .Sum(entry => entry.SizeBytes) ?? 0);

    public int WarningCount =>
        _currentEngines.Count(engine => engine.Health != EngineHealth.Healthy) +
        Projects.Count(project => project.Project.Health != ProjectHealth.Healthy) +
        _currentPlugins.Count(plugin => plugin.Health != PluginHealth.Healthy) +
        _currentLocalEnginePackages.Count(package => package.Health != LocalEnginePackageHealth.Ready) +
        ToolchainProblemCount +
        StorageWarningCount;

    public string HeaderTitle => _section switch
    {
        PageSection.Home => "Vue d’ensemble",
        PageSection.Engines => "Moteurs",
        PageSection.EngineDetails => SelectedEngineDetails?.DisplayName
                                     ?? SelectedLocalEngineDetails?.Name
                                     ?? "Détails du moteur",
        PageSection.Projects => "Projets",
        PageSection.ProjectDetails => SelectedProjectDetails?.Name
                                      ?? SelectedLocalProjectDetails?.Name
                                      ?? "Profil du projet",
        PageSection.Git => "Dépôts Git",
        PageSection.Plugins => "Plugins & packages",
        PageSection.HubExtensions => "Extensions CyEngineHub",
        PageSection.Toolchains => "Toolchains",
        PageSection.Storage => "Stockage",
        PageSection.Epic => "Bibliothèque",
        PageSection.Builds => "Builds & Packaging",
        PageSection.Tasks => "Téléchargements & installations",
        PageSection.Logs => "Logs",
        PageSection.Settings => "Paramètres",
        PageSection.Roadmap => RoadmapTitle,
        _ => "CyEngineHub",
    };

    public string HeaderSubtitle => _section switch
    {
        PageSection.Home => "Tous vos moteurs, projets et assets dans un hub indépendant.",
        PageSection.Engines => "Providers Unreal, Unity, Godot, CryEngine et futurs moteurs.",
        PageSection.EngineDetails => "Installation, plugins, outils de build et options du moteur.",
        PageSection.Projects => "Associations moteur, modules et plugins déclarés.",
        PageSection.ProjectDetails => "Moteur, plugins, build, packaging et options du projet.",
        PageSection.Git => "Snapshots manuels, divergence des remotes et stockage Git LFS.",
        PageSection.Plugins => "Gestion Unreal, packages locaux Unity/Godot et inventaire CRYENGINE.",
        PageSection.HubExtensions => "Outils externes, conversions et intégrations ajoutés au hub.",
        PageSection.Toolchains => "Compatibilité MSVC, SDK Windows, .NET, Android, Linux et Quest.",
        PageSection.Storage => "DDC, projets, moteurs, Vault Cache et Git LFS avec Safe Clean confirmé.",
        PageSection.Epic => "Catalogues Fab et Unity organisés par source dans une bibliothèque multi-moteur.",
        PageSection.Builds => "UBT et UAT dans la file de tâches, avec logs classifiés.",
        PageSection.Tasks => "Téléchargements, installations, progression, reprise et erreurs récentes.",
        PageSection.Logs => "Sorties bornées des dernières opérations Build et Git.",
        PageSection.Settings => "Moteurs, découverte, connexions et stockage local.",
        PageSection.Roadmap => "Module prévu dans le prochain lot fonctionnel.",
        _ => string.Empty,
    };

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                _refreshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsGitBusy
    {
        get => _isGitBusy;
        private set
        {
            if (SetProperty(ref _isGitBusy, value))
            {
                _refreshGitCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsPluginBusy
    {
        get => _isPluginBusy;
        private set
        {
            if (SetProperty(ref _isPluginBusy, value))
            {
                _refreshPluginsCommand.NotifyCanExecuteChanged();
                _confirmPluginInstallCommand.NotifyCanExecuteChanged();
                _prepareLocalEnginePackageInstallCommand.NotifyCanExecuteChanged();
                _confirmLocalEnginePackageInstallCommand.NotifyCanExecuteChanged();
                _confirmLocalEnginePackageRollbackCommand.NotifyCanExecuteChanged();
                _confirmLocalEnginePackageRemovalCommand.NotifyCanExecuteChanged();
                _confirmLocalEnginePackageRestoreCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsHubExtensionBusy
    {
        get => _isHubExtensionBusy;
        private set
        {
            if (SetProperty(ref _isHubExtensionBusy, value))
            {
                _refreshHubExtensionsCommand.NotifyCanExecuteChanged();
                _prepareHubExtensionInstallCommand.NotifyCanExecuteChanged();
                _confirmHubExtensionInstallCommand.NotifyCanExecuteChanged();
                _prepareHubExtensionExecutionCommand.NotifyCanExecuteChanged();
                _confirmHubExtensionExecutionCommand.NotifyCanExecuteChanged();
                _openHubExtensionWebsiteCommand.NotifyCanExecuteChanged();
                _openHubExtensionWindowCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsToolchainBusy
    {
        get => _isToolchainBusy;
        private set
        {
            if (SetProperty(ref _isToolchainBusy, value))
            {
                _refreshToolchainsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsStorageBusy
    {
        get => _isStorageBusy;
        private set
        {
            if (SetProperty(ref _isStorageBusy, value))
            {
                _refreshStorageCommand.NotifyCanExecuteChanged();
                _confirmStorageCleanupCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsEpicBusy
    {
        get => _isEpicBusy;
        private set
        {
            if (SetProperty(ref _isEpicBusy, value))
            {
                _refreshEpicCommand.NotifyCanExecuteChanged();
                (OpenEmbeddedFabAuthorizationCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                _launchFabProjectCommand.NotifyCanExecuteChanged();
                _confirmFabImportCommand.NotifyCanExecuteChanged();
                _confirmFabRollbackCommand.NotifyCanExecuteChanged();
                _launchUnityFabProjectCommand.NotifyCanExecuteChanged();
                _confirmUnityFabImportCommand.NotifyCanExecuteChanged();
                _confirmUnityFabRollbackCommand.NotifyCanExecuteChanged();
                _launchGodotFabProjectCommand.NotifyCanExecuteChanged();
                _confirmGodotFabImportCommand.NotifyCanExecuteChanged();
                _confirmGodotFabRollbackCommand.NotifyCanExecuteChanged();
                _launchCryEngineFabProjectCommand.NotifyCanExecuteChanged();
                _confirmCryEngineFabImportCommand.NotifyCanExecuteChanged();
                _confirmCryEngineFabRollbackCommand.NotifyCanExecuteChanged();
                _refreshFabLocalLibraryCommand.NotifyCanExecuteChanged();
                _addFabLocalLibraryRootCommand.NotifyCanExecuteChanged();
                _refreshDirectFabLibraryCommand.NotifyCanExecuteChanged();
                _refreshQuixelLibraryCommand.NotifyCanExecuteChanged();
                _downloadSelectedFabAssetCommand.NotifyCanExecuteChanged();
                _confirmSourceEngineDownloadCommand.NotifyCanExecuteChanged();
                _downloadSelectedFabVersionsCommand.NotifyCanExecuteChanged();
                _installSelectedFabVersionsInProjectCommand.NotifyCanExecuteChanged();
                _installSelectedFabVersionsInEngineCommand.NotifyCanExecuteChanged();
                _refreshEpicEngineReleasesCommand.NotifyCanExecuteChanged();
                _downloadEpicEngineCommand.NotifyCanExecuteChanged();
                _updateSelectedEngineComponentsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string GitStatusSummary
    {
        get => _gitStatusSummary;
        private set => SetProperty(ref _gitStatusSummary, value);
    }

    public string ActiveGitOperation
    {
        get => _activeGitOperation;
        private set => SetProperty(ref _activeGitOperation, value);
    }

    public string PluginStatusSummary
    {
        get => _pluginStatusSummary;
        private set => SetProperty(ref _pluginStatusSummary, value);
    }

    public string HubExtensionStatus
    {
        get => _hubExtensionStatus;
        private set => SetProperty(ref _hubExtensionStatus, value);
    }

    public string ToolchainStatusSummary
    {
        get => _toolchainStatusSummary;
        private set => SetProperty(ref _toolchainStatusSummary, value);
    }

    public string StorageStatusSummary
    {
        get => _storageStatusSummary;
        private set => SetProperty(ref _storageStatusSummary, value);
    }

    public string EpicStatusSummary
    {
        get => _epicStatusSummary;
        private set => SetProperty(ref _epicStatusSummary, value);
    }

    public string EpicAuthenticationSummary
    {
        get => _epicAuthenticationSummary;
        private set => SetProperty(ref _epicAuthenticationSummary, value);
    }

    public string FabStatusSummary
    {
        get => _fabStatusSummary;
        private set => SetProperty(ref _fabStatusSummary, value);
    }

    public string EngineBinaryDownloadSummary
    {
        get => _engineBinaryDownloadSummary;
        private set => SetProperty(ref _engineBinaryDownloadSummary, value);
    }

    public EpicBinaryEngineRelease? SelectedEpicEngineRelease
    {
        get => _selectedEpicEngineRelease;
        set
        {
            if (SetProperty(ref _selectedEpicEngineRelease, value))
            {
                _downloadEpicEngineCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string EpicEngineTargetDirectory
    {
        get => _epicEngineTargetDirectory;
        set => SetProperty(ref _epicEngineTargetDirectory, value);
    }

    public double EpicEngineDownloadProgress
    {
        get => _epicEngineDownloadProgress;
        private set => SetProperty(ref _epicEngineDownloadProgress, value);
    }

    public bool IsEpicEngineDownloadActive
    {
        get => _isEpicEngineDownloadActive;
        private set => SetProperty(ref _isEpicEngineDownloadActive, value);
    }

    public string SourceEngineDownloadStatus
    {
        get => _sourceEngineDownloadStatus;
        private set => SetProperty(ref _sourceEngineDownloadStatus, value);
    }

    public string SourceEngineRepository
    {
        get => _sourceEngineRepository;
        set
        {
            if (SetProperty(ref _sourceEngineRepository, value))
            {
                CancelPendingSourceEngineDownload();
            }
        }
    }

    public string SourceEngineBranch
    {
        get => _sourceEngineBranch;
        set
        {
            if (SetProperty(ref _sourceEngineBranch, value))
            {
                CancelPendingSourceEngineDownload();
            }
        }
    }

    public string? SelectedSourceEngineVersion
    {
        get => _selectedSourceEngineVersion;
        set
        {
            if (SetProperty(ref _selectedSourceEngineVersion, value) && !string.IsNullOrWhiteSpace(value))
            {
                SourceEngineBranch = value;
            }
        }
    }

    public string SourceEngineVersionsStatus
    {
        get => _sourceEngineVersionsStatus;
        private set => SetProperty(ref _sourceEngineVersionsStatus, value);
    }

    public string SourceEngineTargetDirectory
    {
        get => _sourceEngineTargetDirectory;
        set
        {
            if (SetProperty(ref _sourceEngineTargetDirectory, value))
            {
                CancelPendingSourceEngineDownload();
            }
        }
    }

    public bool SourceEngineShallowClone
    {
        get => _sourceEngineShallowClone;
        set
        {
            if (SetProperty(ref _sourceEngineShallowClone, value))
            {
                CancelPendingSourceEngineDownload();
            }
        }
    }

    public UnrealSourceDownloadPlan? PendingSourceEngineDownload =>
        _pendingSourceEngineDownload;

    public string PendingSourceEngineDownloadSummary
    {
        get => _pendingSourceEngineDownloadSummary;
        private set => SetProperty(ref _pendingSourceEngineDownloadSummary, value);
    }

    public ProjectCardViewModel? SelectedFabProject
    {
        get => _selectedFabProject;
        set
        {
            if (SetProperty(ref _selectedFabProject, value))
            {
                CancelPendingFabImport();
                CancelPendingFabRollback();
                _launchFabProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ManualFabContentSource
    {
        get => _manualFabContentSource;
        set
        {
            if (SetProperty(ref _manualFabContentSource, value))
            {
                CancelPendingFabImport();
            }
        }
    }

    public FabProjectImportPlan? PendingFabImport => _pendingFabImport;

    public string PendingFabImportSummary
    {
        get => _pendingFabImportSummary;
        private set => SetProperty(ref _pendingFabImportSummary, value);
    }

    public FabImportRollbackPlan? PendingFabRollback => _pendingFabRollback;

    public string PendingFabRollbackSummary
    {
        get => _pendingFabRollbackSummary;
        private set => SetProperty(ref _pendingFabRollbackSummary, value);
    }

    public string FabProjectImportStatus
    {
        get => _fabProjectImportStatus;
        private set => SetProperty(ref _fabProjectImportStatus, value);
    }

    public LocalEngineProjectCardViewModel? SelectedUnityFabProject
    {
        get => _selectedUnityFabProject;
        set
        {
            if (SetProperty(ref _selectedUnityFabProject, value))
            {
                CancelPendingUnityFabImport();
                CancelPendingUnityFabRollback();
                _launchUnityFabProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ManualUnityFabContentSource
    {
        get => _manualUnityFabContentSource;
        set
        {
            if (SetProperty(ref _manualUnityFabContentSource, value))
            {
                CancelPendingUnityFabImport();
            }
        }
    }

    public UnityFabAssetImportPlan? PendingUnityFabImport => _pendingUnityFabImport;

    public string PendingUnityFabImportSummary
    {
        get => _pendingUnityFabImportSummary;
        private set => SetProperty(ref _pendingUnityFabImportSummary, value);
    }

    public UnityFabAssetRollbackPlan? PendingUnityFabRollback => _pendingUnityFabRollback;

    public string PendingUnityFabRollbackSummary
    {
        get => _pendingUnityFabRollbackSummary;
        private set => SetProperty(ref _pendingUnityFabRollbackSummary, value);
    }

    public string UnityFabImportStatus
    {
        get => _unityFabImportStatus;
        private set => SetProperty(ref _unityFabImportStatus, value);
    }

    public LocalEngineProjectCardViewModel? SelectedGodotFabProject
    {
        get => _selectedGodotFabProject;
        set
        {
            if (SetProperty(ref _selectedGodotFabProject, value))
            {
                CancelPendingGodotFabImport();
                CancelPendingGodotFabRollback();
                _launchGodotFabProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ManualGodotFabContentSource
    {
        get => _manualGodotFabContentSource;
        set
        {
            if (SetProperty(ref _manualGodotFabContentSource, value))
            {
                CancelPendingGodotFabImport();
            }
        }
    }

    public GodotFabAssetImportPlan? PendingGodotFabImport => _pendingGodotFabImport;

    public string PendingGodotFabImportSummary
    {
        get => _pendingGodotFabImportSummary;
        private set => SetProperty(ref _pendingGodotFabImportSummary, value);
    }

    public GodotFabAssetRollbackPlan? PendingGodotFabRollback => _pendingGodotFabRollback;

    public string PendingGodotFabRollbackSummary
    {
        get => _pendingGodotFabRollbackSummary;
        private set => SetProperty(ref _pendingGodotFabRollbackSummary, value);
    }

    public string GodotFabImportStatus
    {
        get => _godotFabImportStatus;
        private set => SetProperty(ref _godotFabImportStatus, value);
    }

    public LocalEngineProjectCardViewModel? SelectedCryEngineFabProject
    {
        get => _selectedCryEngineFabProject;
        set
        {
            if (SetProperty(ref _selectedCryEngineFabProject, value))
            {
                CancelPendingCryEngineFabImport();
                CancelPendingCryEngineFabRollback();
                _launchCryEngineFabProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ManualCryEngineFabContentSource
    {
        get => _manualCryEngineFabContentSource;
        set
        {
            if (SetProperty(ref _manualCryEngineFabContentSource, value))
            {
                CancelPendingCryEngineFabImport();
            }
        }
    }

    public CryEngineFabAssetImportPlan? PendingCryEngineFabImport => _pendingCryEngineFabImport;

    public string PendingCryEngineFabImportSummary
    {
        get => _pendingCryEngineFabImportSummary;
        private set => SetProperty(ref _pendingCryEngineFabImportSummary, value);
    }

    public CryEngineFabAssetRollbackPlan? PendingCryEngineFabRollback => _pendingCryEngineFabRollback;

    public string PendingCryEngineFabRollbackSummary
    {
        get => _pendingCryEngineFabRollbackSummary;
        private set => SetProperty(ref _pendingCryEngineFabRollbackSummary, value);
    }

    public string CryEngineFabImportStatus
    {
        get => _cryEngineFabImportStatus;
        private set => SetProperty(ref _cryEngineFabImportStatus, value);
    }

    public string ManualFabLibraryRoot
    {
        get => _manualFabLibraryRoot;
        set => SetProperty(ref _manualFabLibraryRoot, value);
    }

    public string DirectFabStatusSummary
    {
        get => _directFabStatusSummary;
        private set => SetProperty(ref _directFabStatusSummary, value);
    }

    public string DirectFabAccount
    {
        get => _directFabAccount;
        private set => SetProperty(ref _directFabAccount, value);
    }

    public bool IsFabLibraryRefreshing
    {
        get => _isFabLibraryRefreshing;
        private set
        {
            if (!SetProperty(ref _isFabLibraryRefreshing, value)) return;
            OnPropertyChanged(nameof(FabRefreshButtonLabel));
            _refreshDirectFabLibraryCommand.NotifyCanExecuteChanged();
            if (value)
            {
                StartFabRefreshSpinner();
            }
            else
            {
                _fabRefreshSpinnerTimer?.Stop();
                FabRefreshSpinnerAngle = 0;
            }
        }
    }

    public string FabRefreshButtonLabel =>
        IsFabLibraryRefreshing ? "Actualisation…" : "Actualiser Fab";

    public double FabRefreshSpinnerAngle
    {
        get => _fabRefreshSpinnerAngle;
        private set => SetProperty(ref _fabRefreshSpinnerAngle, value);
    }

    public string FabAuthorizationCode
    {
        get => _fabAuthorizationCode;
        set => SetProperty(ref _fabAuthorizationCode, value);
    }

    public string QuixelStatusSummary
    {
        get => _quixelStatusSummary;
        private set => SetProperty(ref _quixelStatusSummary, value);
    }

    public bool IsQuixelLibraryRefreshing
    {
        get => _isQuixelLibraryRefreshing;
        private set
        {
            if (!SetProperty(ref _isQuixelLibraryRefreshing, value))
            {
                return;
            }

            OnPropertyChanged(nameof(QuixelRefreshButtonLabel));
            _refreshQuixelLibraryCommand.NotifyCanExecuteChanged();
            if (value)
            {
                StartQuixelRefreshSpinner();
            }
            else
            {
                _quixelRefreshSpinnerTimer?.Stop();
                QuixelRefreshSpinnerAngle = 0;
            }
        }
    }

    public string QuixelRefreshButtonLabel =>
        IsQuixelLibraryRefreshing ? "Actualisation…" : "Actualiser Megascans";

    public double QuixelRefreshSpinnerAngle
    {
        get => _quixelRefreshSpinnerAngle;
        private set => SetProperty(ref _quixelRefreshSpinnerAngle, value);
    }

    public string QuixelAccount
    {
        get => _quixelAccount;
        private set => SetProperty(ref _quixelAccount, value);
    }

    public string QuixelAuthorizationCode
    {
        get => _quixelAuthorizationCode;
        set => SetProperty(ref _quixelAuthorizationCode, value);
    }

    public string QuixelConnectionStatus => _quixelState switch
    {
        QuixelConnectionState.Connected => "MEGASCANS · ✓ CONNECTÉ",
        QuixelConnectionState.Expired => "MEGASCANS · ! SESSION EXPIRÉE",
        QuixelConnectionState.Unavailable => "MEGASCANS · ! INDISPONIBLE",
        _ => "MEGASCANS · × NON CONNECTÉ",
    };

    public string LibraryDownloadRoot
    {
        get => _libraryDownloadRoot;
        set => SetProperty(ref _libraryDownloadRoot, value);
    }

    public string FabCacheDirectory
    {
        get => _fabCacheDirectory;
        set
        {
            if (SetProperty(ref _fabCacheDirectory, value))
            {
                OnPropertyChanged(nameof(FabThumbnailCacheDirectory));
                OnPropertyChanged(nameof(MarketplaceCatalogCacheDirectory));
            }
        }
    }

    public string FabDownloadDirectory
    {
        get => _fabDownloadDirectory;
        set => SetProperty(ref _fabDownloadDirectory, value);
    }

    public string QuixelCacheDirectory
    {
        get => _quixelCacheDirectory;
        set => SetProperty(ref _quixelCacheDirectory, value);
    }

    public string QuixelDownloadDirectory
    {
        get => _quixelDownloadDirectory;
        set => SetProperty(ref _quixelDownloadDirectory, value);
    }

    public string UnityCacheDirectory
    {
        get => _unityCacheDirectory;
        set => SetProperty(ref _unityCacheDirectory, value);
    }

    public string UnityDownloadDirectory
    {
        get => _unityDownloadDirectory;
        set => SetProperty(ref _unityDownloadDirectory, value);
    }

    public MarketplaceCompressionOptionViewModel? SelectedFabCompressionOption
    {
        get => _selectedFabCompressionOption;
        set
        {
            if (SetProperty(ref _selectedFabCompressionOption, value))
            {
                OnPropertyChanged(nameof(PendingNativeStoreConversionSummary));
            }
        }
    }

    public MarketplaceCompressionOptionViewModel? SelectedQuixelCompressionOption
    {
        get => _selectedQuixelCompressionOption;
        set
        {
            if (SetProperty(ref _selectedQuixelCompressionOption, value))
            {
                OnPropertyChanged(nameof(PendingNativeStoreConversionSummary));
            }
        }
    }

    public MarketplaceCompressionOptionViewModel? SelectedUnityCompressionOption
    {
        get => _selectedUnityCompressionOption;
        set => SetProperty(ref _selectedUnityCompressionOption, value);
    }

    public bool IsNativeStoreSourceEditorVisible
    {
        get => _isNativeStoreSourceEditorVisible;
        private set => SetProperty(ref _isNativeStoreSourceEditorVisible, value);
    }

    public string PendingNativeStorePath
    {
        get => _pendingNativeStorePath;
        set => SetProperty(ref _pendingNativeStorePath, value);
    }

    public bool PendingNativeStoreConvertOnUse
    {
        get => _pendingNativeStoreConvertOnUse;
        set
        {
            if (SetProperty(ref _pendingNativeStoreConvertOnUse, value))
            {
                OnPropertyChanged(nameof(PendingNativeStoreModeTitle));
                OnPropertyChanged(nameof(PendingNativeStoreModeDescription));
            }
        }
    }

    public string PendingNativeStoreTitle => _pendingNativeStoreKind == MarketplaceNativeStoreKind.EpicGamesStore
        ? "AJOUTER UN STORE EPIC GAMES"
        : "AJOUTER UNE BIBLIOTHÈQUE MEGASCANS";

    public string PendingNativeStoreDescription => _pendingNativeStoreKind == MarketplaceNativeStoreKind.EpicGamesStore
        ? "Ajoutez un Vault Cache Epic existant ou un dossier de packs Epic déjà présents."
        : "Ajoutez une bibliothèque Megascans/Bridge existante sans déplacer ses fichiers.";

    public string PendingNativeStoreModeTitle => PendingNativeStoreConvertOnUse
        ? "Convertir à la demande"
        : "Utiliser au format natif";

    public string PendingNativeStoreModeDescription => PendingNativeStoreConvertOnUse
        ? "La source reste intacte. Lors de la première utilisation d’un asset, CyEngineHub crée une copie gérée et applique les paramètres du marketplace."
        : "CyEngineHub indexe et lit directement ce dossier en lecture seule. Aucun fichier du store n’est renommé, déplacé ou recompressé.";

    public string PendingNativeStoreConversionSummary
    {
        get
        {
            var option = _pendingNativeStoreKind == MarketplaceNativeStoreKind.EpicGamesStore
                ? SelectedFabCompressionOption
                : SelectedQuixelCompressionOption;
            var libraries = _pendingNativeStoreKind == MarketplaceNativeStoreKind.EpicGamesStore
                ? FabLibraryLocations.Count
                : QuixelLibraryLocations.Count;
            return $"Conversion avec le profil {option?.Label ?? "Rapide"} · {libraries:N0} bibliothèque(s) ordonnée(s) · quotas et priorité du store respectés.";
        }
    }

    public string FabNativeCacheMaximumGigabytes
    {
        get => _fabNativeCacheMaximumGigabytes;
        set => SetProperty(ref _fabNativeCacheMaximumGigabytes, value);
    }

    public string EpicVaultNativeCacheStatus
    {
        get => _epicVaultNativeCacheStatus;
        private set => SetProperty(ref _epicVaultNativeCacheStatus, value);
    }

    public string QuixelLibraryRoot
    {
        get => _quixelLibraryRoot;
        set => SetProperty(ref _quixelLibraryRoot, value);
    }

    public string QuixelLibraryStatus
    {
        get => _quixelLibraryStatus;
        private set => SetProperty(ref _quixelLibraryStatus, value);
    }

    public bool HasQuixelLocalAssets => QuixelLocalAssets.Count > 0;

    public string FabThumbnailCacheDirectory => Path.Combine(FabCacheDirectory, "Images");

    public string MarketplaceCatalogCacheDirectory => Path.Combine(FabCacheDirectory, "Catalog");

    public bool AutoRefreshFabOnStartup
    {
        get => _autoRefreshFabOnStartup;
        set => SetProperty(ref _autoRefreshFabOnStartup, value);
    }

    public bool AutoRefreshQuixelOnStartup
    {
        get => _autoRefreshQuixelOnStartup;
        set => SetProperty(ref _autoRefreshQuixelOnStartup, value);
    }

    public bool AutoRefreshUnityOnStartup
    {
        get => _autoRefreshUnityOnStartup;
        set => SetProperty(ref _autoRefreshUnityOnStartup, value);
    }

    public bool IsWindowsStartupSupported => _windowsStartupService.IsSupported;

    public bool LaunchAtWindowsStartup
    {
        get => _launchAtWindowsStartup;
        set => SetProperty(ref _launchAtWindowsStartup, value);
    }

    public string WindowsStartupStatus
    {
        get => _windowsStartupStatus;
        private set => SetProperty(ref _windowsStartupStatus, value);
    }

    public string UpdateRepository
    {
        get => _updateRepository;
        set
        {
            if (SetProperty(ref _updateRepository, value))
            {
                OnPropertyChanged(nameof(CanCheckForUpdates));
                if (string.IsNullOrWhiteSpace(value))
                    UpdateStatus = "Mises à jour désactivées : aucun dépôt GitHub configuré.";
            }
        }
    }

    public bool CanCheckForUpdates => !string.IsNullOrWhiteSpace(UpdateRepository) &&
        !IsUpdateCheckActive && !IsUpdateDownloadActive;

    public bool AutoCheckStableUpdates
    {
        get => _autoCheckStableUpdates;
        set => SetProperty(ref _autoCheckStableUpdates, value);
    }

    public bool IncludePrereleaseUpdates
    {
        get => _includePrereleaseUpdates;
        set => SetProperty(ref _includePrereleaseUpdates, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public bool IsUpdateCheckActive
    {
        get => _isUpdateCheckActive;
        private set
        {
            if (SetProperty(ref _isUpdateCheckActive, value)) OnPropertyChanged(nameof(CanCheckForUpdates));
        }
    }

    public bool IsUpdateDownloadActive
    {
        get => _isUpdateDownloadActive;
        private set
        {
            if (SetProperty(ref _isUpdateDownloadActive, value)) OnPropertyChanged(nameof(CanCheckForUpdates));
        }
    }

    public bool IsUpdateAvailablePopupVisible
    {
        get => _isUpdateAvailablePopupVisible;
        private set => SetProperty(ref _isUpdateAvailablePopupVisible, value);
    }

    public bool IsUpdateDownloadProgressVisible
    {
        get => _isUpdateDownloadProgressVisible;
        private set => SetProperty(ref _isUpdateDownloadProgressVisible, value);
    }

    public double UpdateProgressPercent
    {
        get => _updateProgressPercent;
        private set => SetProperty(ref _updateProgressPercent, value);
    }

    public string UpdateProgressStatus
    {
        get => _updateProgressStatus;
        private set => SetProperty(ref _updateProgressStatus, value);
    }

    public string UpdateProgressDetails
    {
        get => _updateProgressDetails;
        private set => SetProperty(ref _updateProgressDetails, value);
    }

    public ApplicationRelease? AvailableUpdate
    {
        get => _availableUpdate;
        private set
        {
            if (SetProperty(ref _availableUpdate, value))
            {
                OnPropertyChanged(nameof(AvailableUpdateVersion));
                OnPropertyChanged(nameof(AvailableUpdateTitle));
                OnPropertyChanged(nameof(AvailableUpdateNotes));
                OnPropertyChanged(nameof(AvailableUpdatePublishedAt));
                OnPropertyChanged(nameof(CanInstallAvailableUpdate));
                OnPropertyChanged(nameof(AvailableUpdateInstallerStatus));
            }
        }
    }

    public string AvailableUpdateVersion => AvailableUpdate?.TagName ?? string.Empty;

    public string AvailableUpdateTitle => AvailableUpdate?.Title ?? string.Empty;

    public string AvailableUpdateNotes => AvailableUpdate?.Notes ?? string.Empty;

    public string AvailableUpdatePublishedAt => AvailableUpdate?.PublishedAt is { } publishedAt
        ? $"Publiée le {publishedAt.ToLocalTime():dd/MM/yyyy HH:mm}"
        : "Date de publication inconnue";

    public bool CanInstallAvailableUpdate => AvailableUpdate?.CanInstall == true;

    public string AvailableUpdateInstallerStatus => CanInstallAvailableUpdate
        ? "Installateur Windows et SHA-256 disponibles."
        : "Installation automatique indisponible : la release doit fournir un .exe/.msi et son SHA-256.";

    public event EventHandler<UpdateInstallerReadyEventArgs>? UpdateInstallerReady;

    public string LibrarySearchQuery
    {
        get => _librarySearchQuery;
        set
        {
            if (SetProperty(ref _librarySearchQuery, value))
            {
                ApplyRemoteFabFilter();
            }
        }
    }

    public bool ShowHiddenFabAssets
    {
        get => _showHiddenFabAssets;
        set
        {
            if (SetProperty(ref _showHiddenFabAssets, value))
            {
                ApplyRemoteFabFilter();
            }
        }
    }

    private bool _showDownloadedLibraryAssets = true;
    private bool _showUndownloadedLibraryAssets = true;

    public bool ShowDownloadedLibraryAssets
    {
        get => _showDownloadedLibraryAssets;
        set { if (SetProperty(ref _showDownloadedLibraryAssets, value)) ApplyRemoteFabFilter(); }
    }

    public bool ShowUndownloadedLibraryAssets
    {
        get => _showUndownloadedLibraryAssets;
        set { if (SetProperty(ref _showUndownloadedLibraryAssets, value)) ApplyRemoteFabFilter(); }
    }

    private string _libraryEngineFacetSearchQuery = string.Empty;
    public BulkObservableCollection<LibraryFacetOptionViewModel> VisibleLibraryEngineFacets { get; } = [];
    public string LibraryEngineFacetSearchQuery
    {
        get => _libraryEngineFacetSearchQuery;
        set
        {
            if (SetProperty(ref _libraryEngineFacetSearchQuery, value))
                RefreshVisibleLibraryFacets();
        }
    }

    private string _libraryAssetTypeFacetSearchQuery = string.Empty;
    public BulkObservableCollection<LibraryFacetOptionViewModel> VisibleLibraryAssetTypeFacets { get; } = [];
    public string LibraryAssetTypeFacetSearchQuery
    {
        get => _libraryAssetTypeFacetSearchQuery;
        set
        {
            if (SetProperty(ref _libraryAssetTypeFacetSearchQuery, value))
                RefreshVisibleLibraryFacets();
        }
    }

    private string _libraryCategoryFacetSearchQuery = string.Empty;
    public BulkObservableCollection<LibraryFacetOptionViewModel> VisibleLibraryCategoryFacets { get; } = [];
    public string LibraryCategoryFacetSearchQuery
    {
        get => _libraryCategoryFacetSearchQuery;
        set
        {
            if (SetProperty(ref _libraryCategoryFacetSearchQuery, value))
                RefreshVisibleLibraryFacets();
        }
    }

    private string _libraryCollectionFacetSearchQuery = string.Empty;
    public BulkObservableCollection<LibraryFacetOptionViewModel> VisibleLibraryCollectionFacets { get; } = [];
    public string LibraryCollectionFacetSearchQuery
    {
        get => _libraryCollectionFacetSearchQuery;
        set
        {
            if (SetProperty(ref _libraryCollectionFacetSearchQuery, value))
                RefreshVisibleLibraryFacets();
        }
    }

    private void RefreshVisibleLibraryFacets()
    {
        FilterFacetChoices(LibraryEngineFacets, VisibleLibraryEngineFacets, LibraryEngineFacetSearchQuery);
        OnPropertyChanged(nameof(AreAllLibraryEngineFacetsSelected));
        FilterFacetChoices(LibraryAssetTypeFacets, VisibleLibraryAssetTypeFacets, LibraryAssetTypeFacetSearchQuery);
        OnPropertyChanged(nameof(AreAllLibraryAssetTypeFacetsSelected));
        FilterFacetChoices(LibraryCategoryFacets, VisibleLibraryCategoryFacets, LibraryCategoryFacetSearchQuery);
        OnPropertyChanged(nameof(AreAllLibraryCategoryFacetsSelected));
        FilterFacetChoices(LibraryCollectionFacets, VisibleLibraryCollectionFacets, LibraryCollectionFacetSearchQuery);
        OnPropertyChanged(nameof(AreAllLibraryCollectionFacetsSelected));
    }

    private static void FilterFacetChoices(
        IEnumerable<LibraryFacetOptionViewModel> source,
        BulkObservableCollection<LibraryFacetOptionViewModel> visible,
        string query)
    {
        var search = query.Trim();
        var matches = source.Where(item => search.Length == 0 ||
            item.Value.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!visible.SequenceEqual(matches)) visible.ReplaceWith(matches);
    }

    public bool HasActiveLibraryFilters =>
        !ShowDownloadedLibraryAssets || !ShowUndownloadedLibraryAssets ||
        (IsLibraryCompatibilityFilterVisible && LibraryEngineFacets.Any(item => !item.IsSelected)) ||
        LibraryAssetTypeFacets.Any(item => !item.IsSelected) ||
        LibraryCategoryFacets.Any(item => !item.IsSelected) ||
        LibraryCollectionFacets.Any(item => !item.IsSelected);

    public string ActiveLibraryFilterSummary => HasActiveLibraryFilters ? "Filtres actifs" : string.Empty;

    public bool AreAllLibraryEngineFacetsSelected
    {
        get => VisibleLibraryEngineFacets.Count > 0 && VisibleLibraryEngineFacets.All(item => item.IsSelected);
        set => SetAllFacets(VisibleLibraryEngineFacets, value);
    }

    public bool AreAllLibraryAssetTypeFacetsSelected
    {
        get => VisibleLibraryAssetTypeFacets.Count > 0 && VisibleLibraryAssetTypeFacets.All(item => item.IsSelected);
        set => SetAllFacets(VisibleLibraryAssetTypeFacets, value);
    }

    public bool AreAllLibraryCategoryFacetsSelected
    {
        get => VisibleLibraryCategoryFacets.Count > 0 && VisibleLibraryCategoryFacets.All(item => item.IsSelected);
        set => SetAllFacets(VisibleLibraryCategoryFacets, value);
    }

    public bool AreAllLibraryCollectionFacetsSelected
    {
        get => VisibleLibraryCollectionFacets.Count > 0 && VisibleLibraryCollectionFacets.All(item => item.IsSelected);
        set => SetAllFacets(VisibleLibraryCollectionFacets, value);
    }

    public string SelectedLibrarySort
    {
        get => _selectedLibrarySort;
        set
        {
            if (SetProperty(ref _selectedLibrarySort, value))
            {
                ApplyRemoteFabFilter();
            }
        }
    }

    public string SelectedLibraryCategory
    {
        get => _selectedLibraryCategory;
        set
        {
            if (SetProperty(ref _selectedLibraryCategory, value))
            {
                ApplyRemoteFabFilter();
            }
        }
    }

    public string SelectedLibraryFolder
    {
        get => _selectedLibraryFolder;
        set
        {
            if (SetProperty(ref _selectedLibraryFolder, value))
            {
                ApplyRemoteFabFilter();
            }
        }
    }

    public string SelectedLibraryEngineVersion
    {
        get => _selectedLibraryEngineVersion;
        set
        {
            if (SetProperty(ref _selectedLibraryEngineVersion, value))
            {
                ApplyRemoteFabFilter();
            }
        }
    }

    public string SelectedLibraryTag
    {
        get => _selectedLibraryTag;
        set
        {
            if (SetProperty(ref _selectedLibraryTag, value))
            {
                ApplyRemoteFabFilter();
            }
        }
    }

    public bool IsAdvancedLibraryFilterVisible
    {
        get => _isAdvancedLibraryFilterVisible;
        set => SetProperty(ref _isAdvancedLibraryFilterVisible, value);
    }

    public string FabInstallTargetSearchQuery
    {
        get => _fabInstallTargetSearchQuery;
        set
        {
            if (SetProperty(ref _fabInstallTargetSearchQuery, value))
            {
                ApplyFabInstallTargetFilter();
            }
        }
    }

    public bool ShowOnlyCompatibleFabTargets
    {
        get => _showOnlyCompatibleFabTargets;
        set
        {
            if (SetProperty(ref _showOnlyCompatibleFabTargets, value))
            {
                ApplyFabInstallTargetFilter();
            }
        }
    }

    public bool IsFabInstallCompatibilityFilterVisible =>
        SelectedRemoteFabAssetCard is not null && !IsQuixelAsset(SelectedRemoteFabAssetCard.Asset);

    public bool IsFabCollectionEditorVisible
    {
        get => _isFabCollectionEditorVisible;
        private set => SetProperty(ref _isFabCollectionEditorVisible, value);
    }

    public string FabCollectionSearchQuery
    {
        get => _fabCollectionSearchQuery;
        set
        {
            if (SetProperty(ref _fabCollectionSearchQuery, value))
            {
                ApplyFabCollectionFilter();
            }
        }
    }

    public string NewFabCollectionName
    {
        get => _newFabCollectionName;
        set => SetProperty(ref _newFabCollectionName, value);
    }

    public bool IsLibraryInfiniteMode
    {
        get => _isLibraryInfiniteMode;
        private set
        {
            if (SetProperty(ref _isLibraryInfiniteMode, value))
            {
                OnPropertyChanged(nameof(IsLibraryPagedMode));
                RebuildRemoteFabPresentation();
            }
        }
    }

    public bool IsLibraryPagedMode => !IsLibraryInfiniteMode;

    public bool IsLibraryCompactCards
    {
        get => _isLibraryCompactCards;
        private set
        {
            if (SetProperty(ref _isLibraryCompactCards, value))
            {
                RebuildRemoteFabPage();
                OnPropertyChanged(nameof(IsLibraryLargeCards));
                OnPropertyChanged(nameof(LibraryCardWidth));
                OnPropertyChanged(nameof(LibraryCardHeight));
                OnPropertyChanged(nameof(LibraryCardImageHeight));
            }
        }
    }

    public bool IsLibraryLargeCards => !IsLibraryCompactCards;

    public double LibraryCardWidth => IsLibraryCompactCards ? 230 : 310;

    public double LibraryCardHeight => IsLibraryCompactCards ? 220 : 285;

    public double LibraryCardImageHeight => IsLibraryCompactCards ? 105 : 150;

    public string LibraryResultsSummary =>
        $"{_visibleRemoteFabCatalog.Length:N0} / {_librarySourceAssetCount:N0} élément(s)";

    public string LibraryPageSummary
    {
        get
        {
            if (IsLibraryInfiniteMode)
            {
                return $"{_libraryInfiniteItemCount:N0} / {_visibleRemoteFabCatalog.Length:N0} chargés";
            }
            if (_visibleRemoteFabCatalog.Length == 0)
            {
                return "Page 0 / 0";
            }
            return $"Page {_libraryPageIndex + 1:N0} / {GetLibraryPageCount():N0}";
        }
    }

    public bool CanShowPreviousLibraryPage => IsLibraryPagedMode && _libraryPageIndex > 0;

    public bool CanShowNextLibraryPage => IsLibraryPagedMode && _libraryPageIndex + 1 < GetLibraryPageCount();

    public string LibrarySource
    {
        get => _librarySource;
        private set
        {
            if (SetProperty(ref _librarySource, value))
            {
                _quixelCollectionsNavigation++;
                NotifyQuixelCollectionNavigation();
                // A facet selection belongs to one marketplace, not the next one.
                LibraryEngineFacets.Clear();
                LibraryAssetTypeFacets.Clear();
                LibraryCategoryFacets.Clear();
                LibraryCollectionFacets.Clear();
                RefreshVisibleLibraryFacets();
                OnPropertyChanged(nameof(IsLibraryFabSelected));
                OnPropertyChanged(nameof(IsLibraryFabWebSelected));
                OnPropertyChanged(nameof(IsLibraryUnitySelected));
                OnPropertyChanged(nameof(IsLibraryQuixelSelected));
                OnPropertyChanged(nameof(IsLibraryCompatibilityFilterVisible));
                OnPropertyChanged(nameof(HasActiveLibraryFilters));
                OnPropertyChanged(nameof(ActiveLibraryFilterSummary));
                OnPropertyChanged(nameof(IsLibraryRemoteSelected));
                OnPropertyChanged(nameof(LibraryRemoteHeading));
                OnPropertyChanged(nameof(LibraryProductHeading));
                OnPropertyChanged(nameof(IsSelectedRemoteConnectionReady));
                OnPropertyChanged(nameof(LibraryRemoteStatusSummary));
                OnPropertyChanged(nameof(LibraryRemoteAccount));
                OnPropertyChanged(nameof(IsLibraryLocalSelected));
                OnPropertyChanged(nameof(IsConnectorLibrarySelected));
                if (value is "Fab" or "Quixel" or "FabWeb")
                {
                    ShowCachedRemoteFabPreview();
                    QueueRemoteFabViewRefresh();
                }
            }
        }
    }

    public bool IsLibraryFabSelected => LibrarySource == "Fab";

    public bool IsLibraryUnitySelected => LibrarySource == "Unity";

    public bool IsLibraryQuixelSelected => LibrarySource == "Quixel";

    public bool IsLibraryCompatibilityFilterVisible => !IsLibraryQuixelSelected && !IsLibraryFabWebSelected;

    public bool IsLibraryRemoteSelected => IsLibraryFabSelected || IsLibraryQuixelSelected || IsLibraryFabWebSelected;

    public string LibraryRemoteHeading => IsLibraryFabWebSelected ? "MEGASCANS VIA FAB · FICHES ACQUISES" : IsLibraryQuixelSelected
        ? "MEGASCANS · BIBLIOTHÈQUE HISTORIQUE"
        : "FAB · MY LIBRARY";

    public string LibraryProductHeading => IsLibraryFabWebSelected ? "FICHE PRODUIT MEGASCANS VIA FAB" : IsLibraryQuixelSelected
        ? "FICHE PRODUIT MEGASCANS"
        : "FICHE PRODUIT FAB";

    public bool IsSelectedRemoteConnectionReady => IsLibraryFabWebSelected || (IsLibraryQuixelSelected
        ? IsQuixelConnected
        : IsDirectFabConnected);

    public string LibraryRemoteStatusSummary => IsLibraryFabWebSelected ? "Fiches acquises synchronisées depuis Fab · connexion / synchronisation dans la fenêtre intégrée" : IsLibraryQuixelSelected
        ? QuixelStatusSummary
        : DirectFabStatusSummary;

    public string LibraryRemoteAccount => IsLibraryFabWebSelected ? "Session web Fab indépendante de la connexion Launcher" : IsLibraryQuixelSelected
        ? QuixelAccount
        : DirectFabAccount;

    public bool IsQuixelBridgeLocalPanelVisible => IsLibraryQuixelSelected && !IsQuixelConnected;

    public bool IsLibraryLocalSelected => LibrarySource == "Local";

    public bool IsDirectFabConnected => _directFabState == DirectFabConnectionState.Connected;

    public bool IsQuixelConnected => _quixelState == QuixelConnectionState.Connected;

    public bool IsUnityLibrarySourceAvailable => UnityMarketplaceEnabled && IsProviderEnabled("unity") && IsUnityConnectionEnabled;

    public bool IsQuixelLibrarySourceAvailable => QuixelMarketplaceEnabled && IsQuixelConnectionEnabled;

    public bool IsEpicConnectionEnabled => IsConnectionModuleEnabled("epic");

    public bool IsFabConnectionEnabled => IsConnectionModuleEnabled("fab");

    public bool IsGitConnectionEnabled => IsConnectionModuleEnabled("git");

    public bool IsUnityConnectionEnabled => IsConnectionModuleEnabled("unity");

    public bool IsQuixelConnectionEnabled => IsConnectionModuleEnabled("quixel");

    public bool HasNoRemoteFabAssets => _visibleRemoteFabCatalog.Length == 0;

    public RemoteFabAsset? SelectedRemoteFabAsset
    {
        get => _selectedRemoteFabAsset;
        private set
        {
            if (SetProperty(ref _selectedRemoteFabAsset, value))
            {
                OnPropertyChanged(nameof(HasSelectedRemoteFabAsset));
                OnPropertyChanged(nameof(SelectedRemoteFabAssetTitle));
                _downloadSelectedFabAssetCommand.NotifyCanExecuteChanged();
                _downloadSelectedFabVersionsCommand.NotifyCanExecuteChanged();
                _installSelectedFabVersionsInProjectCommand.NotifyCanExecuteChanged();
                _installSelectedFabVersionsInEngineCommand.NotifyCanExecuteChanged();
                if (InstallSelectedFabVersionsCommand is AsyncRelayCommand installTargetsCommand)
                {
                    installTargetsCommand.NotifyCanExecuteChanged();
                }
            }
        }
    }

    public bool HasSelectedRemoteFabAsset => SelectedRemoteFabAsset is not null;

    public string SelectedRemoteFabAssetTitle => SelectedRemoteFabAsset?.Title ?? "Aucun asset sélectionné";

    public RemoteFabAssetCardViewModel? SelectedRemoteFabAssetCard
    {
        get => _selectedRemoteFabAssetCard;
        private set
        {
            if (SetProperty(ref _selectedRemoteFabAssetCard, value))
            {
                OnPropertyChanged(nameof(HasSelectedRemoteFabListing));
                OnPropertyChanged(nameof(IsSelectedRemoteFabAssetPlugin));
                OnPropertyChanged(nameof(IsFabPluginTargetFilterVisible));
                OnPropertyChanged(nameof(SelectedFabAssetVisibilityLabel));
            }
        }
    }

    public bool HasSelectedRemoteFabListing => SelectedRemoteFabAssetCard?.HasListingUri == true;

    public bool IsSelectedRemoteFabAssetPlugin => SelectedRemoteFabAssetCard?.IsPluginAsset == true;

    public bool IsFabPluginTargetFilterVisible => SelectedRemoteFabAssetCard?.ContentKind is
        LibraryPackageContentKind.CodePlugin or LibraryPackageContentKind.EnginePackage;

    public bool ShowFabProjectInstallTargets
    {
        get => _showFabProjectInstallTargets;
        set
        {
            if (SetProperty(ref _showFabProjectInstallTargets, value))
            {
                ApplyFabInstallTargetFilter();
            }
        }
    }

    public bool ShowFabEngineInstallTargets
    {
        get => _showFabEngineInstallTargets;
        set
        {
            if (SetProperty(ref _showFabEngineInstallTargets, value))
            {
                ApplyFabInstallTargetFilter();
            }
        }
    }

    public bool HasSelectedFabStoredDownloads => SelectedFabStoredDownloads.Count > 0;

    public bool HasSelectedFabVersionDownloads => SelectedFabVersionDownloads.Count > 0;

    public bool IsDeleteAllFabDownloadsConfirmationVisible
    {
        get => _isDeleteAllFabDownloadsConfirmationVisible;
        private set => SetProperty(ref _isDeleteAllFabDownloadsConfirmationVisible, value);
    }

    public string SelectedFabAssetVisibilityLabel => SelectedRemoteFabAssetCard?.IsHidden == true
        ? "Réafficher dans la Bibliothèque"
        : "Masquer de la Bibliothèque";

    public bool IsRemoteFabProductVisible
    {
        get => _isRemoteFabProductVisible;
        private set
        {
            if (SetProperty(ref _isRemoteFabProductVisible, value))
            {
                OnPropertyChanged(nameof(IsRemoteFabCatalogVisible));
            }
        }
    }

    public bool IsRemoteFabCatalogVisible => !IsRemoteFabProductVisible;

    public bool IsFabInstallOptionsVisible
    {
        get => _isFabInstallOptionsVisible;
        private set
        {
            if (SetProperty(ref _isFabInstallOptionsVisible, value))
            {
                if (!value)
                {
                    IsFabInstallationScriptConfigurationVisible = false;
                }
                NotifyFabOptionsModalState();
            }
        }
    }

    public FabInstallTargetOptionViewModel? SelectedFabInstallationScriptTarget
    {
        get => _selectedFabInstallationScriptTarget;
        private set => SetProperty(ref _selectedFabInstallationScriptTarget, value);
    }

    public bool IsFabInstallationScriptConfigurationVisible
    {
        get => _isFabInstallationScriptConfigurationVisible;
        private set => SetProperty(ref _isFabInstallationScriptConfigurationVisible, value);
    }

    public bool IsFabDownloadOptionsVisible
    {
        get => _isFabDownloadOptionsVisible;
        private set
        {
            if (SetProperty(ref _isFabDownloadOptionsVisible, value))
            {
                NotifyFabOptionsModalState();
            }
        }
    }

    public bool IsFabOptionsModalVisible =>
        IsFabInstallOptionsVisible || IsFabDownloadOptionsVisible;

    public bool IsFabImagePreviewVisible
    {
        get => _isFabImagePreviewVisible;
        private set
        {
            if (SetProperty(ref _isFabImagePreviewVisible, value))
            {
                OnPropertyChanged(nameof(IsAnyFabModalVisible));
                OnPropertyChanged(nameof(FabOptionsModalBlurRadius));
            }
        }
    }

    public bool IsAnyFabModalVisible =>
        IsFabOptionsModalVisible || IsFabImagePreviewVisible ||
        IsDerivedWorkspaceCreatorVisible || IsDerivedWorkspaceDeletionVisible ||
        IsDerivedFolderImporterVisible || IsPackTestCreatorVisible ||
        IsEngineRemovalConfirmationVisible || IsEngineUninstallConfirmationVisible ||
        IsProjectCreatorVisible || IsProjectProfileEditorVisible;

    public double FabOptionsModalBlurRadius => IsAnyFabModalVisible ? 7 : 0;

    private void NotifyFabOptionsModalState()
    {
        OnPropertyChanged(nameof(IsFabOptionsModalVisible));
        OnPropertyChanged(nameof(IsAnyFabModalVisible));
        OnPropertyChanged(nameof(FabOptionsModalBlurRadius));
    }

    public bool IsFabDownloadActive
    {
        get => _isFabDownloadActive;
        private set
        {
            if (SetProperty(ref _isFabDownloadActive, value))
            {
                OnPropertyChanged(nameof(FabDownloadActionLabel));
                _downloadSelectedFabAssetCommand.NotifyCanExecuteChanged();
                _downloadSelectedFabVersionsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string FabDownloadActionLabel => IsFabDownloadActive
        ? "Ajouter les versions à la file"
        : "Télécharger les versions sélectionnées";

    public double FabDownloadProgress
    {
        get => _fabDownloadProgress;
        private set => SetProperty(ref _fabDownloadProgress, value);
    }

    public string FabDownloadStatus
    {
        get => _fabDownloadStatus;
        private set => SetProperty(ref _fabDownloadStatus, value);
    }

    public ProjectCardViewModel? SelectedFabInstallProject
    {
        get => _selectedFabInstallProject;
        set
        {
            if (SetProperty(ref _selectedFabInstallProject, value))
            {
                _installSelectedFabVersionsInProjectCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public EngineCardViewModel? SelectedFabInstallEngine
    {
        get => _selectedFabInstallEngine;
        set
        {
            if (SetProperty(ref _selectedFabInstallEngine, value))
            {
                _installSelectedFabVersionsInEngineCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsRemoteFabProductLoading
    {
        get => _isRemoteFabProductLoading;
        private set => SetProperty(ref _isRemoteFabProductLoading, value);
    }

    public string FabLocalLibraryStatus
    {
        get => _fabLocalLibraryStatus;
        private set => SetProperty(ref _fabLocalLibraryStatus, value);
    }

    public StorageCleanupPlan? PendingStorageCleanup => _pendingStorageCleanup;

    public string PendingStorageCleanupSummary
    {
        get => _pendingStorageCleanupSummary;
        private set => SetProperty(ref _pendingStorageCleanupSummary, value);
    }

    public string ManualPluginLibraryRoot
    {
        get => _manualPluginLibraryRoot;
        set => SetProperty(ref _manualPluginLibraryRoot, value);
    }

    public PluginInstallTargetViewModel? SelectedPluginTarget
    {
        get => _selectedPluginTarget;
        set
        {
            if (SetProperty(ref _selectedPluginTarget, value))
            {
                CancelPendingPluginInstall();
            }
        }
    }

    public PluginInstallPlan? PendingPluginInstall => _pendingPluginInstall;

    public string PendingPluginInstallSummary
    {
        get => _pendingPluginInstallSummary;
        private set => SetProperty(ref _pendingPluginInstallSummary, value);
    }

    public string ManualLocalEnginePackageSource
    {
        get => _manualLocalEnginePackageSource;
        set
        {
            if (SetProperty(ref _manualLocalEnginePackageSource, value))
            {
                CancelPendingLocalEnginePackageInstall();
            }
        }
    }

    public LocalEngineProjectCardViewModel? SelectedLocalPackageInstallProject
    {
        get => _selectedLocalPackageInstallProject;
        set
        {
            if (SetProperty(ref _selectedLocalPackageInstallProject, value))
            {
                CancelPendingLocalEnginePackageInstall();
                _prepareLocalEnginePackageInstallCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public LocalEnginePackageInstallPlan? PendingLocalEnginePackageInstall =>
        _pendingLocalEnginePackageInstall;

    public string PendingLocalEnginePackageInstallSummary
    {
        get => _pendingLocalEnginePackageInstallSummary;
        private set => SetProperty(ref _pendingLocalEnginePackageInstallSummary, value);
    }

    public LocalEnginePackageRollbackPlan? PendingLocalEnginePackageRollback =>
        _pendingLocalEnginePackageRollback;

    public string PendingLocalEnginePackageRollbackSummary
    {
        get => _pendingLocalEnginePackageRollbackSummary;
        private set => SetProperty(ref _pendingLocalEnginePackageRollbackSummary, value);
    }

    public LocalEnginePackageRemovalPlan? PendingLocalEnginePackageRemoval =>
        _pendingLocalEnginePackageRemoval;

    public string PendingLocalEnginePackageRemovalSummary
    {
        get => _pendingLocalEnginePackageRemovalSummary;
        private set => SetProperty(ref _pendingLocalEnginePackageRemovalSummary, value);
    }

    public LocalEnginePackageRestorePlan? PendingLocalEnginePackageRestore =>
        _pendingLocalEnginePackageRestore;

    public string PendingLocalEnginePackageRestoreSummary
    {
        get => _pendingLocalEnginePackageRestoreSummary;
        private set => SetProperty(ref _pendingLocalEnginePackageRestoreSummary, value);
    }

    public string ManualHubExtensionPackage
    {
        get => _manualHubExtensionPackage;
        set => SetProperty(ref _manualHubExtensionPackage, value);
    }

    public string HubExtensionInputPath
    {
        get => _hubExtensionInputPath;
        set
        {
            if (SetProperty(ref _hubExtensionInputPath, value))
            {
                CancelPendingHubExtensionExecution();
            }
        }
    }

    public string HubExtensionOutputPath
    {
        get => _hubExtensionOutputPath;
        set
        {
            if (SetProperty(ref _hubExtensionOutputPath, value))
            {
                CancelPendingHubExtensionExecution();
            }
        }
    }

    public string HubExtensionSourceEngine
    {
        get => _hubExtensionSourceEngine;
        set
        {
            if (SetProperty(ref _hubExtensionSourceEngine, value))
            {
                CancelPendingHubExtensionExecution();
            }
        }
    }

    public string HubExtensionTargetEngine
    {
        get => _hubExtensionTargetEngine;
        set
        {
            if (SetProperty(ref _hubExtensionTargetEngine, value))
            {
                CancelPendingHubExtensionExecution();
            }
        }
    }

    public HubExtensionCardViewModel? SelectedHubExtension
    {
        get => _selectedHubExtension;
        set
        {
            if (SetProperty(ref _selectedHubExtension, value))
            {
                CancelPendingHubExtensionExecution();
                OnPropertyChanged(nameof(CanRunSelectedHubExtension));
                OnPropertyChanged(nameof(CanOpenSelectedHubExtensionWebsite));
                OnPropertyChanged(nameof(CanOpenSelectedHubExtensionWindow));
                OnPropertyChanged(nameof(IsUnrealVersionBridgeSelected));
                _prepareHubExtensionExecutionCommand.NotifyCanExecuteChanged();
                _openHubExtensionWebsiteCommand.NotifyCanExecuteChanged();
                _openHubExtensionWindowCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanRunSelectedHubExtension => SelectedHubExtension?.CanRun == true;

    public bool CanOpenSelectedHubExtensionWebsite =>
        SelectedHubExtension?.CanOpenWebsite == true;

    public bool CanOpenSelectedHubExtensionWindow =>
        SelectedHubExtension?.HasDedicatedWindow == true;

    public bool IsUnrealVersionBridgeSelected =>
        string.Equals(
            SelectedHubExtension?.Id,
            "cyenginehub.unreal-version-bridge",
            StringComparison.OrdinalIgnoreCase);

    public string UnrealVersionBridgeStorageRoot
    {
        get => _unrealVersionBridgeStorageRoot;
        set => SetProperty(ref _unrealVersionBridgeStorageRoot, value);
    }

    public string UnrealVersionBridgeProfile
    {
        get => _unrealVersionBridgeProfile;
        set => SetProperty(ref _unrealVersionBridgeProfile, value);
    }

    public string UnrealVersionBridgeAssetProfileFile
    {
        get => _unrealVersionBridgeAssetProfileFile;
        set
        {
            if (SetProperty(ref _unrealVersionBridgeAssetProfileFile, value))
                OnPropertyChanged(nameof(UnrealVersionBridgeAssetProfilesSummary));
        }
    }

    public string UnrealVersionBridgeAssetProfilesSummary
    {
        get
        {
            try
            {
                var file = string.IsNullOrWhiteSpace(UnrealVersionBridgeAssetProfileFile)
                    ? Path.Combine(AppContext.BaseDirectory, "Extensions", "cyenginehub.unreal-version-bridge", "profiles", "ue-5.6-to-5.5.json")
                    : UnrealVersionBridgeAssetProfileFile;
                var profiles = UnrealAssetConversionProfiles.Load(file);
                return string.Join(Environment.NewLine + Environment.NewLine, profiles.Profiles.Select(p =>
                    $"{p.AssetType} · UE {p.SourceVersion} → {p.TargetVersion} · révision {p.Revision}\n{p.Notes}\nContrôles prévus : {string.Join(", ", p.Checks)}"));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or ArgumentException)
            {
                return "Profils indisponibles : " + error.Message;
            }
        }
    }

    public bool UnrealVersionBridgeKeepStaging
    {
        get => _unrealVersionBridgeKeepStaging;
        set => SetProperty(ref _unrealVersionBridgeKeepStaging, value);
    }

    public bool UnrealVersionBridgeValidateOutput
    {
        get => _unrealVersionBridgeValidateOutput;
        set => SetProperty(ref _unrealVersionBridgeValidateOutput, value);
    }

    public HubExtensionInstallPlan? PendingHubExtensionInstall =>
        _pendingHubExtensionInstall;

    public string PendingHubExtensionInstallSummary
    {
        get => _pendingHubExtensionInstallSummary;
        private set => SetProperty(ref _pendingHubExtensionInstallSummary, value);
    }

    public HubExtensionExecutionPlan? PendingHubExtensionExecution =>
        _pendingHubExtensionExecution;

    public string PendingHubExtensionExecutionSummary
    {
        get => _pendingHubExtensionExecutionSummary;
        private set => SetProperty(ref _pendingHubExtensionExecutionSummary, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                AppendRawLog("INFO", "Status", value);
                OnPropertyChanged(nameof(FooterActivityText));
            }
        }
    }

    public string RawLogText
    {
        get => _rawLogText;
        private set => SetProperty(ref _rawLogText, value);
    }

    public string RawLogSearchQuery
    {
        get => _rawLogSearchQuery;
        set
        {
            if (SetProperty(ref _rawLogSearchQuery, value))
            {
                ApplyRawLogFilter(scrollToEnd: true);
            }
        }
    }

    public string RawLogFilePath => _rawLogPath;

    public string LocalEngineDiscoverySummary
    {
        get => _localEngineDiscoverySummary;
        private set => SetProperty(ref _localEngineDiscoverySummary, value);
    }

    public string ManualEnginePath
    {
        get => _manualEnginePath;
        set => SetProperty(ref _manualEnginePath, value);
    }

    public string ManualProjectRoot
    {
        get => _manualProjectRoot;
        set => SetProperty(ref _manualProjectRoot, value);
    }

    public string RoadmapTitle
    {
        get => _roadmapTitle;
        private set => SetProperty(ref _roadmapTitle, value);
    }

    public string RoadmapDescription
    {
        get => _roadmapDescription;
        private set => SetProperty(ref _roadmapDescription, value);
    }

    public string ActiveBuildTitle
    {
        get => _activeBuildTitle;
        private set => SetProperty(ref _activeBuildTitle, value);
    }

    public string BuildSummary
    {
        get => _buildSummary;
        private set => SetProperty(ref _buildSummary, value);
    }

    public ProjectCardViewModel? SelectedPresetProject
    {
        get => _selectedPresetProject;
        set => SetProperty(ref _selectedPresetProject, value);
    }

    public string PresetName
    {
        get => _presetName;
        set => SetProperty(ref _presetName, value);
    }

    public string PresetKind
    {
        get => _presetKind;
        set
        {
            if (SetProperty(ref _presetKind, value))
            {
                OnPropertyChanged(nameof(IsPresetPackage));
            }
        }
    }

    public bool IsPresetPackage => string.Equals(
        PresetKind,
        nameof(UnrealBuildPresetKind.Package),
        StringComparison.Ordinal);

    public string PresetTarget
    {
        get => _presetTarget;
        set => SetProperty(ref _presetTarget, value);
    }

    public string PresetPlatform
    {
        get => _presetPlatform;
        set => SetProperty(ref _presetPlatform, value);
    }

    public string PresetConfiguration
    {
        get => _presetConfiguration;
        set => SetProperty(ref _presetConfiguration, value);
    }

    public string PresetAction
    {
        get => _presetAction;
        set => SetProperty(ref _presetAction, value);
    }

    public string PresetMaximumParallelActions
    {
        get => _presetMaximumParallelActions;
        set => SetProperty(ref _presetMaximumParallelActions, value);
    }

    public string PresetCookFlavor
    {
        get => _presetCookFlavor;
        set => SetProperty(ref _presetCookFlavor, value);
    }

    public bool PresetAttachProject
    {
        get => _presetAttachProject;
        set => SetProperty(ref _presetAttachProject, value);
    }

    public bool PresetCook
    {
        get => _presetCook;
        set => SetProperty(ref _presetCook, value);
    }

    public bool PresetStage
    {
        get => _presetStage;
        set => SetProperty(ref _presetStage, value);
    }

    public bool PresetPackage
    {
        get => _presetPackage;
        set => SetProperty(ref _presetPackage, value);
    }

    public bool PresetArchive
    {
        get => _presetArchive;
        set => SetProperty(ref _presetArchive, value);
    }

    public bool PresetPak
    {
        get => _presetPak;
        set => SetProperty(ref _presetPak, value);
    }

    public bool PresetIoStore
    {
        get => _presetIoStore;
        set => SetProperty(ref _presetIoStore, value);
    }

    public bool PresetCompressed
    {
        get => _presetCompressed;
        set => SetProperty(ref _presetCompressed, value);
    }

    public bool PresetDistribution
    {
        get => _presetDistribution;
        set => SetProperty(ref _presetDistribution, value);
    }

    public bool PresetIncludePrerequisites
    {
        get => _presetIncludePrerequisites;
        set => SetProperty(ref _presetIncludePrerequisites, value);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _database.InitializeAsync().ConfigureAwait(true);
            LoadWindowsStartupSettings();
            await LoadUpdateSettingsAsync().ConfigureAwait(true);
            _workspacePreferences = await _workspacePreferencesService.LoadAsync().ConfigureAwait(true);
            await LoadProjectCreationProfilesAsync().ConfigureAwait(true);
            _fabInstallationScriptPreferences =
                await _fabInstallationScriptPreferenceStore.LoadAsync().ConfigureAwait(true);
            await RefreshFabInstallationScriptsAsync().ConfigureAwait(true);
            await RefreshProjectCreationScriptsAsync().ConfigureAwait(true);
            await LoadLibrarySettingsAsync().ConfigureAwait(true);
            InitializeMarketplaceStorageAutosave();
            await LoadFabLibraryCacheAsync().ConfigureAwait(true);
            await LoadFabWebCacheAsync().ConfigureAwait(true);
            await LoadQuixelLibraryCacheAsync().ConfigureAwait(true);
            RebuildEngineModuleCards();
            RebuildEngineTabs();
            await LoadBuildPresetsAsync().ConfigureAwait(true);
            await LoadGitCacheAsync().ConfigureAwait(true);
            await LoadPluginCacheAsync().ConfigureAwait(true);
            await RefreshHubExtensionsAsync().ConfigureAwait(true);
            await LoadToolchainCacheAsync().ConfigureAwait(true);
            await LoadStorageCacheAsync().ConfigureAwait(true);
            await LoadEpicAsync().ConfigureAwait(true);
            await LoadDirectFabStatusAsync().ConfigureAwait(true);
            await LoadQuixelStatusAsync().ConfigureAwait(true);
            await LoadFabLocalLibraryCacheAsync().ConfigureAwait(true);
            await LoadCachedCatalogAsync().ConfigureAwait(true);
            await LoadDownloadQueueAsync().ConfigureAwait(true);
            await LoadAiSettingsAsync().ConfigureAwait(true);
            await RefreshCatalogAsync().ConfigureAwait(true);
            _ = RefreshMarketplacesOnStartupAsync();
            _ = CheckForUpdatesOnStartupAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private async Task LoadCachedCatalogAsync()
    {
        var engines = await _engineCatalog.GetAllAsync().ConfigureAwait(true);
        var projects = await _projectCatalog.GetAllAsync().ConfigureAwait(true);
        ReplaceCatalog(engines, projects);
    }

    private async Task LoadQuixelLibraryCacheAsync()
    {
        var marketplaceSnapshot = await _marketplaceLibraryCache.LoadAsync("quixel").ConfigureAwait(true);
        marketplaceSnapshot ??= await _legacyMarketplaceLibraryCache.LoadAsync("quixel").ConfigureAwait(true);
        var legacySnapshot = marketplaceSnapshot is null
            ? await _quixelLibraryCache.LoadAsync().ConfigureAwait(true)
            : null;
        var assets = marketplaceSnapshot?.Assets ?? legacySnapshot?.Library.Assets;
        var updatedAt = marketplaceSnapshot?.UpdatedAt ?? legacySnapshot?.UpdatedAt;
        if (assets?.Count is not > 0 || updatedAt is null)
        {
            return;
        }

        QuixelStatusSummary = "Restauration du dernier catalogue Megascans en cache…";
        ReplaceRemoteAssets(IsQuixelAsset, assets);
        _quixelCatalogCachedAt = updatedAt;
        if (IsLibraryQuixelSelected)
        {
            await RefreshRemoteFabViewNowAsync().ConfigureAwait(true);
        }
        QuixelStatusSummary =
            $"{assets.Count:N0} fiche(s) Megascans restaurée(s) · cache {FormatCacheDate(updatedAt)}.";
        AppendRawLog(
            "INFO",
            "Library/Quixel",
            $"Cache Megascans restauré : {assets.Count:N0} fiche(s), {updatedAt:O}.");
    }

    private async Task LoadFabLibraryCacheAsync()
    {
        var snapshot = await _marketplaceLibraryCache.LoadAsync("fab").ConfigureAwait(true);
        snapshot ??= await _legacyMarketplaceLibraryCache.LoadAsync("fab").ConfigureAwait(true);
        if (snapshot?.Assets.Count is not > 0) return;
        ReplaceRemoteAssets(IsLauncherFabAsset, snapshot.Assets);
        _fabCatalogCachedAt = snapshot.UpdatedAt;
        DirectFabStatusSummary =
            $"{snapshot.Assets.Count:N0} élément(s) Fab restauré(s) · cache {FormatCacheDate(snapshot.UpdatedAt)}.";
        AppendRawLog(
            "INFO",
            "Library/Fab",
            $"Cache Fab restauré : {snapshot.Assets.Count:N0} élément(s), {snapshot.UpdatedAt:O}.");
    }

    private async Task LoadLibrarySettingsAsync()
    {
        await LoadMarketplaceAvailabilityAsync().ConfigureAwait(true);
        AutoRefreshFabOnStartup = ParseEnabledSetting(
            await _settings.GetAsync(FabAutoRefreshOnStartupSetting).ConfigureAwait(true));
        AutoRefreshQuixelOnStartup = ParseEnabledSetting(
            await _settings.GetAsync(QuixelAutoRefreshOnStartupSetting).ConfigureAwait(true));
        AutoRefreshUnityOnStartup = ParseEnabledSetting(
            await _settings.GetAsync(UnityAutoRefreshOnStartupSetting).ConfigureAwait(true));
        var legacyCompression = await _settings.GetAsync(LibraryPackageCompressionSetting).ConfigureAwait(true);
        var legacyStorageMode = string.Equals(legacyCompression, bool.FalseString, StringComparison.OrdinalIgnoreCase)
            ? LibraryPackageStorageMode.ZipStore
            : LibraryPackageStorageMode.ZipFastest;
        SelectedFabCompressionOption = FindCompressionOption(ParseCompressionSetting(
            await _settings.GetAsync(FabPackageCompressionSetting).ConfigureAwait(true),
            legacyStorageMode));
        SelectedQuixelCompressionOption = FindCompressionOption(ParseCompressionSetting(
            await _settings.GetAsync(QuixelPackageCompressionSetting).ConfigureAwait(true),
            legacyStorageMode));
        SelectedUnityCompressionOption = FindCompressionOption(ParseCompressionSetting(
            await _settings.GetAsync(UnityPackageCompressionSetting).ConfigureAwait(true),
            legacyStorageMode));
        var versionBridgeRoot = await _settings.GetAsync(UnrealVersionBridgeSettings.StorageRoot).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(versionBridgeRoot))
        {
            UnrealVersionBridgeStorageRoot = Path.GetFullPath(versionBridgeRoot.Trim().Trim((char)34));
        }
        var versionBridgeProfile = await _settings.GetAsync(UnrealVersionBridgeSettings.ConversionProfile).ConfigureAwait(true);
        UnrealVersionBridgeAssetProfileFile = await _settings.GetAsync(UnrealVersionBridgeSettings.AssetProfileFile).ConfigureAwait(true) ?? string.Empty;
        UnrealVersionBridgeProfile = string.Equals(versionBridgeProfile, "extended", StringComparison.OrdinalIgnoreCase)
            ? "extended"
            : "safe";
        UnrealVersionBridgeKeepStaging = bool.TryParse(
            await _settings.GetAsync(UnrealVersionBridgeSettings.KeepStaging).ConfigureAwait(true),
            out var keepVersionBridgeStaging) && keepVersionBridgeStaging;
        UnrealVersionBridgeValidateOutput = ParseEnabledSetting(
            await _settings.GetAsync(UnrealVersionBridgeSettings.ValidateOutput).ConfigureAwait(true));
        var storedRoot = await _settings.GetAsync(LibraryDownloadRootSetting).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(storedRoot))
        {
            LibraryDownloadRoot = Path.GetFullPath(storedRoot.Trim().Trim('"'));
        }
        await LoadMarketplaceStorageProfilesAsync().ConfigureAwait(true);
        await LoadMarketplaceNativeStoresAsync().ConfigureAwait(true);

        var storedQuixelRoot = await _settings.GetAsync(QuixelLibraryRootSetting).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(storedQuixelRoot))
        {
            QuixelLibraryRoot = Path.GetFullPath(storedQuixelRoot.Trim().Trim('"'));
        }
        QuixelLibraryStatus = Directory.Exists(QuixelLibraryRoot)
            ? "Bibliothèque Bridge détectée · analyse disponible."
            : "Aucune bibliothèque Bridge détectée à cet emplacement.";

        var collectionsJson = await _settings.GetAsync(FabCollectionsSetting).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(collectionsJson))
        {
            try
            {
                var collections = JsonSerializer.Deserialize<Dictionary<string, string[]>>(collectionsJson);
                if (collections is not null)
                {
                    _fabCollections.Clear();
                    foreach (var pair in collections.Take(20_000))
                    {
                        _fabCollections[pair.Key] = pair.Value
                            .Where(value => !string.IsNullOrWhiteSpace(value))
                            .Select(value => value.Trim())
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .Take(64)
                            .ToArray();
                    }
                }
            }
            catch (JsonException)
            {
                AppendRawLog("WARN", "Settings/Library", "Collections Fab ignorées : données invalides.");
            }
        }

        var downloadsJson = await _settings.GetAsync(FabStoredDownloadsSetting).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(downloadsJson))
        {
            try
            {
                var downloads = await Task.Run(() =>
                    (JsonSerializer.Deserialize<FabStoredDownloadRecord[]>(downloadsJson) ?? [])
                    .Where(item => !string.IsNullOrWhiteSpace(item.AssetId) && Directory.Exists(item.Path))
                    .Take(10_000).ToArray()).ConfigureAwait(true);
                _fabStoredDownloads.Clear();
                _fabStoredDownloads.AddRange(downloads);
                InvalidateLibraryLocalIndex();
            }
            catch (JsonException)
            {
                AppendRawLog("WARN", "Settings/Library", "Historique des téléchargements Fab ignoré : données invalides.");
            }
        }
        await LoadDerivedWorkspacesAsync().ConfigureAwait(true);

        var modulesJson = await _settings.GetAsync(ConnectionModulesSetting).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(modulesJson))
        {
            try
            {
                var modules = JsonSerializer.Deserialize<Dictionary<string, bool>>(modulesJson);
                if (modules is not null)
                {
                    foreach (var pair in modules.Where(pair => _connectionModules.ContainsKey(pair.Key)))
                    {
                        _connectionModules[pair.Key] = pair.Value;
                    }
                }
            }
            catch (JsonException)
            {
                AppendRawLog("WARN", "Settings/Connections", "Configuration des connexions ignorée : données invalides.");
            }
        }
        RebuildConnectionModuleCards();
    }

    private void LoadWindowsStartupSettings()
    {
        if (!_windowsStartupService.IsSupported)
        {
            LaunchAtWindowsStartup = false;
            WindowsStartupStatus =
                "Le mode tray et le lancement automatique sont disponibles uniquement sous Windows.";
            return;
        }

        try
        {
            LaunchAtWindowsStartup = _windowsStartupService.IsEnabled();
            WindowsStartupStatus = LaunchAtWindowsStartup
                ? "Actif · CyEngineHub démarre avec Windows directement dans le tray."
                : "Inactif · CyEngineHub ne démarre pas automatiquement avec Windows.";
        }
        catch (Exception exception)
        {
            LaunchAtWindowsStartup = false;
            WindowsStartupStatus =
                $"État du démarrage Windows indisponible : {exception.Message}";
            AppendRawLog("WARN", "Settings/WindowsStartup", exception.Message);
        }
    }

    private async Task SaveWindowsStartupSettingsAsync()
    {
        if (!_windowsStartupService.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "Le lancement automatique est disponible uniquement sous Windows.");
        }

        var requestedState = LaunchAtWindowsStartup;
        await Task.Run(() => _windowsStartupService.SetEnabled(requestedState))
            .ConfigureAwait(true);
        LaunchAtWindowsStartup = _windowsStartupService.IsEnabled();
        WindowsStartupStatus = LaunchAtWindowsStartup
            ? "Actif · CyEngineHub démarrera avec Windows directement dans le tray."
            : "Inactif · l’entrée de démarrage Windows a été retirée.";
        StatusMessage = LaunchAtWindowsStartup
            ? "Lancement avec Windows activé."
            : "Lancement avec Windows désactivé.";
        AppendRawLog(
            "INFO",
            "Settings/WindowsStartup",
            $"Lancement automatique Windows : {LaunchAtWindowsStartup}.");
    }

    private async Task LoadUpdateSettingsAsync()
    {
        var savedRepository = (await _settings.GetAsync(UpdateRepositorySetting).ConfigureAwait(true))?.Trim();
        UpdateRepository = string.IsNullOrWhiteSpace(savedRepository) ? OfficialUpdateRepository : savedRepository;
        AutoCheckStableUpdates = ParseEnabledSetting(
            await _settings.GetAsync(AutoCheckStableUpdatesSetting).ConfigureAwait(true));
        var prereleases = await _settings.GetAsync(IncludePrereleaseUpdatesSetting).ConfigureAwait(true);
        IncludePrereleaseUpdates = bool.TryParse(prereleases, out var enabled)
            ? enabled : ApplicationVersion.Contains('-');
        UpdateStatus = string.IsNullOrWhiteSpace(UpdateRepository)
            ? "Aucun dépôt GitHub configuré. Le module reste inactif."
            : AutoCheckStableUpdates
                ? $"Vérification automatique active · {UpdateRepository}"
                : $"Vérification manuelle · {UpdateRepository}";
    }

    private async Task SaveUpdateSettingsAsync()
    {
        UpdateRepository = UpdateRepository.Trim().TrimEnd('/');
        await _settings.SetAsync(UpdateRepositorySetting, UpdateRepository).ConfigureAwait(true);
        await _settings.SetAsync(
            AutoCheckStableUpdatesSetting,
            AutoCheckStableUpdates.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(true);
        await _settings.SetAsync(IncludePrereleaseUpdatesSetting,
            IncludePrereleaseUpdates.ToString(CultureInfo.InvariantCulture)).ConfigureAwait(true);
        UpdateStatus = string.IsNullOrWhiteSpace(UpdateRepository)
            ? "Paramètres enregistrés · aucune recherche ne sera effectuée sans dépôt."
            : "Paramètres de mise à jour enregistrés.";
        StatusMessage = UpdateStatus;
        AppendRawLog(
            "INFO",
            "Settings/Updates",
            $"Dépôt : {(string.IsNullOrWhiteSpace(UpdateRepository) ? "non configuré" : UpdateRepository)} · " +
            $"vérification automatique : {AutoCheckStableUpdates}.");
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!AutoCheckStableUpdates || string.IsNullOrWhiteSpace(UpdateRepository)) return;
        await Task.Delay(1200).ConfigureAwait(true);
        try
        {
            await CheckForUpdatesAsync(showUpToDateStatus: false).ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ShowUpdateError(exception);
        }
    }

    private async Task CheckForUpdatesAsync(bool showUpToDateStatus)
    {
        if (string.IsNullOrWhiteSpace(UpdateRepository))
        {
            AvailableUpdate = null;
            IsUpdateAvailablePopupVisible = false;
            UpdateStatus = "Mises à jour désactivées : configurez le dépôt GitHub dans Paramètres → Avancé → Mises à jour.";
            if (showUpToDateStatus) StatusMessage = UpdateStatus;
            return;
        }

        if (IsUpdateCheckActive || IsUpdateDownloadActive) return;
        IsUpdateCheckActive = true;
        UpdateStatus = IncludePrereleaseUpdates ? "Recherche des releases, Alpha/Bêta incluses…" : "Recherche de la dernière release stable…";
        try
        {
            var release = await _applicationUpdateService.CheckForUpdateAsync(
                UpdateRepository,
                ApplicationVersion,
                IncludePrereleaseUpdates).ConfigureAwait(true);
            if (release is null)
            {
                AvailableUpdate = null;
                UpdateStatus = $"CyEngineHub {ApplicationVersion} est à jour.";
                if (showUpToDateStatus) StatusMessage = UpdateStatus;
                return;
            }

            AvailableUpdate = release;
            UpdateStatus = $"Nouvelle version disponible : {release.TagName}.";
            StatusMessage = UpdateStatus;
            IsUpdateAvailablePopupVisible = true;
            AppendRawLog(
                "INFO",
                "Updates",
                $"Release disponible : {release.TagName} · installation automatique : {release.CanInstall}.");
        }
        finally
        {
            IsUpdateCheckActive = false;
        }
    }

    private async Task OpenAvailableUpdateReleaseAsync()
    {
        if (AvailableUpdate is null) return;
        await _platformLauncher.OpenUriAsync(AvailableUpdate.ReleaseUri).ConfigureAwait(true);
    }

    private async Task DownloadAndInstallUpdateAsync()
    {
        var release = AvailableUpdate ??
            throw new InvalidOperationException("Aucune mise à jour n’est sélectionnée.");
        if (!release.CanInstall)
        {
            throw new ApplicationUpdateException(AvailableUpdateInstallerStatus);
        }

        IsUpdateAvailablePopupVisible = false;
        IsUpdateDownloadProgressVisible = true;
        IsUpdateDownloadActive = true;
        UpdateProgressPercent = 0;
        UpdateProgressStatus = "Préparation du téléchargement…";
        UpdateProgressDetails = release.Installer?.Name ?? release.TagName;
        try
        {
            var progress = new Progress<ApplicationUpdateProgress>(item =>
            {
                UpdateProgressPercent = Math.Clamp(item.Fraction * 100, 0, 100);
                UpdateProgressStatus = item.Stage;
                UpdateProgressDetails = item.TotalBytes is > 0
                    ? $"{FormatBytes(item.BytesProcessed)} / {FormatBytes(item.TotalBytes.Value)}"
                    : FormatBytes(item.BytesProcessed);
            });
            var result = await _applicationUpdateService.DownloadAndVerifyAsync(
                release,
                _applicationUpdateDirectory,
                progress).ConfigureAwait(true);
            UpdateProgressPercent = 100;
            UpdateProgressStatus = "Vérification terminée · fermeture de CyEngineHub…";
            UpdateProgressDetails = $"SHA-256 : {result.Sha256}";
            AppendRawLog(
                "INFO",
                "Updates",
                $"Installateur vérifié : {result.InstallerPath} · SHA-256 {result.Sha256}.");
            UpdateInstallerReady?.Invoke(this, new UpdateInstallerReadyEventArgs(result.InstallerPath));
        }
        catch
        {
            IsUpdateDownloadProgressVisible = false;
            IsUpdateAvailablePopupVisible = true;
            throw;
        }
        finally
        {
            IsUpdateDownloadActive = false;
        }
    }

    private void ShowUpdateError(Exception exception)
    {
        IsUpdateCheckActive = false;
        IsUpdateDownloadActive = false;
        UpdateStatus = $"Mise à jour impossible : {exception.Message}";
        ShowError(exception);
    }

    private async Task SaveLibrarySettingsAsync()
    {
        if (string.IsNullOrWhiteSpace(LibraryDownloadRoot))
        {
            throw new InvalidOperationException("Choisissez un dossier de téléchargement pour la Bibliothèque.");
        }
        var normalized = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(LibraryDownloadRoot.Trim().Trim('"')));
        Directory.CreateDirectory(normalized);
        LibraryDownloadRoot = normalized;
        await _settings.SetAsync(LibraryDownloadRootSetting, normalized).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(QuixelLibraryRoot))
        {
            var quixelRoot = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(QuixelLibraryRoot.Trim().Trim('"')));
            QuixelLibraryRoot = quixelRoot;
            await _settings.SetAsync(QuixelLibraryRootSetting, quixelRoot).ConfigureAwait(true);
        }
        StatusMessage = "Paramètres de la Bibliothèque enregistrés.";
        AppendRawLog(
            "INFO",
            "Settings/Library",
            $"Dossier de téléchargement : {normalized}");
    }

    private async Task SaveMarketplaceSettingsCoreAsync()
    {
        foreach (var connector in MarketplaceConnectors)
            _ = Views.MarketplaceDownloadStorage.ValidateDirectory(connector.DownloadDirectory);
        var nativeCacheMaximumBytes = ParseMaximumLibraryBytes(
            FabNativeCacheMaximumGigabytes,
            "cache Vault Epic");
        var fabStorage = BuildMarketplaceStorageProfile(
            "Fab",
            FabCacheDirectory,
            FabDownloadDirectory,
            FabLibraryLocations);
        var quixelStorage = BuildMarketplaceStorageProfile(
            "Megascans",
            QuixelCacheDirectory,
            QuixelDownloadDirectory,
            QuixelLibraryLocations);
        var unityStorage = BuildMarketplaceStorageProfile(
            "Unity",
            UnityCacheDirectory,
            UnityDownloadDirectory,
            UnityLibraryLocations);
        ApplyMarketplaceStorageProfile(fabStorage, FabLibraryLocations, value =>
        {
            FabCacheDirectory = value.CacheDirectory;
            FabDownloadDirectory = value.DownloadDirectory;
        });
        ApplyMarketplaceStorageProfile(quixelStorage, QuixelLibraryLocations, value =>
        {
            QuixelCacheDirectory = value.CacheDirectory;
            QuixelDownloadDirectory = value.DownloadDirectory;
        });
        ApplyMarketplaceStorageProfile(unityStorage, UnityLibraryLocations, value =>
        {
            UnityCacheDirectory = value.CacheDirectory;
            UnityDownloadDirectory = value.DownloadDirectory;
        });
        await _settings.SetAsync(
            FabAutoRefreshOnStartupSetting,
            AutoRefreshFabOnStartup.ToString()).ConfigureAwait(true);
        await _settings.SetAsync(
            QuixelAutoRefreshOnStartupSetting,
            AutoRefreshQuixelOnStartup.ToString()).ConfigureAwait(true);
        await _settings.SetAsync(
            UnityAutoRefreshOnStartupSetting,
            AutoRefreshUnityOnStartup.ToString()).ConfigureAwait(true);
        await _settings.SetAsync(
            FabPackageCompressionSetting,
            GetCompressionMode(SelectedFabCompressionOption).ToString()).ConfigureAwait(true);
        await _settings.SetAsync(
            QuixelPackageCompressionSetting,
            GetCompressionMode(SelectedQuixelCompressionOption).ToString()).ConfigureAwait(true);
        await _settings.SetAsync(
            UnityPackageCompressionSetting,
            GetCompressionMode(SelectedUnityCompressionOption).ToString()).ConfigureAwait(true);
        await _settings.SetAsync(
            FabStorageProfileSetting,
            JsonSerializer.Serialize(fabStorage)).ConfigureAwait(true);
        await _settings.SetAsync(
            QuixelStorageProfileSetting,
            JsonSerializer.Serialize(quixelStorage)).ConfigureAwait(true);
        await _settings.SetAsync(
            UnityStorageProfileSetting,
            JsonSerializer.Serialize(unityStorage)).ConfigureAwait(true);
        await _settings.SetAsync(
            FabNativeCacheMaximumGigabytesSetting,
            FabNativeCacheMaximumGigabytes.Trim()).ConfigureAwait(true);
        EpicVaultNativeCacheStatus = nativeCacheMaximumBytes > 0
            ? $"Cache de reconstruction prêt · limite {FormatBytes(nativeCacheMaximumBytes)}."
            : "Cache de reconstruction prêt · taille illimitée.";
        await SaveMarketplaceAvailabilityAsync().ConfigureAwait(true);
        StatusMessage = "Paramètres des marketplaces enregistrés.";
        AppendRawLog(
            "INFO",
            "Settings/Marketplaces",
            $"Actualisation au lancement · Fab={AutoRefreshFabOnStartup}, Megascans={AutoRefreshQuixelOnStartup}, Unity={AutoRefreshUnityOnStartup} · compression Fab={GetCompressionMode(SelectedFabCompressionOption)}, Megascans={GetCompressionMode(SelectedQuixelCompressionOption)}, Unity={GetCompressionMode(SelectedUnityCompressionOption)} · bibliothèques Fab={fabStorage.Libraries.Count}, Megascans={quixelStorage.Libraries.Count}, Unity={unityStorage.Libraries.Count}.");
    }

    private async Task LoadMarketplaceStorageProfilesAsync()
    {
        await LoadMarketplaceStorageProfileAsync(
            FabStorageProfileSetting,
            "Fab",
            FabCacheDirectory,
            FabDownloadDirectory,
            FabLibraryLocations,
            value =>
            {
                FabCacheDirectory = value.CacheDirectory;
                FabDownloadDirectory = value.DownloadDirectory;
            }).ConfigureAwait(true);
        await LoadMarketplaceStorageProfileAsync(
            QuixelStorageProfileSetting,
            "Megascans",
            QuixelCacheDirectory,
            QuixelDownloadDirectory,
            QuixelLibraryLocations,
            value =>
            {
                QuixelCacheDirectory = value.CacheDirectory;
                QuixelDownloadDirectory = value.DownloadDirectory;
            }).ConfigureAwait(true);
        await LoadMarketplaceStorageProfileAsync(
            UnityStorageProfileSetting,
            "Unity",
            UnityCacheDirectory,
            UnityDownloadDirectory,
            UnityLibraryLocations,
            value =>
            {
                UnityCacheDirectory = value.CacheDirectory;
                UnityDownloadDirectory = value.DownloadDirectory;
            }).ConfigureAwait(true);
        var nativeCacheMaximum = await _settings.GetAsync(FabNativeCacheMaximumGigabytesSetting)
            .ConfigureAwait(true);
        FabNativeCacheMaximumGigabytes = string.IsNullOrWhiteSpace(nativeCacheMaximum)
            ? "50"
            : nativeCacheMaximum.Trim();
        try
        {
            var nativeCacheMaximumBytes = ParseMaximumLibraryBytes(
                FabNativeCacheMaximumGigabytes,
                "cache Vault Epic");
            EpicVaultNativeCacheStatus = nativeCacheMaximumBytes > 0
                ? $"Cache de reconstruction prêt · limite {FormatBytes(nativeCacheMaximumBytes)}."
                : "Cache de reconstruction prêt · taille illimitée.";
        }
        catch (InvalidOperationException exception)
        {
            FabNativeCacheMaximumGigabytes = "50";
            EpicVaultNativeCacheStatus = "Limite invalide ignorée · valeur rétablie à 50 Gio.";
            AppendRawLog("WARN", "Settings/Marketplaces", exception.Message);
        }
    }

    private void OpenNativeStoreSourceEditor(MarketplaceNativeStoreKind store)
    {
        _pendingNativeStoreKind = store;
        PendingNativeStoreConvertOnUse = false;
        PendingNativeStorePath = store == MarketplaceNativeStoreKind.EpicGamesStore
            ? _currentEpicSnapshot?.VaultCachePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Epic",
                "EpicGamesLauncher",
                "VaultCache")
            : QuixelLibraryRoot;
        OnPropertyChanged(nameof(PendingNativeStoreTitle));
        OnPropertyChanged(nameof(PendingNativeStoreDescription));
        OnPropertyChanged(nameof(PendingNativeStoreModeTitle));
        OnPropertyChanged(nameof(PendingNativeStoreModeDescription));
        OnPropertyChanged(nameof(PendingNativeStoreConversionSummary));
        IsNativeStoreSourceEditorVisible = true;
    }

    private async Task ConfirmNativeStoreSourceAsync()
    {
        if (string.IsNullOrWhiteSpace(PendingNativeStorePath))
        {
            throw new InvalidOperationException("Choisissez le dossier du store à ajouter.");
        }

        var path = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(PendingNativeStorePath.Trim().Trim('"')));
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Le dossier du store est introuvable : {path}");
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                "La racine d’un store natif ne peut pas être un lien ou un point de jonction.");
        }

        var mode = PendingNativeStoreConvertOnUse
            ? MarketplaceNativeStoreMode.ConvertOnUse
            : MarketplaceNativeStoreMode.NativeReadOnly;
        var sources = _pendingNativeStoreKind == MarketplaceNativeStoreKind.EpicGamesStore
            ? EpicNativeStoreSources
            : MegascansNativeStoreSources;
        var existing = sources.FirstOrDefault(item =>
            string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            sources.Remove(existing);
        }
        var source = new MarketplaceNativeStoreSource(
            existing?.Id ?? Guid.NewGuid().ToString("N"),
            _pendingNativeStoreKind,
            path,
            mode,
            existing?.Source.AddedAt ?? DateTimeOffset.UtcNow);
        sources.Add(CreateNativeStoreSourceViewModel(source));
        await PersistMarketplaceNativeStoresAsync().ConfigureAwait(true);

        if (source.Store == MarketplaceNativeStoreKind.EpicGamesStore)
        {
            await AddUniquePathAsync(FabLocalLibraryRootsSetting, path).ConfigureAwait(true);
        }

        IsNativeStoreSourceEditorVisible = false;
        StatusMessage = source.Mode == MarketplaceNativeStoreMode.NativeReadOnly
            ? $"Source {source.Store} ajoutée en lecture seule."
            : $"Source {source.Store} ajoutée · conversion à la demande avec le profil du store.";
        AppendRawLog(
            "INFO",
            "Settings/Marketplaces",
            $"Source native {source.Store} : {path} · mode {source.Mode}.");

        if (source.Store == MarketplaceNativeStoreKind.EpicGamesStore)
        {
            await RefreshFabLocalLibraryAsync().ConfigureAwait(true);
        }
        else
        {
            await RefreshQuixelLocalLibraryAsync().ConfigureAwait(true);
        }
    }

    private async Task LoadMarketplaceNativeStoresAsync()
    {
        EpicNativeStoreSources.Clear();
        MegascansNativeStoreSources.Clear();
        var json = await _settings.GetAsync(MarketplaceNativeStoresSetting).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var sources = JsonSerializer.Deserialize<MarketplaceNativeStoreSource[]>(json) ?? [];
            foreach (var source in sources
                         .Where(source => !string.IsNullOrWhiteSpace(source.Id) &&
                                          !string.IsNullOrWhiteSpace(source.Path) &&
                                          Enum.IsDefined(source.Store) &&
                                          Enum.IsDefined(source.Mode))
                         .Take(64))
            {
                string path;
                try
                {
                    path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.Path));
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                    continue;
                }
                var normalized = source with { Path = path };
                var target = normalized.Store == MarketplaceNativeStoreKind.EpicGamesStore
                    ? EpicNativeStoreSources
                    : MegascansNativeStoreSources;
                if (target.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }
                target.Add(CreateNativeStoreSourceViewModel(normalized));
                if (normalized.Store == MarketplaceNativeStoreKind.EpicGamesStore && Directory.Exists(path))
                {
                    await AddUniquePathAsync(FabLocalLibraryRootsSetting, path).ConfigureAwait(true);
                }
            }
        }
        catch (JsonException exception)
        {
            AppendRawLog(
                "WARN",
                "Settings/Marketplaces",
                $"Sources natives ignorées : {exception.Message}");
        }
    }

    private MarketplaceNativeStoreSourceViewModel CreateNativeStoreSourceViewModel(
        MarketplaceNativeStoreSource source) => new(
        source,
        OpenFolderAsync,
        RemoveNativeStoreSourceAsync,
        ShowError);

    private async Task RemoveNativeStoreSourceAsync(MarketplaceNativeStoreSourceViewModel item)
    {
        var target = item.Source.Store == MarketplaceNativeStoreKind.EpicGamesStore
            ? EpicNativeStoreSources
            : MegascansNativeStoreSources;
        target.Remove(item);
        await PersistMarketplaceNativeStoresAsync().ConfigureAwait(true);
        if (item.Source.Store == MarketplaceNativeStoreKind.EpicGamesStore &&
            !EpicNativeStoreSources.Any(source =>
                string.Equals(source.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
        {
            await RemoveConfiguredPathAsync(FabLocalLibraryRootsSetting, item.Path)
                .ConfigureAwait(true);
            await RefreshFabLocalLibraryAsync().ConfigureAwait(true);
        }
        else if (item.Source.Store == MarketplaceNativeStoreKind.Megascans)
        {
            await RefreshQuixelLocalLibraryAsync().ConfigureAwait(true);
        }
        StatusMessage = "Source native retirée de CyEngineHub · aucun fichier n’a été supprimé.";
    }

    private Task PersistMarketplaceNativeStoresAsync()
    {
        var sources = EpicNativeStoreSources
            .Concat(MegascansNativeStoreSources)
            .Select(item => item.Source)
            .ToArray();
        return _settings.SetAsync(
            MarketplaceNativeStoresSetting,
            JsonSerializer.Serialize(sources));
    }

    private async Task LoadMarketplaceStorageProfileAsync(
        string settingKey,
        string marketplace,
        string defaultCache,
        string defaultDownload,
        ObservableCollection<MarketplaceLibraryLocationViewModel> locations,
        Action<MarketplaceStorageProfile> applyPaths)
    {
        MarketplaceStorageProfile profile;
        var json = await _settings.GetAsync(settingKey).ConfigureAwait(true);
        try
        {
            profile = string.IsNullOrWhiteSpace(json)
                ? new MarketplaceStorageProfile(
                    marketplace,
                    defaultCache,
                    defaultDownload,
                    [new MarketplaceLibraryLocation(LibraryDownloadRoot)])
                : JsonSerializer.Deserialize<MarketplaceStorageProfile>(json) ??
                  throw new JsonException("Profil vide.");
            profile = NormalizeMarketplaceStorageProfile(profile, marketplace);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            AppendRawLog(
                "WARN",
                "Settings/Marketplaces",
                $"Profil {marketplace} ignoré : {exception.Message}");
            profile = NormalizeMarketplaceStorageProfile(
                new MarketplaceStorageProfile(
                    marketplace,
                    defaultCache,
                    defaultDownload,
                    [new MarketplaceLibraryLocation(LibraryDownloadRoot)]),
                marketplace);
        }
        ApplyMarketplaceStorageProfile(profile, locations, applyPaths);
    }

    private static MarketplaceStorageProfile BuildMarketplaceStorageProfile(
        string marketplace,
        string cacheDirectory,
        string downloadDirectory,
        ObservableCollection<MarketplaceLibraryLocationViewModel> locations)
    {
        if (locations.Count == 0)
        {
            throw new InvalidOperationException(
                $"Ajoutez au moins une bibliothèque pour {marketplace}.");
        }
        var libraries = new List<MarketplaceLibraryLocation>();
        var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var location in locations)
        {
            if (string.IsNullOrWhiteSpace(location.Path))
            {
                throw new InvalidOperationException(
                    $"Un emplacement {marketplace} ne possède pas de chemin.");
            }
            var path = NormalizeMarketplacePath(location.Path, $"bibliothèque {marketplace}");
            if (!uniquePaths.Add(path))
            {
                throw new InvalidOperationException(
                    $"La bibliothèque {path} apparaît plusieurs fois pour {marketplace}.");
            }
            var maximumBytes = ParseMaximumLibraryBytes(location.MaximumGigabytes, marketplace);
            Directory.CreateDirectory(path);
            libraries.Add(new MarketplaceLibraryLocation(path, maximumBytes, location.IsEnabled));
        }
        if (!libraries.Any(location => location.Enabled))
        {
            throw new InvalidOperationException(
                $"Activez au moins une bibliothèque pour {marketplace}.");
        }
        var profile = new MarketplaceStorageProfile(
            marketplace,
            NormalizeMarketplacePath(cacheDirectory, $"cache {marketplace}"),
            NormalizeMarketplacePath(downloadDirectory, $"téléchargements {marketplace}"),
            libraries);
        Directory.CreateDirectory(profile.CacheDirectory);
        Directory.CreateDirectory(profile.DownloadDirectory);
        return profile;
    }

    private static MarketplaceStorageProfile NormalizeMarketplaceStorageProfile(
        MarketplaceStorageProfile profile,
        string marketplace)
    {
        var libraries = profile.Libraries
            .Where(location => !string.IsNullOrWhiteSpace(location.Path))
            .Select(location => location with
            {
                Path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(location.Path)),
                MaximumBytes = Math.Max(0, location.MaximumBytes),
            })
            .Take(32)
            .ToArray();
        if (libraries.Length == 0)
        {
            throw new ArgumentException($"Le profil {marketplace} ne contient aucune bibliothèque.");
        }
        return profile with
        {
            Marketplace = marketplace,
            CacheDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.CacheDirectory)),
            DownloadDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profile.DownloadDirectory)),
            Libraries = libraries,
        };
    }

    private static void ApplyMarketplaceStorageProfile(
        MarketplaceStorageProfile profile,
        ObservableCollection<MarketplaceLibraryLocationViewModel> locations,
        Action<MarketplaceStorageProfile> applyPaths)
    {
        applyPaths(profile);
        locations.Clear();
        foreach (var location in profile.Libraries)
        {
            var maximumGigabytes = location.MaximumBytes <= 0
                ? string.Empty
                : (location.MaximumBytes / 1024d / 1024d / 1024d)
                    .ToString("0.##", CultureInfo.CurrentCulture);
            locations.Add(CreateMarketplaceLibraryLocation(
                locations,
                location.Path,
                maximumGigabytes,
                location.Enabled));
        }
        ReindexMarketplaceLibraryLocations(locations);
    }

    private static MarketplaceLibraryLocationViewModel CreateMarketplaceLibraryLocation(
        ObservableCollection<MarketplaceLibraryLocationViewModel> locations,
        string path = "",
        string maximumGigabytes = "",
        bool isEnabled = true) =>
        new(
            path,
            maximumGigabytes,
            isEnabled,
            item => MoveMarketplaceLibraryLocation(locations, item, -1),
            item => MoveMarketplaceLibraryLocation(locations, item, 1),
            item => RemoveMarketplaceLibraryLocation(locations, item));

    private static void AddMarketplaceLibraryLocation(
        ObservableCollection<MarketplaceLibraryLocationViewModel> locations)
    {
        locations.Add(CreateMarketplaceLibraryLocation(locations));
        ReindexMarketplaceLibraryLocations(locations);
    }

    private static void MoveMarketplaceLibraryLocation(
        ObservableCollection<MarketplaceLibraryLocationViewModel> locations,
        MarketplaceLibraryLocationViewModel item,
        int offset)
    {
        var index = locations.IndexOf(item);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= locations.Count) return;
        locations.Move(index, target);
        ReindexMarketplaceLibraryLocations(locations);
    }

    private static void RemoveMarketplaceLibraryLocation(
        ObservableCollection<MarketplaceLibraryLocationViewModel> locations,
        MarketplaceLibraryLocationViewModel item)
    {
        locations.Remove(item);
        ReindexMarketplaceLibraryLocations(locations);
    }

    private static void ReindexMarketplaceLibraryLocations(
        ObservableCollection<MarketplaceLibraryLocationViewModel> locations)
    {
        for (var index = 0; index < locations.Count; index++)
        {
            locations[index].SetPriority(index + 1);
        }
    }

    private static long ParseMaximumLibraryBytes(string value, string marketplace)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        if (!double.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out var gigabytes) &&
            !double.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out gigabytes))
        {
            throw new InvalidOperationException(
                $"Le quota {marketplace} « {value} » n’est pas un nombre valide en Gio.");
        }
        if (gigabytes < 0 || gigabytes > 1024d * 1024d)
        {
            throw new InvalidOperationException(
                $"Le quota {marketplace} doit être compris entre 0 et 1 048 576 Gio.");
        }
        return checked((long)Math.Round(gigabytes * 1024d * 1024d * 1024d));
    }

    private static string NormalizeMarketplacePath(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Choisissez un dossier pour {label}.");
        }
        return MarketplaceStorageSettingsStore.NormalizePath(value);
    }

    private async Task SaveUnrealVersionBridgeSettingsAsync()
    {
        if (!string.IsNullOrWhiteSpace(UnrealVersionBridgeAssetProfileFile))
        {
            var profilePath = Path.GetFullPath(UnrealVersionBridgeAssetProfileFile.Trim().Trim((char)34));
            _ = UnrealAssetConversionProfiles.Load(profilePath);
            UnrealVersionBridgeAssetProfileFile = profilePath;
        }
        if (string.IsNullOrWhiteSpace(UnrealVersionBridgeStorageRoot))
        {
            throw new InvalidOperationException("Choisissez un dossier de stockage pour les variantes converties.");
        }
        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(UnrealVersionBridgeStorageRoot.Trim().Trim((char)34)));
        if (string.Equals(root, Path.GetPathRoot(root), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("La racine d’un volume ne peut pas servir de stockage au plugin.");
        }
        Directory.CreateDirectory(root);
        UnrealVersionBridgeStorageRoot = root;
        UnrealVersionBridgeProfile = string.Equals(
            UnrealVersionBridgeProfile,
            "extended",
            StringComparison.OrdinalIgnoreCase)
            ? "extended"
            : "safe";
        await _settings.SetAsync(UnrealVersionBridgeSettings.StorageRoot, root).ConfigureAwait(true);
        await _settings.SetAsync(UnrealVersionBridgeSettings.AssetProfileFile, UnrealVersionBridgeAssetProfileFile).ConfigureAwait(true);
        OnPropertyChanged(nameof(UnrealVersionBridgeAssetProfilesSummary));
        await _settings.SetAsync(
            UnrealVersionBridgeSettings.ConversionProfile,
            UnrealVersionBridgeProfile).ConfigureAwait(true);
        await _settings.SetAsync(
            UnrealVersionBridgeSettings.KeepStaging,
            UnrealVersionBridgeKeepStaging.ToString()).ConfigureAwait(true);
        await _settings.SetAsync(
            UnrealVersionBridgeSettings.ValidateOutput,
            UnrealVersionBridgeValidateOutput.ToString()).ConfigureAwait(true);
        StatusMessage = "Paramètres Unreal Version Bridge enregistrés.";
        AppendRawLog(
            "INFO",
            "Extensions/VersionBridge",
            $"Stockage={root} · profil={UnrealVersionBridgeProfile} · staging={UnrealVersionBridgeKeepStaging} · validation={UnrealVersionBridgeValidateOutput}");
    }

    private async Task RefreshMarketplacesOnStartupAsync()
    {
        await Task.Delay(800).ConfigureAwait(true);
        try
        {
            if (AutoRefreshFabOnStartup && IsDirectFabConnected && IsFabLibrarySourceAvailable)
            {
                await RefreshDirectFabLibraryAsync().ConfigureAwait(true);
            }
            if (AutoRefreshQuixelOnStartup && IsQuixelConnected && IsQuixelLibrarySourceAvailable)
            {
                await RefreshQuixelLibraryAsync().ConfigureAwait(true);
            }
        }
        catch (Exception exception)
        {
            AppendRawLog("ERROR", "Library/StartupRefresh", exception.ToString());
            StatusMessage = $"Actualisation automatique incomplète : {exception.Message}";
        }
    }

    private static bool ParseEnabledSetting(string? value) =>
        !bool.TryParse(value, out var enabled) || enabled;

    private static LibraryPackageStorageMode ParseCompressionSetting(
        string? value,
        LibraryPackageStorageMode fallback)
    {
        return Enum.TryParse<LibraryPackageStorageMode>(value, ignoreCase: true, out var parsed) &&
               parsed is not LibraryPackageStorageMode.Uncompressed
            ? parsed
            : fallback;
    }

    private MarketplaceCompressionOptionViewModel FindCompressionOption(
        LibraryPackageStorageMode mode) =>
        MarketplaceCompressionOptions.First(option => option.StorageMode == mode);

    private static LibraryPackageStorageMode GetCompressionMode(
        MarketplaceCompressionOptionViewModel? option) =>
        option?.StorageMode ?? LibraryPackageStorageMode.ZipFastest;

    private async Task RefreshQuixelLocalLibraryAsync()
    {
        QuixelLocalAssets.Clear();
        OnPropertyChanged(nameof(HasQuixelLocalAssets));
        var roots = MegascansNativeStoreSources
            .Select(item => item.Path)
            .Prepend(QuixelLibraryRoot)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(path.Trim().Trim('"'))))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (roots.Length == 0)
        {
            QuixelLibraryStatus = "Ajoutez un dossier Megascans natif dans Paramètres → Marketplaces.";
            InvalidateLibraryLocalIndex();
            return;
        }

        QuixelLibraryStatus = $"Analyse bornée de {roots.Length:N0} bibliothèque(s) Megascans native(s)…";
        var entries = await Task.Run(() => roots
                .SelectMany(ScanQuixelLibrary)
                .DistinctBy(item => $"{item.AssetId}\n{item.Path}", StringComparer.OrdinalIgnoreCase)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray())
            .ConfigureAwait(true);
        foreach (var entry in entries)
        {
            QuixelLocalAssets.Add(new QuixelLocalAssetCardViewModel(
                entry.Name,
                entry.AssetId,
                entry.Path,
                entry.Kind,
                UseQuixelLocalAssetAsync,
                OpenFolderAsync,
                ShowError));
        }
        OnPropertyChanged(nameof(HasQuixelLocalAssets));
        QuixelLibraryStatus = entries.Length == 0
            ? "Aucun asset téléchargé par Quixel Bridge n’a été détecté."
            : $"{entries.Length:N0} asset(s) Megascans détecté(s) dans {roots.Length:N0} source(s) native(s).";
        AppendRawLog("INFO", "Library/Quixel", $"Bibliothèques natives : {entries.Length:N0} asset(s) dans {roots.Length:N0} racine(s).");
        InvalidateLibraryLocalIndex();
    }

    private static QuixelLocalEntry[] ScanQuixelLibrary(string root)
    {
        var downloadedRoot = Path.Combine(root, "Downloaded");
        if (!Directory.Exists(downloadedRoot))
        {
            return [];
        }

        var results = new List<QuixelLocalEntry>();
        foreach (var kindDirectory in EnumerateDirectoriesSafe(downloadedRoot).Take(64))
        {
            var kind = Path.GetFileName(kindDirectory);
            foreach (var assetDirectory in EnumerateDirectoriesSafe(kindDirectory).Take(10_000))
            {
                if (results.Count >= 10_000)
                {
                    break;
                }
                try
                {
                    if ((File.GetAttributes(assetDirectory) & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                    var metadataPath = EnumerateFilesSafe(assetDirectory, "*.json")
                        .FirstOrDefault();
                    var assetId = Path.GetFileName(assetDirectory);
                    var name = assetId;
                    if (metadataPath is not null && new FileInfo(metadataPath).Length <= 4 * 1024 * 1024)
                    {
                        using var document = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
                        name = FindQuixelMetadataString(document.RootElement, "name", "title", "displayName")
                               ?? name;
                        assetId = FindQuixelMetadataString(document.RootElement, "id", "assetId")
                                  ?? assetId;
                    }
                    results.Add(new QuixelLocalEntry(name, assetId, assetDirectory, kind));
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    // Un asset Bridge incomplet ne doit pas bloquer le reste de la bibliothèque.
                }
            }
        }
        return results
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task UseQuixelLocalAssetAsync(QuixelLocalAssetCardViewModel card)
    {
        var configuredSource = MegascansNativeStoreSources.FirstOrDefault(item =>
            IsSameOrChildPath(card.Path, item.Path));
        var convert = configuredSource?.Source.Mode == MarketplaceNativeStoreMode.ConvertOnUse;
        DirectFabDownloadResult? converted = null;
        RemoteFabAsset? syntheticAsset = null;
        FabInstallVersionOptionViewModel? syntheticOption = null;
        if (convert)
        {
            var storageProfile = BuildMarketplaceStorageProfile(
                "Megascans",
                QuixelCacheDirectory,
                QuixelDownloadDirectory,
                QuixelLibraryLocations);
            var storageMode = GetCompressionMode(SelectedQuixelCompressionOption);
            var version = new RemoteFabProjectVersion(
                $"megascans-native:{card.AssetId}",
                [],
                [],
                ["Windows"]);
            syntheticAsset = new RemoteFabAsset(
                card.AssetId,
                "quixel-native",
                card.Name,
                "Asset importé depuis une bibliothèque Megascans native.",
                "quixel",
                "NativeBridgeLibrary",
                null,
                null,
                [card.Kind],
                [version]);
            syntheticOption = new FabInstallVersionOptionViewModel(version, true);
            var sourceResult = new DirectFabDownloadResult(
                card.AssetId,
                card.Name,
                card.Path,
                0,
                0,
                DateTimeOffset.UtcNow,
                new DirectFabSourceManifest(
                    "MegascansBridgeNative",
                    card.AssetId,
                    "native",
                    1,
                    0,
                    0));
            var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
                "Bibliothèque · Megascans",
                $"Convertir {card.Name} depuis le store natif",
                async (context, cancellationToken) =>
                {
                    var progress = new InlineProgress<DirectFabDownloadProgress>(value =>
                        context.Report(value.Fraction, value.Message));
                    var conversionCopy = await EpicVaultNativeStoreService.CopyForConversionAsync(
                            sourceResult,
                            storageProfile.DownloadDirectory,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(false);
                    converted = await LibraryPackageService.PackageAsync(
                            syntheticAsset,
                            version,
                            conversionCopy,
                            storageMode,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(false);
                    converted = await MarketplaceStorageRouter.PlaceAsync(
                            converted,
                            storageProfile,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(false);
                    context.Report(1, "Asset Megascans converti et rangé dans la bibliothèque.");
                }));
            var completed = await handle.Completion.ConfigureAwait(true);
            if (completed.Status != HubTaskStatus.Completed || converted is null)
            {
                QuixelLibraryStatus = completed.Status == HubTaskStatus.Canceled
                    ? "Conversion Megascans annulée."
                    : $"Conversion Megascans impossible : {completed.Error}";
                return;
            }
            await SaveStoredFabDownloadAsync(syntheticAsset, syntheticOption, converted)
                .ConfigureAwait(true);
        }

        ManualFabContentSource = card.Path;
        ManualUnityFabContentSource = card.Path;
        ManualGodotFabContentSource = card.Path;
        ManualCryEngineFabContentSource = card.Path;
        var mode = convert
            ? $"copie convertie avec le profil {SelectedQuixelCompressionOption?.Label ?? "Rapide"}"
            : "source native utilisée en lecture seule";
        QuixelLibraryStatus = $"{card.Name} · {mode}.";
        FabProjectImportStatus = $"Asset Megascans sélectionné : {card.Name}. Choisissez un projet cible.";
        StatusMessage = QuixelLibraryStatus;
        ShowSection(PageSection.Epic);
    }

    private static string[] EnumerateDirectoriesSafe(string path)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] EnumerateFilesSafe(string path, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly)
                .Concat(EnumerateDirectoriesSafe(path)
                    .Take(32)
                    .SelectMany(child =>
                    {
                        try { return Directory.EnumerateFiles(child, pattern, SearchOption.TopDirectoryOnly).ToArray(); }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
                    }))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? FindQuixelMetadataString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        foreach (var property in element.EnumerateObject())
        {
            if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.Value.GetString()))
            {
                return property.Value.GetString();
            }
        }
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                var nested = FindQuixelMetadataString(property.Value, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }
        return null;
    }

    private async Task LoadFabImportHistoryAsync()
    {
        var receipts = await _fabProjectContentImport.LoadHistoryAsync(
                Projects.Select(project => project.Project.FilePath).ToArray())
            .ConfigureAwait(true);
        FabImportHistory.Clear();
        foreach (var receipt in receipts)
        {
            FabImportHistory.Add(new FabImportHistoryCardViewModel(
                receipt,
                PrepareFabRollbackAsync,
                ShowError));
        }

        OnPropertyChanged(nameof(HasNoFabImportHistory));

        var unityReceipts = await _unityFabAssetImport.LoadHistoryAsync(
                UnityFabProjects.Select(project => project.Project.RootPath).ToArray())
            .ConfigureAwait(true);
        UnityFabImportHistory.Clear();
        foreach (var receipt in unityReceipts)
        {
            UnityFabImportHistory.Add(new UnityFabImportHistoryCardViewModel(
                receipt,
                PrepareUnityFabRollbackAsync,
                ShowError));
        }
        OnPropertyChanged(nameof(HasNoUnityFabImportHistory));

        var godotReceipts = await _godotFabAssetImport.LoadHistoryAsync(
                GodotFabProjects.Select(project => project.Project.RootPath).ToArray())
            .ConfigureAwait(true);
        GodotFabImportHistory.Clear();
        foreach (var receipt in godotReceipts)
        {
            GodotFabImportHistory.Add(new GodotFabImportHistoryCardViewModel(
                receipt,
                PrepareGodotFabRollbackAsync,
                ShowError));
        }
        OnPropertyChanged(nameof(HasNoGodotFabImportHistory));

        var cryEngineReceipts = await _cryEngineFabAssetImport.LoadHistoryAsync(
                CryEngineFabProjects.Select(project => project.Project.RootPath).ToArray())
            .ConfigureAwait(true);
        CryEngineFabImportHistory.Clear();
        foreach (var receipt in cryEngineReceipts)
        {
            CryEngineFabImportHistory.Add(new CryEngineFabImportHistoryCardViewModel(
                receipt,
                PrepareCryEngineFabRollbackAsync,
                ShowError));
        }
        OnPropertyChanged(nameof(HasNoCryEngineFabImportHistory));
    }

    private async Task LoadFabLocalLibraryCacheAsync()
    {
        var json = await _settings.GetAsync(FabLocalLibrarySnapshotSetting).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var packs = (JsonSerializer.Deserialize<FabLocalContentPack[]>(json) ?? [])
                .Where(IsValidCachedFabPack)
                .ToArray();
            ReplaceFabLocalLibrary(packs);
            if (packs.Length > 0)
            {
                var newest = packs.Max(pack => pack.DiscoveredAt).ToLocalTime();
                FabLocalLibraryStatus =
                    $"{packs.Length:N0} pack(s) en cache · dernière analyse {newest:g}";
            }
        }
        catch (JsonException)
        {
            FabLocalLibraryStatus = "Cache de bibliothèque Fab illisible; relancez une analyse.";
        }
    }

    private async Task RefreshFabLocalLibraryAsync()
    {
        var roots = await ReadConfiguredPathsAsync(FabLocalLibraryRootsSetting)
            .ConfigureAwait(true);
        if (roots.Count == 0)
        {
            FabLocalLibraryStatus =
                "Ajoutez d’abord un dossier contenant vos packs Unreal, Unity ou Godot extraits.";
            await _settings.SetAsync(FabLocalLibrarySnapshotSetting, "[]").ConfigureAwait(true);
            ReplaceFabLocalLibrary([]);
            return;
        }

        IsEpicBusy = true;
        FabLocalLibraryStatus = $"Analyse manuelle de {roots.Count:N0} racine(s) locale(s)…";
        IReadOnlyList<FabLocalContentPack> packs = [];
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Fab",
            "Actualiser la bibliothèque Fab locale",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<FabLocalLibraryProgress>(value =>
                {
                    var ratio = value.Total == 0
                        ? 0
                        : Math.Clamp((double)value.Completed / value.Total, 0, 1);
                    context.Report(ratio, value.Message);
                });
                packs = await _fabLocalContentLibrary.DiscoverAsync(
                        new FabLocalLibraryRequest(roots),
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        if (completed.Status == HubTaskStatus.Canceled)
        {
            FabLocalLibraryStatus = "Analyse Fab locale annulée; le cache précédent est conservé.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed)
        {
            FabLocalLibraryStatus = $"Échec de l’analyse Fab locale : {completed.Error}";
            return;
        }

        var serializedPacks = await Task.Run(
                () => JsonSerializer.Serialize(packs))
            .ConfigureAwait(true);
        await _settings.SetAsync(
                FabLocalLibrarySnapshotSetting,
                serializedPacks)
            .ConfigureAwait(true);
        ReplaceFabLocalLibrary(packs);
        var partialCount = packs.Count(pack => pack.Health == FabLocalContentPackHealth.Partial);
        var unrealCount = packs.Count(pack => pack.Kind == FabLocalContentKind.Unreal);
        var unityCount = packs.Count(pack => pack.Kind == FabLocalContentKind.Unity);
        var godotCount = packs.Count(pack => pack.Kind == FabLocalContentKind.Godot);
        var cryEngineCount = packs.Count(pack => pack.Kind == FabLocalContentKind.CryEngine);
        var portableCount = packs.Count(pack => pack.Kind == FabLocalContentKind.Generic);
        FabLocalLibraryStatus =
            $"{packs.Count:N0} pack(s) · {unrealCount:N0} Unreal · {unityCount:N0} Unity · {godotCount:N0} Godot · {cryEngineCount:N0} CryEngine · {portableCount:N0} portable(s) · " +
            $"{partialCount:N0} inventaire(s) partiel(s) · cache actualisé";
        StatusMessage = "Bibliothèque Fab locale actualisée.";
        ShowSection(PageSection.Epic);
    }

    private void ReplaceFabLocalLibrary(IReadOnlyList<FabLocalContentPack> packs)
    {
        var cards = new List<FabLocalContentPackCardViewModel>(packs.Count);
        foreach (var pack in packs)
        {
            var card = new FabLocalContentPackCardViewModel(
                pack,
                SelectFabLocalContentPack,
                OpenLocalPackCollectionEditor,
                OpenFolderAsync,
                ShowError);
            if (_fabCollections.TryGetValue(pack.Id, out var collections))
            {
                card.SetCollections(collections);
            }
            cards.Add(card);
        }
        FabLocalContentPacks.ReplaceWith(cards);
        InvalidateLibraryLocalIndex();

        OnPropertyChanged(nameof(HasNoFabLocalContentPacks));
        OnPropertyChanged(nameof(FabLocalContentPackCount));
        OnPropertyChanged(nameof(FabConnectionStatus));
        OnPropertyChanged(nameof(FabConnectionIndicator));
        OnPropertyChanged(nameof(IsQuixelLibrarySourceAvailable));
        OnPropertyChanged(nameof(HasLocalFabIntegration));
        RebuildProjectCreationCollections();
    }

    private async Task SelectFabLocalContentPack(FabLocalContentPack pack)
    {
        if (pack.Kind == FabLocalContentKind.EpicVault)
        {
            await PrepareEpicVaultLocalPackAsync(pack).ConfigureAwait(true);
        }
        else if (pack.Kind == FabLocalContentKind.Generic)
        {
            ManualFabContentSource = pack.ContentDirectory;
            ManualUnityFabContentSource = pack.ContentDirectory;
            ManualGodotFabContentSource = pack.ContentDirectory;
            ManualCryEngineFabContentSource = pack.ContentDirectory;
            FabProjectImportStatus = $"Pack portable sélectionné : {pack.DisplayName}. Choisissez un projet Unreal cible.";
            UnityFabImportStatus = $"Pack portable sélectionné : {pack.DisplayName}. Choisissez un projet Unity cible.";
            GodotFabImportStatus = $"Pack portable sélectionné : {pack.DisplayName}. Choisissez un projet Godot cible.";
            CryEngineFabImportStatus = $"Pack portable sélectionné : {pack.DisplayName}. Choisissez un projet CryEngine cible.";
            StatusMessage = $"Pack portable multi-moteur sélectionné : {pack.DisplayName}.";
        }
        else if (pack.Kind == FabLocalContentKind.CryEngine)
        {
            ManualCryEngineFabContentSource = pack.PackageRoot;
            CryEngineFabImportStatus = pack.BlockedFileCount == 0
                ? $"Source CryEngine sélectionnée : {pack.DisplayName}. Choisissez le projet cible puis analysez l’import."
                : $"Source CryEngine sélectionnée : {pack.DisplayName}, mais {pack.BlockedFileCount:N0} fichier(s) actif(s) feront refuser l’import.";
            StatusMessage = $"Pack CryEngine local sélectionné : {pack.DisplayName}.";
        }
        else if (pack.Kind == FabLocalContentKind.Godot)
        {
            ManualGodotFabContentSource = pack.PackageRoot;
            GodotFabImportStatus = pack.BlockedFileCount == 0
                ? $"Source Godot sélectionnée : {pack.DisplayName}. Choisissez le projet cible puis analysez l’import."
                : $"Source Godot sélectionnée : {pack.DisplayName}, mais {pack.BlockedFileCount:N0} fichier(s) actif(s) feront refuser l’import.";
            StatusMessage = $"Pack Godot local sélectionné : {pack.DisplayName}.";
        }
        else if (pack.Kind == FabLocalContentKind.Unity)
        {
            ManualUnityFabContentSource = pack.PackageRoot;
            UnityFabImportStatus = pack.BlockedFileCount == 0
                ? $"Source Unity sélectionnée : {pack.DisplayName}. Choisissez le projet cible puis analysez l’import."
                : $"Source Unity sélectionnée : {pack.DisplayName}, mais {pack.BlockedFileCount:N0} fichier(s) actif(s) feront refuser l’import.";
            StatusMessage = $"Pack Unity local sélectionné : {pack.DisplayName}.";
        }
        else
        {
            ManualFabContentSource = pack.ProjectPath is { } projectPath && File.Exists(projectPath)
                ? projectPath
                : pack.ContentDirectory;
            FabProjectImportStatus =
                $"Source Unreal sélectionnée : {pack.DisplayName}. Choisissez le projet cible puis analysez l’import.";
            StatusMessage = $"Pack Unreal local sélectionné : {pack.DisplayName}.";
        }
        ShowSection(PageSection.Epic);
    }

    private async Task PrepareEpicVaultLocalPackAsync(FabLocalContentPack pack)
    {
        var configuredSource = EpicNativeStoreSources.FirstOrDefault(item =>
            IsSameOrChildPath(pack.PackageRoot, item.Path));
        var convert = configuredSource?.Source.Mode == MarketplaceNativeStoreMode.ConvertOnUse;
        var storageMode = GetCompressionMode(SelectedFabCompressionOption);
        var maximumCacheBytes = ParseMaximumLibraryBytes(
            FabNativeCacheMaximumGigabytes,
            "cache Vault Epic");
        var matchingRemoteAsset = convert ? FindRemoteFabAssetForNativePack(pack) : null;
        MarketplaceStorageProfile? storageProfile = null;
        if (convert)
        {
            storageProfile = BuildMarketplaceStorageProfile(
                "Fab",
                FabCacheDirectory,
                FabDownloadDirectory,
                FabLibraryLocations);
        }

        DirectFabDownloadResult? materialized = null;
        DirectFabDownloadResult? converted = null;
        RemoteFabAsset? syntheticAsset = null;
        FabInstallVersionOptionViewModel? syntheticOption = null;
        IsEpicBusy = true;
        FabLocalLibraryStatus = $"Préparation du Vault natif « {pack.DisplayName} »…";
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Bibliothèque · Epic Vault",
            convert
                ? $"Reconstruire et convertir {pack.DisplayName}"
                : $"Reconstruire {pack.DisplayName} en lecture seule",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<DirectFabDownloadProgress>(value =>
                    context.Report(value.Fraction, value.Message));
                materialized = await EpicVaultNativeStoreService.MaterializeAsync(
                        pack,
                        Path.Combine(FabCacheDirectory, "NativeStores"),
                        progress,
                        maximumCacheBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!convert)
                {
                    context.Report(1, "Package Vault prêt dans le cache natif en lecture seule.");
                    return;
                }

                var version = new RemoteFabProjectVersion(
                    $"epic-vault:{pack.Id}",
                    [],
                    string.IsNullOrWhiteSpace(pack.EngineAssociation)
                        ? []
                        : [pack.EngineAssociation],
                    ["Windows"]);
                syntheticAsset = matchingRemoteAsset ?? new RemoteFabAsset(
                    pack.Id,
                    "epic-vault-native",
                    pack.DisplayName,
                    "Package reconstruit depuis un store Epic Games natif.",
                    "epic-vault",
                    "EpicBuildPatchManifest",
                    null,
                    null,
                    ["Epic Vault"],
                    [version]);
                syntheticOption = new FabInstallVersionOptionViewModel(version, true);
                var conversionCopy = await EpicVaultNativeStoreService.CopyForConversionAsync(
                        materialized,
                        storageProfile!.DownloadDirectory,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                converted = await LibraryPackageService.PackageAsync(
                        syntheticAsset,
                        version,
                        conversionCopy,
                        storageMode,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                converted = await MarketplaceStorageRouter.PlaceAsync(
                        converted,
                        storageProfile,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                context.Report(1, "Package Vault converti et rangé dans la bibliothèque Fab.");
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        if (completed.Status != HubTaskStatus.Completed || materialized is null)
        {
            FabLocalLibraryStatus = completed.Status == HubTaskStatus.Canceled
                ? "Reconstruction du package Vault annulée."
                : $"Reconstruction du package Vault impossible : {completed.Error}";
            return;
        }
        if (converted is not null && syntheticAsset is not null && syntheticOption is not null)
        {
            await SaveStoredFabDownloadAsync(syntheticAsset, syntheticOption, converted)
                .ConfigureAwait(true);
        }

        ManualFabContentSource = FindMaterializedUnrealContent(materialized.DestinationDirectory);
        FabProjectImportStatus = convert
            ? $"Package Vault converti avec le profil {SelectedFabCompressionOption?.Label ?? "Rapide"} et source reconstruite prête pour l’import : {pack.DisplayName}."
            : $"Package Vault reconstruit en cache sans modifier le store source : {pack.DisplayName}.";
        FabLocalLibraryStatus = FabProjectImportStatus;
        StatusMessage = FabProjectImportStatus;
        ShowSection(PageSection.Epic);
    }

    private RemoteFabAsset? FindRemoteFabAssetForNativePack(FabLocalContentPack pack)
    {
        var normalizedTitle = NormalizeProductTitle(pack.DisplayName);
        if (normalizedTitle.Length == 0)
        {
            return null;
        }
        return _remoteFabCatalog.FirstOrDefault(asset =>
            !IsQuixelAsset(asset) &&
            string.Equals(
                NormalizeProductTitle(asset.Title),
                normalizedTitle,
                StringComparison.Ordinal));
    }

    private static string NormalizeProductTitle(string value) => new(
        value.Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

    private async Task TrimEpicVaultNativeCacheAsync()
    {
        var maximumBytes = ParseMaximumLibraryBytes(
            FabNativeCacheMaximumGigabytes,
            "cache Vault Epic");
        EpicVaultCacheMaintenanceResult? result = null;
        IsEpicBusy = true;
        EpicVaultNativeCacheStatus = "Mesure et nettoyage du cache Vault en arrière-plan…";
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Bibliothèque · Epic Vault",
            "Appliquer la limite du cache natif",
            async (context, cancellationToken) =>
            {
                context.Report(null, "Mesure du cache de reconstruction…");
                result = await EpicVaultNativeStoreService.TrimCacheAsync(
                        Path.Combine(FabCacheDirectory, "NativeStores"),
                        maximumBytes,
                        cancellationToken)
                    .ConfigureAwait(false);
                context.Report(1, "Limite du cache Vault appliquée.");
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        if (completed.Status != HubTaskStatus.Completed || result is null)
        {
            EpicVaultNativeCacheStatus = completed.Status == HubTaskStatus.Canceled
                ? "Nettoyage du cache Vault annulé."
                : $"Nettoyage du cache Vault impossible : {completed.Error}";
            return;
        }
        EpicVaultNativeCacheStatus = result.RemovedEntries == 0
            ? $"Cache conforme · {FormatBytes(result.AfterBytes)} utilisés."
            : $"{result.RemovedEntries:N0} reconstruction(s) évincée(s) · {FormatBytes(result.BeforeBytes)} → {FormatBytes(result.AfterBytes)}.";
        StatusMessage = EpicVaultNativeCacheStatus;
    }

    private static string FindMaterializedUnrealContent(string root)
    {
        var direct = Path.Combine(root, "Content");
        if (Directory.Exists(direct)) return direct;
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Depth >= 4) continue;
            foreach (var child in EnumerateDirectoriesSafe(current.Path))
            {
                if (string.Equals(Path.GetFileName(child), "Content", StringComparison.OrdinalIgnoreCase))
                {
                    return child;
                }
                queue.Enqueue((child, current.Depth + 1));
            }
        }
        return root;
    }

    private static bool IsValidCachedFabPack(FabLocalContentPack? pack)
    {
        if (pack is null ||
            string.IsNullOrWhiteSpace(pack.Id) ||
            string.IsNullOrWhiteSpace(pack.DisplayName) ||
            string.IsNullOrWhiteSpace(pack.PackageRoot) ||
            string.IsNullOrWhiteSpace(pack.ContentDirectory) ||
            pack.Issues is null ||
            pack.AssetCount < 0 ||
            pack.MapCount < 0 ||
            pack.SupportedFileCount < 0 ||
            pack.SupportedBytes < 0 ||
            pack.BlockedFileCount < 0 ||
            !Enum.IsDefined(pack.Kind))
        {
            return false;
        }

        try
        {
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(pack.PackageRoot));
            var content = Path.GetFullPath(pack.ContentDirectory);
            var relative = Path.GetRelativePath(root, content);
            var expectedDirectory = pack.Kind == FabLocalContentKind.Unity ? "Assets" : "Content";
            var contentLocationIsValid = pack.Kind switch
            {
                FabLocalContentKind.EpicVault =>
                    string.Equals(content, root, StringComparison.OrdinalIgnoreCase) &&
                    pack.ProjectPath is not null &&
                    File.Exists(pack.ProjectPath),
                FabLocalContentKind.Godot => string.Equals(content, root, StringComparison.OrdinalIgnoreCase),
                FabLocalContentKind.CryEngine => Directory.EnumerateFiles(root, "*.cryproject", SearchOption.TopDirectoryOnly).Take(2).Count() == 1,
                _ => string.Equals(Path.GetFileName(content), expectedDirectory, StringComparison.OrdinalIgnoreCase),
            };
            return Directory.Exists(content) &&
                   contentLocationIsValid &&
                   !Path.IsPathRooted(relative) &&
                   !relative.Equals("..", StringComparison.Ordinal) &&
                   !relative.StartsWith(
                       ".." + Path.DirectorySeparatorChar,
                       StringComparison.Ordinal);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    private async Task LoadGitCacheAsync()
    {
        var snapshots = await _gitSnapshotCache.LoadAsync().ConfigureAwait(true);
        ReplaceGitSnapshots(snapshots);
        if (snapshots.Count > 0)
        {
            var newest = snapshots.Max(snapshot => snapshot.RefreshedAt).ToLocalTime();
            GitStatusSummary =
                $"{snapshots.Count} dépôt(s) en cache · dernier snapshot {newest:g}";
        }
    }

    private async Task LoadPluginCacheAsync()
    {
        var plugins = await _pluginSnapshotCache.LoadAsync().ConfigureAwait(true);
        ReplacePluginCatalog(plugins);
        if (plugins.Count > 0)
        {
            var newest = plugins.Max(plugin => plugin.DiscoveredAt).ToLocalTime();
            PluginStatusSummary =
                $"{plugins.Count} plugin(s) en cache · dernier catalogue {newest:g}";
        }
    }

    private async Task LoadToolchainCacheAsync()
    {
        var snapshot = await _toolchainSnapshotCache.LoadAsync().ConfigureAwait(true);
        if (snapshot is null)
        {
            return;
        }

        ReplaceToolchainSnapshot(snapshot);
        ToolchainStatusSummary =
            $"Inventaire en cache · {snapshot.RefreshedAt.ToLocalTime():g} · " +
            $"{ToolchainProblemCount} problème(s) de compatibilité";
    }

    private async Task LoadStorageCacheAsync()
    {
        var snapshot = await _storageSnapshotCache.LoadAsync().ConfigureAwait(true);
        if (snapshot is null)
        {
            return;
        }

        ReplaceStorageSnapshot(snapshot);
        StorageStatusSummary =
            $"Inventaire en cache · {snapshot.RefreshedAt.ToLocalTime():g} · " +
            $"{snapshot.Entries.Count} emplacement(s)";
    }

    private async Task LoadDirectFabStatusAsync()
    {
        var status = await _directFabClient.GetStatusAsync().ConfigureAwait(true);
        ApplyDirectFabStatus(status);
    }

    private void ApplyDirectFabStatus(DirectFabStatus status)
    {
        _directFabState = status.State;
        DirectFabStatusSummary = _fabCatalogCachedAt is null
            ? status.Message
            : $"{status.Message} · {_fabCatalogCachedAt:dd/MM/yyyy HH:mm} · " +
              $"{_remoteFabCatalog.Count(asset => !IsQuixelAsset(asset)):N0} élément(s) en cache.";
        DirectFabAccount = string.IsNullOrWhiteSpace(status.DisplayName)
            ? string.Empty
            : $"Compte : {status.DisplayName}";
        FabStatusSummary = status.Message;
        OnPropertyChanged(nameof(IsDirectFabConnected));
        OnPropertyChanged(nameof(IsQuixelBridgeLocalPanelVisible));
        OnPropertyChanged(nameof(EpicConnectionStatus));
        OnPropertyChanged(nameof(EpicConnectionIndicator));
        OnPropertyChanged(nameof(FabConnectionStatus));
        OnPropertyChanged(nameof(FabConnectionIndicator));
        _refreshDirectFabLibraryCommand.NotifyCanExecuteChanged();
        (OpenEmbeddedFabAuthorizationCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    private async Task LoadQuixelStatusAsync()
    {
        ApplyQuixelStatus(await _quixelClient.GetStatusAsync().ConfigureAwait(true));
    }

    private void ApplyQuixelStatus(QuixelStatus status)
    {
        _quixelState = status.State;
        QuixelStatusSummary = _quixelCatalogCachedAt is null
            ? status.Message
            : $"{status.Message} · {_quixelCatalogCachedAt:dd/MM/yyyy HH:mm} · " +
              $"{_remoteFabCatalog.Count(IsQuixelAsset):N0} fiche(s) en cache.";
        QuixelAccount = string.IsNullOrWhiteSpace(status.DisplayName)
            ? string.Empty
            : $"Compte : {status.DisplayName}";
        OnPropertyChanged(nameof(IsQuixelConnected));
        OnPropertyChanged(nameof(QuixelConnectionStatus));
        OnPropertyChanged(nameof(IsQuixelBridgeLocalPanelVisible));
        OnPropertyChanged(nameof(IsSelectedRemoteConnectionReady));
        OnPropertyChanged(nameof(LibraryRemoteStatusSummary));
        OnPropertyChanged(nameof(LibraryRemoteAccount));
        _refreshQuixelLibraryCommand.NotifyCanExecuteChanged();
    }

    private void StartQuixelRefreshSpinner()
    {
        if (_quixelRefreshSpinnerTimer is null)
        {
            _quixelRefreshSpinnerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50),
            };
            _quixelRefreshSpinnerTimer.Tick += (_, _) =>
                QuixelRefreshSpinnerAngle = (QuixelRefreshSpinnerAngle + 18) % 360;
        }
        _quixelRefreshSpinnerTimer.Start();
    }

    private void StartFabRefreshSpinner()
    {
        if (_fabRefreshSpinnerTimer is null)
        {
            _fabRefreshSpinnerTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50),
            };
            _fabRefreshSpinnerTimer.Tick += (_, _) =>
                FabRefreshSpinnerAngle = (FabRefreshSpinnerAngle + 18) % 360;
        }
        _fabRefreshSpinnerTimer.Start();
    }

    private static string FormatCacheDate(DateTimeOffset? value) =>
        value is null ? "indisponible" : value.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    private async Task OpenDirectFabAuthorizationAsync()
    {
        if (IsFabWebLoginActive) return;
        await _platformLauncher.OpenUriAsync(_directFabClient.AuthorizationUri).ConfigureAwait(true);
        DirectFabStatusSummary =
            "Autorisation ouverte. Après connexion, copiez la réponse Epic : CyEngineHub la détectera et terminera automatiquement.";
        StatusMessage = "Autorisation Epic ouverte · en attente de la réponse copiée.";
        AppendRawLog("INFO", "Connections/Epic", "Page d’autorisation Epic ouverte; surveillance temporaire du presse-papiers demandée.");
        FabAuthorizationCaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    public async Task<bool> TryConnectDirectFabFromClipboardAsync(string? clipboardText)
    {
        if (IsDirectFabConnected || IsEpicBusy || IsFabWebLoginActive || !LooksLikeEpicAuthorization(clipboardText))
        {
            return false;
        }

        FabAuthorizationCode = clipboardText!.Trim();
        try
        {
            DirectFabStatusSummary = "Réponse Epic détectée · connexion automatique en cours…";
            await ConnectDirectFabAsync().ConfigureAwait(true);
            return IsDirectFabConnected;
        }
        catch (Exception exception)
        {
            DirectFabStatusSummary = $"Réponse détectée, mais la connexion a échoué : {exception.Message}";
            ShowError(exception);
            return false;
        }
    }

    public async Task<bool> TryConnectAuthorizationFromClipboardAsync(string? clipboardText)
    {
        if (IsQuixelConnectionPageSelected && !IsQuixelConnected && !IsEpicBusy &&
            LooksLikeEpicAuthorization(clipboardText))
        {
            QuixelAuthorizationCode = clipboardText!.Trim();
            try
            {
                QuixelStatusSummary = "Réponse Epic/Quixel détectée · connexion automatique en cours…";
                await ConnectQuixelAsync().ConfigureAwait(true);
                return IsQuixelConnected;
            }
            catch (Exception exception)
            {
                QuixelStatusSummary = $"Réponse détectée, mais la connexion a échoué : {exception.Message}";
                ShowError(exception);
                return false;
            }
        }
        return await TryConnectDirectFabFromClipboardAsync(clipboardText).ConfigureAwait(true);
    }

    public void NotifyFabAuthorizationCaptureTimedOut()
    {
        if (IsQuixelConnectionPageSelected)
        {
            if (!IsQuixelConnected)
            {
                QuixelStatusSummary =
                    "Aucune réponse Epic/Quixel détectée. Relancez l’autorisation ou collez le code manuellement.";
                StatusMessage = "Autorisation Epic/Quixel expirée ou non copiée.";
                AppendRawLog("WARN", "Connections/Quixel", "Délai de capture de la réponse Epic/Quixel dépassé.");
            }
            return;
        }
        if (IsDirectFabConnected)
        {
            return;
        }

        DirectFabStatusSummary =
            "Aucune réponse Epic détectée. Relancez l’autorisation ou utilisez le mode manuel de secours.";
        StatusMessage = "Autorisation Epic expirée ou non copiée.";
        AppendRawLog("WARN", "Connections/Epic", "Délai de capture automatique de la réponse Epic dépassé.");
    }

    private static bool LooksLikeEpicAuthorization(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return uri.Query.Contains("code=", StringComparison.OrdinalIgnoreCase) ||
                   uri.Query.Contains("authorizationCode=", StringComparison.OrdinalIgnoreCase);
        }
        if (trimmed.StartsWith('{'))
        {
            try
            {
                using var document = JsonDocument.Parse(trimmed);
                var hasAuthorizationCode =
                    document.RootElement.TryGetProperty("authorizationCode", out var authorizationCode) &&
                    authorizationCode.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(authorizationCode.GetString());
                var hasCode =
                    document.RootElement.TryGetProperty("code", out var code) &&
                    code.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(code.GetString());
                return hasAuthorizationCode || hasCode;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        return trimmed.Length is >= 16 and <= 4096 &&
               trimmed.All(character => char.IsLetterOrDigit(character) || character is '_' or '-' or '.');
    }

    private async Task ConnectDirectFabAsync()
    {
        IsEpicBusy = true;
        try
        {
            DirectFabStatusSummary = "Connexion sécurisée à Epic/Fab…";
            var status = await _directFabClient.ConnectAsync(FabAuthorizationCode).ConfigureAwait(true);
            FabAuthorizationCode = string.Empty;
            ApplyDirectFabStatus(status);
            AppendRawLog("INFO", "Library/Fab", $"Connexion Fab directe établie pour {status.DisplayName ?? "le compte Epic"}.");
            StatusMessage = "Compte Epic/Fab connecté directement, sans Epic Games Launcher.";
            await RefreshDirectFabLibraryAsync().ConfigureAwait(true);
        }
        finally
        {
            IsEpicBusy = false;
        }
    }

    private async Task OpenQuixelAuthorizationAsync()
    {
        await _platformLauncher.OpenUriAsync(_quixelClient.AuthorizationUri).ConfigureAwait(true);
        QuixelStatusSummary =
            "Autorisation Quixel officielle ouverte. Après connexion, copiez la réponse Epic : CyEngineHub la détectera automatiquement.";
        StatusMessage = "Autorisation Epic/Quixel ouverte · en attente de la réponse copiée.";
        AppendRawLog("INFO", "Connections/Quixel", "Autorisation Epic du client officiel Bridge ouverte.");
        FabAuthorizationCaptureRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task ConnectQuixelAsync()
    {
        IsEpicBusy = true;
        try
        {
            QuixelStatusSummary = "Connexion sécurisée à Epic/Quixel…";
            var status = await _quixelClient.ConnectAsync(QuixelAuthorizationCode).ConfigureAwait(true);
            QuixelAuthorizationCode = string.Empty;
            ApplyQuixelStatus(status);
            AppendRawLog("INFO", "Connections/Quixel", "Session Megascans historique enregistrée dans le coffre Windows.");
            StatusMessage = "Compte Epic/Quixel connecté · bibliothèque historique prête.";
        }
        finally
        {
            IsEpicBusy = false;
        }
    }

    private async Task ConnectQuixelFromEpicAsync()
    {
        IsEpicBusy = true;
        try
        {
            QuixelStatusSummary = "Tentative d’échange de la session Epic vers Quixel…";
            ApplyQuixelStatus(await _quixelClient.ConnectFromEpicSessionAsync().ConfigureAwait(true));
            AppendRawLog("INFO", "Connections/Quixel", "Échange direct de la session Epic vers Quixel réussi.");
            StatusMessage = "Session Epic réutilisée pour Megascans.";
        }
        finally
        {
            IsEpicBusy = false;
        }
    }

    private async Task DisconnectQuixelAsync()
    {
        IsEpicBusy = true;
        try
        {
            await _quixelClient.DisconnectAsync().ConfigureAwait(true);
            ApplyQuixelStatus(new QuixelStatus(
                QuixelConnectionState.Disconnected,
                "Compte Epic/Quixel déconnecté. La session a été supprimée du coffre Windows."));
            RemoveRemoteAssets(IsQuixelAsset);
            if (IsLibraryQuixelSelected) ApplyRemoteFabFilter();
            AppendRawLog("INFO", "Connections/Quixel", "Session Megascans supprimée du coffre Windows.");
            StatusMessage = "Compte Epic/Quixel déconnecté.";
        }
        finally
        {
            IsEpicBusy = false;
        }
    }

    private async Task DisconnectDirectFabAsync()
    {
        IsEpicBusy = true;
        try
        {
            await _directFabClient.DisconnectAsync().ConfigureAwait(true);
            ApplyDirectFabStatus(new DirectFabStatus(
                DirectFabConnectionState.Disconnected,
                "Compte Epic/Fab déconnecté. La session a été supprimée du coffre Windows."));
            RemoveRemoteAssets(IsLauncherFabAsset);
            _visibleRemoteFabCatalog = [];
            ClearPagedRemoteFabRows();
            SelectedRemoteFabAsset = null;
            SelectedRemoteFabAssetCard = null;
            IsRemoteFabProductVisible = false;
            OnPropertyChanged(nameof(HasNoRemoteFabAssets));
            AppendRawLog("INFO", "Library/Fab", "Session Fab supprimée du coffre sécurisé Windows.");
            StatusMessage = "Compte Epic/Fab déconnecté.";
        }
        finally
        {
            IsEpicBusy = false;
        }
    }

    private async Task RefreshDirectFabLibraryAsync()
    {
        if (!IsFabLibrarySourceAvailable) return;
        if (!IsDirectFabConnected)
        {
            throw new InvalidOperationException("Connectez d’abord votre compte Epic/Fab.");
        }
        if (IsFabLibraryRefreshing) return;

        IsFabLibraryRefreshing = true;
        IsEpicBusy = true;
        try
        {
            DirectFabStatusSummary = _remoteFabCatalog.Any(asset => !IsQuixelAsset(asset))
                ? "Actualisation Fab en arrière-plan · le dernier cache reste disponible."
                : "Lecture de votre bibliothèque Fab…";
            IReadOnlyList<RemoteFabAsset> assets = [];
            DateTimeOffset? cachedAt = null;
            string? cacheWarning = null;
            var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
                "Bibliothèque · Fab",
                "Actualiser My Library",
                [new HubTaskLink("library", "Fab", "Voir la bibliothèque Fab")],
                async (context, cancellationToken) =>
                {
                    context.Report(null, "Lecture paginée de la bibliothèque Fab…");
                    assets = await _directFabClient.GetLibraryAsync(cancellationToken).ConfigureAwait(false);
                    cachedAt = DateTimeOffset.UtcNow;
                    context.Report(null, "Enregistrement du cache marketplace Fab…");
                    try
                    {
                        await _marketplaceLibraryCache.SaveAsync(
                                new MarketplaceLibraryCacheSnapshot(
                                    cachedAt.Value,
                                    "fab",
                                    assets,
                                    assets.Count),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException or JsonException)
                    {
                        cacheWarning = exception.Message;
                    }
                    context.Report(1, $"{assets.Count:N0} élément(s) reçu(s)");
                }));

            var completed = await handle.Completion.ConfigureAwait(true);
            if (completed.Status == HubTaskStatus.Canceled)
            {
                DirectFabStatusSummary = "Actualisation de la bibliothèque Fab annulée · le cache est conservé.";
                return;
            }
            if (completed.Status == HubTaskStatus.Failed)
            {
                DirectFabStatusSummary = $"Échec du catalogue Fab : {completed.Error} · le cache est conservé.";
                return;
            }

            ResetRemoteFabPresentationForRefresh();
            ReplaceRemoteAssets(IsLauncherFabAsset, assets);
            // Keep the open product snapshot and its modal selection during refresh.
            await RefreshRemoteFabViewNowAsync().ConfigureAwait(true);
            _fabCatalogCachedAt = cachedAt;
            DirectFabStatusSummary =
                $"{assets.Count:N0} élément(s) dans My Library · cache {FormatCacheDate(cachedAt)}.";
            if (!string.IsNullOrWhiteSpace(cacheWarning))
            {
                DirectFabStatusSummary += $" Attention : cache non enregistré ({cacheWarning}).";
            }
            StatusMessage = "Bibliothèque Fab directe actualisée.";
            AppendRawLog("INFO", "Library/Fab", $"Catalogue Fab actualisé : {assets.Count:N0} élément(s).");
        }
        finally
        {
            IsEpicBusy = false;
            IsFabLibraryRefreshing = false;
        }
    }

    private async Task RefreshQuixelLibraryAsync()
    {
        if (!IsQuixelLibrarySourceAvailable) return;
        if (!IsQuixelConnected)
        {
            throw new InvalidOperationException(
                "Connectez d’abord Epic/Quixel dans Paramètres → Connexions → Megascans.");
        }
        if (IsQuixelLibraryRefreshing)
        {
            return;
        }

        IsQuixelLibraryRefreshing = true;
        IsEpicBusy = true;
        try
        {
            QuixelStatusSummary = _remoteFabCatalog.Any(IsQuixelAsset)
                ? "Actualisation Megascans en arrière-plan · le dernier cache reste disponible."
                : "Lecture des acquisitions historiques Megascans…";
            QuixelLibraryResult result = new(0, [], 0);
            DateTimeOffset? cachedAt = null;
            string? cacheWarning = null;
            var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
                "Bibliothèque · Megascans",
                "Actualiser la bibliothèque historique Quixel",
                [new HubTaskLink("library", "Quixel", "Voir la bibliothèque Megascans")],
                async (context, cancellationToken) =>
                {
                    context.Report(null, "Lecture des acquisitions Quixel puis résolution du catalogue…");
                    result = await _quixelClient.GetLibraryAsync(cancellationToken).ConfigureAwait(false);
                    cachedAt = DateTimeOffset.UtcNow;
                    context.Report(null, "Enregistrement du cache Megascans compressé…");
                    try
                    {
                        await _marketplaceLibraryCache.SaveAsync(
                                new MarketplaceLibraryCacheSnapshot(
                                    cachedAt.Value,
                                    "quixel",
                                    result.Assets,
                                    result.AcquiredCount,
                                    result.UnresolvedCount),
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException or JsonException)
                    {
                        cacheWarning = exception.Message;
                    }
                    context.Report(
                        1,
                        $"{result.Assets.Count:N0} fiche(s) résolue(s) sur {result.AcquiredCount:N0} acquisition(s)");
                }));

            var completed = await handle.Completion.ConfigureAwait(true);
            if (completed.Status == HubTaskStatus.Canceled)
            {
                QuixelStatusSummary = "Actualisation annulée · le dernier cache Megascans est conservé.";
                return;
            }
            if (completed.Status == HubTaskStatus.Failed)
            {
                QuixelStatusSummary = $"Échec de l’actualisation : {completed.Error} · le dernier cache est conservé.";
                return;
            }

            ResetRemoteFabPresentationForRefresh();
            ReplaceRemoteAssets(IsQuixelAsset, result.Assets);
            // Refreshing either marketplace must not orphan the open product dialog.
            await RefreshRemoteFabViewNowAsync().ConfigureAwait(true);
            _quixelCatalogCachedAt = cachedAt;
            QuixelStatusSummary = result.UnresolvedCount == 0
                ? $"{result.AcquiredCount:N0} acquisition(s) Megascans chargée(s) · cache {FormatCacheDate(cachedAt)}."
                : $"{result.Assets.Count:N0} fiche(s) disponible(s) sur {result.AcquiredCount:N0} acquisition(s) · " +
                  $"{result.UnresolvedCount:N0} référence(s) historique(s) absente(s) · cache {FormatCacheDate(cachedAt)}.";
            if (!string.IsNullOrWhiteSpace(cacheWarning))
            {
                QuixelStatusSummary += $" Attention : cache non enregistré ({cacheWarning}).";
            }
            StatusMessage = "Bibliothèque Megascans historique actualisée.";
            AppendRawLog(
                "INFO",
                "Library/Quixel",
                $"{result.Assets.Count:N0}/{result.AcquiredCount:N0} acquisitions résolues; {result.UnresolvedCount:N0} non résolues.");
        }
        finally
        {
            IsEpicBusy = false;
            IsQuixelLibraryRefreshing = false;
        }
    }

    private RemoteFabAssetCardViewModel CreateRemoteAssetCard(RemoteFabAsset asset)
    {
        var preference = FindWorkspacePreference(WorkspaceItemKind.Asset, asset.AssetId);
        var imageCache = IsQuixelAsset(asset)
            ? _quixelRemoteImageCache
            : _fabRemoteImageCache;
        var card = new RemoteFabAssetCardViewModel(
            asset,
            CreateOrganization(
                WorkspaceItemKind.Asset,
                asset.AssetId,
                asset.Title,
                canHide: true),
            OpenRemoteFabProduct,
            imageCache.LoadAsync,
            OpenRemoteFabCardListingAsync,
            DownloadRemoteFabCardAsync,
            OpenRemoteFabInstall,
            OpenFabCollectionEditor,
            preference?.IsHidden == true);
        if (_fabCollections.TryGetValue(asset.AssetId, out var collections)) card.SetCollections(collections);
        card.SetStoredLocally(_libraryLocalIndex.Contains(
            NormalizeAssetSource(asset), asset.AssetId, asset.Title));
        card.UpdateInstalledEngineVersions(_currentEngines
            .Select(engine => $"{engine.Version.Major}.{engine.Version.Minor}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase));
        card.UpdatePresentation(IsLibraryCompactCards);
        return card;
    }

    private void OpenRemoteFabProduct(RemoteFabAssetCardViewModel card)
    {
        var productSource = NormalizeAssetSource(card.Asset);
        _libraryProductAssetIds[productSource] = card.Asset.AssetId;
        var detailCard = CreateRemoteAssetCard(card.Asset);
        detailCard.EnsureThumbnailLoaded();
        SelectedRemoteFabAssetCard?.Dispose();
        SelectedRemoteFabAssetCard = detailCard;
        SelectedRemoteFabAsset = detailCard.Asset;
        _activeDerivedInstallArtifactId = null;
        RestoreSupplierInstallVersions(detailCard);
        SelectedFabInstallProject = Projects.FirstOrDefault();
        SelectedFabInstallEngine = Engines.FirstOrDefault();
        ShowOnlyCompatibleFabTargets = !IsQuixelAsset(detailCard.Asset);
        RebuildSelectedFabStoredDownloads(detailCard.Asset.AssetId);
        RebuildSelectedDerivedWorkspaces(detailCard.Asset.AssetId);
        RebuildSelectedFabVersionDownloads(detailCard.Asset.AssetId, productSource);
        IsFabInstallOptionsVisible = false;
        IsFabDownloadOptionsVisible = false;
        IsRemoteFabProductVisible = true;
        OnPropertyChanged(nameof(IsSelectedRemoteFabAssetPlugin));
        OnPropertyChanged(nameof(IsFabInstallCompatibilityFilterVisible));
        OnPropertyChanged(nameof(HasSelectedFabStoredDownloads));
        OnPropertyChanged(nameof(SelectedFabAssetVisibilityLabel));
        OnPropertyChanged(nameof(HasActiveDerivedInstall));
        StatusMessage = detailCard.CanDownload
            ? $"{detailCard.Name} · {detailCard.Asset.ProjectVersions.Count:N0} variante(s) téléchargeable(s)."
            : $"{detailCard.Name} · fiche produit ouverte, aucun téléchargement exposé.";
        if (IsMarketplaceAvailable(productSource)) _ = LoadRemoteFabProductDetailsAsync(detailCard);
    }

    private void RestoreSupplierInstallVersions(RemoteFabAssetCardViewModel detailCard)
    {
        FabInstallVersions.Clear();
        var preferred = detailCard.Asset.PreferredProjectVersion;
        foreach (var version in detailCard.Asset.ProjectVersions
                     .OrderByDescending(item => ParseEngineVersion(
                         item.EngineVersions.Count == 0 ? string.Empty : item.EngineVersions[0])))
        {
            FabInstallVersions.Add(new FabInstallVersionOptionViewModel(version, Equals(version, preferred)));
        }
        RebuildFabInstallTargets(detailCard);
    }

    private async Task OpenRemoteFabCardListingAsync(RemoteFabAssetCardViewModel card)
    {
        if (IsFabWebAsset(card.Asset)) { await OpenFabWebAssetAsync(card.Asset).ConfigureAwait(true); return; }
        var uri = card.ListingUri
            ?? throw new InvalidOperationException("Aucune page produit Fab n’est disponible.");
        await _platformLauncher.OpenUriAsync(uri).ConfigureAwait(true);
        StatusMessage = IsQuixelAsset(card.Asset)
            ? $"Page Megascans ouverte pour {card.Name}."
            : $"Page Fab officielle ouverte pour {card.Name}.";
    }

    private Task DownloadRemoteFabCardAsync(RemoteFabAssetCardViewModel card)
    {
        if (IsFabWebAsset(card.Asset)) return OpenFabWebAssetAsync(card.Asset);
        OpenRemoteFabProduct(card);
        IsFabDownloadOptionsVisible = true;
        return Task.CompletedTask;
    }

    private void OpenRemoteFabInstall(RemoteFabAssetCardViewModel card)
    {
        OpenRemoteFabProduct(card);
        IsFabDownloadOptionsVisible = false;
        IsFabInstallOptionsVisible = true;
    }

    private void RebuildFabInstallTargets(
        RemoteFabAssetCardViewModel card,
        LibraryPackageContentKind? contentKindOverride = null,
        IReadOnlyList<RemoteFabProjectVersion>? versionsOverride = null)
    {
        FabInstallTargets.Clear();
        var contentKind = contentKindOverride ?? card.ContentKind;
        var sourceVersions = versionsOverride ?? card.Asset.ProjectVersions;
        var ignoresEngineCompatibility = IsQuixelAsset(card.Asset) && versionsOverride is null;
        var supported = sourceVersions
            .SelectMany(item => item.EngineVersions)
            .Select(NormalizeEngineVersion)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Compatible(string value) =>
            ignoresEngineCompatibility ||
            supported.Count == 0 ||
            supported.Contains(NormalizeEngineVersion(value));

        var acceptsProjects = contentKind is
            LibraryPackageContentKind.ContentPack or
            LibraryPackageContentKind.CodePlugin or
            LibraryPackageContentKind.EnginePackage or
            LibraryPackageContentKind.RawAsset or
            LibraryPackageContentKind.Unknown;
        var acceptsEngines = contentKind is
            LibraryPackageContentKind.CodePlugin or
            LibraryPackageContentKind.EnginePackage;

        if (acceptsProjects)
        {
            foreach (var project in Projects.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                var engine = ResolveEngine(project.Project.EngineAssociation);
                var engineVersion = engine is null
                    ? project.Project.EngineAssociation ?? "Inconnu"
                    : engine.Version.ToString();
                FabInstallTargets.Add(CreateFabInstallTargetOption(
                    $"project:{project.Project.Id}",
                    project.Name,
                    "Projet Unreal",
                    project.Project.RootPath,
                    engineVersion,
                    Compatible(engineVersion),
                    ignoresEngineCompatibility,
                    contentKind,
                    project,
                    null));
            }
        }
        if (acceptsEngines)
        {
            foreach (var engine in Engines.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                FabInstallTargets.Add(CreateFabInstallTargetOption(
                    $"engine:{engine.Engine.Id}",
                    engine.DisplayName,
                    "Moteur Unreal",
                    engine.Engine.RootPath,
                    engine.Engine.Version.ToString(),
                    Compatible(engine.Engine.Version.ToString()),
                    ignoresEngineCompatibility,
                    contentKind,
                    null,
                    engine));
            }
        }
        ApplyFabInstallTargetFilter();
        RebuildCompatibleFabInstallVersions();
    }

    private FabInstallTargetOptionViewModel CreateFabInstallTargetOption(
        string id,
        string name,
        string kind,
        string path,
        string engineVersion,
        bool isCompatible,
        bool ignoresEngineCompatibility,
        LibraryPackageContentKind contentKind,
        ProjectCardViewModel? project,
        EngineCardViewModel? engine)
    {
        var targetKind = project is not null
            ? FabInstallationTargetKind.Project
            : FabInstallationTargetKind.Engine;
        var scripts = _fabInstallationScripts.GetCompatible(contentKind, targetKind);
        var preference = _fabInstallationScriptPreferences.LastOrDefault(item =>
            string.Equals(item.TargetId, id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ContentKind, contentKind.ToString(), StringComparison.OrdinalIgnoreCase));
        var selectedScriptId = preference?.ScriptId;
        if (project is not null &&
            contentKind is LibraryPackageContentKind.CodePlugin or LibraryPackageContentKind.EnginePackage &&
            (string.IsNullOrWhiteSpace(selectedScriptId) || selectedScriptId == UnrealPluginInstallationScript.ScriptId))
        {
            selectedScriptId = UnrealProjectPluginInstallationScript.ScriptId;
        }
        if (string.IsNullOrWhiteSpace(selectedScriptId) &&
            ignoresEngineCompatibility &&
            project is not null &&
            scripts.Any(script => string.Equals(
                script.Descriptor.Id,
                UnrealMegascansInstallationScript.ScriptId,
                StringComparison.OrdinalIgnoreCase)))
        {
            selectedScriptId = UnrealMegascansInstallationScript.ScriptId;
        }
        if (string.IsNullOrWhiteSpace(selectedScriptId) &&
            !isCompatible &&
            project is not null &&
            scripts.Any(script => string.Equals(
                script.Descriptor.Id,
                UnrealVersionBridgeInstallationScript.ScriptId,
                StringComparison.OrdinalIgnoreCase)))
        {
            selectedScriptId = UnrealVersionBridgeInstallationScript.ScriptId;
        }
        return new FabInstallTargetOptionViewModel(
            id,
            name,
            kind,
            path,
            engineVersion,
            isCompatible,
            ignoresEngineCompatibility,
            contentKind,
            scripts,
            selectedScriptId,
            preference?.Configuration,
            project,
            engine,
            OnFabInstallTargetSelectionChanged,
            OnFabInstallTargetScriptSettingsChanged,
            OpenFabInstallationScriptConfiguration);
    }

    private void OpenFabInstallationScriptConfiguration(FabInstallTargetOptionViewModel target)
    {
        SelectedFabInstallationScriptTarget = target;
        IsFabInstallationScriptConfigurationVisible = true;
    }

    private async Task RefreshFabInstallationScriptsAsync()
    {
        var externalCount = await _fabInstallationScripts.RefreshExternalAsync(
                _installationScriptsDirectory)
            .ConfigureAwait(true);
        AppendRawLog(
            "INFO",
            "Library/InstallationScripts",
            $"{_fabInstallationScripts.Scripts.Count:N0} script(s) disponible(s), dont {externalCount:N0} externe(s).");
        StatusMessage =
            $"{_fabInstallationScripts.Scripts.Count:N0} script(s) d’installation chargé(s).";
        if (SelectedRemoteFabAssetCard is not null)
        {
            RebuildFabInstallTargets(SelectedRemoteFabAssetCard);
        }
    }

    private void OnFabInstallTargetScriptSettingsChanged(FabInstallTargetOptionViewModel target)
    {
        var scriptId = target.SelectedInstallationScript?.Id;
        if (string.IsNullOrWhiteSpace(scriptId)) return;
        var updated = new FabInstallationScriptPreference(
            target.Id,
            target.ContentKind.ToString(),
            scriptId,
            target.GetInstallationConfiguration());
        _fabInstallationScriptPreferences = _fabInstallationScriptPreferences
            .Where(item => !(
                string.Equals(item.TargetId, target.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    item.ContentKind,
                    target.ContentKind.ToString(),
                    StringComparison.OrdinalIgnoreCase)))
            .Append(updated)
            .ToArray();

        var saveVersion = Interlocked.Increment(ref _fabInstallationPreferenceSaveVersion);
        _ = SaveFabInstallationScriptPreferencesAsync(
            _fabInstallationScriptPreferences,
            saveVersion);
        ApplyFabInstallTargetFilter();
        RebuildCompatibleFabInstallVersions();
    }

    private async Task SaveFabInstallationScriptPreferencesAsync(
        IReadOnlyList<FabInstallationScriptPreference> preferences,
        int saveVersion)
    {
        try
        {
            await Task.Delay(250).ConfigureAwait(false);
            if (saveVersion != Volatile.Read(ref _fabInstallationPreferenceSaveVersion)) return;
            await _fabInstallationScriptPreferenceStore.SaveAsync(preferences)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            Dispatcher.UIThread.Post(() => ShowError(exception));
        }
    }

    private void OnFabInstallTargetSelectionChanged()
    {
        RebuildCompatibleFabInstallVersions();
    }

    private void RebuildCompatibleFabInstallVersions()
    {
        var selectedTargets = FabInstallTargets.Where(item => item.IsSelected).ToArray();
        if (selectedTargets.Length == 0)
        {
            CompatibleFabInstallVersions.ReplaceWith([]);
            NotifyFabInstallVersionFilterChanged();
            return;
        }

        var compatible = FabInstallVersions
            .Where(option => selectedTargets.Any(target =>
                CanInstallFabVersionIntoTarget(option.Version, target)))
            .ToArray();
        var compatibleSet = compatible.ToHashSet();
        foreach (var option in FabInstallVersions.Where(option => !compatibleSet.Contains(option)))
        {
            option.IsSelected = false;
        }

        foreach (var target in selectedTargets)
        {
            var candidates = compatible
                .Where(option => CanInstallFabVersionIntoTarget(option.Version, target))
                .ToArray();
            if (candidates.Length > 0 && !candidates.Any(option => option.IsSelected))
            {
                candidates[0].IsSelected = true;
            }
        }

        CompatibleFabInstallVersions.ReplaceWith(compatible);
        NotifyFabInstallVersionFilterChanged();
    }

    private void NotifyFabInstallVersionFilterChanged()
    {
        OnPropertyChanged(nameof(FabInstallVersionResultsLabel));
        OnPropertyChanged(nameof(HasSelectedFabInstallTargets));
        OnPropertyChanged(nameof(HasCompatibleFabInstallVersions));
        OnPropertyChanged(nameof(HasNoCompatibleFabInstallVersions));
    }

    private static bool IsFabVersionCompatibleWithTarget(
        RemoteFabProjectVersion version,
        FabInstallTargetOptionViewModel target)
    {
        if (!SupportsCurrentPlatform(version))
        {
            return false;
        }
        if (version.EngineVersions.Count == 0)
        {
            return true;
        }

        var targetVersion = NormalizeEngineVersion(target.EngineVersion);
        return targetVersion.Length > 0 && version.EngineVersions
            .Select(NormalizeEngineVersion)
            .Any(candidate => string.Equals(candidate, targetVersion, StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanInstallFabVersionIntoTarget(
        RemoteFabProjectVersion version,
        FabInstallTargetOptionViewModel target) =>
        (target.IgnoresEngineCompatibility && SupportsCurrentPlatform(version)) ||
        IsFabVersionCompatibleWithTarget(version, target) ||
        (target.IsUsingVersionBridge && SupportsCurrentPlatform(version));

    private static bool SupportsCurrentPlatform(RemoteFabProjectVersion version)
    {
        var declaredPlatforms = version.Platforms
            .Where(platform =>
                platform.Contains("Windows", StringComparison.OrdinalIgnoreCase) ||
                platform.Contains("Mac", StringComparison.OrdinalIgnoreCase) ||
                platform.Contains("Linux", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (declaredPlatforms.Length == 0)
        {
            return true;
        }

        var currentPlatform = OperatingSystem.IsWindows()
            ? "Windows"
            : OperatingSystem.IsMacOS()
                ? "Mac"
                : "Linux";
        return declaredPlatforms.Any(platform =>
            platform.Contains(currentPlatform, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyFabInstallTargetFilter()
    {
        var query = FabInstallTargetSearchQuery.Trim();
        var filtered = new List<FabInstallTargetOptionViewModel>();
        foreach (var target in FabInstallTargets)
        {
            target.IsVisible = (!ShowOnlyCompatibleFabTargets ||
                                target.IsCompatible ||
                                target.CanUseVersionBridge) &&
                               (!target.IsProject || ShowFabProjectInstallTargets) &&
                               (!target.IsEngine || ShowFabEngineInstallTargets) &&
                               (query.Length == 0 ||
                                target.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                target.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                target.EngineVersion.Contains(query, StringComparison.OrdinalIgnoreCase));
            if (target.IsVisible)
            {
                filtered.Add(target);
            }
        }
        FilteredFabInstallTargets.ReplaceWith(filtered
            .OrderBy(target => target.IsProject ? 0 : 1)
            .ThenBy(target => target.Name, StringComparer.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(FabInstallTargetResultsLabel));
    }

    private void OpenFabCollectionEditor(RemoteFabAssetCardViewModel card)
    {
        OpenCollectionEditor(
            card.Asset.AssetId,
            card.Name,
            card.PersonalCollections,
            card.SetCollections);
    }

    private void OpenLocalPackCollectionEditor(FabLocalContentPackCardViewModel card)
    {
        OpenCollectionEditor(
            card.Pack.Id,
            card.Name,
            card.PersonalCollections,
            card.SetCollections);
    }

    private void OpenCollectionEditor(
        string itemId,
        string itemName,
        IReadOnlyList<string> currentCollections,
        Action<string[]> apply)
    {
        _collectionEditorItemId = itemId;
        _collectionEditorItemName = itemName;
        _collectionEditorApply = apply;
        FabCollectionSearchQuery = string.Empty;
        NewFabCollectionName = string.Empty;
        var names = _fabCollections.Values.SelectMany(value => value)
            .Concat(["Favoris", "À tester", "Environnements"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);
        FabCollectionOptions.Clear();
        foreach (var name in names)
        {
            FabCollectionOptions.Add(new FabCollectionOptionViewModel(
                name,
                currentCollections.Contains(name, StringComparer.OrdinalIgnoreCase)));
        }
        IsFabCollectionEditorVisible = true;
    }

    private void ApplyFabCollectionFilter()
    {
        var query = FabCollectionSearchQuery.Trim();
        foreach (var option in FabCollectionOptions)
        {
            option.IsVisible = query.Length == 0 || option.Name.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void AddFabCollectionFromEditor()
    {
        var name = NewFabCollectionName.Trim();
        if (name.Length is 0 or > 80 || name.Any(char.IsControl)) return;
        var existing = FabCollectionOptions.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            FabCollectionOptions.Add(new FabCollectionOptionViewModel(name, true));
        }
        else
        {
            existing.IsSelected = true;
        }
        NewFabCollectionName = string.Empty;
        ApplyFabCollectionFilter();
    }

    private async Task SaveFabCollectionEditorAsync()
    {
        var itemId = _collectionEditorItemId
            ?? throw new InvalidOperationException("Aucun asset n’est sélectionné.");
        var selected = FabCollectionOptions.Where(item => item.IsSelected)
            .Select(item => item.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (selected.Length == 0) _fabCollections.Remove(itemId);
        else _fabCollections[itemId] = selected;
        await _settings.SetAsync(FabCollectionsSetting, JsonSerializer.Serialize(_fabCollections))
            .ConfigureAwait(true);
        _collectionEditorApply?.Invoke(selected);
        IsFabCollectionEditorVisible = false;
        RebuildProjectCreationCollections();
        await RefreshRemoteFabViewNowAsync().ConfigureAwait(true);
        StatusMessage = selected.Length == 0
            ? $"{_collectionEditorItemName} retiré des collections personnelles."
            : $"{_collectionEditorItemName} ajouté à {selected.Length:N0} collection(s).";
    }

    private async Task ToggleSelectedFabAssetHiddenAsync()
    {
        var card = SelectedRemoteFabAssetCard
            ?? throw new InvalidOperationException("Aucun asset n’est sélectionné.");
        var hidden = !card.IsHidden;
        var existing = FindWorkspacePreference(WorkspaceItemKind.Asset, card.Asset.AssetId);
        var updated = new WorkspaceItemPreference(
            WorkspaceItemKind.Asset,
            card.Asset.AssetId,
            existing?.Alias ?? string.Empty,
            existing?.FolderPath ?? string.Empty,
            hidden,
            existing?.IsExplicit ?? false);
        _workspacePreferences = _workspacePreferences with
        {
            Items = _workspacePreferences.Items
                .Where(item => item.Kind != WorkspaceItemKind.Asset || !SameWorkspaceKey(item.Key, card.Asset.AssetId))
                .Append(updated)
                .ToArray(),
        };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
        card.SetHidden(hidden);
        OnPropertyChanged(nameof(SelectedFabAssetVisibilityLabel));
        ApplyRemoteFabFilter();
        StatusMessage = hidden
            ? $"{card.Name} est maintenant masqué de la Bibliothèque."
            : $"{card.Name} est de nouveau visible dans la Bibliothèque.";
    }

    private void RebuildSelectedFabStoredDownloads(string assetId)
    {
        SelectedFabStoredDownloads.Clear();
        var source = SelectedRemoteFabAsset is null ? null : NormalizeAssetSource(SelectedRemoteFabAsset);
        var title = SelectedRemoteFabAsset?.Title;
        foreach (var stored in _fabStoredDownloads
                     .Where(item => IsStoredDownloadForAsset(item, assetId, title, source))
                     .OrderByDescending(item => item.DownloadedAt))
        {
            var sizes = ResolveStoredDownloadSizes(stored);
            SelectedFabStoredDownloads.Add(new FabStoredDownloadViewModel(
                stored.AssetId,
                stored.Version,
                stored.ArtifactId,
                stored.Path,
                stored.Bytes,
                stored.DownloadedAt,
                stored.Source,
                stored.PackageKind,
                stored.StorageMode,
                stored.ManifestPath,
                sizes.UncompressedBytes,
                sizes.StoredBytes,
                OpenFolderAsync,
                DeleteStoredFabDownloadAsync,
                ShowError,
                stored.DerivationName,
                stored.DerivationNotes,
                stored.SourceArtifactId,
                OpenDerivedForInstallationAsync,
                OpenDerivedWorkspaceCreator,
                OpenPackTestCreator,
                stored.EngineVersions));
        }
        OnPropertyChanged(nameof(HasSelectedFabStoredDownloads));
        if (ConfirmDeleteAllFabDownloadsCommand is AsyncRelayCommand command)
        {
            command.NotifyCanExecuteChanged();
        }
    }

    private static (long? UncompressedBytes, long? StoredBytes) ResolveStoredDownloadSizes(
        FabStoredDownloadRecord stored)
    {
        var uncompressedBytes = stored.UncompressedBytes;
        var storedBytes = stored.StoredBytes ?? stored.Bytes;
        var manifestPath = stored.ManifestPath;
        if (string.IsNullOrWhiteSpace(manifestPath))
        {
            var candidate = Path.Combine(stored.Path, LibraryPackageService.ManifestFileName);
            if (File.Exists(candidate)) manifestPath = candidate;
        }

        if ((!uncompressedBytes.HasValue || !stored.StoredBytes.HasValue) &&
            !string.IsNullOrWhiteSpace(manifestPath) &&
            File.Exists(manifestPath))
        {
            try
            {
                using var input = File.OpenRead(manifestPath);
                using var document = JsonDocument.Parse(input);
                var root = document.RootElement;
                if (!uncompressedBytes.HasValue &&
                    root.TryGetProperty("uncompressedBytes", out var rawProperty) &&
                    rawProperty.TryGetInt64(out var raw))
                {
                    uncompressedBytes = raw;
                }
                if (!stored.StoredBytes.HasValue &&
                    root.TryGetProperty("storedBytes", out var storedProperty) &&
                    storedProperty.TryGetInt64(out var compressed))
                {
                    storedBytes = compressed;
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Les anciens téléchargements restent visibles même si leur sidecar est illisible.
            }
        }

        if (!uncompressedBytes.HasValue &&
            string.Equals(stored.StorageMode, "Uncompressed", StringComparison.OrdinalIgnoreCase))
        {
            uncompressedBytes = stored.Bytes;
        }
        return (uncompressedBytes, storedBytes);
    }

    private void RebuildSelectedFabVersionDownloads(string assetId, string source)
    {
        SelectedFabVersionDownloads.Clear();
        foreach (var download in _fabVersionDownloads.Where(item =>
                     string.Equals(item.AssetId, assetId, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedFabVersionDownloads.Add(download);
        }
        OnPropertyChanged(nameof(HasSelectedFabVersionDownloads));
    }

    private void AddFabVersionDownload(FabVersionDownloadViewModel download)
    {
        _fabVersionDownloads.Add(download);
        if (SelectedRemoteFabAsset is not null &&
            string.Equals(SelectedRemoteFabAsset.AssetId, download.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeAssetSource(SelectedRemoteFabAsset), download.Source, StringComparison.OrdinalIgnoreCase))
        {
            SelectedFabVersionDownloads.Add(download);
            OnPropertyChanged(nameof(HasSelectedFabVersionDownloads));
        }
    }

    private void RemoveFabVersionDownload(FabVersionDownloadViewModel download)
    {
        _fabVersionDownloads.Remove(download);
        SelectedFabVersionDownloads.Remove(download);
        OnPropertyChanged(nameof(HasSelectedFabVersionDownloads));
    }

    private async Task DeleteStoredFabDownloadAsync(FabStoredDownloadViewModel download)
    {
        ArgumentNullException.ThrowIfNull(download);
        DeleteOwnedFabDownloadDirectory(download.Path, download.Source);
        _fabStoredDownloads.RemoveAll(item =>
            string.Equals(item.AssetId, download.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ArtifactId, download.ArtifactId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Path, download.Path, StringComparison.OrdinalIgnoreCase));
        await PersistStoredFabDownloadsAsync().ConfigureAwait(true);
        UpdateStoredFabAssetState(download.AssetId);
        RebuildSelectedFabStoredDownloads(download.AssetId);
        StatusMessage = $"{download.Version} supprimée du stockage local.";
        AppendRawLog("INFO", "Library/Fab", $"Version locale supprimée : {download.Version} · {download.Path}");
    }

    private async Task DeleteAllSelectedFabDownloadsAsync()
    {
        var asset = SelectedRemoteFabAsset
            ?? throw new InvalidOperationException("Aucun asset n’est sélectionné.");
        var downloads = SelectedFabStoredDownloads.ToArray();
        foreach (var download in downloads)
        {
            DeleteOwnedFabDownloadDirectory(download.Path, download.Source);
        }
        _fabStoredDownloads.RemoveAll(item =>
            IsStoredDownloadForAsset(
                item,
                asset.AssetId,
                asset.Title,
                NormalizeAssetSource(asset)));
        await PersistStoredFabDownloadsAsync().ConfigureAwait(true);
        UpdateStoredFabAssetState(asset.AssetId);
        RebuildSelectedFabStoredDownloads(asset.AssetId);
        IsDeleteAllFabDownloadsConfirmationVisible = false;
        StatusMessage = $"Toutes les versions locales de {asset.Title} ont été supprimées.";
        AppendRawLog("INFO", "Library/Fab", $"{downloads.Length:N0} version(s) locale(s) supprimée(s) pour {asset.Title}.");
    }

    private async Task PersistStoredFabDownloadsAsync()
    {
        await _storedDownloadsPersistenceGate.WaitAsync().ConfigureAwait(true);
        try
        {
            InvalidateLibraryLocalIndex();
            await _settings.SetAsync(
                    FabStoredDownloadsSetting,
                    JsonSerializer.Serialize(_fabStoredDownloads.TakeLast(10_000)))
                .ConfigureAwait(true);
        }
        finally { _storedDownloadsPersistenceGate.Release(); }
    }

    private void UpdateStoredFabAssetState(string assetId)
    {
        InvalidateLibraryLocalIndex();
    }

    private void InvalidateLibraryLocalIndex()
    {
        _libraryLocalIndexCreatedAt = DateTimeOffset.MinValue;
        QueueRemoteFabViewRefresh();
    }

    private void DeleteOwnedFabDownloadDirectory(string value, string source)
    {
        var managedFolder = string.Equals(source, "Quixel", StringComparison.OrdinalIgnoreCase)
            ? "Megascans"
            : "Fab";
        var versionBridgeRoot = Path.GetFullPath(UnrealVersionBridgeStorageRoot.Trim().Trim((char)34));
        var target = Path.GetFullPath(value);
        var configuredLocations = string.Equals(source, "Quixel", StringComparison.OrdinalIgnoreCase)
            ? QuixelLibraryLocations
            : FabLibraryLocations;
        var managedRoots = configuredLocations
            .Where(location => !string.IsNullOrWhiteSpace(location.Path))
            .Select(location => Path.GetFullPath(Path.Combine(
                location.Path.Trim().Trim((char)34),
                managedFolder)))
            .Append(Path.GetFullPath(Path.Combine(LibraryDownloadRoot, managedFolder)))
            .Append(versionBridgeRoot)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var ownerRoot = managedRoots
            .FirstOrDefault(candidate => IsOwnedChildPath(candidate, target));
        if (ownerRoot is null)
        {
            throw new InvalidOperationException(
                "Suppression refusée : le dossier n’appartient à aucun stockage géré par CyEngineHub.");
        }
        var relative = Path.GetRelativePath(ownerRoot, target);
        if (!Directory.Exists(target)) return;

        var current = ownerRoot;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current)) break;
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    "Suppression refusée : le chemin contient un lien ou point de jonction.");
            }
        }
        Directory.Delete(target, recursive: true);
    }

    private static bool IsOwnedChildPath(string root, string target)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(target));
        return relative.Length > 0 && relative != "." && !Path.IsPathRooted(relative) &&
               !relative.Equals("..", StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private async Task SaveStoredFabDownloadAsync(
        RemoteFabAsset asset,
        FabInstallVersionOptionViewModel option,
        DirectFabDownloadResult result)
    {
        _fabStoredDownloads.RemoveAll(item =>
            string.Equals(item.AssetId, asset.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ArtifactId, option.Version.ArtifactId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.Path, result.DestinationDirectory, StringComparison.OrdinalIgnoreCase));
        _fabStoredDownloads.Add(new FabStoredDownloadRecord(
            asset.AssetId,
            option.Label,
            option.Version.ArtifactId,
            result.DestinationDirectory,
            result.DownloadedBytes,
            result.CompletedAt,
            NormalizeAssetSource(asset),
            result.PackageKind,
            result.StorageMode,
            result.ManifestPath,
            result.UncompressedBytes,
            result.StoredBytes,
            asset.Title));
        await PersistStoredFabDownloadsAsync().ConfigureAwait(true);
        foreach (var card in GetRealizedRemoteFabCards().Where(card =>
                     string.Equals(card.Asset.AssetId, asset.AssetId, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(NormalizeAssetSource(card.Asset), NormalizeAssetSource(asset), StringComparison.OrdinalIgnoreCase)))
        {
            card.SetStoredLocally(true);
        }
        RebuildSelectedFabStoredDownloads(asset.AssetId);
    }

    private async Task LoadRemoteFabProductDetailsAsync(RemoteFabAssetCardViewModel card)
    {
        if (IsFabWebAsset(card.Asset))
        {
            card.EnsureGalleryLoaded();
            StatusMessage = "Fiche Fab acquise · formats et droits du compte mis en cache. Téléchargement dans la page officielle intégrée.";
            return;
        }
        IsRemoteFabProductLoading = true;
        try
        {
            var details = IsQuixelAsset(card.Asset)
                ? await _quixelClient.GetProductDetailsAsync(card.Asset).ConfigureAwait(true)
                : await _directFabClient.GetProductDetailsAsync(card.Asset).ConfigureAwait(true);
            if (!ReferenceEquals(SelectedRemoteFabAssetCard, card))
            {
                return;
            }
            card.ApplyProductDetails(details);
            card.EnsureGalleryLoaded();
            StatusMessage = $"{card.Name} · description complète et {details.ImageUris.Count:N0} média(s) chargés.";
        }
        catch (Exception exception) when (
            exception is DirectFabException or QuixelException or HttpRequestException)
        {
            AppendRawLog("WARN", "Library/Fab", $"Détail produit indisponible pour {card.Name} : {exception.Message}");
            card.EnsureGalleryLoaded();
            StatusMessage = $"{card.Name} · résumé du catalogue affiché; détail web momentanément indisponible.";
        }
        finally
        {
            IsRemoteFabProductLoading = false;
        }
    }

    private void SelectLibrarySource(string source)
    {
        if (!IsMarketplaceAvailable(source)) return;
        if (string.Equals(LibrarySource, source, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LibrarySource = source;
        CloseRemoteFabProduct(forgetSelection: false);
        if (source is not ("Fab" or "Quixel" or "FabWeb") ||
            !_libraryProductAssetIds.TryGetValue(source, out var assetId))
        {
            return;
        }

        var asset = _remoteFabCatalog.FirstOrDefault(item =>
            string.Equals(item.AssetId, assetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeAssetSource(item), source, StringComparison.OrdinalIgnoreCase));
        if (asset is not null)
        {
            var temporaryCard = CreateRemoteAssetCard(asset);
            try
            {
                OpenRemoteFabProduct(temporaryCard);
            }
            finally
            {
                temporaryCard.Dispose();
            }
        }
    }

    private void CloseRemoteFabProduct() => CloseRemoteFabProduct(forgetSelection: true);

    private void CloseRemoteFabProduct(bool forgetSelection)
    {
        if (forgetSelection && LibrarySource is "Fab" or "Quixel" or "FabWeb")
        {
            _libraryProductAssetIds.Remove(LibrarySource);
        }
        IsFabImagePreviewVisible = false;
        IsFabInstallOptionsVisible = false;
        IsFabDownloadOptionsVisible = false;
        IsRemoteFabProductVisible = false;
        SelectedRemoteFabAssetCard?.Dispose();
        SelectedRemoteFabAssetCard = null;
        SelectedRemoteFabAsset = null;
        SelectedFabVersionDownloads.Clear();
        OnPropertyChanged(nameof(HasSelectedFabVersionDownloads));
        StatusMessage = $"Retour au catalogue {LibrarySource}.";
    }

    private async Task OpenSelectedRemoteFabListingAsync()
    {
        var uri = SelectedRemoteFabAssetCard?.ListingUri
            ?? throw new InvalidOperationException("Aucune page produit Fab n’est disponible.");
        await _platformLauncher.OpenUriAsync(uri).ConfigureAwait(true);
        StatusMessage = IsQuixelAsset(SelectedRemoteFabAssetCard.Asset)
            ? $"Page Megascans ouverte pour {SelectedRemoteFabAssetCard.Name}."
            : $"Page Fab officielle ouverte pour {SelectedRemoteFabAssetCard.Name}.";
    }

    private void ApplyRemoteFabFilter()
    {
        QueueRemoteFabViewRefresh();
    }

    private void QueueRemoteFabViewRefresh()
    {
        var previous = _remoteLibraryViewCancellation;
        _remoteLibraryViewCancellation = null;
        previous?.Cancel();
        previous?.Dispose();
        var current = new CancellationTokenSource();
        _remoteLibraryViewCancellation = current;
        _ = RefreshRemoteFabViewAsync(current, debounce: true);
    }

    private async Task RefreshRemoteFabViewNowAsync()
    {
        var previous = _remoteLibraryViewCancellation;
        _remoteLibraryViewCancellation = null;
        previous?.Cancel();
        previous?.Dispose();
        var current = new CancellationTokenSource();
        _remoteLibraryViewCancellation = current;
        await RefreshRemoteFabViewAsync(current, debounce: false).ConfigureAwait(true);
    }

    private async Task RefreshRemoteFabViewAsync(
        CancellationTokenSource cancellation,
        bool debounce)
    {
        var token = cancellation.Token;
        try
        {
            if (debounce)
            {
                await Task.Delay(60, token).ConfigureAwait(true);
            }
            else
            {
                await Task.Yield();
            }

            var sourceAssets = GetLibrarySourceAssets().ToArray();
            _librarySourceAssetCount = sourceAssets.Length;
            OnPropertyChanged(nameof(QuixelCollectionScopeSummary));
            var preferences = _workspacePreferences.Items
                .Where(item => item.Kind == WorkspaceItemKind.Asset)
                .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
            var personalCollections = _fabCollections.ToDictionary(
                item => item.Key,
                item => item.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase);
            var selectedEngines = IsLibraryCompatibilityFilterVisible ? SelectedFacetValues(LibraryEngineFacets) : null;
            var selectedAssetTypes = SelectedFacetValues(LibraryAssetTypeFacets);
            var selectedCategories = SelectedFacetValues(LibraryCategoryFacets);
            var selectedCollections = SelectedFacetValues(LibraryCollectionFacets);
            var query = LibrarySearchQuery.Trim();
            var sort = SelectedLibrarySort;
            var showHidden = ShowHiddenFabAssets;
            var showDownloaded = ShowDownloadedLibraryAssets;
            var showUndownloaded = ShowUndownloadedLibraryAssets;
            var rebuildLocalIndex = DateTimeOffset.UtcNow - _libraryLocalIndexCreatedAt > TimeSpan.FromSeconds(30);
            var localIndex = _libraryLocalIndex;
            // Snapshot on the UI thread; all path validation happens in the worker below.
            var localDownloads = rebuildLocalIndex
                ? _fabStoredDownloads.Select(item => new MarketplaceLocalAsset(item.Source, item.AssetId, item.AssetTitle, item.Path))
                    .Concat(FabLocalContentPacks.Where(item => item.Pack.Kind == FabLocalContentKind.EpicVault &&
                            item.Pack.Health == FabLocalContentPackHealth.Ready)
                        .Select(item => new MarketplaceLocalAsset("Fab", string.Empty, item.Name, item.Path)))
                    .Concat(QuixelLocalAssets.Select(item => new MarketplaceLocalAsset("Quixel", item.AssetId, item.Name, item.Path)))
                    .ToArray()
                : [];

            var result = await Task.Run(() =>
            {
                if (rebuildLocalIndex) localIndex = MarketplaceLocalAssetIndex.Build(localDownloads, token);
                return ComputeRemoteLibraryView(
                    sourceAssets,
                    preferences,
                    personalCollections,
                    query,
                    sort,
                    showHidden,
                    showDownloaded,
                    showUndownloaded,
                    localIndex,
                    selectedEngines,
                    selectedAssetTypes,
                    selectedCategories,
                    selectedCollections,
                    token);
            }, token)
                .ConfigureAwait(true);
            token.ThrowIfCancellationRequested();
            _libraryLocalIndex = localIndex;
            if (rebuildLocalIndex) _libraryLocalIndexCreatedAt = DateTimeOffset.UtcNow;

            ReplaceLibraryFacets(LibraryEngineFacets, result.EngineFacets);
            ReplaceLibraryFacets(LibraryAssetTypeFacets, result.AssetTypeFacets);
            ReplaceLibraryFacets(LibraryCategoryFacets, result.CategoryFacets);
            ReplaceLibraryFacets(LibraryCollectionFacets, result.CollectionFacets);
            UpdateFacetCounts(LibraryEngineFacets, result.EngineCounts);
            UpdateFacetCounts(LibraryAssetTypeFacets, result.AssetTypeCounts);
            UpdateFacetCounts(LibraryCategoryFacets, result.CategoryCounts);
            UpdateFacetCounts(LibraryCollectionFacets, result.CollectionCounts);
            RefreshVisibleLibraryFacets();
            _visibleRemoteFabCatalog = result.VisibleAssets;
            _libraryPageIndex = 0;
            RebuildRemoteFabPresentation();
            OnPropertyChanged(nameof(HasNoRemoteFabAssets));
            OnPropertyChanged(nameof(LibraryResultsSummary));
            OnPropertyChanged(nameof(HasActiveLibraryFilters));
            OnPropertyChanged(nameof(ActiveLibraryFilterSummary));
            OnPropertyChanged(nameof(AreAllLibraryEngineFacetsSelected));
            OnPropertyChanged(nameof(AreAllLibraryAssetTypeFacetsSelected));
            OnPropertyChanged(nameof(AreAllLibraryCategoryFacetsSelected));
            OnPropertyChanged(nameof(AreAllLibraryCollectionFacetsSelected));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppendRawLog("ERROR", "Library/View", exception.ToString());
            StatusMessage = $"Échec de la vue Bibliothèque : {exception.Message}";
        }
        finally
        {
            if (ReferenceEquals(_remoteLibraryViewCancellation, cancellation))
            {
                _remoteLibraryViewCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private static HashSet<string>? SelectedFacetValues(IEnumerable<LibraryFacetOptionViewModel> facets) =>
        facets.All(item => item.IsSelected) ? null : facets.Where(item => item.IsSelected)
            .Select(item => item.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly RemoteFabFacetMetadataCache LibraryFacetMetadata = new();
    private static RemoteLibraryViewResult ComputeRemoteLibraryView(
        RemoteFabAsset[] sourceAssets,
        Dictionary<string, WorkspaceItemPreference> preferences,
        Dictionary<string, string[]> personalCollections,
        string query,
        string sort,
        bool showHidden,
        bool showDownloaded,
        bool showUndownloaded,
        MarketplaceLocalAssetIndex localIndex,
        HashSet<string>? selectedEngines,
        HashSet<string>? selectedAssetTypes,
        HashSet<string>? selectedCategories,
        HashSet<string>? selectedCollections,
        CancellationToken token)
    {
        var entries = new RemoteLibraryIndexEntry[sourceAssets.Length];
        for (var index = 0; index < sourceAssets.Length; index++)
        {
            if ((index & 255) == 0) token.ThrowIfCancellationRequested();
            var asset = sourceAssets[index];
            preferences.TryGetValue(asset.AssetId, out var preference);
            var name = string.IsNullOrWhiteSpace(preference?.Alias) ? asset.Title : preference.Alias.Trim();
            var folder = string.IsNullOrWhiteSpace(preference?.FolderPath)
                ? "Bibliothèque principale"
                : preference.FolderPath.Trim();
            var collections = (personalCollections.TryGetValue(asset.AssetId, out var values) ? values : [])
                .Append(folder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var metadata = LibraryFacetMetadata.Get(asset);
            var categories = metadata.Categories;
            var engines = metadata.EngineVersions;
            entries[index] = new RemoteLibraryIndexEntry(
                asset,
                name,
                folder,
                collections,
                categories,
                engines,
                ClassifyFabAssetType(asset),
                preference?.IsHidden == true,
                localIndex.Contains(NormalizeAssetSource(asset), asset.AssetId, asset.Title));
        }

        bool Matches(
            RemoteLibraryIndexEntry entry,
            bool includeEngine = true,
            bool includeAssetType = true,
            bool includeCategory = true,
            bool includeCollection = true) =>
            (showHidden || !entry.IsHidden) &&
            (entry.IsDownloaded ? showDownloaded : showUndownloaded) &&
            (query.Length == 0 ||
             entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Asset.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Asset.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             entry.Asset.Categories.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
             entry.Collections.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
             entry.Engines.Any(value => value.Contains(query, StringComparison.OrdinalIgnoreCase))) &&
            (!includeEngine || selectedEngines is null || entry.Engines.Any(selectedEngines.Contains)) &&
            (!includeAssetType || selectedAssetTypes is null || selectedAssetTypes.Contains(entry.AssetType)) &&
            (!includeCategory || selectedCategories is null || entry.Categories.Any(selectedCategories.Contains)) &&
            (!includeCollection || selectedCollections is null || entry.Collections.Any(selectedCollections.Contains));

        IEnumerable<RemoteLibraryIndexEntry> filtered = entries.Where(entry => Matches(entry));
        filtered = sort switch
        {
            "Acquisition · récent" => filtered
                .OrderByDescending(entry => entry.Asset.AcquiredAt ?? DateTimeOffset.MinValue)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            "Acquisition · ancien" => filtered
                .OrderBy(entry => entry.Asset.AcquiredAt ?? DateTimeOffset.MaxValue)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            "Nom · Z → A" => filtered.OrderByDescending(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            "Catégorie" => filtered
                .OrderBy(entry => entry.Categories[0], StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            "Dossier / collection" => filtered
                .OrderBy(entry => entry.Folder, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            "Versions moteur" => filtered
                .OrderByDescending(entry => ParseEngineVersion(
                    entry.Engines.Length == 0 ? string.Empty : entry.Engines[0]))
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            "Téléchargeables d’abord" => filtered
                .OrderByDescending(entry => entry.Asset.ProjectVersions.Count > 0)
                .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
            _ => filtered.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
        };

        var engineFacets = entries.SelectMany(entry => entry.Engines)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(ParseEngineVersion)
            .ToArray();
        var assetTypeFacets = entries.Select(entry => entry.AssetType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var categoryFacets = entries.SelectMany(entry => entry.Categories)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var collectionFacets = entries.SelectMany(entry => entry.Collections)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RemoteLibraryViewResult(
            filtered.Select(entry => entry.Asset).ToArray(),
            engineFacets,
            assetTypeFacets,
            categoryFacets,
            collectionFacets,
            CountMatchingFacets(
                entries,
                entry => Matches(entry, includeEngine: false),
                entry => entry.Engines,
                token),
            CountMatchingFacets(
                entries,
                entry => Matches(entry, includeAssetType: false),
                entry => [entry.AssetType],
                token),
            CountMatchingFacets(
                entries,
                entry => Matches(entry, includeCategory: false),
                entry => entry.Categories,
                token),
            CountMatchingFacets(
                entries,
                entry => Matches(entry, includeCollection: false),
                entry => entry.Collections,
                token));
    }

    private static Dictionary<string, int> CountMatchingFacets(
        IReadOnlyList<RemoteLibraryIndexEntry> entries,
        Func<RemoteLibraryIndexEntry, bool> matches,
        Func<RemoteLibraryIndexEntry, IEnumerable<string>> values,
        CancellationToken token)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < entries.Count; index++)
        {
            if ((index & 255) == 0) token.ThrowIfCancellationRequested();
            var entry = entries[index];
            if (!matches(entry)) continue;
            foreach (var value in values(entry).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                counts[value] = counts.GetValueOrDefault(value) + 1;
            }
        }
        return counts;
    }

    private static void UpdateFacetCounts(
        IEnumerable<LibraryFacetOptionViewModel> facets,
        IReadOnlyDictionary<string, int> counts)
    {
        foreach (var facet in facets)
        {
            facet.UpdateResultCount(counts.GetValueOrDefault(facet.Value));
        }
    }

    private void ShowPreviousLibraryPage()
    {
        if (!CanShowPreviousLibraryPage)
        {
            return;
        }
        _libraryPageIndex--;
        RebuildRemoteFabPage();
    }

    private void ShowNextLibraryPage()
    {
        if (!CanShowNextLibraryPage)
        {
            return;
        }
        _libraryPageIndex++;
        RebuildRemoteFabPage();
    }

    private int GetLibraryPageCount() =>
        (_visibleRemoteFabCatalog.Length + LibraryPageSize - 1) / LibraryPageSize;

    private void RebuildRemoteFabPresentation()
    {
        _libraryPresentationCancellation?.Cancel();
        _libraryPresentationCancellation?.Dispose();
        _libraryPresentationCancellation = null;
        _libraryInfiniteItemCount = 0;
        RebuildRemoteFabPage();
    }

    private void ShowCachedRemoteFabPreview()
    {
        var sourceAssets = GetLibrarySourceAssets().ToArray();
        _librarySourceAssetCount = sourceAssets.Length;
        OnPropertyChanged(nameof(QuixelCollectionScopeSummary));
        _visibleRemoteFabCatalog = sourceAssets;
        _libraryPageIndex = 0;
        RebuildRemoteFabPresentation();
        OnPropertyChanged(nameof(HasNoRemoteFabAssets));
        OnPropertyChanged(nameof(LibraryResultsSummary));
    }

    private void RebuildRemoteFabPage()
    {
        var pageCount = GetLibraryPageCount();
        _libraryPageIndex = pageCount == 0
            ? 0
            : Math.Clamp(_libraryPageIndex, 0, pageCount - 1);
        RemoteFabAsset[] pageAssets;
        if (IsLibraryInfiniteMode)
        {
            var requestedCount = Math.Min(
                Math.Max(_libraryInfiniteItemCount, LibraryPageSize),
                _visibleRemoteFabCatalog.Length);
            pageAssets = _visibleRemoteFabCatalog.Take(requestedCount).ToArray();
            _libraryInfiniteItemCount = pageAssets.Length;
        }
        else
        {
            pageAssets = _visibleRemoteFabCatalog
                .Skip(_libraryPageIndex * LibraryPageSize)
                .Take(LibraryPageSize)
                .ToArray();
            _libraryInfiniteItemCount = 0;
        }
        var cardsPerRow = IsLibraryCompactCards ? 4 : 3;
        var rows = pageAssets
            .Chunk(cardsPerRow)
            .Select(assets => new RemoteFabAssetRowViewModel(assets, CreateRemoteAssetCard))
            .ToArray();
        ReplacePagedRemoteFabRows(rows);

        OnPropertyChanged(nameof(LibraryPageSummary));
        OnPropertyChanged(nameof(CanShowPreviousLibraryPage));
        OnPropertyChanged(nameof(CanShowNextLibraryPage));
    }

    public void LoadMoreLibraryItems()
    {
        if (!IsLibraryInfiniteMode || _libraryInfiniteItemCount >= _visibleRemoteFabCatalog.Length)
        {
            return;
        }

        var nextAssets = _visibleRemoteFabCatalog
            .Skip(_libraryInfiniteItemCount)
            .Take(LibraryPageSize)
            .ToArray();
        if (nextAssets.Length == 0)
        {
            return;
        }

        var cardsPerRow = IsLibraryCompactCards ? 4 : 3;
        var nextRows = nextAssets
            .Chunk(cardsPerRow)
            .Select(assets => new RemoteFabAssetRowViewModel(assets, CreateRemoteAssetCard))
            .ToArray();
        PagedRemoteFabRows.AddRange(nextRows);
        _libraryInfiniteItemCount += nextAssets.Length;
        OnPropertyChanged(nameof(LibraryPageSummary));
    }

    private void RebuildRemoteFabCategories()
    {
        var previous = SelectedLibraryCategory;
        LibraryCategories.Clear();
        LibraryCategories.Add("Toutes les catégories");
        foreach (var category in _remoteFabCatalog
                     .SelectMany(asset => asset.Categories)
                     .Where(category => !string.IsNullOrWhiteSpace(category))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(category => category, StringComparer.OrdinalIgnoreCase))
        {
            LibraryCategories.Add(category);
        }

        SelectedLibraryCategory = LibraryCategories.Contains(previous)
            ? previous
            : "Toutes les catégories";
    }

    private void RebuildRemoteFabFolders()
    {
        var previous = SelectedLibraryFolder;
        LibraryFolders.Clear();
        LibraryFolders.Add("Toutes les collections");
        foreach (var folder in _remoteFabCatalog
                     .SelectMany(GetAssetCollections)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase))
        {
            LibraryFolders.Add(folder);
        }

        SelectedLibraryFolder = LibraryFolders.Contains(previous)
            ? previous
            : "Toutes les collections";
    }

    private void RebuildRemoteFabFacets()
    {
        var sourceAssets = GetLibrarySourceAssets().ToArray();
        LibraryEngineVersions.Clear();
        LibraryEngineVersions.Add("Tous les moteurs");
        var engineVersions = sourceAssets
                     .SelectMany(asset => asset.ProjectVersions)
                     .SelectMany(version => version.EngineVersions)
                     .Where(version => !string.IsNullOrWhiteSpace(version))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(ParseEngineVersion)
                     .ToArray();
        foreach (var version in engineVersions)
        {
            LibraryEngineVersions.Add(version);
        }
        LibraryTags.Clear();
        LibraryTags.Add("Aucun filtre tag");

        ReplaceLibraryFacets(LibraryEngineFacets, engineVersions);
        ReplaceLibraryFacets(
            LibraryAssetTypeFacets,
            sourceAssets
                .Select(ClassifyFabAssetType)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        ReplaceLibraryFacets(
            LibraryCategoryFacets,
            sourceAssets
                .SelectMany(asset => LibraryFacetMetadata.Get(asset).Categories)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        ReplaceLibraryFacets(
            LibraryCollectionFacets,
            sourceAssets
                .SelectMany(GetAssetCollections)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
    }

    private static string ClassifyFabAssetType(RemoteFabAsset asset)
    {
        var distribution = asset.DistributionMethod.Trim().Replace('-', '_').ToUpperInvariant();
        if (distribution == "COMPLETE_PROJECT") return "Projet complet";
        if (distribution == "CODE_PLUGIN") return "Plugin code";
        if (distribution == "ASSET_PACK") return "Pack à ajouter";
        if (distribution == "ENGINE") return "Package moteur";
        var values = string.Join(' ', asset.Categories
            .Append(asset.DistributionMethod)
            .Append(asset.Title))
            .ToLowerInvariant();
        var isPlugin = asset.Categories.Any(value =>
                           value.Contains("plugin", StringComparison.OrdinalIgnoreCase) ||
                           value.Contains("code", StringComparison.OrdinalIgnoreCase)) ||
                       asset.DistributionMethod.Contains("plugin", StringComparison.OrdinalIgnoreCase) ||
                       asset.Title.Contains("plugin", StringComparison.OrdinalIgnoreCase);
        if (isPlugin || ContainsAny(values, "plugin", "code plugin", "outil éditeur"))
        {
            return "Plugin";
        }
        if (ContainsAny(values, "project", "projet", "template", "sample", "game template"))
        {
            return "Projet / template";
        }
        if (ContainsAny(values, "material", "matériau", "texture", "surface", "decal", "imperfection"))
        {
            return "Matériaux & textures";
        }
        if (ContainsAny(values, "character", "personnage", "animation", "metahuman", "motion"))
        {
            return "Personnages & animations";
        }
        if (ContainsAny(values, "audio", "music", "sound", "musique", "son"))
        {
            return "Audio";
        }
        if (ContainsAny(values, "3d", "mesh", "model", "prop", "environment", "building", "vehicle", "plant"))
        {
            return "Assets 3D";
        }
        if (ContainsAny(values, "pack", "bundle", "collection"))
        {
            return "Pack";
        }
        return asset.ProjectVersions.Count > 0 ? "Pack de contenu" : "Autre";

        static bool ContainsAny(string source, params string[] candidates) =>
            candidates.Any(candidate => source.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<RemoteFabAsset> GetLibrarySourceAssets() =>
        IsLibraryFabWebSelected ? _remoteFabCatalog.Where(IsFabWebAsset) : IsLibraryQuixelSelected
            ? _remoteFabCatalog.Where(asset => IsQuixelAsset(asset) &&
                (_quixelCollectionAssetIds is null || _quixelCollectionAssetIds.Contains(asset.AssetId)))
            : _remoteFabCatalog.Where(IsLauncherFabAsset);

    private IEnumerable<string> GetAssetCollections(RemoteFabAsset asset)
    {
        var values = _fabCollections.TryGetValue(asset.AssetId, out var collections)
            ? collections
            : [];
        var folder = FindWorkspacePreference(WorkspaceItemKind.Asset, asset.AssetId)?.FolderPath;
        return values.Append(string.IsNullOrWhiteSpace(folder) ? "Bibliothèque principale" : folder.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsQuixelAsset(RemoteFabAsset asset) =>
        string.Equals(asset.Source, "quixel", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAssetSource(RemoteFabAsset asset) =>
        IsFabWebAsset(asset) ? FabWebLibraryClient.Source : IsQuixelAsset(asset) ? "Quixel" : "Fab";

    private static bool IsStoredDownloadForAsset(
        FabStoredDownloadRecord item,
        string assetId,
        string? assetTitle,
        string? source)
    {
        if (source is not null &&
            !string.Equals(item.Source, source, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (string.Equals(item.AssetId, assetId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(item.Source, "Quixel", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !string.IsNullOrWhiteSpace(assetTitle) &&
               !string.IsNullOrWhiteSpace(item.AssetTitle) &&
               string.Equals(
                   NormalizeProductTitle(item.AssetTitle),
                   NormalizeProductTitle(assetTitle),
                   StringComparison.Ordinal);
    }

    private LibraryPackageStorageMode ResolveMarketplaceStorageMode(RemoteFabAsset asset) =>
        IsQuixelAsset(asset)
            ? GetCompressionMode(SelectedQuixelCompressionOption)
            : GetCompressionMode(SelectedFabCompressionOption);

    private MarketplaceStorageProfile ResolveMarketplaceStorageProfile(RemoteFabAsset asset) =>
        IsQuixelAsset(asset)
            ? BuildMarketplaceStorageProfile(
                "Megascans",
                QuixelCacheDirectory,
                QuixelDownloadDirectory,
                QuixelLibraryLocations)
            : BuildMarketplaceStorageProfile(
                "Fab",
                FabCacheDirectory,
                FabDownloadDirectory,
                FabLibraryLocations);

    private void ReplaceLibraryFacets(
        BulkObservableCollection<LibraryFacetOptionViewModel> target,
        IEnumerable<string> values)
    {
        var nextValues = values.ToArray();
        if (target.Select(item => item.Value).SequenceEqual(nextValues, StringComparer.OrdinalIgnoreCase)) return;
        var existing = target.ToDictionary(item => item.Value, StringComparer.OrdinalIgnoreCase);
        var allSelected = target.All(item => item.IsSelected);
        target.ReplaceWith(nextValues.Select(value => existing.GetValueOrDefault(value) ??
            new LibraryFacetOptionViewModel(value, allSelected, ApplyRemoteFabFilter)));
    }

    private void ClearLibraryFacetSelections()
    {
        LibraryEngineFacetSearchQuery = string.Empty;
        LibraryAssetTypeFacetSearchQuery = string.Empty;
        LibraryCategoryFacetSearchQuery = string.Empty;
        LibraryCollectionFacetSearchQuery = string.Empty;
        ShowDownloadedLibraryAssets = true;
        ShowUndownloadedLibraryAssets = true;
        foreach (var facet in LibraryEngineFacets
                     .Concat(LibraryAssetTypeFacets)
                     .Concat(LibraryCategoryFacets)
                     .Concat(LibraryCollectionFacets))
        {
            facet.SetSelectionSilently(true);
        }
        ApplyRemoteFabFilter();
    }

    private void SetAllFacets(
        ObservableCollection<LibraryFacetOptionViewModel> facets,
        bool selected)
    {
        foreach (var facet in facets)
        {
            facet.SetSelectionSilently(selected);
        }
        ApplyRemoteFabFilter();
    }

    private void ClearRemoteFabAssets()
    {
        _libraryPresentationCancellation?.Cancel();
        _libraryPresentationCancellation?.Dispose();
        _libraryPresentationCancellation = null;
        _remoteFabCatalog = [];
        _visibleRemoteFabCatalog = [];
        _librarySourceAssetCount = 0;
        ClearPagedRemoteFabRows();
    }

    private void RemoveRemoteAssets(Func<RemoteFabAsset, bool> predicate)
    {
        _remoteFabCatalog = _remoteFabCatalog.Where(asset => !predicate(asset)).ToArray();
    }

    private void ReplaceRemoteAssets(
        Func<RemoteFabAsset, bool> predicate,
        IReadOnlyList<RemoteFabAsset> replacements)
    {
        _remoteFabCatalog = _remoteFabCatalog
            .Where(asset => !predicate(asset))
            .Concat(replacements)
            .ToArray();
    }

    private IEnumerable<RemoteFabAssetCardViewModel> GetRealizedRemoteFabCards() =>
        PagedRemoteFabRows.SelectMany(row => row.Cards);

    private void ReplacePagedRemoteFabRows(
        IReadOnlyList<RemoteFabAssetRowViewModel> replacements)
    {
        var previous = PagedRemoteFabRows.ToArray();
        PagedRemoteFabRows.ReplaceWith(replacements);
        foreach (var row in previous) row.Dispose();
    }

    private void ClearPagedRemoteFabRows()
    {
        var previous = PagedRemoteFabRows.ToArray();
        PagedRemoteFabRows.Clear();
        foreach (var row in previous) row.Dispose();
    }

    private void ResetRemoteFabPresentationForRefresh()
    {
        _libraryPresentationCancellation?.Cancel();
        _libraryPresentationCancellation?.Dispose();
        _libraryPresentationCancellation = null;
        ClearPagedRemoteFabRows();
        _visibleRemoteFabCatalog = [];
        OnPropertyChanged(nameof(LibraryPageSummary));
        OnPropertyChanged(nameof(LibraryResultsSummary));
    }

    private Task DownloadSelectedFabAssetAsync() =>
        DownloadSelectedFabVersionsAsync(FabInstallDestination.Library);

    private async Task DownloadSelectedFabVersionsAsync(FabInstallDestination destination,
        RemoteFabAsset? requestedAsset = null, FabInstallVersionOptionViewModel[]? requestedVersions = null,
        Action<HubTaskHandle>? onQueued = null, SavedLibraryDownload? resumedEntry = null)
    {
        var asset = requestedAsset ?? SelectedRemoteFabAsset
            ?? throw new InvalidOperationException("Sélectionnez d’abord un asset Fab.");
        if (!IsMarketplaceAvailable(NormalizeAssetSource(asset)))
            throw new InvalidOperationException("Cette marketplace est désactivée dans les paramètres.");
        var versions = requestedVersions ?? FabInstallVersions.Where(item => item.IsSelected).ToArray();
        if (versions.Length == 0)
        {
            throw new InvalidOperationException("Sélectionnez au moins une version à télécharger.");
        }
        // Queue other packs while an earlier batch is running, but never write the
        // same artifact twice concurrently. No installed engine is needed here.
        var downloadSource = NormalizeAssetSource(asset);
        versions = versions.Where(option => !_fabVersionDownloads.Any(item =>
            !item.IsTerminal &&
            item.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase) &&
            item.Source.Equals(downloadSource, StringComparison.OrdinalIgnoreCase) &&
            item.ArtifactId.Equals(option.Version.ArtifactId, StringComparison.OrdinalIgnoreCase)))
            .DistinctBy(option => option.Version.ArtifactId, StringComparer.OrdinalIgnoreCase).ToArray();
        if (versions.Length == 0)
        {
            StatusMessage = "Ces variantes sont déjà dans la file de téléchargement.";
            return;
        }
        if (destination == FabInstallDestination.Project && SelectedFabInstallProject is null)
        {
            throw new InvalidOperationException("Sélectionnez un projet Unreal cible.");
        }
        if (destination == FabInstallDestination.Engine && SelectedFabInstallEngine is null)
        {
            throw new InvalidOperationException("Sélectionnez un moteur Unreal cible.");
        }

        var targetProject = SelectedFabInstallProject;
        var targetEngine = SelectedFabInstallEngine;
        var results = new DirectFabDownloadResult?[versions.Length];
        var handles = new HubTaskHandle[versions.Length];
        var queueItems = new FabVersionDownloadViewModel[versions.Length];
        if (destination != FabInstallDestination.Library) IsEpicBusy = true;
        IsFabDownloadActive = true;
        FabDownloadProgress = 0;
        FabDownloadStatus = "Préparation du téléchargement…";
        if (requestedAsset is null)
        {
            IsFabInstallOptionsVisible = false;
            IsFabDownloadOptionsVisible = false;
        }
        var verb = destination == FabInstallDestination.Library ? "Télécharger" : "Télécharger et installer";
        var isQuixel = IsQuixelAsset(asset);
        var packageStorageMode = resumedEntry?.Mode ?? ResolveMarketplaceStorageMode(asset);
        var marketplaceStorage = resumedEntry?.Storage ?? ResolveMarketplaceStorageProfile(asset);
        var source = NormalizeAssetSource(asset);

        try
        {
            for (var index = 0; index < versions.Length; index++)
            {
                var option = versions[index];
                var versionIndex = index;
                var durableId = DurableDownloadId(asset, option.Version);
                var durable = destination == FabInstallDestination.Library
                    ? new SavedLibraryDownload(asset, option.Version, marketplaceStorage, packageStorageMode, resumedEntry?.CreatedAt ?? DateTimeOffset.UtcNow)
                    : null;
                if (durable is not null) DownloadJournal.Upsert(durableId, durable);
                var queueItem = new FabVersionDownloadViewModel(
                    asset.AssetId,
                    source,
                    option.Label,
                    option.Version.ArtifactId,
                    CancelTask);
                queueItems[index] = queueItem;
                AddFabVersionDownload(queueItem);

                var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
                    isQuixel ? "Bibliothèque · Megascans" : "Bibliothèque · Fab",
                    $"{verb} {asset.Title} · {option.Label}",
                    ProductTaskLinks(asset, destination == FabInstallDestination.Project && targetProject is not null
                        ? [ProjectTaskLink(targetProject.Project.FilePath, targetProject.Name)]
                        : destination == FabInstallDestination.Engine && targetEngine is not null
                            ? [EngineTaskLink(targetEngine.Path, targetEngine.DisplayName)] : []),
                    async (context, cancellationToken) =>
                    {
                        var assetGate = AssetDownloadLocks.GetOrAdd(source + ":" + asset.AssetId, _ => new SemaphoreSlim(1, 1));
                        await assetGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                        var transfer = new CyEngineHub.Core.Marketplaces.DownloadProgressText();
                        var progress = new InlineProgress<DirectFabDownloadProgress>(value =>
                        {
                            var fraction = value.Fraction ?? 0;
                            var message = transfer.Format($"{option.Label} · {value.Message}", value.DownloadedBytes, value.TotalBytes);
                            context.Report(fraction, message);
                            Dispatcher.UIThread.Post(() => FabDownloadStatus = message);
                        });
                        var result = isQuixel
                            ? await _quixelClient.DownloadAsync(
                                    asset,
                                    option.Version,
                                    marketplaceStorage.DownloadDirectory,
                                    progress,
                                    cancellationToken)
                                .ConfigureAwait(false)
                            : await _directFabClient.DownloadAsync(
                                    asset,
                                    option.Version,
                                    marketplaceStorage.DownloadDirectory,
                                    progress,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        if (destination != FabInstallDestination.Library)
                        {
                            await InstallDownloadedFabPackageAsync(
                                    result.DestinationDirectory,
                                    destination,
                                    targetProject,
                                    targetEngine,
                                    context,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        var packageProgress = new InlineProgress<DirectFabDownloadProgress>(value =>
                        {
                            context.Report(value.Fraction, $"{option.Label} · {value.Message}");
                            Dispatcher.UIThread.Post(() => FabDownloadStatus = $"{option.Label} · {value.Message}");
                        });
                        result = await LibraryPackageService.PackageAsync(
                                asset,
                                option.Version,
                                result,
                                packageStorageMode,
                                packageProgress,
                                cancellationToken)
                            .ConfigureAwait(false);
                        results[versionIndex] = await MarketplaceStorageRouter.PlaceAsync(
                                result,
                                marketplaceStorage,
                                packageProgress,
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (durable is not null)
                            DownloadJournal.Upsert(durableId, durable with { Result = results[versionIndex]! with { SourceManifest = null } });
                        }
                        finally { assetGate.Release(); }
                    }) { IsDownload = destination == FabInstallDestination.Library });
                handles[index] = handle;
                queueItem.AttachTask(handle.Id);
                _fabVersionDownloadsByTaskId[handle.Id] = queueItem;
                if (durable is not null) _durableDownloadTasks[handle.Id] = durableId;
                onQueued?.Invoke(handle);
            }

            var completed = await Task.WhenAll(handles.Select(item => item.Completion)).ConfigureAwait(true);
            var succeeded = 0;
            var canceled = 0;
            var failures = new List<(FabInstallVersionOptionViewModel Option, string Error)>();
            for (var index = 0; index < completed.Length; index++)
            {
                var snapshot = completed[index];
                var result = results[index];
                if (snapshot.Status == HubTaskStatus.Completed && result is not null)
                {
                    succeeded++;
                    await SaveStoredFabDownloadAsync(asset, versions[index], result).ConfigureAwait(true);
                    if (destination == FabInstallDestination.Library)
                    {
                        var id = DurableDownloadId(asset, versions[index].Version);
                        var saved = DownloadJournal.Read()[id];
                        DownloadJournal.Upsert(id, saved with { State = "Completed" });
                    }
                    AppendRawLog(
                        "INFO",
                        isQuixel ? "Library/Quixel" : "Library/Fab",
                        $"{result.DisplayName} téléchargé : {result.FileCount:N0} fichier(s), {FormatBytes(result.DownloadedBytes)} · {result.DestinationDirectory}");
                    RemoveFabVersionDownload(queueItems[index]);
                    _fabVersionDownloadsByTaskId.Remove(snapshot.Id);
                    continue;
                }
                if (snapshot.Status == HubTaskStatus.Canceled)
                {
                    canceled++;
                    continue;
                }
                failures.Add((versions[index], snapshot.Error ?? "Le téléchargement a échoué."));
                if (destination == FabInstallDestination.Library)
                {
                    var id = DurableDownloadId(asset, versions[index].Version);
                    var saved = DownloadJournal.Read()[id];
                    DownloadJournal.Upsert(id, saved with { State = "Failed" });
                }
            }

            foreach (var failure in failures)
            {
                AppendRawLog(
                    "WARN",
                    isQuixel ? "Library/Quixel" : "Library/Fab",
                    $"{failure.Option.Label} : {failure.Error}");
            }

            FabDownloadProgress = versions.Length == 0 ? 0 : (double)succeeded / versions.Length;
            if (failures.Count > 0)
            {
                FabDownloadStatus =
                    $"Lot partiel : {succeeded:N0}/{versions.Length:N0} version(s) terminée(s). " +
                    $"Échec : {failures[0].Option.Label} — {failures[0].Error}";
                if (isQuixel) QuixelStatusSummary = FabDownloadStatus;
                else DirectFabStatusSummary = FabDownloadStatus;
            }
            else if (canceled > 0)
            {
                FabDownloadStatus =
                    $"{canceled:N0} version(s) annulée(s) · {succeeded:N0} terminée(s). Les chunks validés sont conservés.";
            }
            else
            {
                FabDownloadProgress = 1;
                FabDownloadStatus = destination switch
                {
                    FabInstallDestination.Project => $"{succeeded} version(s) installée(s) dans {targetProject!.Name}.",
                    FabInstallDestination.Engine => $"{succeeded} version(s) installée(s) dans {targetEngine!.DisplayName}.",
                    _ => $"{succeeded} version(s) téléchargée(s) et vérifiée(s).",
                };
            }
            StatusMessage = FabDownloadStatus;
        }
        finally
        {
            if (destination != FabInstallDestination.Library) IsEpicBusy = false;
            IsFabDownloadActive = _fabVersionDownloads.Any(item => !item.IsTerminal);
        }
    }

    private async Task InstallSelectedFabVersionsIntoTargetsAsync()
    {
        var asset = SelectedRemoteFabAsset
            ?? throw new InvalidOperationException("Sélectionnez d’abord un asset Fab.");
        var versions = CompatibleFabInstallVersions.Where(item => item.IsSelected).ToArray();
        var targets = FabInstallTargets.Where(item => item.IsSelected).ToArray();
        if (versions.Length == 0) throw new InvalidOperationException("Sélectionnez au moins une version.");
        if (targets.Length == 0) throw new InvalidOperationException("Sélectionnez au moins un projet ou moteur cible.");
        var targetsWithoutScript = targets
            .Where(target => target.SelectedInstallationScript is null)
            .Select(target => target.Name)
            .ToArray();
        if (targetsWithoutScript.Length > 0)
        {
            throw new InvalidOperationException(
                $"Aucun script d’installation compatible pour : {string.Join(", ", targetsWithoutScript)}.");
        }
        var selectedDerived = string.IsNullOrWhiteSpace(_activeDerivedInstallArtifactId)
            ? null
            : _fabStoredDownloads.LastOrDefault(item =>
                item.ArtifactId.Equals(_activeDerivedInstallArtifactId, StringComparison.OrdinalIgnoreCase));
        if (selectedDerived is null && !IsMarketplaceAvailable(NormalizeAssetSource(asset)))
            throw new InvalidOperationException("Cette marketplace est désactivée. Réactivez-la dans les paramètres pour une nouvelle installation distante.");
        var contentKind = selectedDerived is null
            ? SelectedRemoteFabAssetCard?.ContentKind ?? LibraryPackageContentKind.Unknown
            : ParseContentKind(selectedDerived.PackageKind);
        if (contentKind == LibraryPackageContentKind.CompleteProject)
        {
            throw new InvalidOperationException(
                "Ce produit est un projet complet. Téléchargez-le comme projet autonome au lieu de l’injecter dans un projet existant.");
        }
        if (targets.Any(item => item.IsEngine) && contentKind is not (
                LibraryPackageContentKind.CodePlugin or LibraryPackageContentKind.EnginePackage))
        {
            throw new InvalidOperationException("L’installation dans un moteur est réservée aux plugins Unreal.");
        }
        var uncoveredTargets = targets
            .Where(target => !versions.Any(option =>
                CanInstallFabVersionIntoTarget(option.Version, target)))
            .Select(target => target.Name)
            .ToArray();
        if (uncoveredTargets.Length > 0)
        {
            throw new InvalidOperationException(
                $"Aucune variante sélectionnée ne correspond à : {string.Join(", ", uncoveredTargets)}.");
        }

        IsEpicBusy = true;
        IsFabDownloadActive = true;
        FabDownloadProgress = 0;
        FabDownloadStatus = selectedDerived is null
            ? "Préparation du téléchargement et des installations…"
            : "Préparation du dérivé local et des installations…";
        IsFabInstallOptionsVisible = false;
        var completedDownloads = new List<(FabInstallVersionOptionViewModel Option, DirectFabDownloadResult Result)>();
        var completedInstallations = 0;
        var packageStorageMode = ResolveMarketplaceStorageMode(asset);
        var marketplaceStorage = ResolveMarketplaceStorageProfile(asset);
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Bibliothèque · Fab",
            $"Installer {asset.Title} vers {targets.Length:N0} cible(s)",
            ProductTaskLinks(asset, targets.Select(target => target.IsEngine
                ? EngineTaskLink(target.Path, target.Name) : ProjectTaskLink(target.Path, target.Name))),
            async (context, cancellationToken) =>
            {
                for (var index = 0; index < versions.Length; index++)
                {
                    var option = versions[index];
                    var transfer = new CyEngineHub.Core.Marketplaces.DownloadProgressText();
                    var progress = new InlineProgress<DirectFabDownloadProgress>(value =>
                    {
                        var fraction = (index + (value.Fraction ?? 0)) / versions.Length;
                        var message = transfer.Format($"{option.Label} · {value.Message}", value.DownloadedBytes, value.TotalBytes);
                        context.Report(fraction, message);
                        Dispatcher.UIThread.Post(() =>
                        {
                            FabDownloadProgress = fraction;
                            FabDownloadStatus = message;
                        });
                    });
                    var localDerived = _fabStoredDownloads.LastOrDefault(item =>
                        item.AssetId.Equals(asset.AssetId, StringComparison.OrdinalIgnoreCase) &&
                        item.ArtifactId.Equals(option.Version.ArtifactId, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(item.SourceArtifactId));
                    if (localDerived is not null)
                    {
                        var materialized = Path.Combine(
                            DataDirectory,
                            "DerivedInstallSources",
                            Guid.NewGuid().ToString("N"));
                        try
                        {
                            await DerivedLibraryPackageService.MaterializeAsync(
                                    localDerived.Path,
                                    materialized,
                                    ParseContentKind(localDerived.PackageKind),
                                    progress,
                                    cancellationToken)
                                .ConfigureAwait(false);
                            foreach (var target in targets.Where(target =>
                                         CanInstallFabVersionIntoTarget(option.Version, target)))
                            {
                                await ExecuteSelectedFabInstallationScriptAsync(
                                        asset,
                                        option.Version,
                                        materialized,
                                        target,
                                        context,
                                        cancellationToken)
                                    .ConfigureAwait(false);
                                completedInstallations++;
                            }
                        }
                        finally
                        {
                            DeleteOwnedDerivedDirectory(materialized, "DerivedInstallSources");
                        }
                        continue;
                    }
                    var result = IsQuixelAsset(asset)
                        ? await _quixelClient.DownloadAsync(
                                asset,
                                option.Version,
                                marketplaceStorage.DownloadDirectory,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false)
                        : await _directFabClient.DownloadAsync(
                                asset,
                                option.Version,
                                marketplaceStorage.DownloadDirectory,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);
                    var compatibleTargets = targets
                        .Where(target => CanInstallFabVersionIntoTarget(option.Version, target))
                        .ToArray();
                    foreach (var target in compatibleTargets)
                    {
                        await ExecuteSelectedFabInstallationScriptAsync(
                                asset,
                                option.Version,
                                result.DestinationDirectory,
                                target,
                                context,
                                cancellationToken)
                            .ConfigureAwait(false);
                        completedInstallations++;
                    }
                    var packageProgress = new InlineProgress<DirectFabDownloadProgress>(value =>
                    {
                        context.Report(null, $"{option.Label} · {value.Message}");
                        Dispatcher.UIThread.Post(() => FabDownloadStatus = $"{option.Label} · {value.Message}");
                    });
                    result = await LibraryPackageService.PackageAsync(
                            asset,
                            option.Version,
                            result,
                            packageStorageMode,
                            packageProgress,
                            cancellationToken)
                        .ConfigureAwait(false);
                    result = await MarketplaceStorageRouter.PlaceAsync(
                            result,
                            marketplaceStorage,
                            packageProgress,
                            cancellationToken)
                        .ConfigureAwait(false);
                    completedDownloads.Add((option, result));
                }
                context.Report(1, "Toutes les installations sont terminées.");
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        IsFabDownloadActive = false;
        foreach (var downloaded in completedDownloads)
        {
            await SaveStoredFabDownloadAsync(asset, downloaded.Option, downloaded.Result).ConfigureAwait(true);
        }
        if (completed.Status != HubTaskStatus.Completed)
        {
            FabDownloadStatus = completed.Status == HubTaskStatus.Canceled
                ? "Installation annulée; les téléchargements validés restent en cache."
                : $"Échec : {completed.Error}";
            return;
        }
        FabDownloadProgress = 1;
        FabDownloadStatus = selectedDerived is null
            ? $"{versions.Length:N0} version(s) téléchargée(s) · {completedInstallations:N0} installation(s) compatible(s) terminée(s)."
            : $"Dérivé local installé · {completedInstallations:N0} installation(s) compatible(s) terminée(s).";
        StatusMessage = FabDownloadStatus;
    }

    private async Task ExecuteSelectedFabInstallationScriptAsync(
        RemoteFabAsset asset,
        RemoteFabProjectVersion version,
        string sourceDirectory,
        FabInstallTargetOptionViewModel target,
        HubTaskContext taskContext,
        CancellationToken cancellationToken)
    {
        var selectedScript = target.SelectedInstallationScript
            ?? throw new InvalidOperationException(
                $"Aucun script d’installation n’est sélectionné pour {target.Name}.");
        var script = _fabInstallationScripts.Find(selectedScript.Id)
            ?? throw new InvalidOperationException(
                $"Le script {selectedScript.DisplayName} n’est plus disponible.");
        var targetKind = target.IsProject
            ? FabInstallationTargetKind.Project
            : FabInstallationTargetKind.Engine;
        if (!script.Descriptor.SupportedContentKinds.Contains(target.ContentKind) ||
            !script.Descriptor.SupportedTargetKinds.Contains(targetKind))
        {
            throw new InvalidOperationException(
                $"Le script {script.Descriptor.DisplayName} ne prend pas en charge {target.Name}.");
        }
        var engine = target.Engine?.Engine ??
                     (target.Project is null
                         ? null
                         : ResolveEngine(target.Project.Project.EngineAssociation));
        var scriptTarget = new FabInstallationScriptTarget(
            target.Id,
            target.Name,
            targetKind,
            target.Path,
            target.EngineVersion,
            target.Project?.Project,
            engine);
        var progress = new InlineProgress<FabInstallationScriptProgress>(value =>
            taskContext.Report(
                value.Fraction,
                $"{target.Name} · {script.Descriptor.DisplayName} · {value.Message}"));
        var result = await script.InstallAsync(
                new FabInstallationScriptContext(
                    asset,
                    version,
                    target.ContentKind,
                    sourceDirectory,
                    scriptTarget,
                    target.GetInstallationConfiguration()),
                progress,
                cancellationToken)
            .ConfigureAwait(false);
        if (result.Outputs.TryGetValue("variant", out var variantDirectory) &&
            !string.IsNullOrWhiteSpace(variantDirectory) &&
            Directory.Exists(variantDirectory))
        {
            var registration = Dispatcher.UIThread.InvokeAsync(() => RegisterDerivedVariantAsync(
                asset,
                version,
                target,
                variantDirectory,
                result.Outputs.GetValueOrDefault("manifest", string.Empty)));
            await registration;
        }
        taskContext.Report(null, $"{target.Name} · {result.Summary}");
    }

    private async Task RegisterDerivedVariantAsync(
        RemoteFabAsset asset,
        RemoteFabProjectVersion sourceVersion,
        FabInstallTargetOptionViewModel target,
        string variantDirectory,
        string manifestPath)
    {
        var artifactId = $"{sourceVersion.ArtifactId}:converted:{target.EngineVersion}";
        var bytes = Directory.EnumerateFiles(variantDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path).Length)
            .Aggregate(0L, (total, length) => checked(total + length));
        _fabStoredDownloads.RemoveAll(item =>
            string.Equals(item.AssetId, asset.AssetId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ArtifactId, artifactId, StringComparison.OrdinalIgnoreCase));
        _fabStoredDownloads.Add(new FabStoredDownloadRecord(
            asset.AssetId,
            $"Unreal Engine {target.EngineVersion} · convertie",
            artifactId,
            variantDirectory,
            bytes,
            DateTimeOffset.UtcNow,
            NormalizeAssetSource(asset),
            target.ContentKind.ToString(),
            "DerivedVariant",
            string.IsNullOrWhiteSpace(manifestPath) ? null : manifestPath,
            bytes,
            bytes,
            asset.Title,
            $"{asset.Title} · UE {target.EngineVersion}",
            "Conversion créée automatiquement par le script d’installation.",
            sourceVersion.ArtifactId,
            [target.EngineVersion]));
        await PersistStoredFabDownloadsAsync().ConfigureAwait(true);
        UpdateStoredFabAssetState(asset.AssetId);
        RebuildSelectedFabStoredDownloads(asset.AssetId);
        AppendRawLog(
            "INFO",
            "Library/VersionBridge",
            $"Variante locale enregistrée : {asset.Title} · UE {target.EngineVersion} · {variantDirectory}");
    }

    private async Task InstallDownloadedFabPackageAsync(
        string sourceDirectory,
        FabInstallDestination destination,
        ProjectCardViewModel? project,
        EngineCardViewModel? engine,
        HubTaskContext context,
        CancellationToken cancellationToken)
    {
        var plugins = await _pluginDiscovery.RefreshAsync(
                new PluginDiscoveryRequest([], [], [sourceDirectory], MaximumDepth: 10),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var plugin = plugins.FirstOrDefault(item => item.Health != PluginHealth.Broken);
        if (plugin is not null)
        {
            UnrealEngineInstallation targetEngine;
            PluginInstallTarget target;
            if (destination == FabInstallDestination.Engine)
            {
                targetEngine = engine!.Engine;
                target = new PluginInstallTarget(
                    $"engine:{targetEngine.Id}",
                    $"Moteur · {targetEngine.DisplayName}",
                    PluginInstallTargetKind.Engine,
                    Path.Combine(targetEngine.RootPath, "Engine", "Plugins"),
                    targetEngine);
            }
            else
            {
                targetEngine = ResolveEngine(project!.Project.EngineAssociation)
                    ?? throw new InvalidOperationException(
                        $"Aucun moteur Unreal n’est associé au projet {project.Name}.");
                target = new PluginInstallTarget(
                    $"project:{project.Project.Id}",
                    $"Projet · {project.Name}",
                    PluginInstallTargetKind.Project,
                    Path.Combine(project.Project.RootPath, "Plugins"),
                    targetEngine);
            }

            var plan = await _pluginInstallation.PlanAsync(plugin, target, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var progress = new InlineProgress<PluginInstallProgress>(value =>
                context.Report(null, $"Installation plugin · {value.Message}"));
            await _pluginInstallation.InstallAsync(plan, progress, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (destination == FabInstallDestination.Engine)
        {
            throw new InvalidOperationException(
                "Ce produit ne contient aucun plugin .uplugin installable dans un moteur.");
        }

        var importPlan = await _fabProjectContentImport.PrepareAsync(
                sourceDirectory,
                project!.Project.FilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var importProgress = new InlineProgress<FabProjectImportProgress>(value =>
            context.Report(null, $"Import projet · {value.Message}"));
        await _fabProjectContentImport.ExecuteAsync(importPlan, importProgress, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task LoadEpicAsync()
    {
        var authentication = await _epicAuthentication.GetStatusAsync().ConfigureAwait(true);
        var fab = await _fabBackend.GetStatusAsync().ConfigureAwait(true);
        var binaryEngine = await _epicBinaryEngineBackend.GetStatusAsync().ConfigureAwait(true);
        EpicAuthenticationSummary = authentication.Message;
        FabStatusSummary = fab.Message;
        EngineBinaryDownloadSummary = binaryEngine.Message;

        EpicCapabilities.Clear();
        EpicCapabilities.Add(new EpicCapabilityCardViewModel(new EpicCapability(
            "Compte Epic",
            EpicCapabilityState.ExternalOnly,
            "Connexion et compte dans le navigateur officiel. CyEngineHub ne lit pas la session.")));
        EpicCapabilities.Add(new EpicCapabilityCardViewModel(new EpicCapability(
            "Catalogue local Epic",
            EpicCapabilityState.Available,
            "Lecture bornée des manifests d’installation non secrets et des dossiers de cache connus.")));
        EpicCapabilities.Add(new EpicCapabilityCardViewModel(new EpicCapability(
            "Moteur binaire Epic",
            binaryEngine.SupportsInAppDownloads ? EpicCapabilityState.Available : EpicCapabilityState.Unavailable,
            binaryEngine.Message)));
        EpicCapabilities.Add(new EpicCapabilityCardViewModel(new EpicCapability(
            "Moteur source GitHub",
            EpicCapabilityState.Available,
            "Clone confirmé depuis EpicGames/UnrealEngine ou un fork GitHub autorisé; Setup reste une étape séparée.")));
        EpicCapabilities.Add(new EpicCapabilityCardViewModel(new EpicCapability(
            "Fab vers projet",
            EpicCapabilityState.Available,
            "Intégration Fab officielle dans l’Editor ou import transactionnel d’un pack Unreal local extrait.")));
        EpicCapabilities.Add(new EpicCapabilityCardViewModel(new EpicCapability(
            "Téléchargement Fab direct",
            IsDirectFabConnected ? EpicCapabilityState.Available : EpicCapabilityState.Unavailable,
            "Téléchargement par manifests signés, chunks repris et intégrité vérifiée; aucun processus EGL n’est requis.")));
        EpicCapabilities.Add(new EpicCapabilityCardViewModel(new EpicCapability(
            "Epic Online Services",
            EpicCapabilityState.Unavailable,
            "Nécessite un produit et un client enregistrés; cela ne donne pas accès à la bibliothèque Fab globale.")));

        var snapshot = await _epicSnapshotCache.LoadAsync().ConfigureAwait(true);
        if (snapshot is null)
        {
            return;
        }

        ReplaceEpicSnapshot(snapshot);
        EpicStatusSummary =
            $"Inventaire en cache · {snapshot.RefreshedAt.ToLocalTime():g} · " +
            $"{snapshot.Artifacts.Count} élément(s)";
    }

    private async Task RefreshEpicAsync()
    {
        IsEpicBusy = true;
        EpicStatusSummary = "Lecture locale manuelle des données d’installation non secrètes…";
        EpicLocalSnapshot? snapshot = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Epic",
            "Actualiser le catalogue local Epic/Fab",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<EpicCatalogProgress>(value =>
                {
                    var ratio = value.Total == 0
                        ? 0
                        : Math.Clamp((double)value.Completed / value.Total, 0, 1);
                    context.Report(ratio, value.Message);
                });
                snapshot = await _epicLocalCatalog.RefreshAsync(progress, cancellationToken)
                    .ConfigureAwait(false);
                await _epicSnapshotCache.SaveAsync(snapshot, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        if (completed.Status == HubTaskStatus.Canceled)
        {
            EpicStatusSummary = "Inventaire Epic/Fab annulé; le cache précédent est conservé.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed || snapshot is null)
        {
            EpicStatusSummary = $"Échec de l’inventaire Epic/Fab : {completed.Error}";
            return;
        }

        ReplaceEpicSnapshot(snapshot);
        EpicStatusSummary =
            $"{snapshot.Artifacts.Count} élément(s) · {snapshot.VaultItemCount} entrée(s) Vault · " +
            $"{snapshot.FabPluginPackageCount} package(s) Fab · cache actualisé";
        StatusMessage = "Catalogue local Epic/Fab actualisé.";
        ShowSection(PageSection.Settings);
    }

    private void ReplaceEpicSnapshot(EpicLocalSnapshot snapshot)
    {
        _currentEpicSnapshot = snapshot;
        EpicArtifacts.Clear();
        foreach (var artifact in snapshot.Artifacts)
        {
            EpicArtifacts.Add(new EpicArtifactCardViewModel(artifact, OpenFolderAsync, ShowError));
        }

        OnPropertyChanged(nameof(HasNoEpicArtifacts));
        OnPropertyChanged(nameof(EpicArtifactCount));
        OnPropertyChanged(nameof(EpicVaultItemCount));
        OnPropertyChanged(nameof(FabPluginPackageCount));
        OnPropertyChanged(nameof(EpicLauncherStatus));
        OnPropertyChanged(nameof(EpicLauncherPath));
        OnPropertyChanged(nameof(CanLaunchEpicLauncher));
        OnPropertyChanged(nameof(EpicConnectionStatus));
        OnPropertyChanged(nameof(EpicConnectionIndicator));
        OnPropertyChanged(nameof(FabConnectionStatus));
        OnPropertyChanged(nameof(FabConnectionIndicator));
        OnPropertyChanged(nameof(HasLocalFabIntegration));
        _launchEpicLauncherCommand.NotifyCanExecuteChanged();
    }

    private async Task LaunchEpicLauncherAsync()
    {
        var path = _currentEpicSnapshot?.LauncherPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            throw new FileNotFoundException("Epic Games Launcher n’est pas détecté.", path);
        }

        await _platformLauncher.StartDetachedAsync(path).ConfigureAwait(true);
        StatusMessage = "Epic Games Launcher démarré.";
    }

    private async Task ConnectEpicLauncherAsync()
    {
        if (_currentEpicSnapshot?.IsLauncherInstalled != true)
        {
            throw new InvalidOperationException(
                "Epic Games Launcher est requis pour une intégration Epic locale vérifiable.");
        }

        if (_currentEpicSnapshot.IsLauncherRunning != true)
        {
            await LaunchEpicLauncherAsync().ConfigureAwait(true);
            await Task.Delay(TimeSpan.FromSeconds(1.5)).ConfigureAwait(true);
        }

        await RefreshEpicAsync().ConfigureAwait(true);
        StatusMessage = _currentEpicSnapshot?.IsLauncherRunning == true
            ? "Epic Games Launcher actif · intégration locale vérifiée. L’identité du compte reste privée dans EGL."
            : "Epic Games Launcher détecté mais son processus n’est pas encore actif.";
    }

    private async Task ConnectFabViaLauncherAsync()
    {
        await ConnectEpicLauncherAsync().ConfigureAwait(true);
        if (_currentEpicSnapshot?.IsLauncherRunning != true)
        {
            return;
        }

        StatusMessage = HasLocalFabIntegration
            ? "Fab local détecté. Dans Epic Games Launcher, ouvrez Fab ou Unreal Engine → Bibliothèque → Fab Library."
            : "Epic Games Launcher actif. Ouvrez Fab dans le Launcher une première fois, puis actualisez le catalogue local.";
    }

    private async Task OpenOfficialUriAsync(Uri uri, string label)
    {
        await _platformLauncher.OpenUriAsync(uri).ConfigureAwait(true);
        StatusMessage = $"{label} ouvert dans le navigateur officiel.";
    }

    private async Task LaunchSelectedFabProjectAsync()
    {
        var project = SelectedFabProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez d’abord un projet cible.");
        await LaunchProjectAsync(project).ConfigureAwait(true);
        StatusMessage =
            $"{project.Name} lancé. Dans l’Editor : Fenêtre → Obtenir du contenu → Fab.";
    }

    private async Task PrepareFabImportAsync()
    {
        CancelPendingFabRollback();
        var project = SelectedFabProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez d’abord un projet cible.");
        IsEpicBusy = true;
        try
        {
            var plan = await _fabProjectContentImport.PrepareAsync(
                    ManualFabContentSource,
                    project.FilePath)
                .ConfigureAwait(true);
            _pendingFabImport = plan;
            var replacement = plan.ConflictCount == 0
                ? "aucun remplacement"
                : $"{plan.ConflictCount:N0} fichier(s) remplacé(s) avec backup";
            var skipped = plan.SkippedFileCount == 0
                ? string.Empty
                : $" · {plan.SkippedFileCount:N0} fichier(s) non pris en charge ignoré(s)";
            PendingFabImportSummary =
                $"{plan.SourceDisplayName} → {project.Name} · {plan.Files.Count:N0} fichier(s), " +
                $"{FormatBytes(plan.TotalBytes)} · {replacement}{skipped}. " +
                $"{plan.CompatibilitySummary}. Fermez le projet cible avant de confirmer.";
            FabProjectImportStatus = "Plan prêt · confirmation obligatoire avant toute copie.";
            OnPropertyChanged(nameof(PendingFabImport));
            OnPropertyChanged(nameof(HasPendingFabImport));
            _confirmFabImportCommand.NotifyCanExecuteChanged();
            ShowSection(PageSection.Epic);
        }
        finally
        {
            IsEpicBusy = false;
        }
    }

    private void CancelPendingFabImport()
    {
        if (_pendingFabImport is null && string.IsNullOrEmpty(PendingFabImportSummary))
        {
            return;
        }

        _pendingFabImport = null;
        PendingFabImportSummary = string.Empty;
        OnPropertyChanged(nameof(PendingFabImport));
        OnPropertyChanged(nameof(HasPendingFabImport));
        _confirmFabImportCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmFabImportAsync()
    {
        var plan = PendingFabImport;
        if (plan is null)
        {
            return;
        }

        IsEpicBusy = true;
        FabProjectImportStatus = "Import transactionnel Fab en cours…";
        FabProjectImportResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Fab",
            $"Importer {plan.SourceDisplayName}",
            [ProjectTaskLink(plan.TargetProjectPath, Path.GetFileNameWithoutExtension(plan.TargetProjectPath))],
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<FabProjectImportProgress>(value =>
                {
                    var ratio = value.Total == 0
                        ? 0
                        : Math.Clamp((double)value.Completed / value.Total, 0, 1);
                    context.Report(ratio, value.Message);
                });
                result = await _fabProjectContentImport.ExecuteAsync(
                        plan,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        CancelPendingFabImport();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            FabProjectImportStatus =
                "Import annulé; le service a tenté de restaurer tous les fichiers modifiés.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            FabProjectImportStatus = $"Échec de l’import Fab : {completed.Error}";
            return;
        }

        var backup = result.BackupDirectory is null
            ? "aucun fichier remplacé"
            : $"backup conservé dans {result.BackupDirectory}";
        FabProjectImportStatus =
            $"Import terminé · {result.ImportedFileCount:N0} fichier(s), " +
            $"{FormatBytes(result.ImportedBytes)} · {backup}";
        StatusMessage = $"Contenu Fab ajouté à {Path.GetFileNameWithoutExtension(plan.TargetProjectPath)}.";
        await LoadFabImportHistoryAsync().ConfigureAwait(true);
        ShowSection(PageSection.Epic);
    }

    private async Task PrepareFabRollbackAsync(FabImportReceipt receipt)
    {
        CancelPendingFabImport();
        IsEpicBusy = true;
        FabProjectImportStatus = "Vérification cryptographique de l’import avant annulation…";
        try
        {
            var plan = await _fabProjectContentImport.PrepareRollbackAsync(receipt)
                .ConfigureAwait(true);
            _pendingFabRollback = plan;
            PendingFabRollbackSummary =
                $"{receipt.SourceDisplayName} → {Path.GetFileNameWithoutExtension(receipt.TargetProjectPath)} · " +
                $"{plan.RestoreFileCount:N0} fichier(s) d’origine restauré(s), " +
                $"{plan.DeleteFileCount:N0} fichier(s) créé(s) supprimé(s). " +
                "Les empreintes des fichiers installés et des backups sont valides. Fermez le projet avant de confirmer.";
            FabProjectImportStatus = "Plan d’annulation prêt · confirmation obligatoire.";
            OnPropertyChanged(nameof(PendingFabRollback));
            OnPropertyChanged(nameof(HasPendingFabRollback));
            _confirmFabRollbackCommand.NotifyCanExecuteChanged();
            ShowSection(PageSection.Epic);
        }
        finally
        {
            IsEpicBusy = false;
        }
    }

    private void CancelPendingFabRollback()
    {
        if (_pendingFabRollback is null && string.IsNullOrEmpty(PendingFabRollbackSummary))
        {
            return;
        }

        _pendingFabRollback = null;
        PendingFabRollbackSummary = string.Empty;
        OnPropertyChanged(nameof(PendingFabRollback));
        OnPropertyChanged(nameof(HasPendingFabRollback));
        _confirmFabRollbackCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmFabRollbackAsync()
    {
        var plan = PendingFabRollback;
        if (plan is null)
        {
            return;
        }

        IsEpicBusy = true;
        FabProjectImportStatus = "Annulation transactionnelle de l’import Fab en cours…";
        FabImportRollbackResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Fab",
            $"Annuler l’import {plan.Receipt.SourceDisplayName}",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<FabProjectImportProgress>(value =>
                {
                    var ratio = value.Total == 0
                        ? 0
                        : Math.Clamp((double)value.Completed / value.Total, 0, 1);
                    context.Report(ratio, value.Message);
                });
                result = await _fabProjectContentImport.ExecuteRollbackAsync(
                        plan,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        CancelPendingFabRollback();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            FabProjectImportStatus =
                "Annulation interrompue; les fichiers importés ont été remis en place.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            FabProjectImportStatus = $"Échec de l’annulation Fab : {completed.Error}";
            return;
        }

        FabProjectImportStatus =
            $"Import annulé · {result.RestoredFileCount:N0} fichier(s) restauré(s), " +
            $"{result.DeletedFileCount:N0} fichier(s) supprimé(s).";
        StatusMessage =
            $"Import {plan.Receipt.SourceDisplayName} annulé dans " +
            $"{Path.GetFileNameWithoutExtension(plan.Receipt.TargetProjectPath)}.";
        await LoadFabImportHistoryAsync().ConfigureAwait(true);
        ShowSection(PageSection.Epic);
    }

    private async Task LaunchSelectedUnityFabProjectAsync()
    {
        var project = SelectedUnityFabProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez d’abord un projet Unity cible.");
        await LaunchLocalProjectAsync(project).ConfigureAwait(true);
        StatusMessage = $"{project.Name} lancé dans Unity; les nouveaux assets seront importés automatiquement.";
    }

    private async Task PrepareUnityFabImportAsync()
    {
        CancelPendingUnityFabRollback();
        var project = SelectedUnityFabProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez d’abord un projet Unity cible.");
        IsEpicBusy = true;
        try
        {
            var plan = await _unityFabAssetImport.PrepareAsync(ManualUnityFabContentSource, project.RootPath).ConfigureAwait(true);
            _pendingUnityFabImport = plan;
            var replacement = plan.ReplacesExistingImport
                ? "la version précédente sera sauvegardée puis remplacée"
                : "nouvel import isolé";
            var skipped = plan.SkippedFileCount == 0
                ? string.Empty
                : $" · {plan.SkippedFileCount:N0} fichier(s) non pris en charge ignoré(s)";
            PendingUnityFabImportSummary =
                $"{plan.SourceDisplayName} → {project.Name} · {plan.Files.Count:N0} fichier(s), " +
                $"{FormatBytes(plan.TotalBytes)} · {replacement}{skipped}. " +
                "Les scripts, shaders et binaires sont interdits. Fermez Unity avant de confirmer.";
            UnityFabImportStatus = "Plan Unity prêt · confirmation obligatoire avant toute copie.";
            OnPropertyChanged(nameof(PendingUnityFabImport));
            OnPropertyChanged(nameof(HasPendingUnityFabImport));
            _confirmUnityFabImportCommand.NotifyCanExecuteChanged();
            ShowSection(PageSection.Epic);
        }
        finally { IsEpicBusy = false; }
    }

    private void CancelPendingUnityFabImport()
    {
        if (_pendingUnityFabImport is null && string.IsNullOrEmpty(PendingUnityFabImportSummary)) return;
        _pendingUnityFabImport = null;
        PendingUnityFabImportSummary = string.Empty;
        OnPropertyChanged(nameof(PendingUnityFabImport));
        OnPropertyChanged(nameof(HasPendingUnityFabImport));
        _confirmUnityFabImportCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmUnityFabImportAsync()
    {
        var plan = PendingUnityFabImport;
        if (plan is null) return;
        IsEpicBusy = true;
        UnityFabImportStatus = "Import transactionnel Unity/Fab en cours…";
        UnityFabAssetImportResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Fab · Unity",
            $"Importer {plan.SourceDisplayName}",
            [ProjectTaskLink(plan.TargetProjectRoot, Path.GetFileName(plan.TargetProjectRoot))],
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<UnityFabAssetImportProgress>(value =>
                    context.Report(value.Total == 0 ? 0 : Math.Clamp((double)value.Completed / value.Total, 0, 1), value.Message));
                result = await _unityFabAssetImport.ExecuteAsync(plan, progress, cancellationToken).ConfigureAwait(false);
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        CancelPendingUnityFabImport();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            UnityFabImportStatus = "Import Unity annulé; la transaction a restauré la destination précédente.";
            return;
        }
        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            UnityFabImportStatus = $"Échec de l’import Unity/Fab : {completed.Error}";
            return;
        }
        UnityFabImportStatus =
            $"Import Unity terminé · {result.ImportedFileCount:N0} fichier(s), {FormatBytes(result.ImportedBytes)} · " +
            (result.Receipt.ReplacedExistingImport ? "backup conservé" : "nouvel import");
        StatusMessage = $"Contenu Fab ajouté à {Path.GetFileName(plan.TargetProjectRoot)}.";
        await LoadFabImportHistoryAsync().ConfigureAwait(true);
        ShowSection(PageSection.Epic);
    }

    private async Task PrepareUnityFabRollbackAsync(UnityFabAssetImportReceipt receipt)
    {
        CancelPendingUnityFabImport();
        IsEpicBusy = true;
        UnityFabImportStatus = "Vérification cryptographique de l’import Unity avant annulation…";
        try
        {
            var plan = await _unityFabAssetImport.PrepareRollbackAsync(receipt).ConfigureAwait(true);
            _pendingUnityFabRollback = plan;
            PendingUnityFabRollbackSummary =
                $"{receipt.SourceDisplayName} → {Path.GetFileName(receipt.TargetProjectRoot)} · " +
                (receipt.ReplacedExistingImport
                    ? "la version précédente sera restaurée"
                    : "le dossier importé sera supprimé") +
                ". Les assets installés et le backup sont valides. Fermez Unity avant de confirmer.";
            UnityFabImportStatus = "Plan d’annulation Unity prêt · confirmation obligatoire.";
            OnPropertyChanged(nameof(PendingUnityFabRollback));
            OnPropertyChanged(nameof(HasPendingUnityFabRollback));
            _confirmUnityFabRollbackCommand.NotifyCanExecuteChanged();
            ShowSection(PageSection.Epic);
        }
        finally { IsEpicBusy = false; }
    }

    private void CancelPendingUnityFabRollback()
    {
        if (_pendingUnityFabRollback is null && string.IsNullOrEmpty(PendingUnityFabRollbackSummary)) return;
        _pendingUnityFabRollback = null;
        PendingUnityFabRollbackSummary = string.Empty;
        OnPropertyChanged(nameof(PendingUnityFabRollback));
        OnPropertyChanged(nameof(HasPendingUnityFabRollback));
        _confirmUnityFabRollbackCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmUnityFabRollbackAsync()
    {
        var plan = PendingUnityFabRollback;
        if (plan is null) return;
        IsEpicBusy = true;
        UnityFabImportStatus = "Annulation transactionnelle Unity/Fab en cours…";
        UnityFabAssetRollbackResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Fab · Unity",
            $"Annuler l’import {plan.Receipt.SourceDisplayName}",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<UnityFabAssetImportProgress>(value =>
                    context.Report(value.Total == 0 ? 0 : Math.Clamp((double)value.Completed / value.Total, 0, 1), value.Message));
                result = await _unityFabAssetImport.ExecuteRollbackAsync(plan, progress, cancellationToken).ConfigureAwait(false);
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        CancelPendingUnityFabRollback();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            UnityFabImportStatus = "Annulation Unity interrompue; l’import a été remis en place.";
            return;
        }
        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            UnityFabImportStatus = $"Échec de l’annulation Unity/Fab : {completed.Error}";
            return;
        }
        UnityFabImportStatus = result.RestoredPreviousImport
            ? "Import Unity annulé · version précédente restaurée."
            : "Import Unity annulé · dossier importé supprimé.";
        StatusMessage = $"Import {plan.Receipt.SourceDisplayName} annulé dans {Path.GetFileName(plan.Receipt.TargetProjectRoot)}.";
        await LoadFabImportHistoryAsync().ConfigureAwait(true);
        ShowSection(PageSection.Epic);
    }

    private async Task LaunchSelectedGodotFabProjectAsync()
    {
        var project = SelectedGodotFabProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez d’abord un projet Godot cible.");
        await LaunchLocalProjectAsync(project).ConfigureAwait(true);
        StatusMessage = $"{project.Name} lancé dans Godot; les nouveaux formats portables seront importés automatiquement.";
    }

    private async Task PrepareGodotFabImportAsync()
    {
        CancelPendingGodotFabRollback();
        var project = SelectedGodotFabProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez d’abord un projet Godot cible.");
        IsEpicBusy = true;
        try
        {
            var plan = await _godotFabAssetImport.PrepareAsync(ManualGodotFabContentSource, project.RootPath).ConfigureAwait(true);
            _pendingGodotFabImport = plan;
            var replacement = plan.ReplacesExistingImport
                ? "la version précédente sera sauvegardée puis remplacée"
                : "nouvel import isolé";
            var skipped = plan.SkippedFileCount == 0
                ? string.Empty
                : $" · {plan.SkippedFileCount:N0} fichier(s) natif(s) ou non pris en charge ignoré(s)";
            PendingGodotFabImportSummary =
                $"{plan.SourceDisplayName} → {project.Name} · {plan.Files.Count:N0} fichier(s), " +
                $"{FormatBytes(plan.TotalBytes)} · {replacement}{skipped}. " +
                "Les scripts, shaders et binaires sont interdits. Fermez Godot avant de confirmer.";
            GodotFabImportStatus = "Plan Godot prêt · confirmation obligatoire avant toute copie.";
            OnPropertyChanged(nameof(PendingGodotFabImport));
            OnPropertyChanged(nameof(HasPendingGodotFabImport));
            _confirmGodotFabImportCommand.NotifyCanExecuteChanged();
            ShowSection(PageSection.Epic);
        }
        finally { IsEpicBusy = false; }
    }

    private void CancelPendingGodotFabImport()
    {
        if (_pendingGodotFabImport is null && string.IsNullOrEmpty(PendingGodotFabImportSummary)) return;
        _pendingGodotFabImport = null;
        PendingGodotFabImportSummary = string.Empty;
        OnPropertyChanged(nameof(PendingGodotFabImport));
        OnPropertyChanged(nameof(HasPendingGodotFabImport));
        _confirmGodotFabImportCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmGodotFabImportAsync()
    {
        var plan = PendingGodotFabImport;
        if (plan is null) return;
        IsEpicBusy = true;
        GodotFabImportStatus = "Import transactionnel Godot/Fab en cours…";
        GodotFabAssetImportResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Fab · Godot",
            $"Importer {plan.SourceDisplayName}",
            [ProjectTaskLink(plan.TargetProjectRoot, Path.GetFileName(plan.TargetProjectRoot))],
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<GodotFabAssetImportProgress>(value =>
                    context.Report(value.Total == 0 ? 0 : Math.Clamp((double)value.Completed / value.Total, 0, 1), value.Message));
                result = await _godotFabAssetImport.ExecuteAsync(plan, progress, cancellationToken).ConfigureAwait(false);
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        CancelPendingGodotFabImport();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            GodotFabImportStatus = "Import Godot annulé; la transaction a restauré la destination précédente.";
            return;
        }
        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            GodotFabImportStatus = $"Échec de l’import Godot/Fab : {completed.Error}";
            return;
        }
        GodotFabImportStatus =
            $"Import Godot terminé · {result.ImportedFileCount:N0} fichier(s), {FormatBytes(result.ImportedBytes)} · " +
            (result.Receipt.ReplacedExistingImport ? "backup conservé" : "nouvel import");
        StatusMessage = $"Contenu Fab ajouté à {Path.GetFileName(plan.TargetProjectRoot)}.";
        await LoadFabImportHistoryAsync().ConfigureAwait(true);
        ShowSection(PageSection.Epic);
    }

    private async Task PrepareGodotFabRollbackAsync(GodotFabAssetImportReceipt receipt)
    {
        CancelPendingGodotFabImport();
        IsEpicBusy = true;
        GodotFabImportStatus = "Vérification cryptographique de l’import Godot avant annulation…";
        try
        {
            var plan = await _godotFabAssetImport.PrepareRollbackAsync(receipt).ConfigureAwait(true);
            _pendingGodotFabRollback = plan;
            PendingGodotFabRollbackSummary =
                $"{receipt.SourceDisplayName} → {Path.GetFileName(receipt.TargetProjectRoot)} · " +
                (receipt.ReplacedExistingImport
                    ? "la version précédente sera restaurée"
                    : "le dossier importé sera supprimé") +
                ". Les assets installés et le backup sont valides. Fermez Godot avant de confirmer.";
            GodotFabImportStatus = "Plan d’annulation Godot prêt · confirmation obligatoire.";
            OnPropertyChanged(nameof(PendingGodotFabRollback));
            OnPropertyChanged(nameof(HasPendingGodotFabRollback));
            _confirmGodotFabRollbackCommand.NotifyCanExecuteChanged();
            ShowSection(PageSection.Epic);
        }
        finally { IsEpicBusy = false; }
    }

    private void CancelPendingGodotFabRollback()
    {
        if (_pendingGodotFabRollback is null && string.IsNullOrEmpty(PendingGodotFabRollbackSummary)) return;
        _pendingGodotFabRollback = null;
        PendingGodotFabRollbackSummary = string.Empty;
        OnPropertyChanged(nameof(PendingGodotFabRollback));
        OnPropertyChanged(nameof(HasPendingGodotFabRollback));
        _confirmGodotFabRollbackCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmGodotFabRollbackAsync()
    {
        var plan = PendingGodotFabRollback;
        if (plan is null) return;
        IsEpicBusy = true;
        GodotFabImportStatus = "Annulation transactionnelle Godot/Fab en cours…";
        GodotFabAssetRollbackResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Fab · Godot",
            $"Annuler l’import {plan.Receipt.SourceDisplayName}",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<GodotFabAssetImportProgress>(value =>
                    context.Report(value.Total == 0 ? 0 : Math.Clamp((double)value.Completed / value.Total, 0, 1), value.Message));
                result = await _godotFabAssetImport.ExecuteRollbackAsync(plan, progress, cancellationToken).ConfigureAwait(false);
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        CancelPendingGodotFabRollback();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            GodotFabImportStatus = "Annulation Godot interrompue; l’import a été remis en place.";
            return;
        }
        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            GodotFabImportStatus = $"Échec de l’annulation Godot/Fab : {completed.Error}";
            return;
        }
        GodotFabImportStatus = result.RestoredPreviousImport
            ? "Import Godot annulé · version précédente restaurée."
            : "Import Godot annulé · dossier importé supprimé.";
        StatusMessage = $"Import {plan.Receipt.SourceDisplayName} annulé dans {Path.GetFileName(plan.Receipt.TargetProjectRoot)}.";
        await LoadFabImportHistoryAsync().ConfigureAwait(true);
        ShowSection(PageSection.Epic);
    }

    private async Task LaunchSelectedCryEngineFabProjectAsync()
    {
        var project = SelectedCryEngineFabProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez d’abord un projet CryEngine cible.");
        await LaunchLocalProjectAsync(project).ConfigureAwait(true);
        StatusMessage = $"{project.Name} lancé dans CryEngine; les nouveaux formats portables seront importés automatiquement.";
    }

    private async Task PrepareCryEngineFabImportAsync()
    {
        CancelPendingCryEngineFabRollback();
        var project = SelectedCryEngineFabProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez d’abord un projet CryEngine cible.");
        IsEpicBusy = true;
        try
        {
            var plan = await _cryEngineFabAssetImport.PrepareAsync(ManualCryEngineFabContentSource, project.RootPath).ConfigureAwait(true);
            _pendingCryEngineFabImport = plan;
            var replacement = plan.ReplacesExistingImport
                ? "la version précédente sera sauvegardée puis remplacée"
                : "nouvel import isolé";
            var skipped = plan.SkippedFileCount == 0
                ? string.Empty
                : $" · {plan.SkippedFileCount:N0} fichier(s) natif(s) ou non pris en charge ignoré(s)";
            PendingCryEngineFabImportSummary =
                $"{plan.SourceDisplayName} → {project.Name} · {plan.Files.Count:N0} fichier(s), " +
                $"{FormatBytes(plan.TotalBytes)} · {replacement}{skipped}. " +
                "Les scripts, shaders et binaires sont interdits. Fermez CryEngine avant de confirmer.";
            CryEngineFabImportStatus = "Plan CryEngine prêt · confirmation obligatoire avant toute copie.";
            OnPropertyChanged(nameof(PendingCryEngineFabImport));
            OnPropertyChanged(nameof(HasPendingCryEngineFabImport));
            _confirmCryEngineFabImportCommand.NotifyCanExecuteChanged();
            ShowSection(PageSection.Epic);
        }
        finally { IsEpicBusy = false; }
    }

    private void CancelPendingCryEngineFabImport()
    {
        if (_pendingCryEngineFabImport is null && string.IsNullOrEmpty(PendingCryEngineFabImportSummary)) return;
        _pendingCryEngineFabImport = null;
        PendingCryEngineFabImportSummary = string.Empty;
        OnPropertyChanged(nameof(PendingCryEngineFabImport));
        OnPropertyChanged(nameof(HasPendingCryEngineFabImport));
        _confirmCryEngineFabImportCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmCryEngineFabImportAsync()
    {
        var plan = PendingCryEngineFabImport;
        if (plan is null) return;
        IsEpicBusy = true;
        CryEngineFabImportStatus = "Import transactionnel CryEngine/Fab en cours…";
        CryEngineFabAssetImportResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Fab · CryEngine",
            $"Importer {plan.SourceDisplayName}",
            [ProjectTaskLink(plan.TargetProjectRoot, Path.GetFileName(plan.TargetProjectRoot))],
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<CryEngineFabAssetImportProgress>(value =>
                    context.Report(value.Total == 0 ? 0 : Math.Clamp((double)value.Completed / value.Total, 0, 1), value.Message));
                result = await _cryEngineFabAssetImport.ExecuteAsync(plan, progress, cancellationToken).ConfigureAwait(false);
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        CancelPendingCryEngineFabImport();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            CryEngineFabImportStatus = "Import CryEngine annulé; la transaction a restauré la destination précédente.";
            return;
        }
        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            CryEngineFabImportStatus = $"Échec de l’import CryEngine/Fab : {completed.Error}";
            return;
        }
        CryEngineFabImportStatus =
            $"Import CryEngine terminé · {result.ImportedFileCount:N0} fichier(s), {FormatBytes(result.ImportedBytes)} · " +
            (result.Receipt.ReplacedExistingImport ? "backup conservé" : "nouvel import");
        StatusMessage = $"Contenu Fab ajouté à {Path.GetFileName(plan.TargetProjectRoot)}.";
        await LoadFabImportHistoryAsync().ConfigureAwait(true);
        ShowSection(PageSection.Epic);
    }

    private async Task PrepareCryEngineFabRollbackAsync(CryEngineFabAssetImportReceipt receipt)
    {
        CancelPendingCryEngineFabImport();
        IsEpicBusy = true;
        CryEngineFabImportStatus = "Vérification cryptographique de l’import CryEngine avant annulation…";
        try
        {
            var plan = await _cryEngineFabAssetImport.PrepareRollbackAsync(receipt).ConfigureAwait(true);
            _pendingCryEngineFabRollback = plan;
            PendingCryEngineFabRollbackSummary =
                $"{receipt.SourceDisplayName} → {Path.GetFileName(receipt.TargetProjectRoot)} · " +
                (receipt.ReplacedExistingImport
                    ? "la version précédente sera restaurée"
                    : "le dossier importé sera supprimé") +
                ". Les assets installés et le backup sont valides. Fermez CryEngine avant de confirmer.";
            CryEngineFabImportStatus = "Plan d’annulation CryEngine prêt · confirmation obligatoire.";
            OnPropertyChanged(nameof(PendingCryEngineFabRollback));
            OnPropertyChanged(nameof(HasPendingCryEngineFabRollback));
            _confirmCryEngineFabRollbackCommand.NotifyCanExecuteChanged();
            ShowSection(PageSection.Epic);
        }
        finally { IsEpicBusy = false; }
    }

    private void CancelPendingCryEngineFabRollback()
    {
        if (_pendingCryEngineFabRollback is null && string.IsNullOrEmpty(PendingCryEngineFabRollbackSummary)) return;
        _pendingCryEngineFabRollback = null;
        PendingCryEngineFabRollbackSummary = string.Empty;
        OnPropertyChanged(nameof(PendingCryEngineFabRollback));
        OnPropertyChanged(nameof(HasPendingCryEngineFabRollback));
        _confirmCryEngineFabRollbackCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmCryEngineFabRollbackAsync()
    {
        var plan = PendingCryEngineFabRollback;
        if (plan is null) return;
        IsEpicBusy = true;
        CryEngineFabImportStatus = "Annulation transactionnelle CryEngine/Fab en cours…";
        CryEngineFabAssetRollbackResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Fab · CryEngine",
            $"Annuler l’import {plan.Receipt.SourceDisplayName}",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<CryEngineFabAssetImportProgress>(value =>
                    context.Report(value.Total == 0 ? 0 : Math.Clamp((double)value.Completed / value.Total, 0, 1), value.Message));
                result = await _cryEngineFabAssetImport.ExecuteRollbackAsync(plan, progress, cancellationToken).ConfigureAwait(false);
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        CancelPendingCryEngineFabRollback();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            CryEngineFabImportStatus = "Annulation CryEngine interrompue; l’import a été remis en place.";
            return;
        }
        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            CryEngineFabImportStatus = $"Échec de l’annulation CryEngine/Fab : {completed.Error}";
            return;
        }
        CryEngineFabImportStatus = result.RestoredPreviousImport
            ? "Import CryEngine annulé · version précédente restaurée."
            : "Import CryEngine annulé · dossier importé supprimé.";
        StatusMessage = $"Import {plan.Receipt.SourceDisplayName} annulé dans {Path.GetFileName(plan.Receipt.TargetProjectRoot)}.";
        await LoadFabImportHistoryAsync().ConfigureAwait(true);
        ShowSection(PageSection.Epic);
    }

    private async Task ToggleEngineImporterAsync()
    {
        IsEngineImporterVisible = !IsEngineImporterVisible;
        if (IsEngineImporterVisible)
        {
            await RefreshSourceEngineVersionsAsync().ConfigureAwait(true);
            await RefreshEpicEngineReleasesAsync().ConfigureAwait(true);
        }
    }

    private async Task RefreshEpicEngineReleasesAsync()
    {
        IsEpicBusy = true;
        EngineBinaryDownloadSummary = "Lecture du catalogue Unreal officiel…";
        try
        {
            var releases = await _epicBinaryEngineBackend.GetAvailableReleasesAsync()
                .ConfigureAwait(true);
            var installedOfficialVersions = _currentEngines
                .Where(engine => engine.Type == EngineInstallationType.EpicBinary)
                .Select(engine => $"{engine.Version.Major}.{engine.Version.Minor}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var downloadable = releases
                .Where(release => !installedOfficialVersions.Contains(NormalizeEngineVersion(release.Version)))
                .ToArray();
            var selectedApp = SelectedEpicEngineRelease?.AppName;
            AvailableEpicEngineReleases.Clear();
            foreach (var release in downloadable)
            {
                AvailableEpicEngineReleases.Add(release);
            }
            SelectedEpicEngineRelease = AvailableEpicEngineReleases.FirstOrDefault(item =>
                string.Equals(item.AppName, selectedApp, StringComparison.OrdinalIgnoreCase))
                ?? AvailableEpicEngineReleases.FirstOrDefault();
            EngineBinaryDownloadSummary = downloadable.Length == 0
                ? "Aucune version binaire Unreal n’a été retournée par Epic."
                : $"{downloadable.Length:N0} version(s) à télécharger · {installedOfficialVersions.Count:N0} déjà installée(s) masquée(s).";
            AppendRawLog("INFO", "Engines/Epic", $"Catalogue binaire officiel : {releases.Count:N0} version(s), {downloadable.Length:N0} proposée(s).");
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException)
        {
            AvailableEpicEngineReleases.Clear();
            SelectedEpicEngineRelease = null;
            EngineBinaryDownloadSummary = exception.Message;
            AppendRawLog("WARN", "Engines/Epic", exception.Message);
        }
        finally
        {
            IsEpicBusy = false;
        }
    }

    private async Task DownloadEpicEngineAsync()
    {
        var release = SelectedEpicEngineRelease
            ?? throw new InvalidOperationException("Sélectionnez une version Unreal officielle.");
        if (string.IsNullOrWhiteSpace(EpicEngineTargetDirectory))
        {
            throw new InvalidOperationException("Sélectionnez un dossier cible vide.");
        }

        IsEpicBusy = true;
        IsEpicEngineDownloadActive = true;
        EpicEngineDownloadProgress = 0;
        EpicBinaryEngineDownloadResult? result = null;
        var targetDirectory = EpicEngineTargetDirectory;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Moteurs · Epic",
            $"Télécharger {release.DisplayName}",
            [EngineTaskLink(targetDirectory, release.DisplayName)],
            async (context, cancellationToken) =>
            {
                var transfer = new CyEngineHub.Core.Marketplaces.DownloadProgressText();
                var progress = new InlineProgress<EpicBinaryEngineDownloadProgress>(value =>
                {
                    var message = transfer.Format(value.Message, value.DownloadedBytes, value.TotalBytes);
                    context.Report(value.Fraction, message);
                    Dispatcher.UIThread.Post(() =>
                    {
                        EpicEngineDownloadProgress = value.Fraction ?? EpicEngineDownloadProgress;
                        EngineBinaryDownloadSummary = message;
                    });
                });
                result = await _epicBinaryEngineBackend.DownloadAsync(
                        release,
                        targetDirectory,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        IsEpicEngineDownloadActive = false;
        if (completed.Status == HubTaskStatus.Canceled)
        {
            EngineBinaryDownloadSummary =
                "Téléchargement annulé · le cache de chunks validés est conservé pour la reprise.";
            return;
        }
        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            EngineBinaryDownloadSummary = $"Échec du téléchargement Unreal : {completed.Error}";
            return;
        }

        EpicEngineDownloadProgress = 1;
        await AddUniquePathAsync(EngineRootsSetting, result.DestinationDirectory).ConfigureAwait(true);
        await SetWorkspaceEngineHiddenAsync(result.DestinationDirectory, hidden: false).ConfigureAwait(true);
        EngineBinaryDownloadSummary =
            $"{release.DisplayName} installé · build {result.BuildVersion} · {result.FileCount:N0} fichier(s).";
        StatusMessage = $"{release.DisplayName} ajouté aux moteurs de CyEngineHub.";
        AppendRawLog(
            "INFO",
            "Engines/Epic",
            $"{release.DisplayName} téléchargé vers {result.DestinationDirectory} · build {result.BuildVersion}.");
        IsEngineImporterVisible = false;
        await RefreshCatalogAsync().ConfigureAwait(true);
    }

    private async Task RefreshSourceEngineVersionsAsync()
    {
        SourceEngineVersionsStatus = "Lecture des branches et versions du dépôt…";
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "release",
        };

        foreach (var engine in _currentEngines)
        {
            var stableTag = $"{engine.Version}-release";
            discovered.Add(stableTag);
        }

        try
        {
            var result = await _processRunner.RunAsync(new ProcessRequest
            {
                FileName = "git",
                Arguments = ["ls-remote", "--refs", "--heads", "--tags", SourceEngineRepository],
                EnvironmentVariables = new Dictionary<string, string?>
                {
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["LC_ALL"] = "C",
                },
                MaxCapturedLines = 1_000,
            }).ConfigureAwait(true);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    result.CapturedOutput.Count > 0
                        ? result.CapturedOutput[^1].Text
                        : "Git n’a pas pu lire le dépôt.");
            }

            foreach (var line in result.CapturedOutput)
            {
                var reference = line.Text
                    .Split(['\t', ' '], StringSplitOptions.RemoveEmptyEntries)
                    .LastOrDefault();
                if (reference is null)
                {
                    continue;
                }

                const string tagPrefix = "refs/tags/";
                const string branchPrefix = "refs/heads/";
                var candidate = reference.StartsWith(tagPrefix, StringComparison.Ordinal)
                    ? reference[tagPrefix.Length..]
                    : reference.StartsWith(branchPrefix, StringComparison.Ordinal)
                        ? reference[branchPrefix.Length..]
                        : string.Empty;
                if (candidate.Equals("release", StringComparison.OrdinalIgnoreCase) ||
                    candidate.Equals("ue5-main", StringComparison.OrdinalIgnoreCase) ||
                    candidate.EndsWith("-release", StringComparison.OrdinalIgnoreCase))
                {
                    discovered.Add(candidate);
                }
            }

            SourceEngineVersionsStatus =
                $"{discovered.Count:N0} branche(s) ou version(s) officielle(s) disponibles via GitHub.";
        }
        catch (Exception exception)
        {
            SourceEngineVersionsStatus =
                "Le dépôt officiel n’est pas lisible sans l’accès GitHub Epic. " +
                $"Les versions locales restent proposées. Détail : {exception.Message}";
            AppendRawLog("WARN", "Engines/Versions", exception.Message);
        }

        var previous = SourceEngineBranch;
        AvailableSourceEngineVersions.Clear();
        foreach (var version in discovered
                     .OrderBy(value => value.Equals("release", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenByDescending(value => value, StringComparer.OrdinalIgnoreCase)
                     .Take(60))
        {
            AvailableSourceEngineVersions.Add(version);
        }

        SelectedSourceEngineVersion = AvailableSourceEngineVersions.FirstOrDefault(
            value => value.Equals(previous, StringComparison.OrdinalIgnoreCase))
            ?? AvailableSourceEngineVersions.FirstOrDefault();
    }

    private Task PrepareSourceEngineDownloadAsync()
    {
        if (!Uri.TryCreate(SourceEngineRepository, UriKind.Absolute, out var repository))
        {
            throw new ArgumentException("L’URL du dépôt Unreal est invalide.");
        }

        var plan = _unrealSourceDownload.Prepare(new UnrealSourceDownloadRequest(
            repository,
            SourceEngineBranch,
            SourceEngineTargetDirectory,
            SourceEngineShallowClone));
        _pendingSourceEngineDownload = plan;
        var history = plan.Download.ShallowClone
            ? "clone léger, historique limité à 1 commit"
            : "historique complet";
        PendingSourceEngineDownloadSummary =
            $"{plan.Download.RepositoryUri.Host}/{plan.Download.RepositoryUri.Segments[^2].TrimEnd('/')} · " +
            $"branche {plan.Download.Branch} · {history} → {plan.Download.TargetDirectory}. " +
            "L’accès GitHub Epic doit déjà être autorisé; Setup.bat ne sera pas lancé automatiquement.";
        SourceEngineDownloadStatus = "Plan de clone prêt · confirmation obligatoire.";
        OnPropertyChanged(nameof(PendingSourceEngineDownload));
        OnPropertyChanged(nameof(HasPendingSourceEngineDownload));
        _confirmSourceEngineDownloadCommand.NotifyCanExecuteChanged();
        ShowSection(PageSection.Epic);
        return Task.CompletedTask;
    }

    private void CancelPendingSourceEngineDownload()
    {
        if (_pendingSourceEngineDownload is null &&
            string.IsNullOrEmpty(PendingSourceEngineDownloadSummary))
        {
            return;
        }

        _pendingSourceEngineDownload = null;
        PendingSourceEngineDownloadSummary = string.Empty;
        OnPropertyChanged(nameof(PendingSourceEngineDownload));
        OnPropertyChanged(nameof(HasPendingSourceEngineDownload));
        _confirmSourceEngineDownloadCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmSourceEngineDownloadAsync()
    {
        var plan = PendingSourceEngineDownload;
        if (plan is null)
        {
            return;
        }

        IsEpicBusy = true;
        var title = $"Clone Unreal · {plan.Download.Branch}";
        await using var persistentLog = GitOperationLogWriter.Create(
            _gitOperationLogsDirectory,
            title);
        SourceEngineDownloadStatus = $"Téléchargement source en cours · log : {persistentLog.FilePath}";
        UnrealSourceDownloadResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Unreal source",
            title,
            [EngineTaskLink(plan.Download.TargetDirectory, plan.Download.Branch)],
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<UnrealSourceDownloadProgress>(value =>
                {
                    persistentLog.Write(new ProcessOutputLine(
                        DateTimeOffset.Now,
                        ProcessOutputChannel.StandardOutput,
                        value.Message));
                    context.Report(value.Progress, value.Message);
                });
                result = await _unrealSourceDownload.ExecuteAsync(
                        plan,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsEpicBusy = false;
        CancelPendingSourceEngineDownload();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            SourceEngineDownloadStatus =
                $"Clone annulé; le dossier partiel est conservé · log : {persistentLog.FilePath}";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            SourceEngineDownloadStatus =
                $"Échec du clone : {completed.Error} · log : {persistentLog.FilePath}";
            return;
        }

        await AddUniquePathAsync(EngineRootsSetting, result.EngineRoot).ConfigureAwait(true);
        await SetWorkspaceEngineHiddenAsync(result.EngineRoot, hidden: false).ConfigureAwait(true);
        SourceEngineDownloadStatus =
            $"Sources téléchargées en {result.Duration:g} · lancez maintenant Setup depuis la page Moteurs · " +
            $"log : {persistentLog.FilePath}";
        StatusMessage = "Moteur source téléchargé et ajouté aux chemins de découverte.";
        await RefreshCatalogAsync().ConfigureAwait(true);
        IsEngineImporterVisible = false;
        ShowSection(PageSection.Engines);
    }

    private bool IsProviderEnabled(string providerId) =>
        !_workspacePreferences.EnabledProviders.TryGetValue(providerId, out var enabled) || enabled;

    private static string ProviderIdForFamily(EngineFamily family) => family switch
    {
        EngineFamily.Unreal => "unreal",
        EngineFamily.Unity => "unity",
        EngineFamily.Godot => "godot",
        EngineFamily.CryEngine => "cryengine",
        _ => family.ToString().ToLowerInvariant(),
    };

    private void RebuildEngineModuleCards()
    {
        EngineModules.Clear();
        foreach (var module in _engineModules.Modules.OrderBy(item => item.Descriptor.Family))
        {
            EngineModules.Add(new EngineModuleCardViewModel(
                module.Descriptor,
                IsProviderEnabled(module.Descriptor.Id),
                SetProviderEnabledAsync,
                ShowError));
        }

        OnPropertyChanged(nameof(ActiveEngineModuleCount));
    }

    private void RebuildConnectionModuleCards()
    {
        ConnectionModules.Clear();
        var descriptors = new[]
        {
            ("epic", "Epic", "Compte Epic direct et moteurs Unreal officiels"),
            ("fab", "Fab", "Catalogue My Library et téléchargements Fab"),
            ("git", "Git", "Dépôts, remotes et gestionnaire d’identifiants local"),
            ("unity", "Unity", "Connexion Unity et source Unity Asset Store"),
            ("quixel", "Megascans", "Contenus Megascans du compte Epic et bibliothèque Bridge locale"),
        };
        foreach (var descriptor in descriptors)
        {
            ConnectionModules.Add(new ConnectionModuleCardViewModel(
                descriptor.Item1,
                descriptor.Item2,
                descriptor.Item3,
                IsConnectionModuleEnabled(descriptor.Item1),
                SetConnectionModuleEnabledAsync,
                ShowError));
        }
    }

    private bool IsConnectionModuleEnabled(string id) =>
        !_connectionModules.TryGetValue(id, out var enabled) || enabled;

    private async Task SetConnectionModuleEnabledAsync(string id, bool enabled)
    {
        _connectionModules[id] = enabled;
        await _settings.SetAsync(
                ConnectionModulesSetting,
                JsonSerializer.Serialize(_connectionModules))
            .ConfigureAwait(true);
        OnPropertyChanged(nameof(IsEpicConnectionEnabled));
        OnPropertyChanged(nameof(IsFabConnectionEnabled));
        OnPropertyChanged(nameof(IsGitConnectionEnabled));
        OnPropertyChanged(nameof(IsUnityConnectionEnabled));
        OnPropertyChanged(nameof(IsQuixelConnectionEnabled));
        OnPropertyChanged(nameof(IsUnityLibrarySourceAvailable));
        OnPropertyChanged(nameof(IsQuixelLibrarySourceAvailable));
        UpdateMarketplaceAvailability();
        StatusMessage = enabled
            ? $"Connexion {id} activée."
            : $"Connexion {id} masquée; aucune donnée locale n’a été supprimée.";
    }

    private void RebuildEngineTabs()
    {
        var engineFamily = SelectedEngineTab?.Family ?? EngineFamily.Unreal;
        var projectFamily = SelectedProjectTab?.Family ?? EngineFamily.Unreal;
        var pluginFamily = SelectedPluginTab?.Family ?? EngineFamily.Unreal;
        EngineTabs.Clear();
        foreach (var module in _engineModules.Modules
                     .Where(item =>
                         item.Descriptor.Status == EngineModuleStatus.Active &&
                         IsProviderEnabled(item.Descriptor.Id))
                     .OrderBy(item => item.Descriptor.Family))
        {
            EngineTabs.Add(new EngineFamilyOptionViewModel(
                module.Descriptor.Family,
                module.Descriptor.Id,
                module.Descriptor.DisplayName));
        }

        SelectedEngineTab = EngineTabs.FirstOrDefault(item => item.Family == engineFamily)
                            ?? EngineTabs.FirstOrDefault();
        SelectedProjectTab = EngineTabs.FirstOrDefault(item => item.Family == projectFamily)
                             ?? EngineTabs.FirstOrDefault();
        SelectedPluginTab = EngineTabs.FirstOrDefault(item => item.Family == pluginFamily)
                            ?? EngineTabs.FirstOrDefault();
        ApplyEngineFamilyFilters();
    }

    private string _selectedEngineSort = "Version · récent";
    public IReadOnlyList<string> EngineSortOptions { get; } =
        ["Version · récent", "Version · ancien", "Nom · A → Z", "Dossier"];
    public string SelectedEngineSort
    {
        get => _selectedEngineSort;
        set { if (SetProperty(ref _selectedEngineSort, value)) ApplyEngineFamilyFilters(); }
    }

    private IEnumerable<T> SortEngines<T>(IEnumerable<T> items,
        Func<T, string> version, Func<T, string> name, Func<T, string> folder) =>
        SelectedEngineSort switch
        {
            "Version · ancien" => items.OrderBy(item => ParseSortableEngineVersion(version(item))).ThenBy(name, StringComparer.OrdinalIgnoreCase),
            "Nom · A → Z" => items.OrderBy(name, StringComparer.OrdinalIgnoreCase),
            "Dossier" => items.OrderBy(folder, StringComparer.OrdinalIgnoreCase).ThenByDescending(item => ParseSortableEngineVersion(version(item))),
            _ => items.OrderByDescending(item => ParseSortableEngineVersion(version(item))).ThenBy(name, StringComparer.OrdinalIgnoreCase),
        };

    private static Version ParseSortableEngineVersion(string value)
    {
        var numeric = new string(value.Trim().Replace("UE_", "", StringComparison.OrdinalIgnoreCase)
            .TakeWhile(character => char.IsDigit(character) || character == '.').ToArray()).Trim('.');
        return Version.TryParse(numeric, out var parsed) ? parsed : new Version();
    }

    private void ApplyEngineFamilyFilters()
    {
        ReplaceFilteredCollection(
            VisibleEngines,
            SelectedEngineTab?.Family == EngineFamily.Unreal
                ? SortEngines(Engines, item => item.VersionLabel, item => item.Organization.DisplayName, item => item.Organization.FolderPath)
                : []);
        ReplaceFilteredCollection(
            VisibleLocalEngineInstallations,
            SortEngines(LocalEngineInstallations.Where(item => item.Installation.Family == SelectedEngineTab?.Family),
                item => item.Version, item => item.Organization.DisplayName, item => item.Organization.FolderPath));
        ReplaceFilteredCollection(
            VisibleProjects,
            SelectedProjectTab?.Family == EngineFamily.Unreal
                ? Projects.Where(item => MatchesProjectSearch(
                        item.Organization.DisplayName,
                        item.Path))
                    .OrderBy(item => item.Organization.FolderPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Organization.DisplayName, StringComparer.OrdinalIgnoreCase)
                : []);
        ReplaceFilteredCollection(
            VisibleLocalEngineProjects,
            LocalEngineProjects
                .Where(item => item.Project.Family == SelectedProjectTab?.Family)
                .Where(item => MatchesProjectSearch(item.Organization.DisplayName, item.Path))
                .OrderBy(item => item.Organization.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Organization.DisplayName, StringComparer.OrdinalIgnoreCase));
        ReplaceFilteredCollection(
            VisiblePlugins,
            SelectedPluginTab?.Family == EngineFamily.Unreal
                ? Plugins.OrderBy(item => item.Organization.FolderPath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.Organization.DisplayName, StringComparer.OrdinalIgnoreCase)
                : []);
        ReplaceFilteredCollection(
            VisibleLocalEnginePackages,
            LocalEnginePackages
                .Where(item => item.Package.Family == SelectedPluginTab?.Family)
                .OrderBy(item => item.Organization.FolderPath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Organization.DisplayName, StringComparer.OrdinalIgnoreCase));

        OnPropertyChanged(nameof(IsUnrealEngineTabSelected));
        OnPropertyChanged(nameof(IsLocalEngineTabSelected));
        OnPropertyChanged(nameof(IsUnrealEngineCardViewVisible));
        OnPropertyChanged(nameof(IsLocalEngineCardViewVisible));
        OnPropertyChanged(nameof(IsUnrealEngineListViewVisible));
        OnPropertyChanged(nameof(IsLocalEngineListViewVisible));
        OnPropertyChanged(nameof(IsUnrealProjectTabSelected));
        OnPropertyChanged(nameof(IsLocalProjectTabSelected));
        OnPropertyChanged(nameof(IsUnrealProjectCardViewVisible));
        OnPropertyChanged(nameof(IsLocalProjectCardViewVisible));
        OnPropertyChanged(nameof(IsUnrealProjectListViewVisible));
        OnPropertyChanged(nameof(IsLocalProjectListViewVisible));
        OnPropertyChanged(nameof(IsUnrealPluginTabSelected));
        OnPropertyChanged(nameof(IsLocalPackageTabSelected));
        OnPropertyChanged(nameof(VisibleProjectCount));
    }

    private bool MatchesProjectSearch(string name, string path) =>
        string.IsNullOrWhiteSpace(ProjectSearchQuery) ||
        name.Contains(ProjectSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase) ||
        path.Contains(ProjectSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void ReplaceFilteredCollection<T>(
        ObservableCollection<T> target,
        IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values)
        {
            target.Add(value);
        }
    }

    private WorkspaceItemPreference? FindWorkspacePreference(WorkspaceItemKind kind, string key) =>
        _workspacePreferences.Items.FirstOrDefault(item =>
            item.Kind == kind && SameWorkspaceKey(item.Key, key));

    private LibraryItemOrganizationViewModel CreateOrganization(
        WorkspaceItemKind kind,
        string key,
        string originalName,
        bool canHide = false) =>
        new(
            kind,
            key,
            originalName,
            FindWorkspacePreference(kind, key),
            SaveWorkspaceItemAsync,
            canHide ? HideWorkspaceItemAsync : null,
            ShowError);

    private async Task SaveWorkspaceItemAsync(
        WorkspaceItemKind kind,
        string key,
        string alias,
        string folderPath)
    {
        var existing = FindWorkspacePreference(kind, key);
        var updated = new WorkspaceItemPreference(
            kind,
            key,
            WorkspacePreferencesService.NormalizeAlias(alias),
            WorkspacePreferencesService.NormalizeFolderPath(folderPath),
            existing?.IsHidden ?? false,
            existing?.IsExplicit ?? false);
        _workspacePreferences = _workspacePreferences with
        {
            Items = _workspacePreferences.Items
                .Where(item => item.Kind != kind || !SameWorkspaceKey(item.Key, key))
                .Append(updated)
                .ToArray(),
        };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
        ApplyEngineFamilyFilters();
        if (kind == WorkspaceItemKind.Asset)
        {
            await RefreshRemoteFabViewNowAsync().ConfigureAwait(true);
        }
        StatusMessage = "Organisation de la bibliothèque enregistrée.";
    }

    private async Task HideWorkspaceItemAsync(WorkspaceItemKind kind, string key)
    {
        var existing = FindWorkspacePreference(kind, key);
        var updated = new WorkspaceItemPreference(
            kind,
            key,
            existing?.Alias ?? string.Empty,
            existing?.FolderPath ?? string.Empty,
            IsHidden: true,
            IsExplicit: existing?.IsExplicit ?? false);
        _workspacePreferences = _workspacePreferences with
        {
            Items = _workspacePreferences.Items
                .Where(item => item.Kind != kind || !SameWorkspaceKey(item.Key, key))
                .Append(updated)
                .ToArray(),
        };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
        RebuildCatalogCardsFromCurrentSnapshots();
        StatusMessage = "Projet retiré de la bibliothèque CyEngineHub; aucun fichier n’a été supprimé.";
    }

    private async Task SetProviderEnabledAsync(string providerId, bool enabled)
    {
        var providers = _workspacePreferences.EnabledProviders
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        providers[providerId] = enabled;
        _workspacePreferences = _workspacePreferences with { EnabledProviders = providers };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
        RebuildEngineModuleCards();
        RebuildEngineTabs();
        OnPropertyChanged(nameof(UnityStoreConnectionStatus));
        OnPropertyChanged(nameof(UnityConnectionIndicator));
        OnPropertyChanged(nameof(IsUnityLibrarySourceAvailable));
        if (enabled)
        {
            await RefreshCatalogAsync().ConfigureAwait(true);
        }
        else
        {
            ApplyEngineFamilyFilters();
        }

        StatusMessage = enabled
            ? $"Provider {providerId} activé."
            : $"Provider {providerId} désactivé; ses données locales restent intactes.";
    }

    private async Task CreateProjectAsync()
    {
        RecomposeProjectConfiguration();
        if (IsUnrealProjectCreation && !CanCreateProjectConfiguration)
            throw new InvalidOperationException(IsProjectCreationPluginLoading
                ? "Attendez la fin de l’analyse des plugins du moteur."
                : "Vérifiez le moteur sélectionné, les dépendances des plugins et les erreurs dans l’onglet Configuration INI.");
        if (IsUnrealProjectCreation && !string.IsNullOrWhiteSpace(ProjectCompositionIssues))
            throw new InvalidOperationException(ProjectCompositionIssues);
        foreach (var document in ProjectCreationIniDocuments)
        {
            document.Validate();
            if (document.Error.Length > 0) throw new InvalidOperationException(document.Error);
        }
        var preset = SelectedProjectCreationPreset?.Preset
            ?? throw new InvalidOperationException("Sélectionnez un preset de création.");
        var plugins = SplitProjectCreationPlugins(NewProjectPlugins);
        var disabledPlugins = SplitProjectCreationPlugins(NewProjectDisabledPlugins);
        var selectedCollections = ProjectCreationCollections
            .Where(item => item.IsSelected)
            .Select(item => item.Name)
            .ToArray();
        var starterAssets = _remoteFabCatalog
            .Where(asset => _fabCollections.TryGetValue(asset.AssetId, out var collections) &&
                            collections.Any(collection => selectedCollections.Contains(
                                collection,
                                StringComparer.OrdinalIgnoreCase)))
            .DistinctBy(asset => asset.AssetId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var starterLocalPacks = FabLocalContentPacks
            .Where(card => _fabCollections.TryGetValue(card.Pack.Id, out var collections) &&
                           collections.Any(collection => selectedCollections.Contains(
                               collection,
                               StringComparer.OrdinalIgnoreCase)))
            .Select(card => card.Pack)
            .Where(pack => pack.Kind is FabLocalContentKind.Unreal or FabLocalContentKind.Generic)
            .DistinctBy(pack => pack.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var postCreationScripts = ProjectPostCreationScripts
            .Where(item => item.IsSelected)
            .Select(item => item.CreateSelection())
            .ToArray();
        StatusMessage = $"Création du projet {NewProjectName}…";
        var result = await _projectCreation.CreateAsync(new ProjectCreationRequest(
                preset,
                NewProjectName.Trim(),
                NewProjectParentDirectory,
                NewProjectEngineAssociation,
                plugins,
                _effectiveProjectProfile,
                disabledPlugins,
                selectedCollections,
                starterAssets.Select(asset => asset.AssetId)
                    .Concat(starterLocalPacks.Select(pack => pack.Id))
                    .ToArray(),
                postCreationScripts,
                SelectedProjectCreationEngine?.Path,
                IniFileOverrides: IsUnrealProjectCreation
                    ? ProjectCreationIniDocuments.ToDictionary(item => item.FileName, item => item.Content, StringComparer.OrdinalIgnoreCase)
                    : null))
            .ConfigureAwait(true);

        ManualProjectRoot = result.ProjectRoot;
        await AddProjectRootAsync().ConfigureAwait(true);
        var createdProject = Projects.FirstOrDefault(item =>
            string.Equals(
                Path.GetFullPath(item.Project.RootPath),
                Path.GetFullPath(result.ProjectRoot),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal));
        if (createdProject is not null &&
            (starterAssets.Length > 0 || starterLocalPacks.Length > 0))
        {
            QueueProjectStarterCollections(
                createdProject,
                starterAssets,
                starterLocalPacks,
                selectedCollections);
        }
        IsProjectCreatorVisible = false;
        NewProjectName = string.Empty;
        StatusMessage =
            starterAssets.Length + starterLocalPacks.Length == 0
                ? $"Projet {Path.GetFileName(result.ProjectRoot)} créé avec le preset {result.Preset.DisplayName}."
                : $"Projet créé · installation de {starterAssets.Length + starterLocalPacks.Length:N0} package(s) de collection en arrière-plan.";
    }

    private static string[] SplitProjectCreationPlugins(string value) => value.Split(
        [',', ';', '\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private void RebuildProjectCreationConfigurationOptions(string? preferredId = null, string? preferredManagedId = null)
    {
        preferredId ??= SelectedProjectCreationConfiguration?.Profile.Id;
        preferredManagedId ??= SelectedManagedProjectProfile?.Profile.Id;
        var previousOptions = ProjectCreationConfigurations.ToDictionary(option => option.Profile.Id, StringComparer.OrdinalIgnoreCase);
        var profiles = _projectCreation.ConfigurationProfiles
            .Concat(_customProjectCreationProfiles)
            .Where(profile => profile.Engine == EngineFamily.Unreal)
            .OrderBy(profile => profile.IsBuiltIn ? 0 : 1)
            .ThenBy(profile => profile.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        _isRebuildingProjectProfileOptions = true;
        try
        {
            ProjectCreationConfigurations.Clear();
            foreach (var profile in profiles)
            {
                ProjectCreationConfigurations.Add(
                    previousOptions.TryGetValue(profile.Id, out var existing) && existing.Profile == profile
                        ? existing : new ProjectCreationConfigurationOptionViewModel(profile));
            }
        }
        finally { _isRebuildingProjectProfileOptions = false; }

        SelectedProjectCreationConfiguration = ProjectCreationConfigurations.FirstOrDefault(item =>
                                                   string.Equals(
                                                       item.Profile.Id,
                                                       preferredId,
                                                       StringComparison.OrdinalIgnoreCase))
                                               ?? ProjectCreationConfigurations.FirstOrDefault(item =>
                                                   string.Equals(
                                                       item.Profile.Id,
                                                       "unreal-standard",
                                                       StringComparison.OrdinalIgnoreCase))
                                               ?? ProjectCreationConfigurations.FirstOrDefault();
        SelectedManagedProjectProfile = ProjectCreationConfigurations.FirstOrDefault(option =>
            string.Equals(option.Profile.Id, preferredManagedId, StringComparison.OrdinalIgnoreCase)) ??
            SelectedProjectCreationConfiguration;
        OnPropertyChanged(nameof(SelectedProjectCreationConfiguration));
        OnPropertyChanged(nameof(SelectedManagedProjectProfile));
        OnPropertyChanged(nameof(CanDeleteSelectedProjectProfile));
    }

    private async Task LoadProjectCreationProfilesAsync()
    {
        _customProjectCreationProfiles.Clear();
        var json = await _settings.GetAsync(ProjectCreationProfilesSetting).ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var stored = JsonSerializer.Deserialize<List<ProjectConfigurationProfile>>(json) ?? [];
                var builtInIds = _projectCreation.ConfigurationProfiles
                    .Select(profile => profile.Id)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                _customProjectCreationProfiles.AddRange(stored
                    .Where(profile =>
                        profile.Engine == EngineFamily.Unreal &&
                        !string.IsNullOrWhiteSpace(profile.Id) &&
                        !string.IsNullOrWhiteSpace(profile.DisplayName) &&
                        !builtInIds.Contains(profile.Id))
                    .Take(200)
                    .Select(profile => profile with
                    {
                        EngineSettings = profile.EngineSettings ?? new Dictionary<string, string>(),
                        EnabledPlugins = profile.EnabledPlugins ?? [],
                        DisabledPlugins = profile.DisabledPlugins ?? [],
                        IniSettings = profile.IniSettings ?? [],
                        IsBuiltIn = false,
                    }));
            }
            catch (JsonException exception)
            {
                AppendRawLog(
                    "WARN",
                    "Projects/Profiles",
                    $"Profils personnalisés ignorés : {exception.Message}");
            }
        }

        await LoadProjectFeaturePacksAsync().ConfigureAwait(true);
        RebuildProjectCreationConfigurationOptions();
    }

    private Task PersistProjectCreationProfilesAsync() => _settings.SetAsync(
        ProjectCreationProfilesSetting,
        JsonSerializer.Serialize(_customProjectCreationProfiles));

    private void OpenNewProjectProfile()
    {
        IsEditingProjectFeaturePack = false;
        ProjectProfileDisableEnginePluginsByDefault = false;
        _editingProjectProfileId = null;
        ProjectProfileName = "Nouveau profil Unreal";
        ProjectProfileDescription =
            "Configuration personnalisée des plugins et fichiers INI Unreal.";
        SelectedProjectProfileSourceProject ??= Projects.FirstOrDefault();
        ProjectProfileIniSettings.Clear();
        PopulateProjectProfilePlugins([], []);
        ProjectProfilePluginSearchQuery = string.Empty;
        IsProjectProfileEditorVisible = true;
        OnPropertyChanged(nameof(CanDeleteSelectedProjectProfile));
    }

    private void OpenSelectedProjectProfile() => OpenProjectProfile(SelectedProjectCreationConfiguration?.Profile);

    private void OpenProjectProfile(ProjectConfigurationProfile? selected, bool duplicate = false)
    {
        IsEditingProjectFeaturePack = false;
        if (selected is null)
        {
            OpenNewProjectProfile();
            return;
        }

        _editingProjectProfileId = selected.IsBuiltIn || duplicate ? null : selected.Id;
        ProjectProfileName = selected.IsBuiltIn || duplicate
            ? $"{selected.DisplayName} (copie)"
            : selected.DisplayName;
        ProjectProfileDescription = selected.Description;
        ProjectProfileDisableEnginePluginsByDefault = selected.DisableEnginePluginsByDefault;
        SelectedProjectProfileSourceProject ??= Projects.FirstOrDefault();
        ProjectProfileIniSettings.Clear();
        foreach (var setting in selected.EngineSettings)
        {
            ProjectProfileIniSettings.Add(new ProjectProfileIniSettingViewModel(
                new ProjectIniSetting(
                    "DefaultEngine.ini",
                    "/Script/Engine.RendererSettings",
                    setting.Key,
                    setting.Value),
                RemoveProjectProfileIniSetting));
        }
        foreach (var setting in selected.IniSettings)
        {
            ProjectProfileIniSettings.Add(new ProjectProfileIniSettingViewModel(
                setting,
                RemoveProjectProfileIniSetting));
        }

        PopulateProjectProfilePlugins(selected.EnabledPlugins, selected.DisabledPlugins);
        ProjectProfilePluginSearchQuery = string.Empty;
        IsProjectProfileEditorVisible = true;
        OnPropertyChanged(nameof(CanDeleteSelectedProjectProfile));
    }

    private void PopulateProjectProfilePlugins(
        IReadOnlyList<string> enabledPlugins,
        IReadOnlyList<string> disabledPlugins)
    {
        var enabled = enabledPlugins.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var disabled = disabledPlugins.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inventory = _projectCreationEnginePlugins
            .GroupBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var pluginName in enabled.Concat(disabled))
        {
            if (!inventory.ContainsKey(pluginName))
            {
                inventory[pluginName] = new UnrealPluginDescriptor(
                    pluginName,
                    pluginName,
                    pluginName,
                    "Plugin référencé par le profil mais absent du moteur sélectionné.",
                    "Profil",
                    string.Empty,
                    string.Empty,
                    PluginSourceKind.Engine,
                    "Absent du moteur sélectionné",
                    SelectedProjectCreationEngine?.Engine.Id,
                    0,
                    0,
                    string.Empty,
                    null,
                    false,
                    false,
                    false,
                    [],
                    [],
                    [],
                    PluginHealth.Warning,
                    ["Plugin absent du moteur sélectionné."],
                    DateTimeOffset.UtcNow);
            }
        }
        foreach (var pluginName in enabled.Concat(disabled))
        {
            if (!inventory.ContainsKey(pluginName))
            {
                inventory[pluginName] = new UnrealPluginDescriptor(
                    pluginName,
                    pluginName,
                    pluginName,
                    "Plugin référencé par le profil mais absent du moteur sélectionné.",
                    "Profil",
                    string.Empty,
                    string.Empty,
                    PluginSourceKind.Engine,
                    "Absent du moteur sélectionné",
                    SelectedProjectCreationEngine?.Engine.Id,
                    0,
                    0,
                    string.Empty,
                    null,
                    false,
                    false,
                    false,
                    [],
                    [],
                    [],
                    PluginHealth.Warning,
                    ["Plugin absent du moteur sélectionné."],
                    DateTimeOffset.UtcNow);
            }
        }

        ProjectProfilePlugins.Clear();
        foreach (var plugin in inventory.Values
                     .OrderBy(plugin => plugin.FriendlyName, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase))
        {
            bool? state = enabled.Contains(plugin.Name)
                ? true
                : disabled.Contains(plugin.Name)
                    ? false
                    : null;
            var option = new ProjectProfilePluginOptionViewModel(
                plugin.Name,
                plugin.FriendlyName,
                string.IsNullOrWhiteSpace(plugin.Category) ? "Sans catégorie" : plugin.Category,
                plugin.SourceLabel,
                state,
                plugin.EnabledByDefault == true);
            option.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ProjectProfilePluginOptionViewModel.State))
                {
                    OnPropertyChanged(nameof(ProjectProfileOverriddenPluginCount));
                }
            };
            ProjectProfilePlugins.Add(option);
        }

        ApplyProjectProfilePluginFilter();
        OnPropertyChanged(nameof(ProjectProfilePluginCount));
        OnPropertyChanged(nameof(ProjectProfileOverriddenPluginCount));
    }

    private void RebuildProjectProfilePluginInventoryPreservingState()
    {
        var enabled = ProjectProfilePlugins
            .Where(plugin => plugin.State == true)
            .Select(plugin => plugin.Name)
            .ToArray();
        var disabled = ProjectProfilePlugins
            .Where(plugin => plugin.State == false)
            .Select(plugin => plugin.Name)
            .ToArray();
        PopulateProjectProfilePlugins(enabled, disabled);
    }

    private void ApplyProjectProfilePluginFilter()
    {
        var query = ProjectProfilePluginSearchQuery.Trim();
        foreach (var plugin in ProjectProfilePlugins)
        {
            plugin.IsVisible = query.Length == 0 ||
                               plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               plugin.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               plugin.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               plugin.Source.Contains(query, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void PopulateProjectCreationPluginsFromOverrides() => RecomposeProjectConfiguration();

    private void RebuildProjectCreationPluginInventoryPreservingState() => RecomposeProjectConfiguration();

    private void SynchronizeProjectCreationPluginOverrides()
    {
        foreach (var plugin in ProjectCreationPlugins)
        {
            if (plugin.State.HasValue) _manualProjectPlugins[plugin.Name] = plugin.State.Value;
            else _manualProjectPlugins.Remove(plugin.Name);
        }
        RecomposeProjectConfiguration();
    }

    private void ApplyProjectCreationPluginFilter()
    {
        var query = ProjectCreationPluginSearchQuery.Trim();
        var visible = ProjectCreationPlugins.Where(plugin =>
            (!ShowActiveProjectCreationPluginsOnly || plugin.IsActive) &&
            (SelectedProjectCreationPluginCategory == "Toutes les catégories" ||
             plugin.Category == SelectedProjectCreationPluginCategory) &&
            (query.Length == 0 ||
                               plugin.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               plugin.FriendlyName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               plugin.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                               plugin.Source.Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (!VisibleProjectCreationPlugins.SequenceEqual(visible)) VisibleProjectCreationPlugins.ReplaceWith(visible);
        OnPropertyChanged(nameof(ProjectCreationPluginActivationSummary));
    }

    private void RemoveProjectProfileIniSetting(ProjectProfileIniSettingViewModel setting)
    {
        ProjectProfileIniSettings.Remove(setting);
    }

    private async Task SaveProjectProfileAsync()
    {
        if (IsEditingProjectFeaturePack)
        {
            await SaveProjectFeaturePackAsync().ConfigureAwait(true);
            return;
        }
        var name = ProjectProfileName.Trim();
        if (name.Length is < 1 or > 100)
        {
            throw new InvalidOperationException(
                "Le nom du profil doit contenir entre 1 et 100 caractères.");
        }
        if (ProjectProfileDescription.Length > 500)
        {
            throw new InvalidOperationException(
                "La description du profil ne peut pas dépasser 500 caractères.");
        }

        var iniSettings = ProjectProfileIniSettings
            .Select(setting => setting.ToModel())
            .ToArray();
        if (iniSettings.Any(setting =>
                string.IsNullOrWhiteSpace(setting.FileName) ||
                string.IsNullOrWhiteSpace(setting.Section) ||
                string.IsNullOrWhiteSpace(setting.Key)))
        {
            throw new InvalidOperationException(
                "Chaque réglage INI doit avoir un fichier, une section et une clé.");
        }
        if (iniSettings.Length > 5_000)
        {
            throw new InvalidOperationException(
                "Un profil ne peut pas contenir plus de 5 000 réglages INI.");
        }

        foreach (var setting in iniSettings) UnrealProjectIni.ValidateSetting(setting);
        var id = _editingProjectProfileId ?? CreateProjectProfileId(name);
        var enabled = ProjectProfilePlugins
            .Where(plugin => plugin.State == true)
            .Select(plugin => plugin.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var disabled = ProjectProfilePlugins
            .Where(plugin => plugin.State == false)
            .Select(plugin => plugin.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var profile = new ProjectConfigurationProfile(
            id,
            name,
            ProjectProfileDescription.Trim(),
            EngineFamily.Unreal,
            new Dictionary<string, string>(),
            enabled,
            disabled)
        {
            IniSettings = iniSettings,
            IsBuiltIn = false,
            DisableEnginePluginsByDefault = ProjectProfileDisableEnginePluginsByDefault,
        };

        var existingIndex = _customProjectCreationProfiles.FindIndex(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            _customProjectCreationProfiles[existingIndex] = profile;
        }
        else
        {
            _customProjectCreationProfiles.Add(profile);
        }
        await PersistProjectCreationProfilesAsync().ConfigureAwait(true);
        RebuildProjectCreationConfigurationOptions(IsStandaloneProjectProfileEditing ? null : id, id);
        IsProjectProfileEditorVisible = false;
        StatusMessage = $"Profil Unreal « {name} » enregistré.";
        AppendRawLog(
            "INFO",
            "Projects/Profiles",
            $"Profil {name} enregistré : {enabled.Length} plugin(s) activé(s), " +
            $"{disabled.Length} désactivé(s), {iniSettings.Length} réglage(s) INI.");
    }

    private async Task DeleteProjectProfileAsync()
    {
        if (IsEditingProjectFeaturePack)
        {
            await DeleteProjectFeaturePackAsync().ConfigureAwait(true);
            return;
        }
        var profile = _customProjectCreationProfiles.FirstOrDefault(item =>
            string.Equals(item.Id, _editingProjectProfileId, StringComparison.OrdinalIgnoreCase));
        if (profile is null)
        {
            throw new InvalidOperationException(
                "Les profils intégrés servent de modèles et ne peuvent pas être supprimés.");
        }

        _customProjectCreationProfiles.RemoveAll(item =>
            string.Equals(item.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
        await PersistProjectCreationProfilesAsync().ConfigureAwait(true);
        RebuildProjectCreationConfigurationOptions(IsStandaloneProjectProfileEditing ? null : "unreal-standard");
        IsProjectProfileEditorVisible = false;
        StatusMessage = $"Profil « {profile.DisplayName} » supprimé.";
    }

    private async Task ImportProjectProfileAsync()
    {
        var source = SelectedProjectProfileSourceProject ??
                     throw new InvalidOperationException(
                         "Sélectionnez le projet Unreal dont la configuration doit être copiée.");
        var matchingEngine = ProjectCreationEngines.FirstOrDefault(option =>
            string.Equals(
                option.Association,
                source.Project.EngineAssociation,
                StringComparison.OrdinalIgnoreCase)) ??
                             ProjectCreationEngines.FirstOrDefault(option =>
                                 option.Engine.Version.IsKnown &&
                                 (source.Project.EngineAssociation?.Contains(
                                     $"{option.Engine.Version.Major}.{option.Engine.Version.Minor}",
                                     StringComparison.OrdinalIgnoreCase) ?? false));
        if (matchingEngine is not null)
        {
            SelectedProjectCreationEngine = matchingEngine;
        }

        var iniSettings = await ReadProjectIniSettingsAsync(source.Project.RootPath)
            .ConfigureAwait(true);
        using (var descriptor = JsonDocument.Parse(await File.ReadAllTextAsync(source.Project.FilePath).ConfigureAwait(true)))
            ProjectProfileDisableEnginePluginsByDefault = descriptor.RootElement.TryGetProperty("DisableEnginePluginsByDefault", out var minimal) &&
                minimal.ValueKind == JsonValueKind.True;
        _editingProjectProfileId = null;
        ProjectProfileName = $"{source.Name} (profil)";
        ProjectProfileDescription =
            $"Profil importé depuis {source.Project.RootPath}.";
        ProjectProfileIniSettings.Clear();
        foreach (var setting in iniSettings)
        {
            ProjectProfileIniSettings.Add(new ProjectProfileIniSettingViewModel(
                setting,
                RemoveProjectProfileIniSetting));
        }
        PopulateProjectProfilePlugins(
            source.Project.Plugins.Where(plugin => plugin.Enabled).Select(plugin => plugin.Name).ToArray(),
            source.Project.Plugins.Where(plugin => !plugin.Enabled).Select(plugin => plugin.Name).ToArray());
        ProjectProfilePluginSearchQuery = string.Empty;
        IsProjectProfileEditorVisible = true;
        StatusMessage =
            $"Configuration de {source.Name} copiée · {iniSettings.Count:N0} réglage(s) INI.";
    }

    private static async Task<IReadOnlyList<ProjectIniSetting>> ReadProjectIniSettingsAsync(
        string projectRoot)
    {
        var configRoot = Path.Combine(projectRoot, "Config");
        if (!Directory.Exists(configRoot)) return [];

        return await Task.Run(async () =>
        {
            var result = new List<ProjectIniSetting>();
            foreach (var path in Directory.EnumerateFiles(configRoot, "Default*.ini", SearchOption.TopDirectoryOnly)
                         .Take(64))
            {
                var info = new FileInfo(path);
                if (info.Length is <= 0 or > 4 * 1024 * 1024 ||
                    (info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                var section = string.Empty;
                var lines = await File.ReadAllLinesAsync(path).ConfigureAwait(false);
                foreach (var rawLine in lines.Take(100_000))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                    {
                        continue;
                    }
                    if (line.StartsWith('[') && line.EndsWith(']'))
                    {
                        section = line[1..^1].Trim();
                        continue;
                    }
                    var separator = line.IndexOf('=');
                    if (section.Length == 0 || separator <= 0) continue;
                    var key = line[..separator].Trim();
                    var value = line[(separator + 1)..];
                    if (key.Length == 0 ||
                        (string.Equals(
                             section,
                             "/Script/EngineSettings.GeneralProjectSettings",
                             StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(key, "ProjectName", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }
                    result.Add(new ProjectIniSetting(info.Name, section, key, value));
                }
            }
            return (IReadOnlyList<ProjectIniSetting>)result;
        }).ConfigureAwait(false);
    }

    private static string CreateProjectProfileId(string name)
    {
        var slug = new string(name
            .ToLowerInvariant()
            .Select(character =>
                character is >= 'a' and <= 'z' or >= '0' and <= '9'
                    ? character
                    : '-')
            .ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        slug = slug.Trim('-');
        if (slug.Length == 0 || !char.IsLetter(slug[0])) slug = $"profil-{slug}";
        if (slug.Length > 80) slug = slug[..80].TrimEnd('-');
        return $"user.{slug}.{Guid.NewGuid():N}";
    }

    private async Task RefreshProjectCreationEnginePluginsAsync(
        UnrealEngineInstallation engine)
    {
        var refreshVersion = Interlocked.Increment(
            ref _projectCreationEnginePluginRefreshVersion);
        IsProjectCreationPluginLoading = true;
        ProjectCreationPluginCompatibilitySummary =
            $"Analyse des plugins de {engine.DisplayName} en arrière-plan…";
        try
        {
            var plugins = await Task.Run(() => _pluginDiscovery.RefreshAsync(
                    new PluginDiscoveryRequest([engine], [], [], MaximumDepth: 8)))
                .ConfigureAwait(true);
            if (refreshVersion != Volatile.Read(
                    ref _projectCreationEnginePluginRefreshVersion))
            {
                return;
            }

            _projectCreationEnginePlugins = plugins
                .Where(plugin =>
                    plugin.SourceKind == PluginSourceKind.Engine &&
                    string.Equals(
                        plugin.SourceOwnerId,
                        engine.Id,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
            IsProjectCreationPluginLoading = false;
            RebuildProjectCreationPluginInventoryPreservingState();
            if (IsProjectProfileEditorVisible)
            {
                RebuildProjectProfilePluginInventoryPreservingState();
            }
        }
        catch (Exception exception)
        {
            if (refreshVersion != Volatile.Read(
                    ref _projectCreationEnginePluginRefreshVersion))
            {
                return;
            }
            _projectCreationEnginePlugins = [];
            ProjectCreationPlugins.Clear();
            ApplyProjectCreationPluginFilter();
            OnPropertyChanged(nameof(ProjectCreationPluginCount));
            OnPropertyChanged(nameof(ProjectCreationOverriddenPluginCount));
            ProjectCreationPluginCompatibilitySummary =
                $"Inventaire des plugins indisponible : {exception.Message}";
            AppendRawLog("WARN", "Projects/Profiles", exception.Message);
        }
        finally
        {
            if (refreshVersion == Volatile.Read(ref _projectCreationEnginePluginRefreshVersion))
            {
                IsProjectCreationPluginLoading = false;
                OnPropertyChanged(nameof(CanCreateProjectConfiguration));
            }
        }
    }

    private void UpdateProjectCreationPluginCompatibilitySummary()
    {
        if (IsProjectCreationPluginLoading) return;
        var engine = SelectedProjectCreationEngine?.Engine;
        if (engine is null)
        {
            ProjectCreationPluginCompatibilitySummary =
                "Sélectionnez un moteur Unreal pour inventorier ses plugins.";
            return;
        }
        if (_projectCreationEnginePlugins.Length == 0)
        {
            ProjectCreationPluginCompatibilitySummary =
                $"Aucun plugin n’a été trouvé dans {engine.DisplayName}.";
            return;
        }

        var profilePlugins = (_effectiveProjectProfile?.EnabledPlugins ?? [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var available = _projectCreationEnginePlugins
            .Select(plugin => plugin.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = profilePlugins.Where(plugin => !available.Contains(plugin)).ToArray();
        ProjectCreationPluginCompatibilitySummary = missing.Length == 0
            ? $"{_projectCreationEnginePlugins.Length:N0} plugin(s) détecté(s) dans {engine.DisplayName} · " +
              (ProjectCompositionIssues.Length == 0 ? "profil et packs compatibles." : "dépendances à vérifier (voir ci-dessous).")
            : $"{_projectCreationEnginePlugins.Length:N0} plugin(s) moteur · requis mais absent(s) : {string.Join(", ", missing.Take(6))}" +
              (missing.Length > 6 ? $" (+{missing.Length - 6})" : string.Empty);
    }

    private void ApplyProjectCreationPluginDefaults()
    {
        _manualProjectPlugins.Clear();
        ProjectCreationIniDocuments.Clear();
        RecomposeProjectConfiguration();
    }

    private void RebuildProjectCreationEngines()
    {
        var selectedId = SelectedProjectCreationEngine?.Engine.Id;
        ProjectCreationEngines.Clear();
        foreach (var engine in _currentEngines
                     .OrderByDescending(engine => engine.Version.Major)
                     .ThenByDescending(engine => engine.Version.Minor)
                     .ThenByDescending(engine => engine.Version.Patch))
        {
            ProjectCreationEngines.Add(new ProjectCreationEngineOptionViewModel(engine));
        }
        SelectedProjectCreationEngine = ProjectCreationEngines.FirstOrDefault(item =>
                                            string.Equals(
                                                item.Engine.Id,
                                                selectedId,
                                                StringComparison.OrdinalIgnoreCase))
                                        ?? ProjectCreationEngines.FirstOrDefault();
    }

    private void RebuildProjectCreationCollections()
    {
        var selected = ProjectCreationCollections.Where(item => item.IsSelected)
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assetsById = _remoteFabCatalog.ToDictionary(
            asset => asset.AssetId,
            StringComparer.OrdinalIgnoreCase);
        var localPackIds = FabLocalContentPacks
            .Select(card => card.Pack.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var names = _fabCollections.Values
            .SelectMany(collections => collections)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ProjectCreationCollections.Clear();
        foreach (var name in names)
        {
            var ids = _fabCollections
                .Where(pair => pair.Value.Contains(name, StringComparer.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var sources = ids
                .SelectMany(id =>
                {
                    var values = new List<string>(2);
                    if (assetsById.TryGetValue(id, out var asset))
                    {
                        values.Add(IsQuixelAsset(asset) ? "Megascans" : "Fab");
                    }
                    if (localPackIds.Contains(id)) values.Add("Local");
                    return values;
                })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var option = new ProjectCreationCollectionOptionViewModel(
                name,
                ids.Length,
                sources.Length == 0 ? "local / cache" : string.Join(" + ", sources));
            option.IsSelected = selected.Contains(name);
            ProjectCreationCollections.Add(option);
        }
        OnPropertyChanged(nameof(HasProjectCreationCollections));
    }

    private void RebuildProjectPostCreationScriptOptions()
    {
        var selected = ProjectPostCreationScripts.Where(item => item.IsSelected)
            .Select(item => item.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        ProjectPostCreationScripts.Clear();
        foreach (var descriptor in _projectCreation.PostCreationScripts)
        {
            var option = new ProjectPostCreationScriptOptionViewModel(descriptor)
            {
                IsSelected = selected.Contains(descriptor.Id),
            };
            ProjectPostCreationScripts.Add(option);
        }
        OnPropertyChanged(nameof(HasProjectPostCreationScripts));
    }

    private async Task RefreshProjectCreationScriptsAsync()
    {
        var externalCount = await _projectPostCreationScripts.RefreshExternalAsync(
                _projectCreationScriptsDirectory)
            .ConfigureAwait(true);
        RebuildProjectPostCreationScriptOptions();
        AppendRawLog(
            "INFO",
            "Projects/CreationScripts",
            $"{ProjectPostCreationScripts.Count:N0} script(s) post-création, dont {externalCount:N0} externe(s).");
    }

    private void QueueProjectStarterCollections(
        ProjectCardViewModel project,
        RemoteFabAsset[] assets,
        FabLocalContentPack[] localPacks,
        string[] collectionNames)
    {
        var storedDownloads =
            new List<(RemoteFabAsset Asset, FabInstallVersionOptionViewModel Option, DirectFabDownloadResult Result)>();
        var title = collectionNames.Length == 0
            ? $"Packages initiaux · {project.Name}"
            : $"Collections {string.Join(", ", collectionNames)} · {project.Name}";
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Projets · Initialisation",
            title,
            [ProjectTaskLink(project.Project.FilePath, project.Name)],
            async (context, cancellationToken) =>
            {
                var total = assets.Length + localPacks.Length;
                var installed = 0;
                var skipped = 0;
                for (var index = 0; index < assets.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var asset = assets[index];
                    if (!IsMarketplaceAvailable(NormalizeAssetSource(asset)))
                    {
                        skipped++;
                        context.Report((double)(index + 1) / total, $"{asset.Title} ignoré : marketplace désactivée.");
                        continue;
                    }
                    var version = SelectStarterCollectionVersion(
                        asset,
                        project.Project.EngineAssociation);
                    var classification = RemoteFabAssetClassifier.Classify(asset, version);
                    if (version is null ||
                        classification.Kind is LibraryPackageContentKind.CompleteProject or
                            LibraryPackageContentKind.EnginePackage)
                    {
                        skipped++;
                        context.Report(
                            (double)(index + 1) / total,
                            $"{asset.Title} ignoré : package non installable dans un projet.");
                        continue;
                    }

                    var packageStorage = ResolveMarketplaceStorageProfile(asset);
                    var storageMode = ResolveMarketplaceStorageMode(asset);
                    var transfer = new CyEngineHub.Core.Marketplaces.DownloadProgressText();
                    var progress = new InlineProgress<DirectFabDownloadProgress>(value =>
                    {
                        var fraction = (index + (value.Fraction ?? 0)) / total;
                        context.Report(fraction, transfer.Format($"{asset.Title} · {value.Message}", value.DownloadedBytes, value.TotalBytes));
                    });
                    var download = IsQuixelAsset(asset)
                        ? await _quixelClient.DownloadAsync(
                                asset,
                                version,
                                packageStorage.DownloadDirectory,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false)
                        : await _directFabClient.DownloadAsync(
                                asset,
                                version,
                                packageStorage.DownloadDirectory,
                                progress,
                                cancellationToken)
                            .ConfigureAwait(false);
                    await InstallDownloadedFabPackageAsync(
                            download.DestinationDirectory,
                            FabInstallDestination.Project,
                            project,
                            null,
                            context,
                            cancellationToken)
                        .ConfigureAwait(false);
                    download = await LibraryPackageService.PackageAsync(
                            asset,
                            version,
                            download,
                            storageMode,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(false);
                    download = await MarketplaceStorageRouter.PlaceAsync(
                            download,
                            packageStorage,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(false);
                    storedDownloads.Add((
                        asset,
                        new FabInstallVersionOptionViewModel(version, false),
                        download));
                    installed++;
                    context.Report(
                        (double)(index + 1) / total,
                        $"{asset.Title} installé · {installed:N0} terminé(s), {skipped:N0} ignoré(s).");
                }
                for (var index = 0; index < localPacks.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var pack = localPacks[index];
                    var operationIndex = assets.Length + index;
                    context.Report(
                        (double)operationIndex / total,
                        $"{pack.DisplayName} · analyse du pack local…");
                    var sourceDirectory = Directory.Exists(pack.ContentDirectory)
                        ? pack.ContentDirectory
                        : pack.PackageRoot;
                    var plan = await _fabProjectContentImport.PrepareAsync(
                            sourceDirectory,
                            project.Project.FilePath,
                            cancellationToken)
                        .ConfigureAwait(false);
                    var progress = new InlineProgress<FabProjectImportProgress>(value =>
                        context.Report(
                            (operationIndex +
                             (value.Total == 0
                                 ? 0
                                 : Math.Clamp((double)value.Completed / value.Total, 0, 1))) / total,
                            $"{pack.DisplayName} · {value.Message}"));
                    await _fabProjectContentImport.ExecuteAsync(plan, progress, cancellationToken)
                        .ConfigureAwait(false);
                    installed++;
                    context.Report(
                        (double)(operationIndex + 1) / total,
                        $"{pack.DisplayName} installé · {installed:N0} terminé(s), {skipped:N0} ignoré(s).");
                }
            }));
        _ = CompleteProjectStarterCollectionsAsync(
            handle,
            project,
            storedDownloads,
            localPacks.Length);
    }

    private async Task CompleteProjectStarterCollectionsAsync(
        HubTaskHandle handle,
        ProjectCardViewModel project,
        List<(RemoteFabAsset Asset, FabInstallVersionOptionViewModel Option, DirectFabDownloadResult Result)> downloads,
        int localPackCount)
    {
        try
        {
            var completed = await handle.Completion.ConfigureAwait(true);
            if (completed.Status != HubTaskStatus.Completed)
            {
                StatusMessage = completed.Status == HubTaskStatus.Canceled
                    ? $"Initialisation des collections de {project.Name} annulée."
                    : $"Initialisation des collections de {project.Name} en échec : {completed.Error}";
                return;
            }
            foreach (var download in downloads)
            {
                await SaveStoredFabDownloadAsync(download.Asset, download.Option, download.Result)
                    .ConfigureAwait(true);
            }
            StatusMessage =
                $"{project.Name} initialisé · {downloads.Count + localPackCount:N0} package(s) de collection installé(s).";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private static RemoteFabProjectVersion? SelectStarterCollectionVersion(
        RemoteFabAsset asset,
        string? engineAssociation)
    {
        if (IsQuixelAsset(asset))
        {
            return asset.ProjectVersions
                       .Where(version => version.ArtifactId.StartsWith(
                           "quixel-tier:",
                           StringComparison.OrdinalIgnoreCase))
                       .OrderBy(version => version.ArtifactId, StringComparer.OrdinalIgnoreCase)
                       .FirstOrDefault()
                   ?? asset.ProjectVersions
                       .Where(version => version.ArtifactId.StartsWith(
                           "quixel-raw:",
                           StringComparison.OrdinalIgnoreCase))
                       .OrderBy(version => version.ArtifactId.Contains(
                           ":2048:fbx",
                           StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                       .FirstOrDefault();
        }

        var target = NormalizeEngineVersion(engineAssociation ?? string.Empty);
        return asset.ProjectVersions.FirstOrDefault(version =>
                   version.EngineVersions.Count == 0 ||
                   version.EngineVersions.Any(value => string.Equals(
                       NormalizeEngineVersion(value),
                       target,
                       StringComparison.OrdinalIgnoreCase)))
               ?? asset.PreferredProjectVersion;
    }

    private async Task ToggleAutoDiscoverProjectsAsync()
    {
        _workspacePreferences = _workspacePreferences with
        {
            AutoDiscoverProjects = !_workspacePreferences.AutoDiscoverProjects,
        };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
        OnPropertyChanged(nameof(AutoDiscoverProjects));
        OnPropertyChanged(nameof(AutoDiscoverProjectsLabel));
        RebuildCatalogCardsFromCurrentSnapshots();
    }

    private async Task ToggleLogsAsync()
    {
        _workspacePreferences = _workspacePreferences with
        {
            ShowLogs = !_workspacePreferences.ShowLogs,
        };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
        OnPropertyChanged(nameof(ShowLogs));
        OnPropertyChanged(nameof(ShowLogsLabel));
        OnPropertyChanged(nameof(IsLogsNavigationVisible));
        if (!ShowLogs && _section == PageSection.Logs)
        {
            ShowSection(PageSection.Settings);
        }
    }

    private async Task RestoreHiddenProjectsAsync()
    {
        _workspacePreferences = _workspacePreferences with
        {
            Items = _workspacePreferences.Items
                .Select(item => item.Kind == WorkspaceItemKind.Project
                    ? item with { IsHidden = false }
                    : item)
                .ToArray(),
        };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
        RebuildCatalogCardsFromCurrentSnapshots();
        StatusMessage = "Tous les projets masqués ont été réintégrés à la bibliothèque.";
    }

    private async Task RestoreHiddenEnginesAsync()
    {
        _workspacePreferences = _workspacePreferences with
        {
            Items = _workspacePreferences.Items
                .Select(item => item.Kind == WorkspaceItemKind.Engine
                    ? item with { IsHidden = false }
                    : item)
                .ToArray(),
        };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
        RebuildCatalogCardsFromCurrentSnapshots();
        StatusMessage = "Tous les moteurs retirés ont été réintégrés à CyEngineHub.";
    }

    private async Task SetWorkspaceEngineHiddenAsync(string path, bool hidden)
    {
        var existing = FindWorkspacePreference(WorkspaceItemKind.Engine, path);
        if (existing?.IsHidden == hidden)
        {
            return;
        }
        var updated = new WorkspaceItemPreference(
            WorkspaceItemKind.Engine,
            path,
            existing?.Alias ?? string.Empty,
            existing?.FolderPath ?? string.Empty,
            hidden,
            existing?.IsExplicit ?? false);
        _workspacePreferences = _workspacePreferences with
        {
            Items = _workspacePreferences.Items
                .Where(item => item.Kind != WorkspaceItemKind.Engine || !SameWorkspaceKey(item.Key, path))
                .Append(updated)
                .ToArray(),
        };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
    }

    private async Task RemoveSelectedEngineFromHubAsync()
    {
        var path = SelectedEngineManagementPath;
        var name = SelectedEngineManagementName;
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Aucun moteur n’est sélectionné.");
        }

        await SetWorkspaceEngineHiddenAsync(path, hidden: true).ConfigureAwait(true);
        IsEngineRemovalConfirmationVisible = false;
        SelectedEngineDetails = null;
        SelectedLocalEngineDetails = null;
        RebuildCatalogCardsFromCurrentSnapshots();
        ShowSection(PageSection.Engines);
        EngineManagementStatus = $"{name} a été retiré de CyEngineHub; aucun fichier n’a été supprimé.";
        StatusMessage = EngineManagementStatus;
    }

    private async Task UninstallSelectedEngineAsync()
    {
        var path = SelectedEngineManagementPath;
        var name = SelectedEngineManagementName;
        var unrealEngine = SelectedEngineDetails?.Engine;
        ValidateEngineUninstallRoot(path, unrealEngine is not null);

        IsEngineUninstallConfirmationVisible = false;
        IsEngineUninstallActive = true;
        EngineManagementStatus = $"Désinstallation de {name}…";
        try
        {
            var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
                "Moteurs · Désinstallation",
                $"Désinstaller {name}",
                async (context, cancellationToken) =>
                {
                    context.Report(null, $"Suppression de {path}");
                    cancellationToken.ThrowIfCancellationRequested();
                    await Task.Run(() => Directory.Delete(path, recursive: true), cancellationToken)
                        .ConfigureAwait(false);
                    context.Report(1, "Dossier moteur supprimé.");
                }));
            var completed = await handle.Completion.ConfigureAwait(true);
            if (completed.Status == HubTaskStatus.Canceled)
            {
                EngineManagementStatus = "Désinstallation annulée avant la suppression du dossier.";
                StatusMessage = EngineManagementStatus;
                return;
            }
            if (completed.Status == HubTaskStatus.Failed)
            {
                EngineManagementStatus = $"Échec de la désinstallation : {completed.Error}";
                StatusMessage = EngineManagementStatus;
                return;
            }

            if (unrealEngine is not null)
            {
                await Task.Run(() => _engineRegistration.Unregister(unrealEngine.RootPath)).ConfigureAwait(true);
            }
            await RemoveConfiguredPathAsync(EngineRootsSetting, path).ConfigureAwait(true);
            await SetWorkspaceEngineHiddenAsync(path, hidden: true).ConfigureAwait(true);
            SelectedEngineDetails = null;
            SelectedLocalEngineDetails = null;
            await RefreshCatalogAsync().ConfigureAwait(true);
            ShowSection(PageSection.Engines);
            EngineManagementStatus = $"{name} a été désinstallé et retiré de CyEngineHub.";
            StatusMessage = EngineManagementStatus;
        }
        finally
        {
            IsEngineUninstallActive = false;
        }
    }

    private static void ValidateEngineUninstallRoot(string path, bool isUnreal)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            throw new DirectoryNotFoundException("Le dossier du moteur sélectionné n’existe pas.");
        }
        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var driveRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath) ?? string.Empty);
        if (fullPath.Length < 6 || SameWorkspaceKey(fullPath, driveRoot) ||
            IsSameOrChildPath(AppContext.BaseDirectory, fullPath))
        {
            throw new InvalidOperationException("Ce chemin est trop large ou contient CyEngineHub; désinstallation refusée.");
        }
        if (isUnreal && !Directory.Exists(Path.Combine(fullPath, "Engine")))
        {
            throw new InvalidOperationException("Le dossier sélectionné ne contient pas une installation Unreal Engine valide.");
        }
    }

    private void NotifySelectedEngineManagementProperties()
    {
        OnPropertyChanged(nameof(SelectedEngineManagementName));
        OnPropertyChanged(nameof(SelectedEngineManagementPath));
    }

    private bool ShouldDisplayProject(string key)
    {
        var preference = FindWorkspacePreference(WorkspaceItemKind.Project, key);
        // Disabling discovery stops scans; it must not hide already catalogued projects.
        return preference?.IsHidden != true;
    }

    private void RebuildCatalogCardsFromCurrentSnapshots()
    {
        ReplaceCatalog(_currentEngines, _currentUnrealProjects);
        ReplaceLocalEngineCatalog(_currentLocalSnapshots);
        ReplacePluginCatalog(_currentPlugins);
        ReplaceLocalEnginePackageCatalog(_currentLocalPackageSnapshots);
        ApplyEngineFamilyFilters();
    }

    private static bool SameWorkspaceKey(string left, string right) =>
        string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private async Task RefreshCatalogAsync()
    {
        IsBusy = true;
        StatusMessage = "Analyse locale en cours…";
        IReadOnlyList<UnrealEngineInstallation> engines = [];
        IReadOnlyList<UnrealProject> projects = [];
        IReadOnlyList<LocalEngineDiscoverySnapshot> localSnapshots = [];
        IReadOnlyList<LocalEnginePackageInstallReceipt> localPackageHistory = [];
        IReadOnlyList<LocalEnginePackageRemovalReceipt> localPackageRemovalHistory = [];
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Discovery",
            "Actualiser moteurs et projets",
            async (context, cancellationToken) =>
            {
                if (IsProviderEnabled("unreal"))
                {
                    context.Report(0.05, "Lecture du registre et des manifests Epic");
                    await _engineDiscovery.RefreshAsync(cancellationToken).ConfigureAwait(false);
                    if (_workspacePreferences.AutoDiscoverProjects || _forceProjectDiscoveryOnce)
                    {
                        context.Report(0.55, "Analyse des descripteurs .uproject");
                        await _projectDiscovery.RefreshAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                context.Report(0.7, "Analyse locale Unity, Godot et CryEngine");
                var engineRoots = await ReadConfiguredPathsAsync(EngineRootsSetting)
                    .ConfigureAwait(false);
                var projectRoots = await ReadConfiguredPathsAsync(ProjectRootsSetting)
                    .ConfigureAwait(false);
                var snapshots = new List<LocalEngineDiscoverySnapshot>();
                foreach (var provider in _engineModules.Modules
                             .OfType<ILocalEngineProvider>()
                             .Where(item => IsProviderEnabled(item.Descriptor.Id)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    snapshots.Add(await provider.DiscoverAsync(
                            new LocalEngineDiscoveryRequest(engineRoots, projectRoots),
                            cancellationToken)
                        .ConfigureAwait(false));
                }

                localSnapshots = snapshots;
                context.Report(0.84, "Lecture des reçus packages Unity/Godot");
                localPackageHistory = await DiscoverLocalPackageInstallHistoryAsync(
                        snapshots.SelectMany(snapshot => snapshot.Projects).ToArray(),
                        cancellationToken)
                    .ConfigureAwait(false);
                localPackageRemovalHistory = await DiscoverLocalPackageRemovalHistoryAsync(
                        snapshots.SelectMany(snapshot => snapshot.Projects).ToArray(),
                        cancellationToken)
                    .ConfigureAwait(false);
                context.Report(0.9, "Lecture du catalogue Unreal local");
                engines = await _engineCatalog.GetAllAsync(cancellationToken).ConfigureAwait(false);
                projects = await _projectCatalog.GetAllAsync(cancellationToken).ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        _forceProjectDiscoveryOnce = false;
        IsBusy = false;
        if (completed.Status == HubTaskStatus.Failed)
        {
            StatusMessage = $"Échec de l’analyse : {completed.Error}";
            return;
        }

        ReplaceCatalog(engines, projects);
        ReplaceLocalEngineCatalog(localSnapshots);
        ReplaceLocalEnginePackageInstallHistory(localPackageHistory);
        ReplaceLocalEnginePackageRemovalHistory(localPackageRemovalHistory);
        await LoadFabImportHistoryAsync().ConfigureAwait(true);
        StatusMessage = $"{EngineCount} moteur(s) · {ProjectCount} projet(s) · catalogue à jour";
    }

    private void ReplaceLocalEngineCatalog(
        IReadOnlyList<LocalEngineDiscoverySnapshot> snapshots)
    {
        _currentLocalSnapshots = snapshots;
        var selectedUnityFabProjectId = SelectedUnityFabProject?.Project.Id;
        var selectedGodotFabProjectId = SelectedGodotFabProject?.Project.Id;
        var selectedCryEngineFabProjectId = SelectedCryEngineFabProject?.Project.Id;
        var selectedLocalPackageProjectId = SelectedLocalPackageInstallProject?.Project.Id;
        _currentLocalEngineInstallations = snapshots
            .SelectMany(snapshot => snapshot.Installations)
            .ToArray();
        LocalEngineInstallations.Clear();
        foreach (var installation in _currentLocalEngineInstallations)
        {
            if (FindWorkspacePreference(WorkspaceItemKind.Engine, installation.RootPath)?.IsHidden == true)
            {
                continue;
            }
            LocalEngineInstallations.Add(new LocalEngineInstallationCardViewModel(
                installation,
                CreateOrganization(
                    WorkspaceItemKind.Engine,
                    installation.RootPath,
                    installation.DisplayName),
                GetEngineEffectiveSizeLabel(installation.RootPath),
                LaunchLocalEngineAsync,
                OpenFolderAsync,
                OpenLocalEngineDetails,
                ShowError));
        }

        LocalEngineProjects.Clear();
        UnityFabProjects.Clear();
        GodotFabProjects.Clear();
        CryEngineFabProjects.Clear();
        LocalPackageInstallProjects.Clear();
        foreach (var project in snapshots.SelectMany(snapshot => snapshot.Projects))
        {
            if (!ShouldDisplayProject(project.MarkerPath))
            {
                continue;
            }

            var card = new LocalEngineProjectCardViewModel(
                project,
                CreateOrganization(
                    WorkspaceItemKind.Project,
                    project.MarkerPath,
                    project.Name,
                    canHide: true),
                LaunchLocalProjectAsync,
                OpenFolderAsync,
                OpenLocalProjectDetails,
                ShowError);
            LocalEngineProjects.Add(card);
            if (project.Family == EngineFamily.Unity)
            {
                UnityFabProjects.Add(card);
                LocalPackageInstallProjects.Add(card);
            }
            else if (project.Family == EngineFamily.Godot)
            {
                GodotFabProjects.Add(card);
                LocalPackageInstallProjects.Add(card);
            }
            else if (project.Family == EngineFamily.CryEngine)
            {
                CryEngineFabProjects.Add(card);
            }
        }

        SelectedUnityFabProject = UnityFabProjects.FirstOrDefault(item =>
            string.Equals(item.Project.Id, selectedUnityFabProjectId, StringComparison.Ordinal))
            ?? UnityFabProjects.FirstOrDefault();
        SelectedGodotFabProject = GodotFabProjects.FirstOrDefault(item =>
            string.Equals(item.Project.Id, selectedGodotFabProjectId, StringComparison.Ordinal))
            ?? GodotFabProjects.FirstOrDefault();
        SelectedCryEngineFabProject = CryEngineFabProjects.FirstOrDefault(item =>
            string.Equals(item.Project.Id, selectedCryEngineFabProjectId, StringComparison.Ordinal))
            ?? CryEngineFabProjects.FirstOrDefault();
        SelectedLocalPackageInstallProject = LocalPackageInstallProjects.FirstOrDefault(item =>
            string.Equals(item.Project.Id, selectedLocalPackageProjectId, StringComparison.Ordinal))
            ?? LocalPackageInstallProjects.FirstOrDefault();
        OnPropertyChanged(nameof(HasLocalPackageInstallTargets));

        var warnings = snapshots.SelectMany(snapshot => snapshot.Warnings).ToArray();
        LocalEngineDiscoverySummary =
            $"{LocalEngineInstallationCount} Editor(s) Unity/Godot/CryEngine · " +
            $"{LocalEngineProjectCount} projet(s)" +
            (warnings.Length == 0 ? " · analyse complète" : $" · {warnings.Length} avertissement(s)");
        NotifyCatalogProperties();
        ApplyEngineFamilyFilters();
    }

    private void ReplaceCatalog(
        IReadOnlyList<UnrealEngineInstallation> engines,
        IReadOnlyList<UnrealProject> projects)
    {
        var selectedProjectId = SelectedPresetProject?.Project.Id;
        var selectedFabProjectId = SelectedFabProject?.Project.Id;
        _currentEngines = engines;
        _currentUnrealProjects = projects;
        RebuildProjectCreationEngines();
        Engines.Clear();
        foreach (var engine in engines)
        {
            if (FindWorkspacePreference(WorkspaceItemKind.Engine, engine.RootPath)?.IsHidden == true)
            {
                continue;
            }
            Engines.Add(new EngineCardViewModel(
                engine,
                FindRegistration(engine),
                CreateOrganization(
                    WorkspaceItemKind.Engine,
                    engine.RootPath,
                    engine.DisplayName),
                GetEngineEffectiveSizeLabel(engine.RootPath),
                LaunchEngineAsync,
                OpenFolderAsync,
                SetupEngineAsync,
                GenerateEngineProjectFilesAsync,
                BuildEngineEditorAsync,
                RegisterEngineAsync,
                UnregisterEngineAsync,
                OpenUnrealEngineDetails,
                ShowError));
        }
        var installedEngineVersions = engines
            .Select(engine => $"{engine.Version.Major}.{engine.Version.Minor}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in GetRealizedRemoteFabCards())
        {
            asset.UpdateInstalledEngineVersions(installedEngineVersions);
        }

        Projects.Clear();
        foreach (var project in projects)
        {
            if (!ShouldDisplayProject(project.FilePath))
            {
                continue;
            }

            Projects.Add(new ProjectCardViewModel(
                project,
                CreateOrganization(
                    WorkspaceItemKind.Project,
                    project.FilePath,
                    project.Name,
                    canHide: true),
                LaunchProjectAsync,
                OpenFolderAsync,
                GenerateProjectProjectFilesAsync,
                BuildProjectEditorAsync,
                PackageProjectAsync,
                OpenUnrealProjectDetails,
                ShowError));
        }

        SelectedPresetProject = Projects.FirstOrDefault(item =>
            string.Equals(item.Project.Id, selectedProjectId, StringComparison.Ordinal))
            ?? Projects.FirstOrDefault();
        SelectedFabProject = Projects.FirstOrDefault(item =>
            string.Equals(item.Project.Id, selectedFabProjectId, StringComparison.Ordinal))
            ?? Projects.FirstOrDefault();

        RebuildPluginTargets();
        NotifyCatalogProperties();
        ApplyEngineFamilyFilters();
    }

    private async Task AddEnginePathAsync()
    {
        var path = NormalizeExistingEnginePath(ManualEnginePath);
        await AddUniquePathAsync(EngineRootsSetting, path).ConfigureAwait(true);
        await SetWorkspaceEngineHiddenAsync(path, hidden: false).ConfigureAwait(true);
        ManualEnginePath = string.Empty;
        await RefreshCatalogAsync().ConfigureAwait(true);
        IsEngineImporterVisible = false;
    }

    private async Task AddProjectRootAsync()
    {
        var path = NormalizeExistingProjectRoot(ManualProjectRoot);
        await AddUniquePathAsync(ProjectRootsSetting, path).ConfigureAwait(true);
        ManualProjectRoot = string.Empty;
        _forceProjectDiscoveryOnce = true;
        await RefreshCatalogAsync().ConfigureAwait(true);
        await MarkProjectsExplicitUnderPathAsync(path).ConfigureAwait(true);
        RebuildCatalogCardsFromCurrentSnapshots();
    }

    private async Task ScanProjectImportCandidatesAsync()
    {
        var input = NormalizeInputPath(ManualProjectRoot);
        if (!Directory.Exists(input) && !File.Exists(input))
        {
            throw new FileNotFoundException("Le dossier ou descripteur à scanner n’existe pas.", input);
        }

        StatusMessage = "Analyse des descripteurs de projets…";
        var detected = await Task.Run(() => FindProjectImportCandidates(input)).ConfigureAwait(true);
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var existingMarkers = _currentUnrealProjects.Select(project => project.FilePath)
            .Concat(_currentLocalSnapshots.SelectMany(snapshot => snapshot.Projects).Select(project => project.MarkerPath))
            .Select(Path.GetFullPath)
            .ToHashSet(comparer);

        ProjectImportCandidates.Clear();
        foreach (var candidate in detected)
        {
            ProjectImportCandidates.Add(new ProjectImportCandidateViewModel(
                candidate.MarkerPath,
                candidate.ProjectRoot,
                candidate.Name,
                candidate.Engine,
                existingMarkers.Contains(Path.GetFullPath(candidate.MarkerPath))));
        }
        StatusMessage = $"{ProjectImportCandidates.Count:N0} descripteur(s) détecté(s), sélectionnez ceux à ajouter.";
    }

    private async Task ConfirmProjectImportCandidatesAsync()
    {
        var selected = ProjectImportCandidates
            .Where(candidate => candidate.IsSelected && !candidate.IsAlreadyCatalogued)
            .ToArray();
        if (selected.Length == 0)
        {
            throw new InvalidOperationException("Sélectionnez au moins un nouveau projet.");
        }

        foreach (var candidate in selected)
        {
            await AddUniquePathAsync(ProjectRootsSetting, candidate.ProjectRoot).ConfigureAwait(true);
        }
        _forceProjectDiscoveryOnce = true;
        await RefreshCatalogAsync().ConfigureAwait(true);
        foreach (var candidate in selected)
        {
            await MarkProjectsExplicitUnderPathAsync(candidate.ProjectRoot).ConfigureAwait(true);
        }
        RebuildCatalogCardsFromCurrentSnapshots();
        ProjectImportCandidates.Clear();
        IsManualProjectAddVisible = false;
        ManualProjectRoot = string.Empty;
        StatusMessage = $"{selected.Length:N0} projet(s) ajouté(s) au catalogue.";
    }

    private static (string MarkerPath, string ProjectRoot, string Name, string Engine)[]
        FindProjectImportCandidates(string input)
    {
        var markerPaths = new List<string>();
        if (File.Exists(input))
        {
            markerPaths.Add(input);
        }
        else
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MatchCasing = MatchCasing.CaseInsensitive,
            };
            markerPaths.AddRange(Directory.EnumerateFiles(input, "*", options)
                .Where(path =>
                    string.Equals(Path.GetExtension(path), ".uproject", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetExtension(path), ".cryproject", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), "project.godot", StringComparison.OrdinalIgnoreCase) ||
                    (string.Equals(Path.GetFileName(path), "ProjectVersion.txt", StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), "ProjectSettings", StringComparison.OrdinalIgnoreCase)))
                .Take(5000));
        }

        return markerPaths
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Select(path =>
            {
                var fullPath = Path.GetFullPath(path);
                var fileName = Path.GetFileName(fullPath);
                var isUnity = string.Equals(fileName, "ProjectVersion.txt", StringComparison.OrdinalIgnoreCase);
                var root = isUnity
                    ? Directory.GetParent(Path.GetDirectoryName(fullPath)!)?.FullName ?? Path.GetDirectoryName(fullPath)!
                    : Path.GetDirectoryName(fullPath)!;
                var engine = isUnity
                    ? "Unity"
                    : string.Equals(fileName, "project.godot", StringComparison.OrdinalIgnoreCase)
                        ? "Godot"
                        : string.Equals(Path.GetExtension(fullPath), ".cryproject", StringComparison.OrdinalIgnoreCase)
                            ? "CryEngine"
                            : "Unreal Engine";
                var name = engine is "Unreal Engine" or "CryEngine"
                    ? Path.GetFileNameWithoutExtension(fullPath)
                    : Path.GetFileName(root);
                return (MarkerPath: fullPath, ProjectRoot: root, Name: name, Engine: engine);
            })
            .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task MarkProjectsExplicitUnderPathAsync(string selectedPath)
    {
        var root = Path.GetFullPath(selectedPath);
        var keys = _currentUnrealProjects
            .Where(project => IsSameOrChildPath(project.RootPath, root))
            .Select(project => project.FilePath)
            .Concat(_currentLocalSnapshots
                .SelectMany(snapshot => snapshot.Projects)
                .Where(project => IsSameOrChildPath(project.RootPath, root))
                .Select(project => project.MarkerPath))
            .Distinct(OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
        {
            return;
        }

        var items = _workspacePreferences.Items.ToList();
        foreach (var key in keys)
        {
            var existing = items.FirstOrDefault(item =>
                item.Kind == WorkspaceItemKind.Project && SameWorkspaceKey(item.Key, key));
            items.RemoveAll(item =>
                item.Kind == WorkspaceItemKind.Project && SameWorkspaceKey(item.Key, key));
            items.Add(new WorkspaceItemPreference(
                WorkspaceItemKind.Project,
                key,
                existing?.Alias ?? string.Empty,
                existing?.FolderPath ?? string.Empty,
                IsHidden: false,
                IsExplicit: true));
        }

        _workspacePreferences = _workspacePreferences with { Items = items };
        await _workspacePreferencesService.SaveAsync(_workspacePreferences).ConfigureAwait(true);
    }

    private static bool IsSameOrChildPath(string candidate, string root)
    {
        var normalizedCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedCandidate, normalizedRoot, comparison) ||
               normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private async Task AddPluginLibraryRootAsync()
    {
        var path = NormalizeExistingPluginLibraryRoot(ManualPluginLibraryRoot);
        await AddUniquePathAsync(PluginLibraryRootsSetting, path).ConfigureAwait(true);
        ManualPluginLibraryRoot = string.Empty;
        await RefreshPluginsAsync().ConfigureAwait(true);
    }

    private async Task AddFabLocalLibraryRootAsync()
    {
        var path = NormalizeExistingFabLibraryRoot(ManualFabLibraryRoot);
        await AddUniquePathAsync(FabLocalLibraryRootsSetting, path).ConfigureAwait(true);
        ManualFabLibraryRoot = string.Empty;
        await RefreshFabLocalLibraryAsync().ConfigureAwait(true);
    }

    private async Task AddUniquePathAsync(string settingKey, string path)
    {
        var json = await _settings.GetAsync(settingKey).ConfigureAwait(true);
        var paths = string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json) ?? [];
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (!paths.Contains(path, comparer))
        {
            paths.Add(path);
            await _settings.SetAsync(settingKey, JsonSerializer.Serialize(paths)).ConfigureAwait(true);
        }
    }

    private async Task RemoveConfiguredPathAsync(string settingKey, string path)
    {
        var configured = (await ReadConfiguredPathsAsync(settingKey).ConfigureAwait(true)).ToList();
        var removed = configured.RemoveAll(item => SameWorkspaceKey(item, path));
        if (removed > 0)
        {
            await _settings.SetAsync(settingKey, JsonSerializer.Serialize(configured)).ConfigureAwait(true);
        }
    }

    private async Task LaunchEngineAsync(UnrealEngineInstallation engine)
    {
        await _platformLauncher.StartDetachedAsync(
            engine.EditorPath,
            workingDirectory: engine.RootPath).ConfigureAwait(true);
        StatusMessage = $"{engine.DisplayName} lancé.";
    }

    private async Task LaunchProjectAsync(UnrealProject project)
    {
        var engine = ResolveEngine(project.EngineAssociation)
            ?? throw new InvalidOperationException(
                $"Aucun moteur local ne correspond à l’association '{project.EngineAssociation}'.");
        await _platformLauncher.StartDetachedAsync(
            engine.EditorPath,
            [project.FilePath],
            engine.RootPath).ConfigureAwait(true);
        StatusMessage = $"{project.Name} lancé avec {engine.DisplayName}.";
    }

    private async Task LaunchLocalEngineAsync(LocalEngineInstallation installation)
    {
        var provider = ResolveLocalProvider(installation.ProviderId, installation.Family);
        var request = provider.CreateEditorLaunch(installation);
        await _platformLauncher.StartDetachedAsync(
            request.ExecutablePath,
            request.Arguments,
            request.WorkingDirectory).ConfigureAwait(true);
        StatusMessage = $"{installation.DisplayName} lancé.";
    }

    private async Task LaunchLocalProjectAsync(LocalEngineProject project)
    {
        var provider = ResolveLocalProvider(project.ProviderId, project.Family);
        var request = provider.CreateProjectLaunch(project, _currentLocalEngineInstallations);
        await _platformLauncher.StartDetachedAsync(
            request.ExecutablePath,
            request.Arguments,
            request.WorkingDirectory).ConfigureAwait(true);
        StatusMessage = $"{project.Name} lancé avec {project.Family}.";
    }

    private ILocalEngineProvider ResolveLocalProvider(string providerId, EngineFamily family) =>
        _engineModules.Modules
            .OfType<ILocalEngineProvider>()
            .SingleOrDefault(provider =>
                provider.Descriptor.Family == family &&
                string.Equals(provider.Descriptor.Id, providerId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"Provider local indisponible : {providerId}.");

    private Task OpenFolderAsync(string path) =>
        _platformLauncher.OpenFolderAsync(path);

    private Task SetupEngineAsync(UnrealEngineInstallation engine)
    {
        var request = UnrealBuildCommandFactory.CreateSetup(new SetupEngineRequest(engine));
        return ExecuteUnrealOperationAsync(
            $"Setup · {engine.DisplayName}",
            request);
    }

    private async Task RegisterEngineAsync(UnrealEngineInstallation engine)
    {
        await AddUniquePathAsync(EngineRootsSetting, engine.RootPath).ConfigureAwait(true);
        await SetWorkspaceEngineHiddenAsync(engine.RootPath, hidden: false).ConfigureAwait(true);
        var registration = await Task.Run(() => _engineRegistration.Register(engine.RootPath))
            .ConfigureAwait(true);
        await RefreshCatalogAsync().ConfigureAwait(true);
        StatusMessage = $"{engine.DisplayName} enregistré sous {registration.Identifier}.";
    }

    private async Task UnregisterEngineAsync(UnrealEngineInstallation engine)
    {
        await AddUniquePathAsync(EngineRootsSetting, engine.RootPath).ConfigureAwait(true);
        var removed = await Task.Run(() => _engineRegistration.Unregister(engine.RootPath))
            .ConfigureAwait(true);
        await RefreshCatalogAsync().ConfigureAwait(true);
        StatusMessage = removed
            ? $"{engine.DisplayName} désenregistré. Son chemin reste connu de CyEngineHub."
            : $"Aucune association utilisateur trouvée pour {engine.DisplayName}.";
    }

    private Task GenerateEngineProjectFilesAsync(UnrealEngineInstallation engine)
    {
        var request = UnrealBuildCommandFactory.CreateGenerateProjectFiles(
            new GenerateProjectFilesRequest(
                engine,
                Format: UnrealProjectFileFormat.VisualStudio2022));
        return ExecuteUnrealOperationAsync(
            $"Generate Project Files · {engine.DisplayName}",
            request);
    }

    private Task BuildEngineEditorAsync(UnrealEngineInstallation engine)
    {
        var request = UnrealBuildCommandFactory.CreateTargetBuild(new UnrealTargetBuildRequest(
            engine,
            "UnrealEditor",
            "Win64",
            "Development"));
        return ExecuteUnrealOperationAsync(
            $"Build UnrealEditor · {engine.DisplayName}",
            request);
    }

    private Task GenerateProjectProjectFilesAsync(UnrealProject project)
    {
        var engine = ResolveEngine(project.EngineAssociation)
            ?? throw new InvalidOperationException(
                $"Aucun moteur local ne correspond à l’association '{project.EngineAssociation}'.");
        var request = UnrealBuildCommandFactory.CreateGenerateProjectFiles(
            new GenerateProjectFilesRequest(
                engine,
                project,
                UnrealProjectFileFormat.VisualStudio2022));
        return ExecuteUnrealOperationAsync(
            $"Generate Project Files · {project.Name}",
            request);
    }

    private Task BuildProjectEditorAsync(UnrealProject project)
    {
        var engine = ResolveEngine(project.EngineAssociation)
            ?? throw new InvalidOperationException(
                $"Aucun moteur local ne correspond à l’association '{project.EngineAssociation}'.");
        var request = UnrealBuildCommandFactory.CreateTargetBuild(new UnrealTargetBuildRequest(
            engine,
            $"{project.Name}Editor",
            "Win64",
            "Development",
            Project: project));
        return ExecuteUnrealOperationAsync(
            $"Build {project.Name}Editor · Win64 Development",
            request);
    }

    private Task PackageProjectAsync(UnrealProject project)
    {
        var engine = ResolveEngine(project.EngineAssociation)
            ?? throw new InvalidOperationException(
                $"Aucun moteur local ne correspond à l’association '{project.EngineAssociation}'.");
        var archive = Path.Combine(
            project.RootPath,
            "Saved",
            "CyEngineHubPackages",
            "Win64",
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var request = UnrealBuildCommandFactory.CreatePackage(new UnrealPackageRequest(
            engine,
            project,
            "Win64",
            "Development",
            archive));
        return ExecuteUnrealOperationAsync(
            $"Package {project.Name} · Win64 Development",
            request);
    }

    private async Task LoadBuildPresetsAsync()
    {
        var presets = await _buildPresetCatalog.GetAllAsync().ConfigureAwait(true);
        BuildPresets.Clear();
        foreach (var preset in presets)
        {
            BuildPresets.Add(new BuildPresetCardViewModel(
                preset,
                RunBuildPresetAsync,
                EditBuildPreset,
                DeleteBuildPresetAsync,
                ShowError));
        }

        OnPropertyChanged(nameof(HasNoBuildPresets));
    }

    private async Task RefreshToolchainsAsync()
    {
        var keepEngineProfileOpen = _section == PageSection.EngineDetails;
        var engines = _currentEngines.ToArray();
        IsToolchainBusy = true;
        ToolchainStatusSummary = "Inventaire manuel des toolchains en cours…";
        ToolchainSnapshot? snapshot = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Toolchains",
            "Analyser les toolchains",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<ToolchainRefreshProgress>(value =>
                {
                    var ratio = value.Total == 0
                        ? 0
                        : Math.Clamp((double)value.Completed / value.Total, 0, 1);
                    context.Report(ratio, value.Message);
                });
                snapshot = await _toolchainInventory.RefreshAsync(
                        engines,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                await _toolchainSnapshotCache.SaveAsync(snapshot, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsToolchainBusy = false;
        if (completed.Status == HubTaskStatus.Canceled)
        {
            ToolchainStatusSummary = "Analyse Toolchains annulée; le cache précédent est conservé.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed || snapshot is null)
        {
            ToolchainStatusSummary = $"Échec de l’analyse Toolchains : {completed.Error}";
            return;
        }

        ReplaceToolchainSnapshot(snapshot);
        ToolchainStatusSummary =
            $"{snapshot.Installations.Count} installation(s) · " +
            $"{ToolchainProblemCount} problème(s) · " +
            $"{snapshot.AndroidDevices.Count} appareil(s) ADB · cache actualisé";
        StatusMessage = "Inventaire Toolchains actualisé.";
        if (!keepEngineProfileOpen)
        {
            ShowSection(PageSection.Toolchains);
        }
    }

    private void ReplaceToolchainSnapshot(ToolchainSnapshot snapshot)
    {
        _currentToolchainSnapshot = snapshot;
        ToolchainInstallations.Clear();
        foreach (var installation in snapshot.Installations)
        {
            ToolchainInstallations.Add(new ToolchainInstallationCardViewModel(
                installation,
                OpenFolderAsync,
                ShowError));
        }

        EngineToolchains.Clear();
        foreach (var engine in snapshot.Engines.OrderBy(
                     item => item.EngineDisplayName,
                     StringComparer.OrdinalIgnoreCase))
        {
            EngineToolchains.Add(new EngineToolchainCardViewModel(engine));
        }
        RefreshSelectedEngineToolchains();

        ToolchainEnvironmentVariables.Clear();
        foreach (var variable in snapshot.EnvironmentVariables)
        {
            ToolchainEnvironmentVariables.Add(new EnvironmentVariableCardViewModel(variable));
        }

        AndroidDevices.Clear();
        foreach (var device in snapshot.AndroidDevices)
        {
            AndroidDevices.Add(new AndroidDeviceCardViewModel(device));
        }

        OnPropertyChanged(nameof(HasNoToolchainInstallations));
        OnPropertyChanged(nameof(HasNoEngineToolchains));
        OnPropertyChanged(nameof(HasNoAndroidDevices));
        OnPropertyChanged(nameof(ToolchainProblemCount));
        OnPropertyChanged(nameof(WarningCount));
    }

    private async Task RefreshStorageAsync()
    {
        CancelPendingStorageCleanup();
        var request = new StorageAnalysisRequest(
            _currentEngines.ToArray(),
            Projects.Select(project => project.Project).ToArray(),
            GitRepositories.Select(repository => repository.Snapshot).ToArray());
        IsStorageBusy = true;
        StorageStatusSummary = "Analyse manuelle et bornée des emplacements de stockage…";
        StorageSnapshot? snapshot = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Storage",
            "Analyser le stockage Unreal",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<StorageAnalysisProgress>(value =>
                {
                    var ratio = value.Total == 0
                        ? 0
                        : Math.Clamp((double)value.Completed / value.Total, 0, 1);
                    context.Report(ratio, value.Message);
                });
                snapshot = await _storageAnalysis.AnalyzeAsync(
                        request,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                await _storageSnapshotCache.SaveAsync(snapshot, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsStorageBusy = false;
        if (completed.Status == HubTaskStatus.Canceled)
        {
            StorageStatusSummary = "Analyse Stockage annulée; le cache précédent est conservé.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed || snapshot is null)
        {
            StorageStatusSummary = $"Échec de l’analyse Stockage : {completed.Error}";
            return;
        }

        ReplaceStorageSnapshot(snapshot);
        StorageStatusSummary =
            $"{snapshot.Entries.Count} emplacement(s) · {StorageReclaimableSize} récupérable(s) · " +
            $"{StorageWarningCount} résultat(s) à vérifier · cache actualisé";
        StatusMessage = "Inventaire Stockage actualisé.";
        ShowSection(PageSection.Storage);
    }

    private void ReplaceStorageSnapshot(StorageSnapshot snapshot)
    {
        _currentStorageSnapshot = snapshot;
        StorageEntries.Clear();
        foreach (var entry in snapshot.Entries)
        {
            StorageEntries.Add(new StorageEntryCardViewModel(
                entry,
                OpenFolderAsync,
                PrepareStorageCleanup,
                ShowError));
        }

        StorageDrives.Clear();
        foreach (var drive in snapshot.Drives)
        {
            StorageDrives.Add(new StorageDriveCardViewModel(drive));
        }

        foreach (var engine in Engines)
        {
            engine.UpdateEffectiveSize(GetEngineEffectiveSizeLabel(engine.Engine.RootPath));
        }
        foreach (var engine in LocalEngineInstallations)
        {
            engine.UpdateEffectiveSize(GetEngineEffectiveSizeLabel(engine.Installation.RootPath));
        }

        OnPropertyChanged(nameof(HasNoStorageEntries));
        OnPropertyChanged(nameof(HasNoStorageDrives));
        OnPropertyChanged(nameof(StorageWarningCount));
        OnPropertyChanged(nameof(StorageEngineSize));
        OnPropertyChanged(nameof(StorageProjectSize));
        OnPropertyChanged(nameof(StorageDdcSize));
        OnPropertyChanged(nameof(StorageVaultSize));
        OnPropertyChanged(nameof(StorageLfsSize));
        OnPropertyChanged(nameof(StorageReclaimableSize));
        OnPropertyChanged(nameof(WarningCount));
    }

    private void PrepareStorageCleanup(StorageEntry entry)
    {
        _pendingStorageCleanup = _storageCleanup.Prepare(entry);
        PendingStorageCleanupSummary =
            $"{_pendingStorageCleanup.DisplayName} · " +
            $"{FormatBytes(_pendingStorageCleanup.EstimatedBytes)} estimés · " +
            $"{_pendingStorageCleanup.EstimatedFileCount:N0} fichier(s). " +
            "La suppression est permanente et limitée au dossier affiché.";
        OnPropertyChanged(nameof(PendingStorageCleanup));
        OnPropertyChanged(nameof(HasPendingStorageCleanup));
        _confirmStorageCleanupCommand.NotifyCanExecuteChanged();
        ShowSection(PageSection.Storage);
    }

    private void CancelPendingStorageCleanup()
    {
        _pendingStorageCleanup = null;
        PendingStorageCleanupSummary = string.Empty;
        OnPropertyChanged(nameof(PendingStorageCleanup));
        OnPropertyChanged(nameof(HasPendingStorageCleanup));
        _confirmStorageCleanupCommand.NotifyCanExecuteChanged();
    }

    private async Task ConfirmStorageCleanupAsync()
    {
        var plan = PendingStorageCleanup;
        if (plan is null)
        {
            return;
        }

        IsStorageBusy = true;
        StorageStatusSummary = $"Safe Clean · {plan.DisplayName}…";
        StorageCleanupResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Storage",
            $"Safe Clean · {plan.DisplayName}",
            async (context, cancellationToken) =>
            {
                context.Report(0.05, "Validation de la cible exacte");
                result = await _storageCleanup.ExecuteAsync(plan, cancellationToken)
                    .ConfigureAwait(false);
                context.Report(1, "Dossier régénérable supprimé");
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsStorageBusy = false;
        CancelPendingStorageCleanup();
        if (completed.Status == HubTaskStatus.Canceled)
        {
            StorageStatusSummary =
                "Safe Clean annulé; relancez l’analyse pour vérifier un éventuel nettoyage partiel.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            StorageStatusSummary = $"Échec du Safe Clean : {completed.Error}";
            return;
        }

        await RefreshStorageAsync().ConfigureAwait(true);
        StorageStatusSummary =
            $"Safe Clean terminé · environ {FormatBytes(result.EstimatedBytes)} libéré(s) · " +
            "inventaire actualisé";
        StatusMessage = $"Safe Clean terminé : {plan.DisplayName}.";
    }

    private async Task RefreshHubExtensionsAsync()
    {
        IsHubExtensionBusy = true;
        HubExtensionStatus = "Analyse des extensions intégrées et utilisateur…";
        IReadOnlyList<HubExtensionSnapshot> extensions = [];
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Extensions Hub",
            "Actualiser les extensions CyEngineHub",
            async (context, cancellationToken) =>
            {
                context.Report(0.2, "Validation des manifests extension.json");
                extensions = await _hubExtensionCatalog.DiscoverAsync(
                        _builtInExtensionsDirectory,
                        _userExtensionsDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                context.Report(1, "Catalogue d’extensions prêt");
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsHubExtensionBusy = false;
        if (completed.Status != HubTaskStatus.Completed)
        {
            HubExtensionStatus = completed.Status == HubTaskStatus.Canceled
                ? "Analyse des extensions annulée."
                : $"Échec du catalogue d’extensions : {completed.Error}";
            return;
        }

        ReplaceHubExtensions(extensions);
        await RebuildMarketplaceConnectorsAsync(extensions).ConfigureAwait(true);
        var invalidCount = extensions.Count(extension => extension.Health != HubExtensionHealth.Ready);
        HubExtensionStatus =
            $"{extensions.Count:N0} extension(s) · {invalidCount:N0} invalide(s) · " +
            "outils utilisateur non exécutés sans approbation first-party";
    }

    private void ReplaceHubExtensions(IReadOnlyList<HubExtensionSnapshot> extensions)
    {
        var selectedId = SelectedHubExtension?.Snapshot.Manifest.Id;
        HubExtensions.Clear();
        foreach (var extension in extensions)
        {
            HubExtensions.Add(new HubExtensionCardViewModel(extension));
        }

        SelectedHubExtension = HubExtensions.FirstOrDefault(extension =>
            string.Equals(
                extension.Snapshot.Manifest.Id,
                selectedId,
                StringComparison.OrdinalIgnoreCase)) ?? HubExtensions.FirstOrDefault();
        RebuildHubUiContributions();
        OnPropertyChanged(nameof(HasNoHubExtensions));
    }

    private void RebuildHubUiContributions()
    {
        HubGlobalToolbarContributions.Clear();
        HubEngineToolbarContributions.Clear();
        HubProjectToolbarContributions.Clear();
        HubPluginToolbarContributions.Clear();

        foreach (var extension in HubExtensions)
        {
            foreach (var contribution in extension.Snapshot.Manifest.UiContributions
                         .OrderBy(item => item.Order)
                         .ThenBy(item => item.Label, StringComparer.OrdinalIgnoreCase))
            {
                var button = new HubUiContributionButtonViewModel(
                    extension,
                    contribution,
                    InvokeHubUiContributionAsync,
                    ShowError);
                var target = contribution.Placement switch
                {
                    HubUiPlacement.EnginesToolbar => HubEngineToolbarContributions,
                    HubUiPlacement.ProjectsToolbar => HubProjectToolbarContributions,
                    HubUiPlacement.PluginsToolbar => HubPluginToolbarContributions,
                    _ => HubGlobalToolbarContributions,
                };
                target.Add(button);
            }
        }
    }

    private async Task InvokeHubUiContributionAsync(
        HubExtensionCardViewModel extension,
        HubUiContribution contribution)
    {
        SelectedHubExtension = extension;
        if (contribution.Action == HubUiAction.OpenWebsite)
        {
            await OpenSelectedHubExtensionWebsiteAsync().ConfigureAwait(true);
            return;
        }

        if (contribution.Action == HubUiAction.OpenExtension)
        {
            RequestSelectedHubExtensionWindow();
            return;
        }

        ShowSection(PageSection.HubExtensions);
        StatusMessage = contribution.Action == HubUiAction.PrepareExecution
            ? $"Extension {extension.Name} sélectionnée. Renseignez sa source et sa destination avant confirmation."
            : $"Extension {extension.Name} ouverte depuis la contribution {contribution.Label}.";
    }

    private void RequestSelectedHubExtensionWindow()
    {
        var extension = SelectedHubExtension
            ?? throw new InvalidOperationException("Sélectionnez une extension.");
        if (!extension.HasDedicatedWindow)
        {
            throw new InvalidOperationException("Cette extension ne déclare aucune fenêtre dédiée.");
        }

        StatusMessage = $"Ouverture de l’interface dédiée : {extension.Name}.";
        ExtensionWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private async Task PrepareHubExtensionInstallAsync()
    {
        IsHubExtensionBusy = true;
        HubExtensionStatus = "Inventaire et validation du package d’extension…";
        HubExtensionInstallPlan? plan = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Extensions Hub",
            "Préparer l’installation d’une extension",
            async (context, cancellationToken) =>
            {
                context.Report(0.1, "Validation du manifeste et des chemins");
                plan = await _hubExtensionInstallation.PrepareAsync(
                        ManualHubExtensionPackage,
                        _userExtensionsDirectory,
                        cancellationToken)
                    .ConfigureAwait(false);
                context.Report(1, "Empreintes du package vérifiées");
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsHubExtensionBusy = false;
        if (completed.Status != HubTaskStatus.Completed || plan is null)
        {
            HubExtensionStatus = completed.Status == HubTaskStatus.Canceled
                ? "Préparation de l’installation annulée."
                : $"Package refusé : {completed.Error}";
            return;
        }

        _pendingHubExtensionInstall = plan;
        PendingHubExtensionInstallSummary =
            $"{plan.Manifest.DisplayName} {plan.Manifest.Version} · " +
            $"{plan.Files.Count:N0} fichier(s), {FormatBytes(plan.TotalBytes)} → " +
            $"{plan.TargetDirectory}. " +
            (plan.ReplacesExisting
                ? "La version précédente sera déplacée dans .backups."
                : "Nouvelle installation.") +
            " Les exécutables utilisateur restent désactivés tant qu’ils ne sont pas approuvés first-party.";
        OnPropertyChanged(nameof(PendingHubExtensionInstall));
        OnPropertyChanged(nameof(HasPendingHubExtensionInstall));
        _confirmHubExtensionInstallCommand.NotifyCanExecuteChanged();
        HubExtensionStatus = "Plan d’installation prêt · confirmation explicite requise.";
        ShowSection(PageSection.HubExtensions);
    }

    private async Task ConfirmHubExtensionInstallAsync()
    {
        var plan = PendingHubExtensionInstall
            ?? throw new InvalidOperationException("Aucune extension à installer.");
        IsHubExtensionBusy = true;
        HubExtensionInstallResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Extensions Hub",
            $"Installer · {plan.Manifest.DisplayName}",
            async (context, cancellationToken) =>
            {
                context.Report(0.1, "Nouvelle validation du package confirmé");
                result = await _hubExtensionInstallation.ExecuteAsync(plan, cancellationToken)
                    .ConfigureAwait(false);
                context.Report(1, "Installation transactionnelle terminée");
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsHubExtensionBusy = false;
        if (completed.Status != HubTaskStatus.Completed || result is null)
        {
            HubExtensionStatus = completed.Status == HubTaskStatus.Canceled
                ? "Installation annulée."
                : $"Échec de l’installation : {completed.Error}";
            return;
        }

        CancelPendingHubExtensionInstall();
        ManualHubExtensionPackage = string.Empty;
        await RefreshHubExtensionsAsync().ConfigureAwait(true);
        HubExtensionStatus = result.BackupDirectory is null
            ? $"{result.Extension.Manifest.DisplayName} installé."
            : $"{result.Extension.Manifest.DisplayName} mis à jour · backup : {result.BackupDirectory}";
        StatusMessage = "Extension CyEngineHub installée.";
    }

    private void CancelPendingHubExtensionInstall()
    {
        _pendingHubExtensionInstall = null;
        PendingHubExtensionInstallSummary = string.Empty;
        OnPropertyChanged(nameof(PendingHubExtensionInstall));
        OnPropertyChanged(nameof(HasPendingHubExtensionInstall));
        _confirmHubExtensionInstallCommand.NotifyCanExecuteChanged();
    }

    private async Task OpenSelectedHubExtensionWebsiteAsync()
    {
        var extension = SelectedHubExtension?.Snapshot
            ?? throw new InvalidOperationException("Sélectionnez une extension.");
        if (extension.Manifest.Kind == HubExtensionKind.Marketplace)
        {
            var connector = MarketplaceConnectors.FirstOrDefault(item => item.Id == extension.Manifest.Id);
            if (connector is null || !connector.IsEnabled)
                throw new InvalidOperationException("Cette marketplace est désactivée ou son connecteur est invalide.");
            await OpenMarketplaceBrowserAsync(connector.Id, connector.Name, new Uri(connector.InventoryUrl)).ConfigureAwait(true);
            return;
        }
        if (!HubExtensionManifestParser.TryGetHttpsUri(extension.Manifest.Website, out var uri) ||
            uri is null)
        {
            throw new InvalidOperationException("Cette extension ne fournit pas de site HTTPS valide.");
        }

        await _platformLauncher.OpenUriAsync(uri).ConfigureAwait(true);
        HubExtensionStatus = $"Page officielle ouverte : {extension.Manifest.DisplayName}.";
    }

    private async Task PrepareHubExtensionExecutionAsync()
    {
        var selected = SelectedHubExtension?.Snapshot
            ?? throw new InvalidOperationException("Sélectionnez une extension.");
        if (!selected.CanRun)
        {
            throw new InvalidOperationException(
                "Seuls les outils first-party explicitement approuvés peuvent être exécutés.");
        }

        if (!Enum.TryParse<EngineFamily>(HubExtensionSourceEngine, true, out var sourceEngine) ||
            !Enum.TryParse<EngineFamily>(HubExtensionTargetEngine, true, out var targetEngine))
        {
            throw new ArgumentException("Le moteur source ou cible est invalide.");
        }

        IsHubExtensionBusy = true;
        HubExtensionExecutionPlan? plan = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Extensions Hub",
            $"Préparer · {selected.Manifest.DisplayName}",
            async (context, cancellationToken) =>
            {
                context.Report(0.2, "Validation de l’Asset Bridge first-party");
                plan = await _hubExtensionExecution.PrepareAsync(
                        selected,
                        HubExtensionInputPath,
                        HubExtensionOutputPath,
                        sourceEngine,
                        targetEngine,
                        cancellationToken)
                    .ConfigureAwait(false);
                context.Report(1, "Empreinte de l’exécutable enregistrée");
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsHubExtensionBusy = false;
        if (completed.Status != HubTaskStatus.Completed || plan is null)
        {
            HubExtensionStatus = completed.Status == HubTaskStatus.Canceled
                ? "Préparation de l’Asset Bridge annulée."
                : $"Exécution refusée : {completed.Error}";
            return;
        }

        _pendingHubExtensionExecution = plan;
        var inputSummary = plan.InputSummary;
        PendingHubExtensionExecutionSummary =
            $"{plan.Extension.Manifest.DisplayName} · {plan.SourceEngine} → {plan.TargetEngine} · " +
            $"{inputSummary.TransferableFileCount} fichier(s) transférable(s), " +
            $"{FormatBytes(inputSummary.TransferableBytes)} · " +
            $"{inputSummary.EngineNativeFileCount} natif(s) ignoré(s) · " +
            $"{inputSummary.UnsupportedFileCount} non pris en charge · " +
            $"source : {plan.InputPath} · sortie sécurisée : {plan.OutputPath}. " +
            "Aucun fichier existant ne sera écrasé.";
        OnPropertyChanged(nameof(PendingHubExtensionExecution));
        OnPropertyChanged(nameof(HasPendingHubExtensionExecution));
        _confirmHubExtensionExecutionCommand.NotifyCanExecuteChanged();
        HubExtensionStatus = "Plan Asset Bridge prêt · confirmation explicite requise.";
    }

    private async Task ConfirmHubExtensionExecutionAsync()
    {
        var plan = PendingHubExtensionExecution
            ?? throw new InvalidOperationException("Aucune exécution d’extension à confirmer.");
        if (!plan.Extension.CanRun)
        {
            throw new InvalidOperationException("L’extension n’est pas approuvée pour l’exécution locale.");
        }

        IsHubExtensionBusy = true;
        HubExtensionExecutionResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Extensions Hub",
            $"Exécuter · {plan.Extension.Manifest.DisplayName}",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<ProcessOutputLine>(line =>
                    context.Report(null, line.Text));
                result = await _hubExtensionExecution.ExecuteAsync(
                        plan,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));
        var completed = await handle.Completion.ConfigureAwait(true);
        IsHubExtensionBusy = false;
        if (completed.Status != HubTaskStatus.Completed || result is null)
        {
            HubExtensionStatus = completed.Status == HubTaskStatus.Canceled
                ? "Asset Bridge annulé."
                : $"Échec de l’Asset Bridge : {completed.Error}";
            return;
        }

        CancelPendingHubExtensionExecution();
        var capturedOutput = result.ProcessResult.CapturedOutput;
        var lastOutput = capturedOutput.Count == 0 ? null : capturedOutput[^1].Text;
        HubExtensionStatus = result.ProcessResult.Succeeded
            ? $"Asset Bridge terminé. {lastOutput}".Trim()
            : $"Asset Bridge terminé avec le code {result.ProcessResult.ExitCode}. {lastOutput}".Trim();
        StatusMessage = "Asset Bridge terminé.";
    }

    private void CancelPendingHubExtensionExecution()
    {
        _pendingHubExtensionExecution = null;
        PendingHubExtensionExecutionSummary = string.Empty;
        OnPropertyChanged(nameof(PendingHubExtensionExecution));
        OnPropertyChanged(nameof(HasPendingHubExtensionExecution));
        _confirmHubExtensionExecutionCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshPluginsAsync()
    {
        var libraryRoots = await ReadConfiguredPathsAsync(PluginLibraryRootsSetting)
            .ConfigureAwait(true);
        var engines = _currentEngines.ToArray();
        var projects = Projects.Select(item => item.Project).ToArray();
        var localProjects = LocalEngineProjects.Select(item => item.Project).ToArray();
        IsPluginBusy = true;
        PluginStatusSummary = "Analyse des plugins Unreal et packages multi-moteurs…";
        IReadOnlyList<UnrealPluginDescriptor> plugins = [];
        IReadOnlyList<LocalEnginePackageInventorySnapshot> packageSnapshots = [];
        IReadOnlyList<LocalEnginePackageInstallReceipt> packageInstallHistory = [];
        IReadOnlyList<LocalEnginePackageRemovalReceipt> packageRemovalHistory = [];
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            "Actualiser le catalogue Plugins",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<PluginDiscoveryProgress>(value =>
                {
                    var ratio = value.Total == 0
                        ? 0
                        : Math.Clamp((double)value.Completed / value.Total, 0, 1);
                    context.Report(ratio, value.Message);
                });
                plugins = await _pluginDiscovery.RefreshAsync(
                        new PluginDiscoveryRequest(engines, projects, libraryRoots),
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                await _pluginSnapshotCache.SaveAsync(plugins, cancellationToken)
                    .ConfigureAwait(false);
                context.Report(0.82, "Inventaire UPM, addons Godot et plugins CRYENGINE");
                var snapshots = new List<LocalEnginePackageInventorySnapshot>();
                foreach (var provider in _engineModules.Modules
                             .OfType<ILocalEnginePackageProvider>()
                             .Where(item => IsProviderEnabled(item.Descriptor.Id)))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    snapshots.Add(await provider.DiscoverPackagesAsync(localProjects, cancellationToken)
                        .ConfigureAwait(false));
                }

                packageSnapshots = snapshots;
                packageInstallHistory = await DiscoverLocalPackageInstallHistoryAsync(
                        localProjects,
                        cancellationToken)
                    .ConfigureAwait(false);
                packageRemovalHistory = await DiscoverLocalPackageRemovalHistoryAsync(
                        localProjects,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status == HubTaskStatus.Canceled)
        {
            PluginStatusSummary = "Analyse Plugins annulée; le cache précédent est conservé.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed)
        {
            PluginStatusSummary = $"Échec de l’analyse Plugins : {completed.Error}";
            return;
        }

        ReplacePluginCatalog(plugins);
        ReplaceLocalEnginePackageCatalog(packageSnapshots);
        ReplaceLocalEnginePackageInstallHistory(packageInstallHistory);
        ReplaceLocalEnginePackageRemovalHistory(packageRemovalHistory);
        var warningCount = plugins.Count(plugin => plugin.Health != PluginHealth.Healthy) +
                           _currentLocalEnginePackages.Count(package =>
                               package.Health != LocalEnginePackageHealth.Ready);
        PluginStatusSummary =
            $"{plugins.Count} plugin(s) Unreal · {_currentLocalEnginePackages.Length} package(s)/addon(s) · " +
            $"{warningCount} à vérifier";
        StatusMessage = "Catalogue Plugins & packages actualisé.";
    }

    private void ReplacePluginCatalog(IReadOnlyList<UnrealPluginDescriptor> plugins)
    {
        _currentPlugins = plugins;
        Plugins.Clear();
        foreach (var plugin in plugins)
        {
            Plugins.Add(new PluginCardViewModel(
                plugin,
                CreateOrganization(
                    WorkspaceItemKind.Plugin,
                    plugin.RootPath,
                    plugin.FriendlyName),
                OpenFolderAsync,
                PreparePluginInstallAsync,
                BuildPluginAsync,
                ShowError));
        }

        OnPropertyChanged(nameof(HasNoPlugins));
        OnPropertyChanged(nameof(PluginCount));
        OnPropertyChanged(nameof(WarningCount));
        ApplyEngineFamilyFilters();
    }

    private void ReplaceLocalEnginePackageCatalog(
        IReadOnlyList<LocalEnginePackageInventorySnapshot> snapshots)
    {
        _currentLocalPackageSnapshots = snapshots;
        _currentLocalEnginePackages = snapshots
            .SelectMany(snapshot => snapshot.Packages)
            .OrderBy(package => package.Family)
            .ThenBy(package => package.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        LocalEnginePackages.Clear();
        foreach (var package in _currentLocalEnginePackages)
        {
            LocalEnginePackages.Add(new LocalEnginePackageCardViewModel(
                package,
                CreateOrganization(
                    WorkspaceItemKind.Package,
                    package.RootPath,
                    package.Name),
                OpenFolderAsync,
                PrepareLocalEnginePackageRemovalAsync,
                ShowError));
        }

        OnPropertyChanged(nameof(HasNoLocalEnginePackages));
        OnPropertyChanged(nameof(HasLocalEnginePackages));
        OnPropertyChanged(nameof(PluginCount));
        OnPropertyChanged(nameof(WarningCount));
        ApplyEngineFamilyFilters();
    }

    private async Task<IReadOnlyList<LocalEnginePackageInstallReceipt>> DiscoverLocalPackageInstallHistoryAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken)
    {
        var receipts = new List<LocalEnginePackageInstallReceipt>();
        foreach (var provider in _engineModules.Modules.OfType<ILocalEnginePackageInstallationProvider>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            receipts.AddRange(await provider.LoadPackageInstallHistoryAsync(projects, cancellationToken)
                .ConfigureAwait(false));
        }

        return receipts.OrderByDescending(receipt => receipt.InstalledAt).ToArray();
    }

    private void ReplaceLocalEnginePackageInstallHistory(
        IReadOnlyList<LocalEnginePackageInstallReceipt> receipts)
    {
        LocalEnginePackageInstallHistory.Clear();
        foreach (var receipt in receipts)
        {
            LocalEnginePackageInstallHistory.Add(new LocalEnginePackageInstallHistoryCardViewModel(
                receipt,
                PrepareLocalEnginePackageRollbackAsync,
                ShowError));
        }

        OnPropertyChanged(nameof(HasLocalEnginePackageInstallHistory));
        OnPropertyChanged(nameof(HasNoLocalEnginePackageInstallHistory));
    }

    private async Task<IReadOnlyList<LocalEnginePackageRemovalReceipt>> DiscoverLocalPackageRemovalHistoryAsync(
        IReadOnlyCollection<LocalEngineProject> projects,
        CancellationToken cancellationToken)
    {
        var receipts = new List<LocalEnginePackageRemovalReceipt>();
        foreach (var provider in _engineModules.Modules.OfType<ILocalEnginePackageManagementProvider>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            receipts.AddRange(await provider.LoadPackageRemovalHistoryAsync(projects, cancellationToken)
                .ConfigureAwait(false));
        }

        return receipts.OrderByDescending(receipt => receipt.RemovedAt).ToArray();
    }

    private void ReplaceLocalEnginePackageRemovalHistory(
        IReadOnlyList<LocalEnginePackageRemovalReceipt> receipts)
    {
        LocalEnginePackageRemovalHistory.Clear();
        foreach (var receipt in receipts)
        {
            LocalEnginePackageRemovalHistory.Add(new LocalEnginePackageRemovalHistoryCardViewModel(
                receipt,
                PrepareLocalEnginePackageRestoreAsync,
                ShowError));
        }

        OnPropertyChanged(nameof(HasLocalEnginePackageRemovalHistory));
        OnPropertyChanged(nameof(HasNoLocalEnginePackageRemovalHistory));
    }

    private void RebuildPluginTargets()
    {
        var selectedId = SelectedPluginTarget?.Target.Id;
        var targets = _currentEngines
            .Select(engine => new PluginInstallTarget(
                $"engine:{engine.Id}",
                $"Moteur · {engine.DisplayName}",
                PluginInstallTargetKind.Engine,
                Path.Combine(engine.RootPath, "Engine", "Plugins"),
                engine))
            .Concat(Projects.Select(project =>
            {
                var engine = ResolveEngine(project.Project.EngineAssociation);
                return engine is null
                    ? null
                    : new PluginInstallTarget(
                        $"project:{project.Project.Id}",
                        $"Projet · {project.Name} · {engine.DisplayName}",
                        PluginInstallTargetKind.Project,
                        Path.Combine(project.Project.RootPath, "Plugins"),
                        engine);
            }).Where(target => target is not null).Select(target => target!))
            .ToArray();

        PluginTargets.Clear();
        foreach (var target in targets)
        {
            PluginTargets.Add(new PluginInstallTargetViewModel(target));
        }

        SelectedPluginTarget = PluginTargets.FirstOrDefault(item =>
            string.Equals(item.Target.Id, selectedId, StringComparison.Ordinal))
            ?? PluginTargets.FirstOrDefault();
        OnPropertyChanged(nameof(HasPluginTargets));
    }

    private async Task PreparePluginInstallAsync(UnrealPluginDescriptor plugin)
    {
        var target = SelectedPluginTarget?.Target
            ?? throw new InvalidOperationException(
                "Sélectionnez un moteur ou un projet cible avant de préparer l’installation.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Préparation de {plugin.FriendlyName}…";
        PluginInstallPlan? plan = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Préparer l’installation · {plugin.FriendlyName}",
            async (context, cancellationToken) =>
            {
                context.Report(null, "Inventaire et compatibilité");
                plan = await _pluginInstallation.PlanAsync(
                        plugin,
                        target,
                        "Win64",
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || plan is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Préparation de l’installation annulée."
                : $"Installation impossible : {completed.Error}";
            return;
        }

        SetPendingPluginInstall(plan);
        ShowSection(PageSection.Plugins);
        PluginStatusSummary = "Plan prêt; confirmation explicite requise.";
    }

    private async Task ConfirmPluginInstallAsync()
    {
        var plan = PendingPluginInstall
            ?? throw new InvalidOperationException("Aucune installation Plugin à confirmer.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Installation de {plan.Plugin.FriendlyName}…";
        PluginInstallResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Installer · {plan.Plugin.FriendlyName}",
            [plan.Target.Kind == PluginInstallTargetKind.Engine
                ? EngineTaskLink(plan.Target.Engine.RootPath, plan.Target.DisplayName)
                : ProjectTaskLink(Path.GetDirectoryName(plan.Target.PluginsDirectory)!, plan.Target.DisplayName)],
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<PluginInstallProgress>(value =>
                {
                    var ratio = value.TotalFiles == 0
                        ? 0
                        : Math.Clamp((double)value.CompletedFiles / value.TotalFiles, 0, 1);
                    context.Report(ratio, value.Message);
                });
                result = await _pluginInstallation.InstallAsync(
                        plan,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || result is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Installation annulée avant le remplacement final."
                : $"Échec de l’installation : {completed.Error}";
            return;
        }

        CancelPendingPluginInstall();
        await RefreshPluginsAsync().ConfigureAwait(true);
        var backup = result.BackupDirectory is null
            ? "aucune version précédente"
            : $"sauvegarde conservée : {result.BackupDirectory}";
        PluginStatusSummary =
            $"{plan.Plugin.FriendlyName} installé · {result.CopiedFiles:N0} fichier(s) · {backup}";
        StatusMessage = $"Plugin installé dans {result.DestinationDirectory}.";
        ShowSection(PageSection.Plugins);
    }

    private Task BuildPluginAsync(UnrealPluginDescriptor plugin)
    {
        var target = SelectedPluginTarget?.Target
            ?? throw new InvalidOperationException(
                "Sélectionnez une cible pour choisir le moteur de BuildPlugin.");
        var output = Path.Combine(
            _pluginPackagesDirectory,
            plugin.Name,
            $"UE-{target.Engine.Version.Major}.{target.Engine.Version.Minor}",
            $"Win64-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        var request = UnrealBuildCommandFactory.CreateBuildPlugin(new BuildPluginRequest(
            target.Engine,
            plugin.DescriptorPath,
            output,
            ["Win64"],
            NoHostPlatform: true));
        return ExecuteUnrealOperationAsync(
            $"BuildPlugin {plugin.FriendlyName} · {target.Engine.DisplayName} · Win64",
            request);
    }

    private void SetPendingPluginInstall(PluginInstallPlan plan)
    {
        _pendingPluginInstall = plan;
        var replacement = plan.WillReplaceExisting
            ? "L’ancienne version sera déplacée dans .cyenginehub-backups."
            : "Nouvelle installation.";
        var rebuild = plan.Compatibility.RequiresRebuild
            ? " Recompilation recommandée pour ce moteur."
            : string.Empty;
        var warnings = plan.Compatibility.Warnings.Count == 0
            ? string.Empty
            : $" {string.Join(" ", plan.Compatibility.Warnings)}";
        PendingPluginInstallSummary =
            $"{plan.Plugin.FriendlyName} → {plan.Target.DisplayName} · " +
            $"{plan.FileCount:N0} fichier(s), {FormatBytes(plan.TotalBytes)}. " +
            replacement + rebuild + warnings;
        OnPropertyChanged(nameof(PendingPluginInstall));
        OnPropertyChanged(nameof(HasPendingPluginInstall));
        _confirmPluginInstallCommand.NotifyCanExecuteChanged();
    }

    private void CancelPendingPluginInstall()
    {
        if (_pendingPluginInstall is null && string.IsNullOrEmpty(PendingPluginInstallSummary))
        {
            return;
        }

        _pendingPluginInstall = null;
        PendingPluginInstallSummary = string.Empty;
        OnPropertyChanged(nameof(PendingPluginInstall));
        OnPropertyChanged(nameof(HasPendingPluginInstall));
        _confirmPluginInstallCommand.NotifyCanExecuteChanged();
    }

    private async Task PrepareLocalEnginePackageInstallAsync()
    {
        var target = SelectedLocalPackageInstallProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez un projet Unity ou Godot cible.");
        var provider = _engineModules.Modules
            .OfType<ILocalEnginePackageInstallationProvider>()
            .FirstOrDefault(item => item.Descriptor.Family == target.Family)
            ?? throw new InvalidOperationException(
                $"Le provider {target.Family} ne prend pas en charge l’installation locale de packages.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Analyse du package local pour {target.Name}…";
        LocalEnginePackageInstallPlan? plan = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Préparer package · {target.Name}",
            async (context, cancellationToken) =>
            {
                context.Report(null, "Validation du manifeste et calcul SHA-256");
                plan = await provider.PreparePackageInstallAsync(
                        ManualLocalEnginePackageSource,
                        target,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || plan is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Analyse du package annulée."
                : $"Package local non installable : {completed.Error}";
            return;
        }

        SetPendingLocalEnginePackageInstall(plan);
        PluginStatusSummary = "Plan package prêt; confirmation explicite requise.";
        ShowSection(PageSection.Plugins);
    }

    private async Task ConfirmLocalEnginePackageInstallAsync()
    {
        var plan = PendingLocalEnginePackageInstall
            ?? throw new InvalidOperationException("Aucune installation de package local à confirmer.");
        var provider = _engineModules.Modules
            .OfType<ILocalEnginePackageInstallationProvider>()
            .FirstOrDefault(item =>
                item.Descriptor.Family == plan.Family &&
                string.Equals(item.Descriptor.Id, plan.ProviderId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Le provider du plan d’installation n’est plus disponible.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Installation de {plan.DisplayName}…";
        LocalEnginePackageInstallResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Installer package · {plan.DisplayName}",
            [ProjectTaskLink(plan.TargetProject.RootPath, plan.TargetProject.Name)],
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<LocalEnginePackageInstallProgress>(value =>
                {
                    var ratio = value.TotalFiles == 0
                        ? 0
                        : Math.Clamp((double)value.CompletedFiles / value.TotalFiles, 0, 1);
                    context.Report(ratio, value.Message);
                });
                result = await provider.InstallPackageAsync(plan, progress, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || result is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Installation annulée avant le remplacement final."
                : $"Échec de l’installation du package : {completed.Error}";
            return;
        }

        CancelPendingLocalEnginePackageInstall();
        await RefreshPluginsAsync().ConfigureAwait(true);
        var backup = result.BackupDirectory is null
            ? "nouvelle installation"
            : $"ancienne version conservée : {result.BackupDirectory}";
        PluginStatusSummary =
            $"{plan.DisplayName} installé dans {plan.TargetProject.Name} · " +
            $"{result.CopiedFiles:N0} fichier(s) · {backup}";
        StatusMessage = $"Package installé dans {result.DestinationDirectory}.";
        ShowSection(PageSection.Plugins);
    }

    private void SetPendingLocalEnginePackageInstall(LocalEnginePackageInstallPlan plan)
    {
        CancelPendingLocalEnginePackageRollback();
        CancelPendingLocalEnginePackageRemoval();
        CancelPendingLocalEnginePackageRestore();
        _pendingLocalEnginePackageInstall = plan;
        var replacement = plan.ReplacesExisting
            ? "L’ancienne version sera sauvegardée avant remplacement."
            : "Nouvelle installation.";
        PendingLocalEnginePackageInstallSummary =
            $"{plan.DisplayName} {plan.Version} → {plan.TargetProject.Name} · " +
            $"{plan.Files.Count:N0} fichier(s), {FormatBytes(plan.TotalBytes)}. " +
            $"{replacement} {plan.RiskSummary}";
        OnPropertyChanged(nameof(PendingLocalEnginePackageInstall));
        OnPropertyChanged(nameof(HasPendingLocalEnginePackageInstall));
        _confirmLocalEnginePackageInstallCommand.NotifyCanExecuteChanged();
    }

    private async Task PrepareLocalEnginePackageRollbackAsync(
        LocalEnginePackageInstallReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var provider = _engineModules.Modules
            .OfType<ILocalEnginePackageInstallationProvider>()
            .FirstOrDefault(item =>
                item.Descriptor.Family == receipt.Family &&
                string.Equals(item.Descriptor.Id, receipt.ProviderId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Le provider du reçu n’est plus disponible.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Vérification du rollback de {receipt.DisplayName}…";
        LocalEnginePackageRollbackPlan? plan = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Préparer annulation package · {receipt.DisplayName}",
            async (context, cancellationToken) =>
            {
                context.Report(null, "Vérification des empreintes installée et sauvegardée");
                plan = await provider.PreparePackageRollbackAsync(receipt, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || plan is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Préparation du rollback annulée."
                : $"Rollback impossible : {completed.Error}";
            return;
        }

        SetPendingLocalEnginePackageRollback(plan);
        PluginStatusSummary = "Rollback vérifié; confirmation explicite requise.";
        ShowSection(PageSection.Plugins);
    }

    private async Task ConfirmLocalEnginePackageRollbackAsync()
    {
        var plan = PendingLocalEnginePackageRollback
            ?? throw new InvalidOperationException("Aucun rollback de package à confirmer.");
        var provider = _engineModules.Modules
            .OfType<ILocalEnginePackageInstallationProvider>()
            .FirstOrDefault(item =>
                item.Descriptor.Family == plan.Receipt.Family &&
                string.Equals(item.Descriptor.Id, plan.Receipt.ProviderId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Le provider du rollback n’est plus disponible.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Annulation de {plan.Receipt.DisplayName}…";
        LocalEnginePackageRollbackResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Annuler package · {plan.Receipt.DisplayName}",
            async (context, cancellationToken) =>
            {
                context.Report(null, "Revalidation et permutation transactionnelle");
                result = await provider.RollbackPackageAsync(plan, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || result is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Annulation du package interrompue avant la permutation."
                : $"Échec du rollback : {completed.Error}";
            return;
        }

        CancelPendingLocalEnginePackageRollback();
        await RefreshPluginsAsync().ConfigureAwait(true);
        PluginStatusSummary = result.RestoredPreviousVersion
            ? $"Version précédente de {plan.Receipt.DisplayName} restaurée · version retirée conservée : {result.RecoveryDirectory}"
            : $"{plan.Receipt.DisplayName} retiré · copie récupérable conservée : {result.RecoveryDirectory}";
        StatusMessage = $"Rollback package terminé pour {plan.Receipt.TargetProject.Name}.";
        ShowSection(PageSection.Plugins);
    }

    private void SetPendingLocalEnginePackageRollback(LocalEnginePackageRollbackPlan plan)
    {
        CancelPendingLocalEnginePackageInstall();
        CancelPendingLocalEnginePackageRemoval();
        CancelPendingLocalEnginePackageRestore();
        _pendingLocalEnginePackageRollback = plan;
        var action = plan.Receipt.BackupDirectory is null
            ? "Le package sera retiré du projet."
            : "La version précédente sauvegardée sera restaurée.";
        PendingLocalEnginePackageRollbackSummary =
            $"{plan.Receipt.DisplayName} {plan.Receipt.Version} · {plan.Receipt.TargetProject.Name}. " +
            $"{action} La version actuelle restera récupérable dans {plan.RecoveryDirectory}.";
        OnPropertyChanged(nameof(PendingLocalEnginePackageRollback));
        OnPropertyChanged(nameof(HasPendingLocalEnginePackageRollback));
        _confirmLocalEnginePackageRollbackCommand.NotifyCanExecuteChanged();
    }

    private void CancelPendingLocalEnginePackageRollback()
    {
        if (_pendingLocalEnginePackageRollback is null &&
            string.IsNullOrEmpty(PendingLocalEnginePackageRollbackSummary))
        {
            return;
        }

        _pendingLocalEnginePackageRollback = null;
        PendingLocalEnginePackageRollbackSummary = string.Empty;
        OnPropertyChanged(nameof(PendingLocalEnginePackageRollback));
        OnPropertyChanged(nameof(HasPendingLocalEnginePackageRollback));
        _confirmLocalEnginePackageRollbackCommand.NotifyCanExecuteChanged();
    }

    private void CancelPendingLocalEnginePackageInstall()
    {
        if (_pendingLocalEnginePackageInstall is null &&
            string.IsNullOrEmpty(PendingLocalEnginePackageInstallSummary))
        {
            return;
        }

        _pendingLocalEnginePackageInstall = null;
        PendingLocalEnginePackageInstallSummary = string.Empty;
        OnPropertyChanged(nameof(PendingLocalEnginePackageInstall));
        OnPropertyChanged(nameof(HasPendingLocalEnginePackageInstall));
        _confirmLocalEnginePackageInstallCommand.NotifyCanExecuteChanged();
    }

    private async Task PrepareLocalEnginePackageRemovalAsync(LocalEnginePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        var targetProject = LocalEngineProjects
            .Select(item => item.Project)
            .FirstOrDefault(project => string.Equals(project.Id, package.ProjectId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Le projet du package n’est plus disponible.");
        var provider = _engineModules.Modules
            .OfType<ILocalEnginePackageManagementProvider>()
            .FirstOrDefault(item =>
                item.Descriptor.Family == package.Family &&
                string.Equals(item.Descriptor.Id, package.ProviderId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Le provider du package ne prend pas en charge sa suppression.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Vérification du retrait de {package.Name}…";
        LocalEnginePackageRemovalPlan? plan = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Préparer retrait package · {package.Name}",
            async (context, cancellationToken) =>
            {
                context.Report(null, "Inventaire et calcul des empreintes avant retrait");
                plan = await provider.PreparePackageRemovalAsync(
                        package,
                        targetProject,
                        cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || plan is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Préparation du retrait annulée."
                : $"Retrait impossible : {completed.Error}";
            return;
        }

        SetPendingLocalEnginePackageRemoval(plan);
        PluginStatusSummary = "Retrait vérifié; confirmation explicite requise.";
        ShowSection(PageSection.Plugins);
    }

    private async Task ConfirmLocalEnginePackageRemovalAsync()
    {
        var plan = PendingLocalEnginePackageRemoval
            ?? throw new InvalidOperationException("Aucune suppression de package à confirmer.");
        var provider = _engineModules.Modules
            .OfType<ILocalEnginePackageManagementProvider>()
            .FirstOrDefault(item =>
                item.Descriptor.Family == plan.Package.Family &&
                string.Equals(item.Descriptor.Id, plan.Package.ProviderId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Le provider de suppression n’est plus disponible.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Retrait de {plan.Package.Name}…";
        LocalEnginePackageRemovalResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Retirer package · {plan.Package.Name}",
            async (context, cancellationToken) =>
            {
                context.Report(null, "Revalidation puis déplacement vers la zone récupérable");
                result = await provider.RemovePackageAsync(plan, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || result is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Retrait interrompu avant la permutation."
                : $"Échec du retrait : {completed.Error}";
            return;
        }

        CancelPendingLocalEnginePackageRemoval();
        await RefreshPluginsAsync().ConfigureAwait(true);
        PluginStatusSummary =
            $"{plan.Package.Name} retiré de {plan.TargetProject.Name} · " +
            $"copie récupérable : {result.RecoveryDirectory}";
        StatusMessage = "Package retiré; restauration disponible dans l’historique.";
        ShowSection(PageSection.Plugins);
    }

    private void SetPendingLocalEnginePackageRemoval(LocalEnginePackageRemovalPlan plan)
    {
        CancelPendingLocalEnginePackageInstall();
        CancelPendingLocalEnginePackageRollback();
        CancelPendingLocalEnginePackageRestore();
        _pendingLocalEnginePackageRemoval = plan;
        PendingLocalEnginePackageRemovalSummary =
            $"{plan.Package.Name} {plan.Package.Version} · {plan.TargetProject.Name} · " +
            $"{plan.Files.Count:N0} fichier(s), {FormatBytes(plan.TotalBytes)}. " +
            "Le dossier sera déplacé dans l’espace récupérable du projet; aucun fichier ne sera détruit.";
        OnPropertyChanged(nameof(PendingLocalEnginePackageRemoval));
        OnPropertyChanged(nameof(HasPendingLocalEnginePackageRemoval));
        _confirmLocalEnginePackageRemovalCommand.NotifyCanExecuteChanged();
    }

    private void CancelPendingLocalEnginePackageRemoval()
    {
        if (_pendingLocalEnginePackageRemoval is null &&
            string.IsNullOrEmpty(PendingLocalEnginePackageRemovalSummary))
        {
            return;
        }

        _pendingLocalEnginePackageRemoval = null;
        PendingLocalEnginePackageRemovalSummary = string.Empty;
        OnPropertyChanged(nameof(PendingLocalEnginePackageRemoval));
        OnPropertyChanged(nameof(HasPendingLocalEnginePackageRemoval));
        _confirmLocalEnginePackageRemovalCommand.NotifyCanExecuteChanged();
    }

    private async Task PrepareLocalEnginePackageRestoreAsync(
        LocalEnginePackageRemovalReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var provider = _engineModules.Modules
            .OfType<ILocalEnginePackageManagementProvider>()
            .FirstOrDefault(item =>
                item.Descriptor.Family == receipt.Family &&
                string.Equals(item.Descriptor.Id, receipt.ProviderId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Le provider du reçu de suppression n’est plus disponible.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Vérification de la restauration de {receipt.DisplayName}…";
        LocalEnginePackageRestorePlan? plan = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Préparer restauration package · {receipt.DisplayName}",
            async (context, cancellationToken) =>
            {
                context.Report(null, "Vérification du reçu, de la destination et de la copie récupérable");
                plan = await provider.PreparePackageRestoreAsync(receipt, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || plan is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Préparation de la restauration annulée."
                : $"Restauration impossible : {completed.Error}";
            return;
        }

        SetPendingLocalEnginePackageRestore(plan);
        PluginStatusSummary = "Restauration vérifiée; confirmation explicite requise.";
        ShowSection(PageSection.Plugins);
    }

    private async Task ConfirmLocalEnginePackageRestoreAsync()
    {
        var plan = PendingLocalEnginePackageRestore
            ?? throw new InvalidOperationException("Aucune restauration de package à confirmer.");
        var provider = _engineModules.Modules
            .OfType<ILocalEnginePackageManagementProvider>()
            .FirstOrDefault(item =>
                item.Descriptor.Family == plan.Receipt.Family &&
                string.Equals(item.Descriptor.Id, plan.Receipt.ProviderId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Le provider de restauration n’est plus disponible.");
        IsPluginBusy = true;
        PluginStatusSummary = $"Restauration de {plan.Receipt.DisplayName}…";
        LocalEnginePackageRestoreResult? result = null;
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Plugins",
            $"Restaurer package · {plan.Receipt.DisplayName}",
            async (context, cancellationToken) =>
            {
                context.Report(null, "Revalidation puis restauration du dossier natif");
                result = await provider.RestorePackageAsync(plan, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsPluginBusy = false;
        if (completed.Status != HubTaskStatus.Completed || result is null)
        {
            PluginStatusSummary = completed.Status == HubTaskStatus.Canceled
                ? "Restauration interrompue avant la permutation."
                : $"Échec de la restauration : {completed.Error}";
            return;
        }

        CancelPendingLocalEnginePackageRestore();
        await RefreshPluginsAsync().ConfigureAwait(true);
        PluginStatusSummary =
            $"{plan.Receipt.DisplayName} restauré dans {plan.Receipt.TargetProject.Name}.";
        StatusMessage = $"Package restauré dans {result.DestinationDirectory}.";
        ShowSection(PageSection.Plugins);
    }

    private void SetPendingLocalEnginePackageRestore(LocalEnginePackageRestorePlan plan)
    {
        CancelPendingLocalEnginePackageInstall();
        CancelPendingLocalEnginePackageRollback();
        CancelPendingLocalEnginePackageRemoval();
        _pendingLocalEnginePackageRestore = plan;
        PendingLocalEnginePackageRestoreSummary =
            $"{plan.Receipt.DisplayName} {plan.Receipt.Version} · {plan.Receipt.TargetProject.Name}. " +
            $"La copie récupérable sera replacée dans {plan.Receipt.DestinationDirectory}; " +
            "l’opération sera refusée si ce chemin est occupé ou si la copie a changé.";
        OnPropertyChanged(nameof(PendingLocalEnginePackageRestore));
        OnPropertyChanged(nameof(HasPendingLocalEnginePackageRestore));
        _confirmLocalEnginePackageRestoreCommand.NotifyCanExecuteChanged();
    }

    private void CancelPendingLocalEnginePackageRestore()
    {
        if (_pendingLocalEnginePackageRestore is null &&
            string.IsNullOrEmpty(PendingLocalEnginePackageRestoreSummary))
        {
            return;
        }

        _pendingLocalEnginePackageRestore = null;
        PendingLocalEnginePackageRestoreSummary = string.Empty;
        OnPropertyChanged(nameof(PendingLocalEnginePackageRestore));
        OnPropertyChanged(nameof(HasPendingLocalEnginePackageRestore));
        _confirmLocalEnginePackageRestoreCommand.NotifyCanExecuteChanged();
    }

    private async Task<IReadOnlyList<string>> ReadConfiguredPathsAsync(string settingKey)
    {
        var json = await _settings.GetAsync(settingKey).ConfigureAwait(true);
        return string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<string[]>(json) ?? [];
    }

    private async Task RefreshGitAsync()
    {
        var candidates = _currentEngines
            .Select(engine => new GitRepositoryCandidate(
                engine.DisplayName,
                engine.RootPath,
                GitRepositoryOwnerKind.Engine,
                engine.Id))
            .Concat(Projects.Select(project => new GitRepositoryCandidate(
                project.Name,
                project.Project.RootPath,
                GitRepositoryOwnerKind.Project,
                project.Project.Id)))
            .Concat(_currentPlugins
                .Where(plugin => plugin.Health != PluginHealth.Broken)
                .Select(plugin => new GitRepositoryCandidate(
                    plugin.FriendlyName,
                    plugin.RootPath,
                    GitRepositoryOwnerKind.Plugin,
                    plugin.Id)))
            .ToArray();
        if (candidates.Length == 0)
        {
            GitStatusSummary = "Aucun moteur, projet ou plugin à analyser.";
            return;
        }

        IsGitBusy = true;
        GitStatusSummary = $"Analyse manuelle de {candidates.Length} emplacement(s)…";
        IReadOnlyList<GitRepositorySnapshot> snapshots = [];
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Git",
            "Actualiser les dépôts Git",
            async (context, cancellationToken) =>
            {
                var progress = new InlineProgress<GitDiscoveryProgress>(value =>
                {
                    var ratio = value.Total == 0
                        ? 0
                        : Math.Clamp((double)value.Completed / value.Total, 0, 1);
                    context.Report(ratio, value.Message);
                });
                snapshots = await _gitDiscovery.RefreshAsync(
                        candidates,
                        progress,
                        cancellationToken)
                    .ConfigureAwait(false);
                await _gitSnapshotCache.SaveAsync(snapshots, cancellationToken)
                    .ConfigureAwait(false);
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        IsGitBusy = false;
        if (completed.Status == HubTaskStatus.Canceled)
        {
            GitStatusSummary = "Analyse Git annulée; le cache précédent est conservé.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed)
        {
            GitStatusSummary = $"Échec de l’analyse Git : {completed.Error}";
            return;
        }

        ReplaceGitSnapshots(snapshots);
        var dirtyCount = snapshots.Count(snapshot => snapshot.WorkingTree.IsDirty);
        var errorCount = snapshots.Count(snapshot => !string.IsNullOrWhiteSpace(snapshot.Error));
        GitStatusSummary =
            $"{snapshots.Count} dépôt(s) · {dirtyCount} modifié(s) · {errorCount} erreur(s) · " +
            "snapshot manuel en cache";
        StatusMessage = "Catalogue Git actualisé.";
    }

    private void ReplaceGitSnapshots(IReadOnlyList<GitRepositorySnapshot> snapshots)
    {
        GitRepositories.Clear();
        foreach (var snapshot in snapshots.OrderBy(
                     item => item.Owners.Count > 0 ? item.Owners[0].DisplayName : string.Empty,
                     StringComparer.OrdinalIgnoreCase))
        {
            GitRepositories.Add(new GitRepositoryCardViewModel(
                snapshot,
                OpenFolderAsync,
                FetchGitRemoteAsync,
                ShowError));
        }

        OnPropertyChanged(nameof(HasNoGitRepositories));
        OnPropertyChanged(nameof(GitRepositoryCount));
        OnPropertyChanged(nameof(GitConnectionStatus));
        OnPropertyChanged(nameof(GitConnectionIndicator));
    }

    private async Task FetchGitRemoteAsync(GitRepositorySnapshot snapshot, GitRemote remote)
    {
        var title = $"Fetch {remote.Name} · {Path.GetFileName(snapshot.RepositoryRoot)}";
        var request = GitCommandFactory.CreateFetch(snapshot.RepositoryRoot, remote.Name);
        await using var persistentLog = GitOperationLogWriter.Create(
            _gitOperationLogsDirectory,
            title);
        ShowSection(PageSection.Git);
        ActiveGitOperation = title;
        GitLogs.Clear();
        var preview = new ProcessOutputLine(
            DateTimeOffset.Now,
            ProcessOutputChannel.StandardOutput,
            GitCommandFactory.FormatPreview(request));
        persistentLog.Write(preview);
        AppendRawLog("OUT", "Git", preview.Text);
        GitLogs.Add(new GitLogItemViewModel(preview));
        OnPropertyChanged(nameof(HasNoGitLogs));

        ProcessResult? result = null;
        var processProgress = new InlineProgress<ProcessOutputLine>(line =>
        {
            persistentLog.Write(line);
            AppendRawLog(
                line.Channel == ProcessOutputChannel.StandardError ? "ERR" : "OUT",
                "Git",
                line.Text);
            Dispatcher.UIThread.Post(() =>
            {
                GitLogs.Add(new GitLogItemViewModel(line));
                while (GitLogs.Count > 1_000)
                {
                    GitLogs.RemoveAt(0);
                }

                OnPropertyChanged(nameof(HasNoGitLogs));
            });
        });
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Git",
            title,
            async (context, cancellationToken) =>
            {
                context.Report(null, "git fetch en cours");
                result = await _processRunner.RunAsync(
                        request,
                        processProgress,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    var lastError = result.CapturedOutput
                        .LastOrDefault(line => line.Channel == ProcessOutputChannel.StandardError)
                        ?.Text;
                    throw new GitOperationException(
                        lastError ?? $"git fetch a retourné le code {result.ExitCode}.",
                        "fetch",
                        snapshot.RepositoryRoot,
                        result.ExitCode);
                }
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        if (completed.Status == HubTaskStatus.Canceled)
        {
            GitStatusSummary = $"Fetch annulé · log : {persistentLog.FilePath}";
            StatusMessage = $"{title} annulé.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            GitStatusSummary =
                $"{completed.Error ?? "Échec de git fetch."} · log : {persistentLog.FilePath}";
            StatusMessage = $"{title} : échec";
            return;
        }

        var fetchSummary =
            $"Fetch terminé en {result.Duration:g} · log : {persistentLog.FilePath}";
        GitStatusSummary = fetchSummary;
        StatusMessage = $"{title} terminé.";
        await RefreshGitAsync().ConfigureAwait(true);
        GitStatusSummary = $"{fetchSummary} · {GitStatusSummary}";
    }

    private async Task SavePresetAsync()
    {
        if (!Enum.TryParse<UnrealBuildPresetKind>(PresetKind, out var kind))
        {
            throw new ArgumentException("Type de preset invalide.");
        }

        if (!Enum.TryParse<UnrealBuildAction>(PresetAction, out var action))
        {
            throw new ArgumentException("Action UBT invalide.");
        }

        int? maximumParallelActions = null;
        if (!string.IsNullOrWhiteSpace(PresetMaximumParallelActions))
        {
            if (!int.TryParse(
                    PresetMaximumParallelActions,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedMaximum))
            {
                throw new ArgumentException("Le parallélisme doit être un entier positif.");
            }

            maximumParallelActions = parsedMaximum;
        }

        var preset = new UnrealBuildPreset(
            _editingPresetId ?? $"custom:{Guid.NewGuid():N}",
            PresetName,
            kind,
            PresetTarget,
            PresetPlatform,
            PresetConfiguration,
            PresetAttachProject,
            action,
            maximumParallelActions,
            PresetCook,
            PresetStage,
            PresetPackage,
            PresetArchive,
            PresetPak,
            PresetIoStore,
            PresetCompressed,
            PresetDistribution,
            PresetIncludePrerequisites,
            PresetCookFlavor);
        var saved = await _buildPresetCatalog.SaveCustomAsync(preset).ConfigureAwait(true);
        _editingPresetId = saved.Id;
        PresetName = saved.Name;
        await LoadBuildPresetsAsync().ConfigureAwait(true);
        StatusMessage = $"Preset '{saved.Name}' enregistré.";
    }

    private void EditBuildPreset(UnrealBuildPreset preset)
    {
        var isBuiltIn = UnrealBuildPresetDefaults.IsBuiltIn(preset.Id);
        _editingPresetId = isBuiltIn ? null : preset.Id;
        PresetName = isBuiltIn ? $"{preset.Name} (copie)" : preset.Name;
        PresetKind = preset.Kind.ToString();
        PresetTarget = preset.Target;
        PresetPlatform = preset.Platform;
        PresetConfiguration = preset.Configuration;
        PresetAction = preset.Action.ToString();
        PresetMaximumParallelActions = preset.MaximumParallelActions?.ToString(
            CultureInfo.InvariantCulture) ?? string.Empty;
        PresetCookFlavor = preset.CookFlavor ?? string.Empty;
        PresetAttachProject = preset.AttachProject;
        PresetCook = preset.Cook;
        PresetStage = preset.Stage;
        PresetPackage = preset.Package;
        PresetArchive = preset.Archive;
        PresetPak = preset.Pak;
        PresetIoStore = preset.IoStore;
        PresetCompressed = preset.Compressed;
        PresetDistribution = preset.Distribution;
        PresetIncludePrerequisites = preset.IncludePrerequisites;
        StatusMessage = isBuiltIn
            ? "Le preset intégré sera enregistré comme copie personnalisée."
            : $"Modification du preset '{preset.Name}'.";
    }

    private void ResetPresetEditor()
    {
        _editingPresetId = null;
        PresetName = string.Empty;
        PresetKind = nameof(UnrealBuildPresetKind.Target);
        PresetTarget = "{Project}Editor";
        PresetPlatform = "Win64";
        PresetConfiguration = "Development";
        PresetAction = nameof(UnrealBuildAction.Build);
        PresetMaximumParallelActions = string.Empty;
        PresetCookFlavor = string.Empty;
        PresetAttachProject = true;
        PresetCook = true;
        PresetStage = true;
        PresetPackage = true;
        PresetArchive = true;
        PresetPak = true;
        PresetIoStore = true;
        PresetCompressed = false;
        PresetDistribution = false;
        PresetIncludePrerequisites = true;
        StatusMessage = "Nouveau preset personnalisé.";
    }

    private async Task DeleteBuildPresetAsync(UnrealBuildPreset preset)
    {
        var deleted = await _buildPresetCatalog.DeleteCustomAsync(preset.Id).ConfigureAwait(true);
        if (deleted)
        {
            if (string.Equals(_editingPresetId, preset.Id, StringComparison.OrdinalIgnoreCase))
            {
                ResetPresetEditor();
            }

            await LoadBuildPresetsAsync().ConfigureAwait(true);
            StatusMessage = $"Preset '{preset.Name}' supprimé.";
        }
    }

    private Task RunBuildPresetAsync(UnrealBuildPreset preset)
    {
        var project = SelectedPresetProject?.Project
            ?? throw new InvalidOperationException("Sélectionnez un projet pour exécuter le preset.");
        var engine = ResolveEngine(project.EngineAssociation)
            ?? throw new InvalidOperationException(
                $"Aucun moteur local ne correspond à l’association '{project.EngineAssociation}'.");
        var normalized = UnrealBuildPresetValidator.ValidateAndNormalize(preset);
        var archive = Path.Combine(
            project.RootPath,
            "Saved",
            "CyEngineHubPackages",
            normalized.Platform,
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var operation = UnrealBuildPresetCommandFactory.Create(
            normalized,
            engine,
            project,
            archive);
        return ExecuteUnrealOperationAsync(operation.Title, operation.ProcessRequest);
    }

    private UnrealEngineRegistration? FindRegistration(UnrealEngineInstallation engine)
    {
        try
        {
            return _engineRegistration.FindByRoot(engine.RootPath);
        }
        catch (UnrealEngineRegistrationException exception)
        {
            StatusMessage = exception.Message;
            return null;
        }
    }

    private async Task ExecuteUnrealOperationAsync(string title, ProcessRequest request)
    {
        await using var persistentLog = BuildLogFileWriter.Create(_operationLogsDirectory, title);
        ShowSection(PageSection.Builds);
        ActiveBuildTitle = title;
        BuildSummary = "En attente dans la file…";
        BuildLogs.Clear();
        var preview = new BuildLogLine(
            DateTimeOffset.Now,
            BuildLogSeverity.Information,
            UnrealBuildCommandFactory.FormatPreview(request));
        AppendRawLog("INFO", "Build", preview.Text);
        BuildLogs.Add(new BuildLogItemViewModel(preview));
        OnPropertyChanged(nameof(HasNoBuildLogs));

        UnrealBuildExecutionResult? result = null;
        var progress = new Progress<BuildLogLine>(line =>
        {
            AppendRawLog(line.Severity.ToString().ToUpperInvariant(), "Build", line.Text);
            BuildLogs.Add(new BuildLogItemViewModel(line));
            while (BuildLogs.Count > 2_000)
            {
                BuildLogs.RemoveAt(0);
            }

            OnPropertyChanged(nameof(HasNoBuildLogs));
        });
        var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
            "Build",
            title,
            async (context, cancellationToken) =>
            {
                context.Report(null, "Processus Unreal en cours");
                result = await _buildExecutor.ExecuteAsync(
                        request,
                        progress,
                        persistentLog,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (result.ExitCode != 0)
                {
                    var lastError = result.Tail.LastOrDefault(line =>
                        line.Severity is BuildLogSeverity.Error or BuildLogSeverity.Fatal);
                    var diagnosis = lastError is null
                        ? null
                        : BuildLogClassifier.Diagnose(lastError.Text);
                    throw new BuildOperationException(
                        diagnosis ?? $"Unreal a retourné le code {result.ExitCode}.",
                        title,
                        request.WorkingDirectory ?? string.Empty);
                }
            }));

        var completed = await handle.Completion.ConfigureAwait(true);
        if (completed.Status == HubTaskStatus.Canceled)
        {
            BuildSummary =
                $"Opération annulée. L’arbre de processus a été arrêté. Log : {persistentLog.FilePath}";
            StatusMessage = $"{title} annulé.";
            return;
        }

        if (completed.Status == HubTaskStatus.Failed || result is null)
        {
            BuildSummary =
                $"{completed.Error ?? "Échec de l’opération Unreal."} · Log : {persistentLog.FilePath}";
            StatusMessage = $"{title} : échec";
            return;
        }

        BuildSummary =
            $"Terminé en {result.Duration:g} · {result.WarningCount} avertissement(s) · " +
            $"{result.ErrorCount} erreur(s) classifiée(s) · Log : {persistentLog.FilePath}";
        StatusMessage = $"{title} terminé.";
    }

    private UnrealEngineInstallation? ResolveEngine(string? association)
    {
        if (string.IsNullOrWhiteSpace(association))
        {
            return null;
        }

        var direct = _currentEngines.FirstOrDefault(engine =>
            string.Equals(engine.AssociationId, association, StringComparison.OrdinalIgnoreCase));
        if (direct is not null)
        {
            return direct;
        }

        var normalized = association.Replace("UE_", string.Empty, StringComparison.OrdinalIgnoreCase);
        return _currentEngines.FirstOrDefault(engine =>
            string.Equals(
                $"{engine.Version.Major}.{engine.Version.Minor}",
                normalized,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(engine.Version.ToString(), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private void OnTaskChanged(object? sender, HubTaskChangedEventArgs eventArgs)
    {
        AppendRawLog(
            eventArgs.Task.Status == HubTaskStatus.Failed ? "ERROR" : "TASK",
            eventArgs.Task.Category,
            $"{eventArgs.Task.Title} · {eventArgs.Task.Status} · {eventArgs.Task.Message ?? eventArgs.Task.Error ?? string.Empty}");
        if (Interlocked.Exchange(ref _taskRefreshPending, 1) != 0) return;
        Dispatcher.UIThread.Post(() =>
        {
            Interlocked.Exchange(ref _taskRefreshPending, 0);
            var snapshot = _taskScheduler.GetSnapshot();
            foreach (var task in snapshot)
            {
                if (_fabVersionDownloadsByTaskId.TryGetValue(task.Id, out var versionDownload))
                    versionDownload.ApplySnapshot(task);
            }
            if (_fabVersionDownloadsByTaskId.Count > 0)
            {
                var activeDownloads = _fabVersionDownloads
                    .Where(item => !item.IsTerminal)
                    .ToArray();
                IsFabDownloadActive = activeDownloads.Length > 0;
                if (_fabVersionDownloads.Count > 0)
                {
                    FabDownloadProgress = _fabVersionDownloads.Average(item => item.ProgressPercent) / 100D;
                }
            }
            var existing = Tasks.ToDictionary(item => item.Id);
            foreach (var task in snapshot)
            {
                if (existing.TryGetValue(task.Id, out var row)) row.ApplySnapshot(task);
                else
                {
                    var index = snapshot.TakeWhile(item => item.Id != task.Id).Count();
                    Tasks.Insert(Math.Min(index, Tasks.Count), new TaskItemViewModel(task, CancelTask, NavigateTaskLink));
                }
            }

            OnPropertyChanged(nameof(HasNoTasks));
            OnPropertyChanged(nameof(FooterProgressPercent));
            OnPropertyChanged(nameof(FooterActivityText));
        });
    }

    private void CancelTask(Guid taskId)
    {
        if (_durableDownloadTasks.TryGetValue(taskId, out var id) && DownloadJournal.Read().TryGetValue(id, out var saved))
            DownloadJournal.Upsert(id, saved with { State = "Canceled" });
        if (_taskScheduler.Cancel(taskId))
        {
            StatusMessage = "Annulation demandée…";
        }
    }

    private void ShowSection(PageSection section)
    {
        if (_section == section)
        {
            NotifySectionProperties();
            if (section == PageSection.Logs)
            {
                RequestRawLogScroll();
            }
            return;
        }

        if (!_isHistoryNavigation)
        {
            _backNavigation.Push(_section);
            _forwardNavigation.Clear();
        }

        _section = section;
        NotifySectionProperties();
        if (section == PageSection.Logs)
        {
            RequestRawLogScroll();
        }
        _navigateBackCommand.NotifyCanExecuteChanged();
        _navigateForwardCommand.NotifyCanExecuteChanged();
    }

    private void NavigateBack()
    {
        if (_backNavigation.Count == 0)
        {
            return;
        }

        var target = _backNavigation.Pop();
        _forwardNavigation.Push(_section);
        NavigateThroughHistory(target);
    }

    private void NavigateForward()
    {
        if (_forwardNavigation.Count == 0)
        {
            return;
        }

        var target = _forwardNavigation.Pop();
        _backNavigation.Push(_section);
        NavigateThroughHistory(target);
    }

    private void NavigateThroughHistory(PageSection target)
    {
        _isHistoryNavigation = true;
        try
        {
            ShowSection(target);
        }
        finally
        {
            _isHistoryNavigation = false;
            _navigateBackCommand.NotifyCanExecuteChanged();
            _navigateForwardCommand.NotifyCanExecuteChanged();
        }
    }

    private void OpenSettings()
    {
        if (_section != PageSection.Settings)
        {
            _settingsReturnSection = _section;
        }

        ShowSection(PageSection.Settings);
    }

    private void OpenConnectionSettings(string connectionPage)
    {
        _selectedConnectionPage = connectionPage;
        OnPropertyChanged(nameof(IsEpicConnectionPageSelected));
        OnPropertyChanged(nameof(IsFabConnectionPageSelected));
        OnPropertyChanged(nameof(IsGitConnectionPageSelected));
        OnPropertyChanged(nameof(IsUnityConnectionPageSelected));
        OnPropertyChanged(nameof(IsQuixelConnectionPageSelected));
        SelectedSettingsCategory = SettingsCategories.First(category => category.Id == "connections");
        OpenSettings();
    }

    private void CloseSettings() => ShowSection(
        _settingsReturnSection == PageSection.Settings
            ? PageSection.Home
            : _settingsReturnSection);

    private void OpenUnrealEngineDetails(EngineCardViewModel engine)
    {
        SelectedLocalEngineDetails = null;
        SelectedEngineDetails = engine;
        SetEngineInstallationEditing(false);
        EngineComponentUpdateStatus = engine.IsOfficialEpic
            ? "Modifier permet d’ajouter les composants absents depuis le manifest Epic officiel."
            : "Les composants des moteurs source ou personnalisés se gèrent avec Setup et la toolchain.";
        RefreshSelectedEngineToolchains();
        ShowSection(PageSection.EngineDetails);
    }

    private void SetEngineInstallationEditing(bool editing)
    {
        if (SelectedEngineDetails?.IsOfficialEpic != true)
        {
            editing = false;
        }
        SelectedEngineDetails?.SetInstallationOptionsEditing(editing);
        IsEngineInstallationEditing = editing;
        if (editing)
        {
            EngineComponentUpdateStatus =
                "Cochez les composants absents à télécharger. Les composants déjà installés sont conservés.";
        }
    }

    private async Task UpdateSelectedEngineComponentsAsync()
    {
        var selected = SelectedEngineDetails
            ?? throw new InvalidOperationException("Aucun moteur Unreal n’est sélectionné.");
        if (!selected.IsOfficialEpic)
        {
            throw new InvalidOperationException(
                "La mise à jour par composants est disponible uniquement pour les installations Epic officielles.");
        }
        var components = selected.InstallationOptions
            .Where(option => option.IsSelected && !option.IsInstalled)
            .Select(option => option.Id)
            .ToArray();
        if (components.Length == 0)
        {
            EngineComponentUpdateStatus = "Aucun nouveau composant n’est sélectionné.";
            return;
        }

        IsEpicBusy = true;
        EngineComponentUpdateStatus = "Recherche du manifest officiel correspondant…";
        var version = $"{selected.Engine.Version.Major}.{selected.Engine.Version.Minor}";
        EpicBinaryEngineComponentUpdateResult? result = null;
        try
        {
            var releases = await _epicBinaryEngineBackend.GetAvailableReleasesAsync()
                .ConfigureAwait(true);
            var release = releases.FirstOrDefault(item =>
                              string.Equals(
                                  NormalizeEngineVersion(item.Version),
                                  NormalizeEngineVersion(version),
                                  StringComparison.OrdinalIgnoreCase))
                          ?? throw new InvalidOperationException(
                              $"Epic n’a retourné aucun manifest officiel pour Unreal Engine {version}.");
            var handle = _taskScheduler.Enqueue(new HubTaskDefinition(
                "Moteurs · Epic",
                $"Modifier {selected.DisplayName}",
                [EngineTaskLink(selected.Path, selected.DisplayName)],
                async (context, cancellationToken) =>
                {
                    var transfer = new CyEngineHub.Core.Marketplaces.DownloadProgressText();
                    var progress = new InlineProgress<EpicBinaryEngineDownloadProgress>(value =>
                    {
                        var message = transfer.Format(value.Message, value.DownloadedBytes, value.TotalBytes);
                        context.Report(value.Fraction, message);
                        Dispatcher.UIThread.Post(() => EngineComponentUpdateStatus = message);
                    });
                    result = await _epicBinaryEngineBackend.UpdateComponentsAsync(
                            release,
                            selected.Engine.RootPath,
                            components,
                            progress,
                            cancellationToken)
                        .ConfigureAwait(false);
                }));
            var completed = await handle.Completion.ConfigureAwait(true);
            if (completed.Status == HubTaskStatus.Canceled)
            {
                EngineComponentUpdateStatus =
                    "Mise à jour annulée · le cache vérifié est conservé pour la reprise.";
                return;
            }
            if (completed.Status == HubTaskStatus.Failed || result is null)
            {
                EngineComponentUpdateStatus = $"Échec de la mise à jour : {completed.Error}";
                return;
            }

            EngineComponentUpdateStatus =
                $"{result.InstalledFileCount:N0} fichier(s) installé(s) · build {result.BuildVersion}.";
            AppendRawLog(
                "INFO",
                "Engines/Epic",
                $"Composants {string.Join(", ", result.Components)} ajoutés à {result.DestinationDirectory}.");
            SetEngineInstallationEditing(false);
            await RefreshCatalogAsync().ConfigureAwait(true);
            var refreshed = Engines.FirstOrDefault(item =>
                string.Equals(item.Engine.RootPath, result.DestinationDirectory, StringComparison.OrdinalIgnoreCase));
            if (refreshed is not null)
            {
                OpenUnrealEngineDetails(refreshed);
                EngineComponentUpdateStatus =
                    $"Mise à jour terminée · {result.InstalledFileCount:N0} fichier(s) vérifié(s).";
            }
        }
        finally
        {
            IsEpicBusy = false;
        }
    }

    private void OpenLocalEngineDetails(LocalEngineInstallationCardViewModel engine)
    {
        SelectedEngineDetails = null;
        SelectedLocalEngineDetails = engine;
        RefreshSelectedEngineToolchains();
        ShowSection(PageSection.EngineDetails);
    }

    private void RefreshSelectedEngineToolchains()
    {
        var selectedId = SelectedEngineDetails?.Engine.Id;
        var selectedRoot = SelectedEngineDetails?.Engine.RootPath
                           ?? SelectedLocalEngineDetails?.Installation.RootPath;
        var selectedVersion = SelectedEngineDetails?.Engine.Version.ToString()
                              ?? SelectedLocalEngineDetails?.Installation.Version;
        SelectedEngineToolchains.Clear();
        if (_currentToolchainSnapshot is not null)
        {
            foreach (var snapshot in _currentToolchainSnapshot.Engines.Where(snapshot =>
                         (!string.IsNullOrWhiteSpace(selectedId) &&
                          string.Equals(snapshot.EngineId, selectedId, StringComparison.OrdinalIgnoreCase)) ||
                         (!string.IsNullOrWhiteSpace(selectedRoot) &&
                          snapshot.EngineId.Contains(selectedRoot, StringComparison.OrdinalIgnoreCase)) ||
                         (!string.IsNullOrWhiteSpace(selectedVersion) &&
                          string.Equals(snapshot.EngineVersion, selectedVersion, StringComparison.OrdinalIgnoreCase))))
            {
                SelectedEngineToolchains.Add(new EngineToolchainCardViewModel(snapshot));
            }
        }
        OnPropertyChanged(nameof(HasNoSelectedEngineToolchains));
    }

    private string GetEngineEffectiveSizeLabel(string rootPath)
    {
        if (_currentStorageSnapshot is null)
        {
            return "Taille · à analyser";
        }

        var bytes = _currentStorageSnapshot.Entries
            .Where(entry => entry.Category == StorageCategory.EngineInstallation &&
                            (string.Equals(entry.OwnerRoot, rootPath, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(entry.Path, rootPath, StringComparison.OrdinalIgnoreCase)))
            .Sum(entry => entry.SizeBytes);
        return bytes > 0 ? $"Taille · {FormatBytes(bytes)}" : "Taille · non mesurée";
    }

    private void OpenUnrealProjectDetails(ProjectCardViewModel project)
    {
        SelectedLocalProjectDetails = null;
        SelectedProjectDetails = project;
        ShowSection(PageSection.ProjectDetails);
    }

    private void OpenLocalProjectDetails(LocalEngineProjectCardViewModel project)
    {
        SelectedProjectDetails = null;
        SelectedLocalProjectDetails = project;
        ShowSection(PageSection.ProjectDetails);
    }

    private void OpenSelectedEnginePlugins()
    {
        var family = SelectedEngineDetails is not null
            ? EngineFamily.Unreal
            : SelectedLocalEngineDetails?.Installation.Family;
        if (family is not null)
        {
            SelectedPluginTab = EngineTabs.FirstOrDefault(item => item.Family == family.Value)
                                ?? SelectedPluginTab;
        }

        ShowSection(PageSection.Plugins);
    }

    private void OpenSelectedProjectPlugins()
    {
        var family = SelectedProjectDetails is not null
            ? EngineFamily.Unreal
            : SelectedLocalProjectDetails?.Project.Family;
        if (family is not null)
        {
            SelectedPluginTab = EngineTabs.FirstOrDefault(item => item.Family == family.Value)
                                ?? SelectedPluginTab;
        }

        ShowSection(PageSection.Plugins);
    }

    private void ShowRoadmap(string title, string description)
    {
        RoadmapTitle = title;
        RoadmapDescription = description;
        ShowSection(PageSection.Roadmap);
    }

    private void ShowError(Exception exception)
    {
        AppendRawLog("ERROR", "Command", exception.ToString());
        StatusMessage = $"{exception.GetType().Name} : {exception.Message}";
    }

    private void LoadRawLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_rawLogPath)!);
            if (!File.Exists(_rawLogPath))
            {
                RawLogText = "Le journal RAW est vide.";
                ReplaceRawLogEntries(RawLogText);
                return;
            }

            const int maximumBytes = 1024 * 1024;
            using var stream = new FileStream(
                _rawLogPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > maximumBytes)
            {
                stream.Seek(-maximumBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            if (stream.Length > maximumBytes)
            {
                var firstLineBreak = text.IndexOf('\n');
                text = firstLineBreak >= 0 ? text[(firstLineBreak + 1)..] : text;
            }

            RawLogText = text;
            ReplaceRawLogEntries(text);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            RawLogText = $"Impossible de lire le journal RAW : {exception.Message}";
            ReplaceRawLogEntries(RawLogText);
        }
    }

    private void AppendRawLog(string level, string category, string message)
    {
        var normalized = message.Replace("\r", string.Empty, StringComparison.Ordinal);
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} [{level}] [{category}] {normalized}";
        try
        {
            lock (_rawLogSync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_rawLogPath)!);
                File.AppendAllText(_rawLogPath, line + Environment.NewLine);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            line += $" · écriture disque impossible : {exception.Message}";
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            AppendRawLogToView(line);
        }
        else
        {
            Dispatcher.UIThread.Post(() => AppendRawLogToView(line));
        }
    }

    private void AppendRawLogToView(string line)
    {
        _latestRawLogLine = line.TrimEnd();
        OnPropertyChanged(nameof(FooterActivityText));
        var combined = string.IsNullOrWhiteSpace(RawLogText) || RawLogText == "Le journal RAW est vide."
            ? line + Environment.NewLine
            : RawLogText + line + Environment.NewLine;
        const int maximumCharacters = 500_000;
        RawLogText = combined.Length <= maximumCharacters
            ? combined
            : combined[^maximumCharacters..];

        var entry = new RawLogEntryViewModel(_latestRawLogLine);
        _rawLogEntries.Add(entry);
        const int maximumEntries = 20_000;
        if (_rawLogEntries.Count > maximumEntries)
        {
            _rawLogEntries.RemoveRange(0, _rawLogEntries.Count - maximumEntries);
        }
        if (MatchesRawLogSearch(entry))
        {
            FilteredRawLogEntries.Add(entry);
            while (FilteredRawLogEntries.Count > maximumEntries)
            {
                FilteredRawLogEntries.RemoveAt(0);
            }
            RequestRawLogScroll();
        }
    }

    private void ReplaceRawLogEntries(string text)
    {
        _rawLogEntries.Clear();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            _rawLogEntries.Add(new RawLogEntryViewModel(line.TrimEnd('\r')));
        }
        ApplyRawLogFilter(scrollToEnd: true);
    }

    private void ApplyRawLogFilter(bool scrollToEnd)
    {
        FilteredRawLogEntries.ReplaceWith(_rawLogEntries.Where(MatchesRawLogSearch));
        if (scrollToEnd)
        {
            RequestRawLogScroll();
        }
    }

    private bool MatchesRawLogSearch(RawLogEntryViewModel entry) =>
        string.IsNullOrWhiteSpace(RawLogSearchQuery) ||
        entry.Text.Contains(RawLogSearchQuery.Trim(), StringComparison.OrdinalIgnoreCase);

    private void RequestRawLogScroll() =>
        Dispatcher.UIThread.Post(() => RawLogScrollRequested?.Invoke(this, EventArgs.Empty));

    private void NotifySectionProperties()
    {
        OnPropertyChanged(nameof(IsHomeVisible));
        OnPropertyChanged(nameof(IsHomeNavigationActive));
        OnPropertyChanged(nameof(IsEnginesNavigationActive));
        OnPropertyChanged(nameof(IsProjectsNavigationActive));
        OnPropertyChanged(nameof(IsLibraryNavigationActive));
        OnPropertyChanged(nameof(IsGitNavigationActive));
        OnPropertyChanged(nameof(IsPluginsNavigationActive));
        OnPropertyChanged(nameof(IsExtensionsNavigationActive));
        OnPropertyChanged(nameof(IsStorageNavigationActive));
        OnPropertyChanged(nameof(IsTasksNavigationActive));
        OnPropertyChanged(nameof(IsLogsNavigationActive));
        OnPropertyChanged(nameof(IsSettingsNavigationActive));
        OnPropertyChanged(nameof(IsEnginesVisible));
        OnPropertyChanged(nameof(IsEngineDetailsVisible));
        OnPropertyChanged(nameof(IsProjectsVisible));
        OnPropertyChanged(nameof(IsProjectDetailsVisible));
        OnPropertyChanged(nameof(IsGitVisible));
        OnPropertyChanged(nameof(IsPluginsVisible));
        OnPropertyChanged(nameof(IsHubExtensionsVisible));
        OnPropertyChanged(nameof(IsToolchainsVisible));
        OnPropertyChanged(nameof(IsStorageVisible));
        OnPropertyChanged(nameof(IsLibraryVisible));
        OnPropertyChanged(nameof(IsMainNavigationVisible));
        OnPropertyChanged(nameof(IsEpicVisible));
        OnPropertyChanged(nameof(IsBuildsVisible));
        OnPropertyChanged(nameof(IsTasksVisible));
        OnPropertyChanged(nameof(IsLogsVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
        OnPropertyChanged(nameof(IsRoadmapVisible));
        OnPropertyChanged(nameof(HeaderTitle));
        OnPropertyChanged(nameof(HeaderSubtitle));
    }

    private void NotifyCatalogProperties()
    {
        OnPropertyChanged(nameof(EngineCount));
        OnPropertyChanged(nameof(ProjectCount));
        OnPropertyChanged(nameof(LocalEngineInstallationCount));
        OnPropertyChanged(nameof(LocalEngineProjectCount));
        OnPropertyChanged(nameof(WarningCount));
        OnPropertyChanged(nameof(HasNoEngines));
        OnPropertyChanged(nameof(HasNoProjects));
        OnPropertyChanged(nameof(HasNoLocalEngineInstallations));
        OnPropertyChanged(nameof(HasNoLocalEngineProjects));
    }

    private static string NormalizeExistingEnginePath(string value)
    {
        var path = NormalizeInputPath(value);
        if (!Directory.Exists(path) && !File.Exists(path))
        {
            throw new FileNotFoundException("Le chemin du moteur n’existe pas.", path);
        }

        return path;
    }

    private static string NormalizeExistingProjectRoot(string value)
    {
        var path = NormalizeInputPath(value);
        if (File.Exists(path) &&
            (string.Equals(Path.GetExtension(path), ".uproject", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(Path.GetFileName(path), "project.godot", StringComparison.OrdinalIgnoreCase)))
        {
            return Path.GetDirectoryName(path)!;
        }

        if (File.Exists(path) &&
            string.Equals(Path.GetFileName(path), "ProjectVersion.txt", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                Path.GetFileName(Path.GetDirectoryName(path)),
                "ProjectSettings",
                StringComparison.OrdinalIgnoreCase))
        {
            return Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName;
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Le dossier de projets n’existe pas : {path}");
        }

        return path;
    }

    private static string NormalizeExistingPluginLibraryRoot(string value)
    {
        var path = NormalizeInputPath(value);
        if (File.Exists(path) &&
            string.Equals(Path.GetExtension(path), ".uplugin", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetDirectoryName(path)!;
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Le dossier de bibliothèque Plugins n’existe pas : {path}");
        }

        return path;
    }

    private static string NormalizeExistingFabLibraryRoot(string value)
    {
        var path = NormalizeInputPath(value);
        if (File.Exists(path) &&
            string.Equals(Path.GetExtension(path), ".uproject", StringComparison.OrdinalIgnoreCase))
        {
            path = Path.GetDirectoryName(path)!;
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException(
                $"Le dossier de bibliothèque Fab n’existe pas : {path}");
        }

        var volumeRoot = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(path) ?? string.Empty);
        if (string.Equals(
                Path.TrimEndingDirectorySeparator(path),
                volumeRoot,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Sélectionnez un dossier précis; une racine de volume complète n’est pas autorisée.");
        }

        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new ArgumentException(
                "Une racine de bibliothèque Fab ne peut pas être un lien ou point de réanalyse.");
        }

        return path;
    }

    private static string NormalizeInputPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Saisissez d’abord un chemin.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim().Trim('"')));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static Version ParseEngineVersion(string value)
    {
        var normalized = NormalizeEngineVersion(value);
        return Version.TryParse(normalized, out var parsed) ? parsed : new Version();
    }

    private static string NormalizeEngineVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var source = value.Trim().Replace("UE_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("UE", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimStart('_', '-', ' ');
        var chars = source.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray();
        var normalized = new string(chars).Trim('.');
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : normalized;
    }

    private long SumStorage(StorageCategory category) => _currentStorageSnapshot?.Entries
        .Where(entry => entry.Category == category)
        .Sum(entry => entry.SizeBytes) ?? 0;

    private enum FabInstallDestination
    {
        Library,
        Project,
        Engine,
    }

    private enum PageSection
    {
        Home,
        Engines,
        Projects,
        Git,
        Plugins,
        HubExtensions,
        Toolchains,
        Storage,
        Epic,
        Builds,
        Tasks,
        Logs,
        Settings,
        EngineDetails,
        ProjectDetails,
        Roadmap,
    }

    private sealed record FabStoredDownloadRecord(
        string AssetId,
        string Version,
        string ArtifactId,
        string Path,
        long Bytes,
        DateTimeOffset DownloadedAt,
        string Source = "Fab",
        string PackageKind = "Unknown",
        string StorageMode = "Uncompressed",
        string? ManifestPath = null,
        long? UncompressedBytes = null,
        long? StoredBytes = null,
        string? AssetTitle = null,
        string? DerivationName = null,
        string? DerivationNotes = null,
        string? SourceArtifactId = null,
        string[]? EngineVersions = null);

    private sealed record QuixelLocalEntry(
        string Name,
        string AssetId,
        string Path,
        string Kind);

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
