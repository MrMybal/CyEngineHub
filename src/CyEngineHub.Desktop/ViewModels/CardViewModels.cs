using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CyEngineHub.Build.Presets;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Settings;
using CyEngineHub.Epic.Models;
using CyEngineHub.Engines;
using CyEngineHub.Extensions;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Installation;
using CyEngineHub.Fab.Library;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Git.Models;
using CyEngineHub.Plugins.Models;
using CyEngineHub.Projects.Models;
using CyEngineHub.Projects.Creation;
using CyEngineHub.Storage.Models;
using CyEngineHub.Toolchains.Models;
using CyEngineHub.Unreal.Models;
using CyEngineHub.Unreal.Registration;

namespace CyEngineHub.Desktop.ViewModels;

public sealed record MarketplaceCompressionOptionViewModel(
    LibraryPackageStorageMode StorageMode,
    string Label,
    string Description);

public enum MarketplaceNativeStoreKind
{
    EpicGamesStore,
    Megascans,
}

public enum MarketplaceNativeStoreMode
{
    NativeReadOnly,
    ConvertOnUse,
}

public sealed record MarketplaceNativeStoreSource(
    string Id,
    MarketplaceNativeStoreKind Store,
    string Path,
    MarketplaceNativeStoreMode Mode,
    DateTimeOffset AddedAt);

public sealed class MarketplaceNativeStoreSourceViewModel
{
    public MarketplaceNativeStoreSourceViewModel(
        MarketplaceNativeStoreSource source,
        Func<string, Task> openFolder,
        Func<MarketplaceNativeStoreSourceViewModel, Task> remove,
        Action<Exception> onError)
    {
        Source = source;
        OpenFolderCommand = new AsyncRelayCommand(
            () => openFolder(source.Path),
            () => Directory.Exists(source.Path),
            onError);
        RemoveCommand = new AsyncRelayCommand(() => remove(this), onError: onError);
    }

    public MarketplaceNativeStoreSource Source { get; }
    public string Id => Source.Id;
    public string Path => Source.Path;
    public string StoreLabel => Source.Store == MarketplaceNativeStoreKind.EpicGamesStore
        ? "EPIC GAMES STORE / VAULT"
        : "MEGASCANS / BRIDGE";
    public string ModeLabel => Source.Mode == MarketplaceNativeStoreMode.NativeReadOnly
        ? "NATIF · LECTURE SEULE"
        : "CONVERSION À LA DEMANDE";
    public string ModeDescription => Source.Mode == MarketplaceNativeStoreMode.NativeReadOnly
        ? "Le dossier source reste intact et CyEngineHub l’utilise directement."
        : "Une copie est convertie lors de l’utilisation avec les paramètres actuels du marketplace.";
    public string AvailabilityLabel => Directory.Exists(Path) ? "DISPONIBLE" : "INTROUVABLE";
    public bool IsAvailable => Directory.Exists(Path);
    public ICommand OpenFolderCommand { get; }
    public ICommand RemoveCommand { get; }
}

public sealed class MarketplaceLibraryLocationViewModel : ObservableObject
{
    private string _path;
    private string _maximumGigabytes;
    private bool _isEnabled;
    private int _priority;

    public MarketplaceLibraryLocationViewModel(
        string path,
        string maximumGigabytes,
        bool isEnabled,
        Action<MarketplaceLibraryLocationViewModel> moveUp,
        Action<MarketplaceLibraryLocationViewModel> moveDown,
        Action<MarketplaceLibraryLocationViewModel> remove)
    {
        _path = path;
        _maximumGigabytes = maximumGigabytes;
        _isEnabled = isEnabled;
        MoveUpCommand = new RelayCommand(() => moveUp(this));
        MoveDownCommand = new RelayCommand(() => moveDown(this));
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public string MaximumGigabytes
    {
        get => _maximumGigabytes;
        set => SetProperty(ref _maximumGigabytes, value);
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    public int Priority
    {
        get => _priority;
        private set
        {
            if (SetProperty(ref _priority, value)) OnPropertyChanged(nameof(PriorityLabel));
        }
    }

    public string PriorityLabel => $"#{Priority}";
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand RemoveCommand { get; }

    public void SetPriority(int priority) => Priority = priority;
}

public sealed record EngineFamilyOptionViewModel(
    EngineFamily Family,
    string ProviderId,
    string Label);

public sealed class ProjectCreationPresetOptionViewModel(ProjectCreationPreset preset)
{
    public ProjectCreationPreset Preset { get; } = preset;

    public string Name => Preset.DisplayName;

    public string Engine => Preset.Engine.ToString();

    public string Description => Preset.Description;
}

public sealed class ProjectCreationConfigurationOptionViewModel(ProjectConfigurationProfile profile)
{
    public ProjectConfigurationProfile Profile { get; } = profile;
    public string Name => Profile.DisplayName;
    public string Description => Profile.Description;
    public bool IsBuiltIn => Profile.IsBuiltIn;
    public string Origin => IsBuiltIn ? "INTÉGRÉ" : "PERSONNALISÉ";
    public string Inventory =>
        $"{(Profile.DisableEnginePluginsByDefault ? "Base minimale · " : string.Empty)}{Profile.EnabledPlugins.Count:N0} activé(s) · {Profile.DisabledPlugins.Count:N0} désactivé(s) · " +
        $"{Profile.EngineSettings.Count + Profile.IniSettings.Count:N0} réglage(s) INI";
}

public sealed class ProjectProfilePluginOptionViewModel : ObservableObject
{
    private bool? _state;
    private bool _isVisible = true;

    public ProjectProfilePluginOptionViewModel(
        string name,
        string friendlyName,
        string category,
        string source,
        bool? state,
        bool defaultEnabled = false,
        string defaultOrigin = "Valeur du moteur (héritée)")
    {
        Name = name;
        FriendlyName = friendlyName;
        Category = category;
        Source = source;
        _state = state;
        DefaultEnabled = defaultEnabled;
        DefaultOrigin = defaultOrigin;
        ResetToDefaultCommand = new RelayCommand(() => State = null);
    }

    public string Name { get; }
    public string FriendlyName { get; }
    public string Category { get; }
    public string Source { get; }
    public string DisplayName => string.IsNullOrWhiteSpace(FriendlyName) ? Name : FriendlyName;
    public string Detail => $"{Name} · {Category} · {Source}";
    public bool DefaultEnabled { get; private set; }
    public string DefaultOrigin { get; private set; }
    public bool IsActive { get => State ?? DefaultEnabled; set => State = value; }
    public string ActivationOrigin => State.HasValue ? "Choix explicite" : DefaultOrigin;
    public ICommand ResetToDefaultCommand { get; }

    public void UpdateComposition(bool? state, bool defaultEnabled, string defaultOrigin)
    {
        DefaultEnabled = defaultEnabled;
        DefaultOrigin = defaultOrigin;
        State = state;
        OnPropertyChanged(nameof(DefaultEnabled));
        OnPropertyChanged(nameof(DefaultOrigin));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ActivationOrigin));
    }

    public bool? State
    {
        get => _state;
        set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(ActivationOrigin));
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

public sealed class ProjectProfileIniSettingViewModel : ObservableObject
{
    private string _fileName;
    private string _section;
    private string _key;
    private string _value;

    public ProjectProfileIniSettingViewModel(
        ProjectIniSetting setting,
        Action<ProjectProfileIniSettingViewModel> remove)
    {
        _fileName = setting.FileName;
        _section = setting.Section;
        _key = setting.Key;
        _value = setting.Value;
        RemoveCommand = new RelayCommand(() => remove(this));
    }

    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    public string Section
    {
        get => _section;
        set => SetProperty(ref _section, value);
    }

    public string Key
    {
        get => _key;
        set => SetProperty(ref _key, value);
    }

    public string Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    public ICommand RemoveCommand { get; }

    public ProjectIniSetting ToModel() => new(
        FileName.Trim(),
        Section.Trim().Trim('[', ']'),
        Key.Trim(),
        Value);
}

public sealed class ProjectCreationEngineOptionViewModel(UnrealEngineInstallation engine)
{
    public UnrealEngineInstallation Engine { get; } = engine;
    public string Name => Engine.DisplayName;
    public string Version => Engine.Version.IsKnown
        ? $"Unreal Engine {Engine.Version.Major}.{Engine.Version.Minor}.{Engine.Version.Patch}"
        : Engine.AssociationId;
    public string Association => Engine.AssociationId;
    public string Path => Engine.RootPath;
}

public sealed class ProjectCreationCollectionOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public ProjectCreationCollectionOptionViewModel(
        string name,
        int assetCount,
        string sourceSummary)
    {
        Name = name;
        AssetCount = assetCount;
        SourceSummary = sourceSummary;
    }

    public string Name { get; }
    public int AssetCount { get; }
    public string SourceSummary { get; }
    public string Summary => $"{AssetCount:N0} asset(s) · {SourceSummary}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ProjectPostCreationOptionValueViewModel : ObservableObject
{
    private string _value;

    public ProjectPostCreationOptionValueViewModel(ProjectPostCreationOptionDefinition definition)
    {
        Definition = definition;
        _value = definition.DefaultValue;
        Choices = new ObservableCollection<string>(definition.Choices);
    }

    public ProjectPostCreationOptionDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name => Definition.DisplayName;
    public string Description => Definition.Description;
    public bool IsBoolean => Definition.Kind == ProjectPostCreationOptionKind.Boolean;
    public bool IsChoice => Definition.Kind == ProjectPostCreationOptionKind.Choice;
    public bool IsText => Definition.Kind == ProjectPostCreationOptionKind.Text;
    public ObservableCollection<string> Choices { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(BooleanValue));
            }
        }
    }

    public bool BooleanValue
    {
        get => bool.TryParse(Value, out var parsed) && parsed;
        set => Value = value.ToString().ToLowerInvariant();
    }
}

public sealed class ProjectPostCreationScriptOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public ProjectPostCreationScriptOptionViewModel(ProjectPostCreationScriptDescriptor descriptor)
    {
        Descriptor = descriptor;
        Options = new ObservableCollection<ProjectPostCreationOptionValueViewModel>(
            descriptor.Options.Select(option => new ProjectPostCreationOptionValueViewModel(option)));
    }

    public ProjectPostCreationScriptDescriptor Descriptor { get; }
    public string Id => Descriptor.Id;
    public string Name => Descriptor.DisplayName;
    public string Description => Descriptor.Description;
    public string Origin => Descriptor.IsBuiltIn ? "INTÉGRÉ" : Descriptor.Publisher;
    public ObservableCollection<ProjectPostCreationOptionValueViewModel> Options { get; }
    public bool HasOptions => Options.Count > 0;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ProjectPostCreationScriptSelection CreateSelection() => new(
        Descriptor.Id,
        Options.ToDictionary(
            option => option.Id,
            option => option.Value,
            StringComparer.OrdinalIgnoreCase));
}

public sealed class LibraryItemOrganizationViewModel : ObservableObject
{
    private string _alias;
    private string _folderPath;

    public LibraryItemOrganizationViewModel(
        WorkspaceItemKind kind,
        string key,
        string originalName,
        WorkspaceItemPreference? preference,
        Func<WorkspaceItemKind, string, string, string, Task> save,
        Func<WorkspaceItemKind, string, Task>? hide,
        Action<Exception> onError)
    {
        Kind = kind;
        Key = key;
        OriginalName = originalName;
        _alias = preference?.Alias ?? string.Empty;
        _folderPath = preference?.FolderPath ?? string.Empty;
        SaveCommand = new AsyncRelayCommand(
            async () =>
            {
                await save(Kind, Key, Alias, FolderPath).ConfigureAwait(true);
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(FolderLabel));
            },
            onError: onError);
        HideCommand = new AsyncRelayCommand(
            () => hide?.Invoke(Kind, Key) ?? Task.CompletedTask,
            () => hide is not null,
            onError);
    }

    public WorkspaceItemKind Kind { get; }

    public string Key { get; }

    public string OriginalName { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? OriginalName : Alias.Trim();

    public string Alias
    {
        get => _alias;
        set
        {
            if (SetProperty(ref _alias, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string FolderPath
    {
        get => _folderPath;
        set
        {
            if (SetProperty(ref _folderPath, value))
            {
                OnPropertyChanged(nameof(FolderLabel));
            }
        }
    }

    public string FolderLabel => string.IsNullOrWhiteSpace(FolderPath)
        ? "Bibliothèque principale"
        : FolderPath.Trim();

    public bool CanHide => HideCommand.CanExecute(null);

    public ICommand SaveCommand { get; }

    public ICommand HideCommand { get; }
}

public sealed class EngineModuleCardViewModel : ObservableObject
{
    private readonly EngineModuleDescriptor _descriptor;
    private bool _isEnabled;

    public EngineModuleCardViewModel(
        EngineModuleDescriptor descriptor,
        bool isEnabled,
        Func<string, bool, Task> setEnabled,
        Action<Exception> onError)
    {
        _descriptor = descriptor;
        _isEnabled = isEnabled;
        ToggleEnabledCommand = new AsyncRelayCommand(
            async () =>
            {
                await setEnabled(descriptor.Id, !IsEnabled).ConfigureAwait(true);
                IsEnabled = !IsEnabled;
            },
            onError: onError);
    }

    public string Name => _descriptor.DisplayName;

    public string Vendor => _descriptor.Vendor;

    public string Family => _descriptor.Family.ToString().ToUpperInvariant();

    public string Status => !IsEnabled ? "DÉSACTIVÉ" : _descriptor.Status switch
    {
        EngineModuleStatus.Active => "ACTIF",
        EngineModuleStatus.Preview => "APERÇU",
        EngineModuleStatus.Disabled => "DÉSACTIVÉ",
        _ => "PLANIFIÉ",
    };

    public string ProjectMarkers => string.Join(" · ", _descriptor.ProjectMarkers);

    public string Capabilities => _descriptor.Capabilities == EngineCapability.None
        ? "Contrat provider prêt · implémentation à venir"
        : string.Join(
            " · ",
            Enum.GetValues<EngineCapability>()
                .Where(capability => capability != EngineCapability.None &&
                                     _descriptor.Capabilities.HasFlag(capability))
                .Select(FormatCapability));

    public string Summary => _descriptor.Summary;

    public bool IsActive => IsEnabled && _descriptor.Status == EngineModuleStatus.Active;

    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(Status));
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(ToggleLabel));
            }
        }
    }

    public string ToggleLabel => IsEnabled ? "Désactiver" : "Activer";

    public ICommand ToggleEnabledCommand { get; }

    private static string FormatCapability(EngineCapability capability) => capability switch
    {
        EngineCapability.InstallationDiscovery => "installations",
        EngineCapability.ProjectDiscovery => "projets",
        EngineCapability.EditorLaunch => "éditeur",
        EngineCapability.ProjectLaunch => "lancement",
        EngineCapability.Build => "build",
        EngineCapability.Package => "packaging",
        EngineCapability.PluginManagement => "plugins",
        EngineCapability.AssetLibrary => "assets",
        EngineCapability.SourceBuild => "sources",
        EngineCapability.VersionManagement => "versions",
        EngineCapability.PackageInventory => "inventaire packages",
        EngineCapability.PackageInstallation => "installation packages",
        EngineCapability.PackageRemoval => "suppression packages",
        _ => capability.ToString(),
    };
}

public sealed class ConnectionModuleCardViewModel : ObservableObject
{
    private bool _isEnabled;

    public ConnectionModuleCardViewModel(
        string id,
        string name,
        string description,
        bool isEnabled,
        Func<string, bool, Task> setEnabled,
        Action<Exception> onError)
    {
        Id = id;
        Name = name;
        Description = description;
        _isEnabled = isEnabled;
        ToggleEnabledCommand = new AsyncRelayCommand(async () =>
        {
            var next = !IsEnabled;
            await setEnabled(Id, next).ConfigureAwait(true);
            IsEnabled = next;
        }, onError: onError);
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string ToggleLabel => IsEnabled ? "Désactiver" : "Activer";
    public string Status => IsEnabled ? "ACTIF" : "MASQUÉ";

    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                OnPropertyChanged(nameof(ToggleLabel));
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public ICommand ToggleEnabledCommand { get; }
}

public sealed class LocalEngineInstallationCardViewModel : ObservableObject
{
    private string _effectiveSizeLabel;

    public LocalEngineInstallationCardViewModel(
        LocalEngineInstallation installation,
        LibraryItemOrganizationViewModel organization,
        string effectiveSizeLabel,
        Func<LocalEngineInstallation, Task> launch,
        Func<string, Task> openFolder,
        Action<LocalEngineInstallationCardViewModel> openDetails,
        Action<Exception> onError)
    {
        Installation = installation;
        Organization = organization;
        _effectiveSizeLabel = effectiveSizeLabel;
        LaunchCommand = new AsyncRelayCommand(() => launch(installation), onError: onError);
        OpenFolderCommand = new AsyncRelayCommand(
            () => openFolder(installation.RootPath),
            onError: onError);
        OpenDetailsCommand = new RelayCommand(() => openDetails(this));
    }

    public LocalEngineInstallation Installation { get; }

    public LibraryItemOrganizationViewModel Organization { get; }

    public string Family => Installation.Family.ToString().ToUpperInvariant();

    public string Name => Organization.DisplayName;

    public string Version => string.IsNullOrWhiteSpace(Installation.Version)
        ? "Version inconnue"
        : Installation.Version;

    public string Path => Installation.EditorPath;

    public string Source => Installation.DiscoverySource;

    public string EffectiveSizeLabel
    {
        get => _effectiveSizeLabel;
        private set => SetProperty(ref _effectiveSizeLabel, value);
    }

    public void UpdateEffectiveSize(string label) => EffectiveSizeLabel = label;

    public ICommand LaunchCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand OpenDetailsCommand { get; }
}

public sealed class LocalEngineProjectCardViewModel
{
    public LocalEngineProjectCardViewModel(
        LocalEngineProject project,
        LibraryItemOrganizationViewModel organization,
        Func<LocalEngineProject, Task> launch,
        Func<string, Task> openFolder,
        Action<LocalEngineProjectCardViewModel> openDetails,
        Action<Exception> onError)
    {
        Project = project;
        Organization = organization;
        LaunchCommand = new AsyncRelayCommand(() => launch(project), onError: onError);
        OpenFolderCommand = new AsyncRelayCommand(
            () => openFolder(project.RootPath),
            onError: onError);
        OpenDetailsCommand = new RelayCommand(() => openDetails(this));
    }

    public LocalEngineProject Project { get; }

    public LibraryItemOrganizationViewModel Organization { get; }

    public string Family => Project.Family.ToString().ToUpperInvariant();

    public string Name => Organization.DisplayName;

    public string Path => Project.RootPath;

    public string Version => string.IsNullOrWhiteSpace(Project.RequiredEditorVersion)
        ? "Version non déclarée"
        : $"Requiert {Project.RequiredEditorVersion}";

    public string Description => Project.Description;

    public ICommand LaunchCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand OpenDetailsCommand { get; }
}

public sealed class LocalEnginePackageCardViewModel
{
    public LocalEnginePackageCardViewModel(
        LocalEnginePackage package,
        LibraryItemOrganizationViewModel organization,
        Func<string, Task> openFolder,
        Func<LocalEnginePackage, Task> prepareRemoval,
        Action<Exception> onError)
    {
        Package = package;
        Organization = organization;
        OpenFolderCommand = new AsyncRelayCommand(
            () => openFolder(package.RootPath),
            () => Directory.Exists(package.RootPath),
            onError);
        PrepareRemovalCommand = new AsyncRelayCommand(
            () => prepareRemoval(package),
            () => CanRemove,
            onError);
    }

    public LocalEnginePackage Package { get; }

    public LibraryItemOrganizationViewModel Organization { get; }

    public string Family => Package.Family.ToString().ToUpperInvariant();

    public string Name => Organization.DisplayName;

    public string Version => Package.Version;

    public string Project => Package.ProjectName;

    public string Source => Package.Source;

    public string State => Package.State;

    public string Path => Package.ManifestPath;

    public string Description => Package.Description;

    public string Health => Package.Health switch
    {
        LocalEnginePackageHealth.Ready => "PRÊT",
        LocalEnginePackageHealth.Warning => "À VÉRIFIER",
        _ => "INVALIDE",
    };

    public string DependencySummary => Package.Dependencies.Count == 0
        ? "Aucune dépendance déclarée"
        : $"Dépendances · {string.Join(" · ", Package.Dependencies)}";

    public string IssueSummary => string.Join(" · ", Package.Issues);

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasIssues => Package.Issues.Count > 0;

    public bool CanRemove => Package.Family switch
    {
        EngineFamily.Unity =>
            string.Equals(Package.State, "Package embarqué", StringComparison.Ordinal) &&
            string.Equals(Package.Source, "UPM · embarqué", StringComparison.Ordinal),
        EngineFamily.Godot =>
            !Package.State.StartsWith("Plugin éditeur", StringComparison.OrdinalIgnoreCase) ||
            Package.State.Contains("désactivé", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    public ICommand OpenFolderCommand { get; }

    public ICommand PrepareRemovalCommand { get; }
}

public sealed class LocalEnginePackageInstallHistoryCardViewModel
{
    public LocalEnginePackageInstallHistoryCardViewModel(
        LocalEnginePackageInstallReceipt receipt,
        Func<LocalEnginePackageInstallReceipt, Task> prepareRollback,
        Action<Exception> onError)
    {
        Receipt = receipt;
        PrepareRollbackCommand = new AsyncRelayCommand(
            () => prepareRollback(receipt),
            () => receipt.RolledBackAt is null,
            onError);
    }

    public LocalEnginePackageInstallReceipt Receipt { get; }

    public string Family => Receipt.Family.ToString().ToUpperInvariant();

    public string Name => Receipt.DisplayName;

    public string Version => Receipt.Version;

    public string Project => Receipt.TargetProject.Name;

    public string InstalledAt => Receipt.InstalledAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Status => Receipt.RolledBackAt is null
        ? "INSTALLÉ"
        : $"ANNULÉ · {Receipt.RolledBackAt.Value.ToLocalTime():g}";

    public string Detail => Receipt.BackupDirectory is null
        ? "Nouvelle installation"
        : "Version précédente sauvegardée";

    public string Recovery => Receipt.RolledBackPackageDirectory is null
        ? string.Empty
        : $"Version retirée conservée · {Receipt.RolledBackPackageDirectory}";

    public bool CanRollback => Receipt.RolledBackAt is null;

    public bool HasRecovery => Receipt.RolledBackPackageDirectory is not null;

    public ICommand PrepareRollbackCommand { get; }
}

public sealed class LocalEnginePackageRemovalHistoryCardViewModel
{
    public LocalEnginePackageRemovalHistoryCardViewModel(
        LocalEnginePackageRemovalReceipt receipt,
        Func<LocalEnginePackageRemovalReceipt, Task> prepareRestore,
        Action<Exception> onError)
    {
        Receipt = receipt;
        PrepareRestoreCommand = new AsyncRelayCommand(
            () => prepareRestore(receipt),
            () => receipt.RestoredAt is null,
            onError);
    }

    public LocalEnginePackageRemovalReceipt Receipt { get; }

    public string Family => Receipt.Family.ToString().ToUpperInvariant();

    public string Name => Receipt.DisplayName;

    public string Version => Receipt.Version;

    public string Project => Receipt.TargetProject.Name;

    public string RemovedAt => Receipt.RemovedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Status => Receipt.RestoredAt is null
        ? "RETIRÉ"
        : $"RESTAURÉ · {Receipt.RestoredAt.Value.ToLocalTime():g}";

    public string Detail =>
        $"{Receipt.FileCount:N0} fichier(s) · {FormatBytes(Receipt.TotalBytes)}";

    public string Recovery => Receipt.RestoredAt is null
        ? $"Copie récupérable · {Receipt.RecoveryDirectory}"
        : $"Restauré vers · {Receipt.DestinationDirectory}";

    public bool CanRestore => Receipt.RestoredAt is null;

    public ICommand PrepareRestoreCommand { get; }

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
}

public sealed class HubExtensionCardViewModel
{
    public HubExtensionCardViewModel(HubExtensionSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public HubExtensionSnapshot Snapshot { get; }

    public string Id => Snapshot.Manifest.Id;

    public string Name => Snapshot.Manifest.DisplayName;

    public string Version => Snapshot.Manifest.Version;

    public string Publisher => Snapshot.Manifest.Publisher;

    public string Description => Snapshot.Manifest.Description;

    public string Origin => Snapshot.Origin == HubExtensionOrigin.BuiltIn
        ? "INTÉGRÉE"
        : "UTILISATEUR";

    public string Health => Snapshot.Health == HubExtensionHealth.Ready
        ? "PRÊTE"
        : "INVALIDE";

    public string Kind => Snapshot.Manifest.Kind switch
    {
        HubExtensionKind.AssetProcessor => "ASSET PROCESSOR",
        HubExtensionKind.EngineIntegration => "INTÉGRATION MOTEUR",
        HubExtensionKind.Marketplace => "MARKETPLACE · INVENTAIRE",
        _ => "OUTIL",
    };

    public string Engines => string.Join(" ↔ ", Snapshot.Manifest.SupportedEngines);

    public string Capabilities => string.Join(
        " · ",
        Enum.GetValues<HubExtensionCapability>()
            .Where(capability =>
                capability != HubExtensionCapability.None &&
                Snapshot.Manifest.Capabilities.HasFlag(capability))
            .Select(capability => capability switch
            {
                HubExtensionCapability.AssetInspect => "Inspection",
                HubExtensionCapability.AssetCopy => "Copie",
                HubExtensionCapability.AssetConvert => "Conversion",
                HubExtensionCapability.AssetDowngrade => "Downgrade",
                _ => "Outil projet",
            }));

    public string Formats => Snapshot.Manifest.SupportedFileExtensions.Count == 0
        ? "Non spécifiés"
        : string.Join(" · ", Snapshot.Manifest.SupportedFileExtensions);

    public string ExecutionPolicy => Id == "cyenginehub.unreal-version-bridge"
        ? "Pipeline locale first-party · exécution uniquement après sélection du script d’installation et confirmation des cibles"
        : Snapshot.CanRun
        ? "Exécution first-party autorisée après confirmation"
        : Snapshot.CanOpenWebsite
            ? "Intégration HTTPS · aucun code local"
            : Snapshot.Manifest.ExecutionKind == HubExtensionExecutionKind.ExternalProcess
                ? "Exécution locale bloquée · extension non approuvée"
                : "Extension déclarative";

    public string Error => Snapshot.Error ?? string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public bool CanRun => Snapshot.CanRun;

    public bool CanOpenWebsite => Snapshot.CanOpenWebsite;

    public bool HasDedicatedWindow => Snapshot.Manifest.UiContributions.Any(
        contribution => contribution.Action == HubUiAction.OpenExtension);
}

public sealed class HubUiContributionButtonViewModel
{
    public HubUiContributionButtonViewModel(
        HubExtensionCardViewModel extension,
        HubUiContribution contribution,
        Func<HubExtensionCardViewModel, HubUiContribution, Task> invoke,
        Action<Exception> onError)
    {
        Extension = extension;
        Contribution = contribution;
        InvokeCommand = new AsyncRelayCommand(
            () => invoke(extension, contribution),
            onError: onError);
    }

    public HubExtensionCardViewModel Extension { get; }

    public HubUiContribution Contribution { get; }

    public string Label => Contribution.Label;

    public string ToolTip => $"{Extension.Name} · {Contribution.Action}";

    public ICommand InvokeCommand { get; }
}

public sealed class EngineCardViewModel : ObservableObject
{
    private bool _isUnregisterConfirmationVisible;
    private string _effectiveSizeLabel;

    public EngineCardViewModel(
        UnrealEngineInstallation engine,
        UnrealEngineRegistration? registration,
        LibraryItemOrganizationViewModel organization,
        string effectiveSizeLabel,
        Func<UnrealEngineInstallation, Task> launch,
        Func<string, Task> openFolder,
        Func<UnrealEngineInstallation, Task> setup,
        Func<UnrealEngineInstallation, Task> generateProjectFiles,
        Func<UnrealEngineInstallation, Task> buildEditor,
        Func<UnrealEngineInstallation, Task> register,
        Func<UnrealEngineInstallation, Task> unregister,
        Action<EngineCardViewModel> openDetails,
        Action<Exception> onError)
    {
        Engine = engine;
        Registration = registration;
        Organization = organization;
        _effectiveSizeLabel = effectiveSizeLabel;
        InstallationOptions = BuildInstallationOptions(engine.RootPath);
        LaunchCommand = new AsyncRelayCommand(() => launch(engine), () => File.Exists(engine.EditorPath), onError);
        OpenFolderCommand = new AsyncRelayCommand(() => openFolder(engine.RootPath), onError: onError);
        var canSetup = File.Exists(System.IO.Path.Combine(engine.RootPath, "Setup.bat"));
        SetupCommand = new AsyncRelayCommand(() => setup(engine), () => canSetup, onError);
        var canGenerate = File.Exists(System.IO.Path.Combine(engine.RootPath, "GenerateProjectFiles.bat")) ||
                          File.Exists(System.IO.Path.Combine(
                              engine.RootPath,
                              "Engine",
                              "Build",
                              "BatchFiles",
                              "GenerateProjectFiles.bat"));
        GenerateProjectFilesCommand = new AsyncRelayCommand(
            () => generateProjectFiles(engine),
            () => canGenerate,
            onError);
        BuildEditorCommand = new AsyncRelayCommand(() => buildEditor(engine), () => CanBuildEditor, onError);
        RegisterCommand = new AsyncRelayCommand(
            () => register(engine),
            () => CanRegister,
            onError);
        RequestUnregisterCommand = new RelayCommand(() =>
            IsUnregisterConfirmationVisible = true);
        CancelUnregisterCommand = new RelayCommand(() =>
            IsUnregisterConfirmationVisible = false);
        ConfirmUnregisterCommand = new AsyncRelayCommand(
            () => unregister(engine),
            () => IsRegistered,
            onError);
        OpenDetailsCommand = new RelayCommand(() => openDetails(this));
    }

    public UnrealEngineInstallation Engine { get; }

    public UnrealEngineRegistration? Registration { get; }

    public LibraryItemOrganizationViewModel Organization { get; }

    public string DisplayName => Organization.DisplayName;

    public string VersionLabel => Engine.Version.ToString();

    public string TypeLabel => Engine.Type.ToString();

    public bool IsOfficialEpic => Engine.Type == EngineInstallationType.EpicBinary;
    public bool CanBuildEditor => Engine.Type is EngineInstallationType.Source or EngineInstallationType.SourceFork or EngineInstallationType.Custom &&
        File.Exists(System.IO.Path.Combine(Engine.RootPath, "Engine", "Build", "BatchFiles", "Build.bat"));

    public string OfficialSourceLabel => IsOfficialEpic ? "OFFICIEL EPIC" : "SOURCE / PERSONNALISÉ";

    public IReadOnlyList<EngineInstallationOptionViewModel> InstallationOptions { get; }

    public string LinuxComponentStatus => Directory.Exists(System.IO.Path.Combine(
        Engine.RootPath,
        "Engine",
        "Extras",
        "ThirdPartyNotUE",
        "SDKs",
        "HostWin64"))
        ? "Toolchain Linux détectée"
        : "Toolchain Linux non détectée";

    public string DebugSymbolsStatus => File.Exists(System.IO.Path.Combine(
        Engine.RootPath,
        "Engine",
        "Binaries",
        "Win64",
        "UnrealEditor.pdb"))
        ? "Symboles de débogage détectés"
        : "Symboles de débogage non détectés";

    public string TemplatesStatus => Directory.Exists(System.IO.Path.Combine(Engine.RootPath, "Templates"))
        ? "Templates et exemples détectés"
        : "Templates et exemples non détectés";

    public string HealthLabel => Engine.Health.ToString();

    public string Path => Engine.RootPath;

    public string BranchLabel => string.IsNullOrWhiteSpace(Engine.Branch) ? "—" : Engine.Branch;

    public string IssueSummary => Engine.HealthIssues.Count == 0
        ? "Installation complète"
        : string.Join(" · ", Engine.HealthIssues);

    public string EffectiveSizeLabel
    {
        get => _effectiveSizeLabel;
        private set => SetProperty(ref _effectiveSizeLabel, value);
    }

    public void UpdateEffectiveSize(string label) => EffectiveSizeLabel = label;

    public void SetInstallationOptionsEditing(bool editing)
    {
        foreach (var option in InstallationOptions)
        {
            option.SetEditing(editing);
        }
    }

    public bool IsRegistered => Registration is not null;

    public bool CanRegister => !IsRegistered && Engine.Type != EngineInstallationType.EpicBinary;

    public bool CanSetup => File.Exists(System.IO.Path.Combine(Engine.RootPath, "Setup.bat"));

    public string RegistrationLabel => IsRegistered
        ? $"Associé {Registration!.Identifier}"
        : Engine.Type == EngineInstallationType.EpicBinary
            ? "Association Epic"
            : "Non enregistré";

    public bool IsUnregisterConfirmationVisible
    {
        get => _isUnregisterConfirmationVisible;
        private set => SetProperty(ref _isUnregisterConfirmationVisible, value);
    }

    public ICommand LaunchCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand SetupCommand { get; }

    public ICommand GenerateProjectFilesCommand { get; }

    public ICommand BuildEditorCommand { get; }

    public ICommand RegisterCommand { get; }

    public ICommand RequestUnregisterCommand { get; }

    public ICommand CancelUnregisterCommand { get; }

    public ICommand ConfirmUnregisterCommand { get; }

    public ICommand OpenDetailsCommand { get; }

    private static IReadOnlyList<EngineInstallationOptionViewModel> BuildInstallationOptions(string rootPath)
    {
        var engineRoot = System.IO.Path.Combine(rootPath, "Engine");
        bool HasDirectory(params string[] parts) => Directory.Exists(
            parts.Aggregate(rootPath, System.IO.Path.Combine));
        bool HasFile(params string[] parts) => File.Exists(
            parts.Aggregate(rootPath, System.IO.Path.Combine));

        return
        [
            new EngineInstallationOptionViewModel(
                "source",
                "Moteur Source",
                "Code source C++ du moteur",
                Directory.Exists(System.IO.Path.Combine(engineRoot, "Source"))),
            new EngineInstallationOptionViewModel(
                "metahuman",
                "Données essentielles de MetaHuman Creator",
                "Core Data et plugins MetaHuman installés localement",
                HasDirectory("Engine", "Plugins", "Marketplace", "MetaHuman") ||
                HasDirectory("Engine", "Plugins", "Animation", "MetaHuman")),
            new EngineInstallationOptionViewModel(
                "symbols",
                "Symboles d’édition pour débogage",
                "PDB de l’éditeur Unreal",
                HasFile("Engine", "Binaries", "Win64", "UnrealEditor.pdb") ||
                HasFile("Engine", "Binaries", "Win64", "UE4Editor.pdb")),
            new EngineInstallationOptionViewModel(
                "android",
                "Plateforme cible Android",
                "Modules et outils de compilation Android",
                HasDirectory("Engine", "Platforms", "Android") ||
                HasDirectory("Engine", "Source", "Runtime", "Android")),
            new EngineInstallationOptionViewModel(
                "ios",
                "Plateforme cible iOS",
                "Modules de compilation iOS",
                HasDirectory("Engine", "Platforms", "IOS") ||
                HasDirectory("Engine", "Source", "Runtime", "IOS")),
            new EngineInstallationOptionViewModel(
                "linux",
                "Plateforme cible Linux",
                "Modules et toolchain Linux",
                HasDirectory("Engine", "Platforms", "Linux") ||
                HasDirectory("Engine", "Extras", "ThirdPartyNotUE", "SDKs", "HostWin64")),
            new EngineInstallationOptionViewModel(
                "mac",
                "Plateforme cible Mac",
                "Modules de compilation macOS",
                HasDirectory("Engine", "Platforms", "Mac") ||
                HasDirectory("Engine", "Source", "Runtime", "Apple")),
        ];
    }
}

public sealed class EngineInstallationOptionViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isEditable;

    public EngineInstallationOptionViewModel(
        string id,
        string name,
        string description,
        bool isInstalled)
    {
        Id = id;
        Name = name;
        Description = description;
        IsInstalled = isInstalled;
        _isSelected = isInstalled;
    }

    public string Id { get; }

    public string Name { get; }

    public string Description { get; }

    public bool IsInstalled { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(Status));
            }
        }
    }

    public bool IsEditable
    {
        get => _isEditable;
        private set
        {
            if (SetProperty(ref _isEditable, value))
            {
                OnPropertyChanged(nameof(CanEditSelection));
            }
        }
    }

    public bool CanEditSelection => IsEditable && !IsInstalled;

    public string Status => IsInstalled
        ? "Installé"
        : IsSelected
            ? "À télécharger"
            : "Non installé";

    public void SetEditing(bool editing)
    {
        IsEditable = editing;
        if (!editing)
        {
            IsSelected = IsInstalled;
        }
    }
}

public sealed class SettingsCategoryOptionViewModel(
    string id,
    string label,
    string description)
{
    public string Id { get; } = id;

    public string Label { get; } = label;

    public string Description { get; } = description;
}

public sealed class QuixelLocalAssetCardViewModel
{
    public QuixelLocalAssetCardViewModel(
        string name,
        string assetId,
        string path,
        string kind,
        Func<QuixelLocalAssetCardViewModel, Task> use,
        Func<string, Task> openFolder,
        Action<Exception> onError)
    {
        Name = name;
        AssetId = assetId;
        Path = path;
        Kind = kind;
        UseCommand = new AsyncRelayCommand(() => use(this), onError: onError);
        OpenFolderCommand = new AsyncRelayCommand(() => openFolder(path), onError: onError);
    }

    public string Name { get; }

    public string AssetId { get; }

    public string Path { get; }

    public string Kind { get; }

    public ICommand UseCommand { get; }

    public ICommand OpenFolderCommand { get; }
}

public sealed class ProjectImportCandidateViewModel : ObservableObject
{
    private bool _isSelected;

    public ProjectImportCandidateViewModel(
        string markerPath,
        string projectRoot,
        string name,
        string engine,
        bool isAlreadyCatalogued)
    {
        MarkerPath = markerPath;
        ProjectRoot = projectRoot;
        Name = name;
        Engine = engine;
        IsAlreadyCatalogued = isAlreadyCatalogued;
        _isSelected = !isAlreadyCatalogued;
    }

    public string MarkerPath { get; }

    public string ProjectRoot { get; }

    public string Name { get; }

    public string Engine { get; }

    public bool IsAlreadyCatalogued { get; }

    public string Status => IsAlreadyCatalogued ? "Déjà présent" : "Nouveau";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class LibraryFacetOptionViewModel : ObservableObject
{
    private readonly Action _selectionChanged;
    private bool _isSelected;
    private int _resultCount;

    public LibraryFacetOptionViewModel(string value, bool isSelected, Action selectionChanged)
    {
        Value = value;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged;
    }

    public string Value { get; }

    public string Label => $"{Value} ({ResultCount:N0})";

    public bool HasResults => ResultCount > 0;

    public int ResultCount
    {
        get => _resultCount;
        private set
        {
            if (SetProperty(ref _resultCount, value))
            {
                OnPropertyChanged(nameof(Label));
                OnPropertyChanged(nameof(HasResults));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged();
            }
        }
    }

    public void UpdateResultCount(int count) => ResultCount = count;

    public void ClearSelectionSilently()
    {
        if (_isSelected)
        {
            _isSelected = false;
            OnPropertyChanged(nameof(IsSelected));
        }
    }

    public void SetSelectionSilently(bool selected)
    {
        if (_isSelected == selected) return;
        _isSelected = selected;
        OnPropertyChanged(nameof(IsSelected));
    }
}

public sealed class FabInstallVersionOptionViewModel : ObservableObject
{
    private bool _isSelected;

    public FabInstallVersionOptionViewModel(RemoteFabProjectVersion version, bool isSelected)
    {
        Version = version;
        _isSelected = isSelected;
    }

    public RemoteFabProjectVersion Version { get; }

    public string Label
    {
        get
        {
            if (Version.ArtifactId.StartsWith("quixel-tier:", StringComparison.OrdinalIgnoreCase))
            {
                var tier = Version.ArtifactId.Split(':').LastOrDefault() ?? "?";
                return $"Megascans UAsset — Tier {tier}";
            }
            if (Version.ArtifactId.StartsWith("quixel-raw:", StringComparison.OrdinalIgnoreCase))
            {
                var parts = Version.ArtifactId.Split(':');
                var resolution = parts.Length > 2 ? parts[2] : "?";
                var resolutionLabel = resolution switch
                {
                    "1024" => "1K",
                    "2048" => "2K",
                    "4096" => "4K",
                    "8192" => "8K",
                    _ => $"{resolution}px",
                };
                var format = parts.Length > 3 ? parts[3].ToUpperInvariant() : "RAW";
                var content = format == "TEXTURES" ? "Textures source" : $"{format} + textures";
                return $"Megascans RAW — {resolutionLabel} — {content}";
            }
            var engines = Version.EngineVersions.Count == 0
                ? "Version générique"
                : string.Join(" · ", Version.EngineVersions.Select(FormatEngineVersion));
            var platform = Version.Platforms.Count == 0
                ? "Toutes plateformes"
                : Version.Platforms[0];
            return $"{engines} — {platform}";
        }
    }

    public string ArtifactLabel => Version.ArtifactId.StartsWith("quixel-raw:", StringComparison.OrdinalIgnoreCase)
        ? "Fichiers source multi-moteur"
        : $"Artefact {Version.ArtifactId[..Math.Min(12, Version.ArtifactId.Length)]}";

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private static string FormatEngineVersion(string value)
    {
        var normalized = value.Trim()
            .Replace("Unreal Engine", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("UE_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("UE", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimStart('_', '-', ' ');
        return normalized.Length == 0 ? value : $"UE : {normalized}";
    }
}

public sealed class FabInstallationScriptOptionViewModel(
    FabInstallationScriptDescriptor descriptor)
{
    public FabInstallationScriptDescriptor Descriptor { get; } = descriptor;
    public string Id => Descriptor.Id;
    public string DisplayName => Descriptor.DisplayName;
    public string Description => Descriptor.Description;
    public string OriginLabel => Descriptor.IsBuiltIn ? "Intégré" : Descriptor.Publisher;
}

public sealed class FabInstallationConfigurationOptionViewModel : ObservableObject
{
    private readonly Action _changed;
    private string _value;

    public FabInstallationConfigurationOptionViewModel(
        FabInstallationOptionDefinition definition,
        string value,
        Action changed)
    {
        Definition = definition;
        _value = value;
        _changed = changed;
        Choices = new ObservableCollection<string>(definition.Choices);
    }

    public FabInstallationOptionDefinition Definition { get; }
    public string Id => Definition.Id;
    public string DisplayName => Definition.DisplayName;
    public string Description => Definition.Description;
    public ObservableCollection<string> Choices { get; }
    public bool IsBoolean => Definition.Kind == FabInstallationOptionKind.Boolean;
    public bool IsText => Definition.Kind == FabInstallationOptionKind.Text;
    public bool IsChoice => Definition.Kind == FabInstallationOptionKind.Choice;

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(BooleanValue));
                _changed();
            }
        }
    }

    public bool BooleanValue
    {
        get => bool.TryParse(Value, out var parsed) && parsed;
        set => Value = value ? bool.TrueString : bool.FalseString;
    }
}

public sealed class FabInstallTargetOptionViewModel : ObservableObject
{
    private readonly Action? _selectionChanged;
    private readonly Action<FabInstallTargetOptionViewModel>? _scriptSettingsChanged;
    private bool _isSelected;
    private bool _isVisible = true;
    private bool _isScriptConfigurationVisible;
    private FabInstallationScriptOptionViewModel? _selectedInstallationScript;

    public FabInstallTargetOptionViewModel(
        string id,
        string name,
        string kind,
        string path,
        string engineVersion,
        bool isCompatible,
        bool ignoresEngineCompatibility,
        LibraryPackageContentKind contentKind,
        IReadOnlyList<IFabInstallationScript> installationScripts,
        string? selectedInstallationScriptId = null,
        IReadOnlyDictionary<string, string>? installationConfiguration = null,
        ProjectCardViewModel? project = null,
        EngineCardViewModel? engine = null,
        Action? selectionChanged = null,
        Action<FabInstallTargetOptionViewModel>? scriptSettingsChanged = null,
        Action<FabInstallTargetOptionViewModel>? openScriptConfiguration = null)
    {
        Id = id;
        Name = name;
        Kind = kind;
        Path = path;
        EngineVersion = engineVersion;
        IsCompatible = isCompatible;
        IgnoresEngineCompatibility = ignoresEngineCompatibility;
        ContentKind = contentKind;
        Project = project;
        Engine = engine;
        _selectionChanged = selectionChanged;
        _scriptSettingsChanged = scriptSettingsChanged;
        OpenInstallationConfigurationCommand = new RelayCommand(() =>
            openScriptConfiguration?.Invoke(this));
        AvailableInstallationScripts = new ObservableCollection<FabInstallationScriptOptionViewModel>(
            installationScripts.Select(script => new FabInstallationScriptOptionViewModel(script.Descriptor)));
        _selectedInstallationScript = AvailableInstallationScripts.FirstOrDefault(script =>
                                          string.Equals(
                                              script.Id,
                                              selectedInstallationScriptId,
                                              StringComparison.OrdinalIgnoreCase))
                                      ?? AvailableInstallationScripts.FirstOrDefault();
        RebuildInstallationConfiguration(installationConfiguration);
    }

    public string Id { get; }
    public string Name { get; }
    public string Kind { get; }
    public string Path { get; }
    public string EngineVersion { get; }
    public bool IsCompatible { get; }
    public bool IgnoresEngineCompatibility { get; }
    public LibraryPackageContentKind ContentKind { get; }
    public bool CanUseVersionBridge => IsProject && AvailableInstallationScripts.Any(script =>
        string.Equals(
            script.Id,
            UnrealVersionBridgeInstallationScript.ScriptId,
            StringComparison.OrdinalIgnoreCase));
    public bool IsUsingVersionBridge => string.Equals(
        SelectedInstallationScript?.Id,
        UnrealVersionBridgeInstallationScript.ScriptId,
        StringComparison.OrdinalIgnoreCase);
    public string CompatibilityLabel => IgnoresEngineCompatibility
        ? "Megascans · toute version"
        : IsCompatible
        ? "Compatible"
        : CanUseVersionBridge
            ? "Convertible"
            : "Compatibilité à vérifier";
    public ProjectCardViewModel? Project { get; }
    public EngineCardViewModel? Engine { get; }
    public bool IsProject => Project is not null;
    public bool IsEngine => Engine is not null;
    public ObservableCollection<FabInstallationScriptOptionViewModel> AvailableInstallationScripts { get; }
    public ObservableCollection<FabInstallationConfigurationOptionViewModel> InstallationConfiguration { get; } = [];
    public bool HasInstallationScripts => AvailableInstallationScripts.Count > 0;
    public bool HasInstallationConfiguration => InstallationConfiguration.Count > 0;
    public string InstallationScriptSummary => SelectedInstallationScript?.Description
                                               ?? "Aucun script compatible avec ce type de contenu.";
    public string InstallationScriptEngineVersionLabel =>
        $"Version utilisée par le script : Unreal Engine {EngineVersion}";
    public ICommand OpenInstallationConfigurationCommand { get; }

    public FabInstallationScriptOptionViewModel? SelectedInstallationScript
    {
        get => _selectedInstallationScript;
        set
        {
            if (SetProperty(ref _selectedInstallationScript, value))
            {
                RebuildInstallationConfiguration(null);
                OnPropertyChanged(nameof(HasInstallationConfiguration));
                OnPropertyChanged(nameof(InstallationScriptSummary));
                OnPropertyChanged(nameof(IsUsingVersionBridge));
                _scriptSettingsChanged?.Invoke(this);
            }
        }
    }

    public bool IsScriptConfigurationVisible
    {
        get => _isScriptConfigurationVisible;
        set => SetProperty(ref _isScriptConfigurationVisible, value);
    }

    public IReadOnlyDictionary<string, string> GetInstallationConfiguration() =>
        InstallationConfiguration.ToDictionary(
            option => option.Id,
            option => option.Value,
            StringComparer.OrdinalIgnoreCase);

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                _selectionChanged?.Invoke();
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    private void RebuildInstallationConfiguration(
        IReadOnlyDictionary<string, string>? values)
    {
        InstallationConfiguration.Clear();
        if (SelectedInstallationScript is null) return;
        foreach (var definition in SelectedInstallationScript.Descriptor.Options)
        {
            var value = values is not null && values.TryGetValue(definition.Id, out var stored)
                ? stored
                : definition.DefaultValue;
            InstallationConfiguration.Add(new FabInstallationConfigurationOptionViewModel(
                definition,
                value,
                () => _scriptSettingsChanged?.Invoke(this)));
        }
    }
}

public sealed class FabCollectionOptionViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isVisible = true;

    public FabCollectionOptionViewModel(string name, bool isSelected)
    {
        Name = name;
        _isSelected = isSelected;
    }

    public string Name { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}

public sealed class FabStoredDownloadViewModel : ObservableObject
{
    private bool _isDeleteConfirmationVisible;
    private readonly long? _uncompressedBytes;
    private readonly long? _storedBytes;

    public FabStoredDownloadViewModel(
        string assetId,
        string version,
        string artifactId,
        string path,
        long bytes,
        DateTimeOffset downloadedAt,
        string source,
        string packageKind,
        string storageMode,
        string? manifestPath,
        long? uncompressedBytes,
        long? storedBytes,
        Func<string, Task> openFolder,
        Func<FabStoredDownloadViewModel, Task> delete,
        Action<Exception> onError,
        string? derivationName = null,
        string? derivationNotes = null,
        string? sourceArtifactId = null,
        Func<FabStoredDownloadViewModel, Task>? installDerived = null,
        Action<FabStoredDownloadViewModel>? createDerived = null,
        Action<FabStoredDownloadViewModel>? createTest = null,
        IReadOnlyList<string>? engineVersions = null)
    {
        AssetId = assetId;
        Version = version;
        ArtifactId = artifactId;
        Path = path;
        Size = FormatBytes(bytes);
        _uncompressedBytes = uncompressedBytes is >= 0 ? uncompressedBytes : null;
        _storedBytes = storedBytes is >= 0 ? storedBytes : bytes;
        RawSize = _uncompressedBytes is not null ? FormatBytes(_uncompressedBytes.Value) : "Inconnue";
        StoredSize = _storedBytes is not null ? FormatBytes(_storedBytes.Value) : Size;
        DownloadedAt = downloadedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        Source = source;
        PackageKind = packageKind;
        StorageMode = storageMode;
        ManifestPath = manifestPath;
        DerivationName = derivationName;
        DerivationNotes = derivationNotes;
        SourceArtifactId = sourceArtifactId;
        EngineVersions = engineVersions ?? [];
        OpenFolderCommand = new AsyncRelayCommand(() => openFolder(path), onError: onError);
        RequestDeleteCommand = new RelayCommand(() => IsDeleteConfirmationVisible = true);
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirmationVisible = false);
        ConfirmDeleteCommand = new AsyncRelayCommand(
            async () =>
            {
                await delete(this).ConfigureAwait(true);
                IsDeleteConfirmationVisible = false;
            },
            onError: onError);
        InstallDerivedCommand = new AsyncRelayCommand(
            () => installDerived?.Invoke(this) ?? Task.CompletedTask,
            () => IsDerived,
            onError);
        CreateDerivedCommand = new RelayCommand(
            () => createDerived?.Invoke(this));
        CreateTestCommand = new RelayCommand(
            () => createTest?.Invoke(this));
    }

    public string AssetId { get; }
    public string Version { get; }
    public string ArtifactId { get; }
    public string Path { get; }
    public string Size { get; }
    public string RawSize { get; }
    public string StoredSize { get; }
    public string DownloadedAt { get; }
    public string Source { get; }
    public string PackageKind { get; }
    public string StorageMode { get; }
    public string? ManifestPath { get; }
    public string? DerivationName { get; }
    public string? DerivationNotes { get; }
    public string? SourceArtifactId { get; }
    public IReadOnlyList<string> EngineVersions { get; }
    public bool IsDerived => !string.IsNullOrWhiteSpace(SourceArtifactId) ||
                             ArtifactId.StartsWith("derived:", StringComparison.OrdinalIgnoreCase) ||
                             StorageMode.Equals("DerivedVariant", StringComparison.OrdinalIgnoreCase);
    public string OriginLabel => IsDerived
        ? $"Dérivé de {SourceArtifactId ?? "une version locale"}"
        : "Version fournisseur";
    public string PackageSummary => $"{FormatPackageKind(PackageKind)} · {FormatStorageMode(StorageMode)}";
    public bool IsCompressed => StorageMode.StartsWith("Zip", StringComparison.OrdinalIgnoreCase);
    public string RawSizeLabel => $"Non compressé : {RawSize}";
    public string StoredSizeLabel =>
        $"{(IsCompressed ? "Compressé" : "Stocké")} : {StoredSize}{FormatSavings()}";
    public ICommand OpenFolderCommand { get; }
    public ICommand RequestDeleteCommand { get; }
    public ICommand CancelDeleteCommand { get; }
    public ICommand ConfirmDeleteCommand { get; }
    public ICommand InstallDerivedCommand { get; }
    public ICommand CreateDerivedCommand { get; }
    public ICommand CreateTestCommand { get; }

    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        private set => SetProperty(ref _isDeleteConfirmationVisible, value);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)Math.Max(0, bytes);
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.##} {units[index]}";
    }

    private static string FormatStorageMode(string value) => value switch
    {
        "ZipStore" => "ZIP stockage",
        "ZipFastest" => "ZIP rapide",
        "ZipBalanced" => "ZIP moyen",
        "ZipHigh" => "ZIP élevé",
        "ZipUltra" => "ZIP ultra",
        "DerivedVariant" => "Variante convertie",
        _ => "Non compressé",
    };

    private string FormatSavings()
    {
        if (!IsCompressed || _uncompressedBytes is not > 0 || _storedBytes is null)
        {
            return string.Empty;
        }
        var savings = (1 - Math.Clamp((double)_storedBytes.Value / _uncompressedBytes.Value, 0, 1)) * 100;
        return savings >= 0.05 ? $" · gain {savings:0.#} %" : " · aucun gain";
    }

    private static string FormatPackageKind(string value) => value switch
    {
        "CompleteProject" => "Projet complet",
        "ContentPack" => "Pack de contenu",
        "CodePlugin" => "Plugin code",
        "EnginePackage" => "Package moteur",
        "RawAsset" => "Asset RAW",
        _ => "Package",
    };
}

public sealed class FabVersionDownloadViewModel : ObservableObject
{
    private readonly RelayCommand _cancelCommand;
    private Guid _taskId;
    private string _status = "En attente";
    private string _message = "Le téléchargement est dans la file.";
    private double _progressPercent;
    private bool _canCancel = true;

    public FabVersionDownloadViewModel(
        string assetId,
        string source,
        string version,
        string artifactId,
        Action<Guid> cancel)
    {
        AssetId = assetId;
        Source = source;
        Version = version;
        ArtifactId = artifactId;
        _cancelCommand = new RelayCommand(
            () => cancel(_taskId),
            () => _taskId != Guid.Empty && CanCancel);
        CancelCommand = _cancelCommand;
    }

    public string AssetId { get; }
    public string Source { get; }
    public string Version { get; }
    public string ArtifactId { get; }
    public ICommand CancelCommand { get; }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public bool CanCancel
    {
        get => _canCancel;
        private set
        {
            if (SetProperty(ref _canCancel, value))
            {
                _cancelCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsTerminal => Status is "Terminé" or "Échec" or "Annulé";

    public void AttachTask(Guid taskId)
    {
        _taskId = taskId;
        _cancelCommand.NotifyCanExecuteChanged();
    }

    public void ApplySnapshot(HubTaskSnapshot snapshot)
    {
        ProgressPercent = Math.Clamp((snapshot.Progress ?? 0) * 100, 0, 100);
        Status = snapshot.Status switch
        {
            HubTaskStatus.Queued => "En attente",
            HubTaskStatus.Running => "Téléchargement",
            HubTaskStatus.Completed => "Terminé",
            HubTaskStatus.Failed => "Échec",
            HubTaskStatus.Canceled => "Annulé",
            _ => snapshot.Status.ToString(),
        };
        Message = snapshot.Error ?? snapshot.Message ?? Status;
        CanCancel = snapshot.Status is HubTaskStatus.Queued or HubTaskStatus.Running;
        OnPropertyChanged(nameof(IsTerminal));
    }
}

public sealed class FabEngineVersionBadgeViewModel
{
    public FabEngineVersionBadgeViewModel(string version, bool isInstalled)
    {
        Version = version;
        IsInstalled = isInstalled;
    }

    public string Version { get; }
    public string DisplayVersion => $"{(IsInstalled ? "✓" : "○")} UE : {FormatVersion(Version)}";
    public bool IsInstalled { get; }
    public IBrush Foreground => IsInstalled ? Brushes.LightGreen : Brushes.IndianRed;
    public string ToolTip => IsInstalled
        ? $"Unreal Engine {FormatVersion(Version)} est installé"
        : $"Unreal Engine {FormatVersion(Version)} n’est pas installé";

    private static string FormatVersion(string value)
    {
        var normalized = value.Trim()
            .Replace("Unreal Engine", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("UE_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("UE", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimStart('_', '-', ' ');
        return normalized.Length == 0 ? value : normalized;
    }
}

public sealed class RawLogEntryViewModel
{
    private static readonly IBrush DefaultForeground = new SolidColorBrush(Color.Parse("#93A4B6"));
    private static readonly IBrush InfoForeground = new SolidColorBrush(Color.Parse("#9EB0C2"));
    private static readonly IBrush TaskForeground = new SolidColorBrush(Color.Parse("#70A8FA"));
    private static readonly IBrush WarningForeground = new SolidColorBrush(Color.Parse("#D9A55C"));
    private static readonly IBrush ErrorForeground = new SolidColorBrush(Color.Parse("#E8747C"));
    private static readonly IBrush OutputForeground = new SolidColorBrush(Color.Parse("#79C7C3"));

    public RawLogEntryViewModel(string text)
    {
        Text = text;
        Foreground = ResolveForeground(text);
    }

    public string Text { get; }
    public IBrush Foreground { get; }

    private static IBrush ResolveForeground(string text)
    {
        if (text.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("[FATAL]", StringComparison.OrdinalIgnoreCase))
        {
            return ErrorForeground;
        }
        if (text.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("[WARNING]", StringComparison.OrdinalIgnoreCase))
        {
            return WarningForeground;
        }
        if (text.Contains("[TASK]", StringComparison.OrdinalIgnoreCase))
        {
            return TaskForeground;
        }
        if (text.Contains("[INFO]", StringComparison.OrdinalIgnoreCase))
        {
            return InfoForeground;
        }
        if (text.Contains("[OUT]", StringComparison.OrdinalIgnoreCase))
        {
            return OutputForeground;
        }
        return DefaultForeground;
    }
}

public sealed class ProjectCardViewModel
{
    public ProjectCardViewModel(
        UnrealProject project,
        LibraryItemOrganizationViewModel organization,
        Func<UnrealProject, Task> launch,
        Func<string, Task> openFolder,
        Func<UnrealProject, Task> generateProjectFiles,
        Func<UnrealProject, Task> buildEditor,
        Func<UnrealProject, Task> package,
        Action<ProjectCardViewModel> openDetails,
        Action<Exception> onError)
    {
        Project = project;
        Organization = organization;
        Thumbnail = LoadThumbnail(project);
        LaunchCommand = new AsyncRelayCommand(() => launch(project), onError: onError);
        OpenFolderCommand = new AsyncRelayCommand(() => openFolder(project.RootPath), onError: onError);
        GenerateProjectFilesCommand = new AsyncRelayCommand(
            () => generateProjectFiles(project),
            onError: onError);
        BuildEditorCommand = new AsyncRelayCommand(() => buildEditor(project), onError: onError);
        PackageCommand = new AsyncRelayCommand(() => package(project), onError: onError);
        OpenDetailsCommand = new RelayCommand(() => openDetails(this));
    }

    public UnrealProject Project { get; }

    public LibraryItemOrganizationViewModel Organization { get; }

    public string Name => Organization.DisplayName;

    public Bitmap? Thumbnail { get; }

    public bool HasThumbnail => Thumbnail is not null;

    public string EngineLabel => string.IsNullOrWhiteSpace(Project.EngineAssociation)
        ? "Non associé"
        : Project.EngineAssociation;

    public string Path => Project.RootPath;

    public string SelectionLabel => $"{Name} — {EngineLabel}";

    public int PluginCount => Project.Plugins.Count(plugin => plugin.Enabled);

    public int ModuleCount => Project.Modules.Count;

    public string HealthLabel => Project.Health.ToString();

    public ICommand LaunchCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand GenerateProjectFilesCommand { get; }

    public ICommand BuildEditorCommand { get; }

    public ICommand PackageCommand { get; }

    public ICommand OpenDetailsCommand { get; }

    private static Bitmap? LoadThumbnail(UnrealProject project)
    {
        var candidates = new List<string>();
        var configuredThumbnail = ReadConfiguredProjectThumbnail(project.RootPath);
        if (configuredThumbnail is not null)
        {
            candidates.Add(configuredThumbnail);
        }

        candidates.Add(System.IO.Path.Combine(project.RootPath, "Saved", "AutoScreenshot.png"));
        candidates.Add(System.IO.Path.Combine(project.RootPath, "Saved", "AutoScreenshot.jpg"));
        candidates.Add(System.IO.Path.Combine(project.RootPath, $"{project.Name}.png"));
        foreach (var path in candidates)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists && info.Length is > 0 and <= 20 * 1024 * 1024 &&
                    (info.Attributes & FileAttributes.ReparsePoint) == 0)
                {
                    return new Bitmap(path);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Une miniature invalide ne doit jamais bloquer le catalogue projet.
            }
        }

        return null;
    }

    private static string? ReadConfiguredProjectThumbnail(string projectRoot)
    {
        try
        {
            var configPath = System.IO.Path.Combine(projectRoot, "Config", "DefaultGame.ini");
            var info = new FileInfo(configPath);
            if (!info.Exists || info.Length is <= 0 or > 1024 * 1024 ||
                (info.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return null;
            }

            var value = File.ReadLines(configPath)
                .Take(20_000)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.StartsWith(
                    "ProjectThumbnail=",
                    StringComparison.OrdinalIgnoreCase));
            if (value is null)
            {
                return null;
            }

            value = value[(value.IndexOf('=') + 1)..].Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(value) || System.IO.Path.IsPathRooted(value))
            {
                return null;
            }

            var root = System.IO.Path.GetFullPath(projectRoot)
                .TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) +
                System.IO.Path.DirectorySeparatorChar;
            var candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, value));
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                ? candidate
                : null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

public sealed class PluginCardViewModel
{
    public PluginCardViewModel(
        UnrealPluginDescriptor plugin,
        LibraryItemOrganizationViewModel organization,
        Func<string, Task> openFolder,
        Func<UnrealPluginDescriptor, Task> prepareInstall,
        Func<UnrealPluginDescriptor, Task> buildPlugin,
        Action<Exception> onError)
    {
        Plugin = plugin;
        Organization = organization;
        OpenFolderCommand = new AsyncRelayCommand(
            () => openFolder(plugin.RootPath),
            onError: onError);
        PrepareInstallCommand = new AsyncRelayCommand(
            () => prepareInstall(plugin),
            () => CanInstall,
            onError);
        BuildPluginCommand = new AsyncRelayCommand(
            () => buildPlugin(plugin),
            () => CanBuild,
            onError);
    }

    public UnrealPluginDescriptor Plugin { get; }

    public LibraryItemOrganizationViewModel Organization { get; }

    public string Name => Organization.DisplayName;

    public string InternalName => Plugin.Name;

    public string VersionLabel => !string.IsNullOrWhiteSpace(Plugin.VersionName)
        ? Plugin.VersionName
        : Plugin.Version > 0 ? Plugin.Version.ToString(CultureInfo.InvariantCulture) : "Non versionné";

    public string SourceLabel => $"{Plugin.SourceKind} · {Plugin.SourceLabel}";

    public string Path => Plugin.RootPath;

    public string EngineVersionLabel => string.IsNullOrWhiteSpace(Plugin.EngineVersion)
        ? "UE non spécifiée"
        : $"UE {Plugin.EngineVersion}";

    public string ModuleSummary => Plugin.Modules.Count == 0
        ? Plugin.CanContainContent ? "Contenu uniquement" : "Aucun module"
        : string.Join(", ", Plugin.Modules.Select(module => module.Name));

    public string PlatformSummary => Plugin.SupportedTargetPlatforms.Count == 0
        ? "Plateformes non restreintes"
        : string.Join(" · ", Plugin.SupportedTargetPlatforms);

    public string DependencySummary => Plugin.Dependencies.Count == 0
        ? "Aucune dépendance déclarée"
        : $"{Plugin.Dependencies.Count} dépendance(s) déclarée(s)";

    public string HealthLabel => Plugin.Health.ToString();

    public string IssueSummary => Plugin.Issues.Count == 0
        ? "Descripteur valide"
        : string.Join(" · ", Plugin.Issues);

    public bool HasSource => Plugin.HasSource;

    public bool CanInstall => Plugin.Health != PluginHealth.Broken;

    public bool CanBuild => Plugin.Health != PluginHealth.Broken && Plugin.HasSource;

    public ICommand OpenFolderCommand { get; }

    public ICommand PrepareInstallCommand { get; }

    public ICommand BuildPluginCommand { get; }
}

public sealed class PluginInstallTargetViewModel
{
    public PluginInstallTargetViewModel(PluginInstallTarget target)
    {
        Target = target;
    }

    public PluginInstallTarget Target { get; }

    public string SelectionLabel => Target.DisplayName;

    public string Path => Target.PluginsDirectory;
}

public sealed class ToolchainInstallationCardViewModel
{
    public ToolchainInstallationCardViewModel(
        ToolchainInstallation installation,
        Func<string, Task> openFolder,
        Action<Exception> onError)
    {
        Installation = installation;
        var folder = Directory.Exists(installation.Path)
            ? installation.Path
            : System.IO.Path.GetDirectoryName(installation.Path) ?? installation.Path;
        OpenFolderCommand = new AsyncRelayCommand(() => openFolder(folder), onError: onError);
    }

    public ToolchainInstallation Installation { get; }

    public string Kind => Installation.Kind.ToString();

    public string Name => Installation.DisplayName;

    public string Version => Installation.Version;

    public string Path => Installation.Path;

    public string Source => Installation.Source;

    public string Health => Installation.Health.ToString();

    public string IssueSummary => Installation.Issues.Count == 0
        ? "Détection valide"
        : string.Join(" · ", Installation.Issues);

    public ICommand OpenFolderCommand { get; }
}

public sealed class EngineToolchainCardViewModel
{
    public EngineToolchainCardViewModel(EngineToolchainSnapshot snapshot)
    {
        Snapshot = snapshot;
        Checks = snapshot.Checks.Select(check => new ToolchainCheckItemViewModel(check)).ToArray();
    }

    public EngineToolchainSnapshot Snapshot { get; }

    public string Name => Snapshot.EngineDisplayName;

    public string Version => Snapshot.EngineVersion;

    public string Health => Snapshot.OverallHealth.ToString();

    public string Summary
    {
        get
        {
            var ready = Snapshot.Checks.Count(check => check.Health == ToolchainHealth.Ready);
            var warnings = Snapshot.Checks.Count(check => check.Health == ToolchainHealth.Warning);
            var missing = Snapshot.Checks.Count(check => check.Health == ToolchainHealth.Missing);
            return $"{ready} prêt(s) · {warnings} avertissement(s) · {missing} manquant(s)";
        }
    }

    public IReadOnlyList<ToolchainCheckItemViewModel> Checks { get; }
}

public sealed class ToolchainCheckItemViewModel
{
    public ToolchainCheckItemViewModel(ToolchainCompatibilityCheck check)
    {
        Label = check.Label;
        Health = check.Health.ToString();
        Required = check.Required;
        Installed = check.Installed;
        Diagnostic = check.Diagnostic;
    }

    public string Label { get; }

    public string Health { get; }

    public string Required { get; }

    public string Installed { get; }

    public string Diagnostic { get; }
}

public sealed class EnvironmentVariableCardViewModel
{
    public EnvironmentVariableCardViewModel(EnvironmentVariableSnapshot variable)
    {
        Name = variable.Name;
        ProcessValue = Format(variable.ProcessValue);
        UserValue = Format(variable.UserValue);
        MachineValue = Format(variable.MachineValue);
        Health = variable.HasValue ? "Définie" : "Absente";
    }

    public string Name { get; }

    public string ProcessValue { get; }

    public string UserValue { get; }

    public string MachineValue { get; }

    public string Health { get; }

    private static string Format(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}

public sealed class AndroidDeviceCardViewModel
{
    public AndroidDeviceCardViewModel(AndroidDeviceSnapshot device)
    {
        Serial = device.Serial;
        State = device.State;
        Model = device.Model;
        Product = device.Product;
        Kind = device.IsQuest ? "Meta Quest" : "Android";
        Transport = string.IsNullOrWhiteSpace(device.TransportId) ? "—" : device.TransportId;
    }

    public string Serial { get; }

    public string State { get; }

    public string Model { get; }

    public string Product { get; }

    public string Kind { get; }

    public string Transport { get; }
}

public sealed class StorageEntryCardViewModel
{
    public StorageEntryCardViewModel(
        StorageEntry entry,
        Func<string, Task> openFolder,
        Action<StorageEntry> prepareCleanup,
        Action<Exception> onError)
    {
        Entry = entry;
        OpenFolderCommand = new AsyncRelayCommand(
            () => openFolder(entry.Path),
            onError: onError);
        PrepareCleanupCommand = new RelayCommand(
            () => prepareCleanup(entry),
            () => entry.IsSafeToClean);
    }

    public StorageEntry Entry { get; }

    public string Name => Entry.DisplayName;

    public string Owner => Entry.OwnerName;

    public string Path => Entry.Path;

    public string Category => Entry.Category switch
    {
        StorageCategory.EngineInstallation => "MOTEUR",
        StorageCategory.Project => "PROJET",
        StorageCategory.EngineDerivedDataCache => "DDC MOTEUR",
        StorageCategory.ProjectDerivedDataCache => "DDC PROJET",
        StorageCategory.GlobalDerivedDataCache => "DDC GLOBAL",
        StorageCategory.SharedDerivedDataCache => "DDC PARTAGÉ",
        StorageCategory.ProjectIntermediate => "INTERMEDIATE",
        StorageCategory.ProjectBinaries => "BINARIES",
        StorageCategory.ProjectLogs => "LOGS",
        StorageCategory.ProjectCooked => "COOKED",
        StorageCategory.ProjectStagedBuilds => "STAGED",
        StorageCategory.VaultCache => "VAULT CACHE",
        StorageCategory.GitLfs => "GIT LFS",
        StorageCategory.MarketplaceLibrary => "PACKS",
        StorageCategory.MarketplaceDownloads => "TÉLÉCHARGEMENTS",
        StorageCategory.MarketplaceCache => "CACHE MARKETPLACE",
        _ => Entry.Category.ToString().ToUpperInvariant(),
    };

    public string Role => Entry.Role.ToString();

    public string Size => FormatBytes(Entry.SizeBytes);

    public string Counts => $"{Entry.FileCount:N0} fichier(s) · {Entry.DirectoryCount:N0} dossier(s)";

    public string LastModified => Entry.LastModified?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "—";

    public string Health => Entry.Health.ToString();

    public string Diagnostic => Entry.Diagnostic;

    public bool CanClean => Entry.IsSafeToClean;

    public ICommand OpenFolderCommand { get; }

    public ICommand PrepareCleanupCommand { get; }

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
}

public sealed class StorageDriveCardViewModel
{
    public StorageDriveCardViewModel(DriveStorageSnapshot drive)
    {
        Drive = drive;
    }

    public DriveStorageSnapshot Drive { get; }

    public string Name => string.IsNullOrWhiteSpace(Drive.VolumeLabel)
        ? Drive.RootPath
        : $"{Drive.VolumeLabel} ({Drive.RootPath})";

    public string Free => FormatBytes(Drive.AvailableFreeBytes);

    public string Total => FormatBytes(Drive.TotalBytes);

    public double UsedPercent => Drive.TotalBytes <= 0
        ? 0
        : Math.Clamp(
            (double)(Drive.TotalBytes - Drive.AvailableFreeBytes) / Drive.TotalBytes * 100,
            0,
            100);

    public string Health => Drive.IsLowSpace ? "ESPACE FAIBLE" : "OK";

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
}

public sealed class EpicArtifactCardViewModel
{
    public EpicArtifactCardViewModel(
        EpicLocalArtifact artifact,
        Func<string, Task> openFolder,
        Action<Exception> onError)
    {
        Artifact = artifact;
        OpenFolderCommand = new AsyncRelayCommand(
            () => openFolder(artifact.InstallLocation),
            () => CanOpenFolder,
            onError);
    }

    public EpicLocalArtifact Artifact { get; }

    public string Name => Artifact.DisplayName;

    public string AppName => Artifact.AppName;

    public string Version => Artifact.Version;

    public string Path => string.IsNullOrWhiteSpace(Artifact.InstallLocation)
        ? "Aucun emplacement local"
        : Artifact.InstallLocation;

    public string Kind => Artifact.Kind switch
    {
        EpicArtifactKind.UnrealEngine => "MOTEUR",
        EpicArtifactKind.FabIntegration => "FAB",
        EpicArtifactKind.UnrealPlugin => "PLUGIN",
        EpicArtifactKind.UnrealContent => "CONTENU",
        EpicArtifactKind.EpicApplication => "APPLICATION",
        _ => "INCONNU",
    };

    public string Installation => Artifact.IsInstalled ? "Installé" : "Référencé";

    public string Source => Artifact.Source;

    public bool CanOpenFolder =>
        !string.IsNullOrWhiteSpace(Artifact.InstallLocation) &&
        Directory.Exists(Artifact.InstallLocation);

    public ICommand OpenFolderCommand { get; }
}

public sealed class EpicCapabilityCardViewModel(EpicCapability capability)
{
    public string Name => capability.Name;

    public string State => capability.State switch
    {
        EpicCapabilityState.Available => "DISPONIBLE",
        EpicCapabilityState.ExternalOnly => "NAVIGATEUR",
        EpicCapabilityState.ResearchRequired => "NON EXPOSÉ",
        _ => "CONFIGURATION REQUISE",
    };

    public string Description => capability.Description;
}

public sealed class FabImportHistoryCardViewModel
{
    public FabImportHistoryCardViewModel(
        FabImportReceipt receipt,
        Func<FabImportReceipt, Task> prepareRollback,
        Action<Exception> onError)
    {
        Receipt = receipt;
        PrepareRollbackCommand = new AsyncRelayCommand(
            () => prepareRollback(receipt),
            () => CanRollback,
            onError);
    }

    public FabImportReceipt Receipt { get; }

    public string SourceName => Receipt.SourceDisplayName;

    public string ProjectName => Path.GetFileNameWithoutExtension(Receipt.TargetProjectPath);

    public string ImportedAt => Receipt.ImportedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Summary =>
        $"{Receipt.Files.Count:N0} fichier(s) · {FormatBytes(Receipt.ImportedBytes)} · " +
        $"{Receipt.ReplacedFileCount:N0} remplacé(s) · {Receipt.CreatedFileCount:N0} créé(s)";

    public string State => Receipt.IsApplied
        ? "APPLIQUÉ"
        : $"ANNULÉ · {Receipt.RolledBackAt!.Value.ToLocalTime():g}";

    public bool CanRollback => Receipt.IsApplied;

    public ICommand PrepareRollbackCommand { get; }

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
}

public sealed class UnityFabImportHistoryCardViewModel
{
    public UnityFabImportHistoryCardViewModel(
        UnityFabAssetImportReceipt receipt,
        Func<UnityFabAssetImportReceipt, Task> prepareRollback,
        Action<Exception> onError)
    {
        Receipt = receipt;
        PrepareRollbackCommand = new AsyncRelayCommand(
            () => prepareRollback(receipt),
            () => CanRollback,
            onError);
    }

    public UnityFabAssetImportReceipt Receipt { get; }

    public string SourceName => Receipt.SourceDisplayName;

    public string ProjectName => Path.GetFileName(Receipt.TargetProjectRoot);

    public string ImportedAt => Receipt.ImportedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Summary =>
        $"{Receipt.Files.Count:N0} fichier(s) · {FormatBytes(Receipt.ImportedBytes)} · " +
        (Receipt.ReplacedExistingImport ? "version précédente sauvegardée" : "nouvel import isolé");

    public string State => Receipt.IsApplied
        ? "APPLIQUÉ"
        : $"ANNULÉ · {Receipt.RolledBackAt!.Value.ToLocalTime():g}";

    public bool CanRollback => Receipt.IsApplied;

    public ICommand PrepareRollbackCommand { get; }

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
}

public sealed class GodotFabImportHistoryCardViewModel
{
    public GodotFabImportHistoryCardViewModel(
        GodotFabAssetImportReceipt receipt,
        Func<GodotFabAssetImportReceipt, Task> prepareRollback,
        Action<Exception> onError)
    {
        Receipt = receipt;
        PrepareRollbackCommand = new AsyncRelayCommand(
            () => prepareRollback(receipt),
            () => CanRollback,
            onError);
    }

    public GodotFabAssetImportReceipt Receipt { get; }

    public string SourceName => Receipt.SourceDisplayName;

    public string ProjectName => Path.GetFileName(Receipt.TargetProjectRoot);

    public string ImportedAt => Receipt.ImportedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Summary =>
        $"{Receipt.Files.Count:N0} fichier(s) · {FormatBytes(Receipt.ImportedBytes)} · " +
        (Receipt.ReplacedExistingImport ? "version précédente sauvegardée" : "nouvel import isolé");

    public string State => Receipt.IsApplied
        ? "APPLIQUÉ"
        : $"ANNULÉ · {Receipt.RolledBackAt!.Value.ToLocalTime():g}";

    public bool CanRollback => Receipt.IsApplied;

    public ICommand PrepareRollbackCommand { get; }

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
}

public sealed class CryEngineFabImportHistoryCardViewModel
{
    public CryEngineFabImportHistoryCardViewModel(
        CryEngineFabAssetImportReceipt receipt,
        Func<CryEngineFabAssetImportReceipt, Task> prepareRollback,
        Action<Exception> onError)
    {
        Receipt = receipt;
        PrepareRollbackCommand = new AsyncRelayCommand(
            () => prepareRollback(receipt),
            () => CanRollback,
            onError);
    }

    public CryEngineFabAssetImportReceipt Receipt { get; }

    public string SourceName => Receipt.SourceDisplayName;

    public string ProjectName => Path.GetFileName(Receipt.TargetProjectRoot);

    public string ImportedAt => Receipt.ImportedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    public string Summary =>
        $"{Receipt.Files.Count:N0} fichier(s) · {FormatBytes(Receipt.ImportedBytes)} · " +
        (Receipt.ReplacedExistingImport ? "version précédente sauvegardée" : "nouvel import isolé");

    public string State => Receipt.IsApplied
        ? "APPLIQUÉ"
        : $"ANNULÉ · {Receipt.RolledBackAt!.Value.ToLocalTime():g}";

    public bool CanRollback => Receipt.IsApplied;

    public ICommand PrepareRollbackCommand { get; }

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
}

public sealed class FabLocalContentPackCardViewModel : ObservableObject
{
    private string[] _personalCollections = [];

    public FabLocalContentPackCardViewModel(
        FabLocalContentPack pack,
        Func<FabLocalContentPack, Task> select,
        Action<FabLocalContentPackCardViewModel> editCollections,
        Func<string, Task> openFolder,
        Action<Exception> onError)
    {
        Pack = pack;
        UseForImportCommand = new AsyncRelayCommand(() => select(pack), onError: onError);
        EditCollectionsCommand = new RelayCommand(() => editCollections(this));
        OpenFolderCommand = new AsyncRelayCommand(
            () => openFolder(pack.PackageRoot),
            () => Directory.Exists(pack.PackageRoot),
            onError);
    }

    public FabLocalContentPack Pack { get; }

    public string Name => Pack.DisplayName;

    public string Path => Pack.PackageRoot;

    public string Engine => Pack.Kind switch
    {
        FabLocalContentKind.EpicVault => "EPIC VAULT NATIF",
        FabLocalContentKind.Unity when string.IsNullOrWhiteSpace(Pack.EngineAssociation) => "UNITY ?",
        FabLocalContentKind.Unity => $"UNITY {Pack.EngineAssociation}",
        FabLocalContentKind.Godot when string.IsNullOrWhiteSpace(Pack.EngineAssociation) => "GODOT ?",
        FabLocalContentKind.Godot => $"GODOT {Pack.EngineAssociation}",
        FabLocalContentKind.CryEngine when string.IsNullOrWhiteSpace(Pack.EngineAssociation) => "CRYENGINE ?",
        FabLocalContentKind.CryEngine => $"CRYENGINE {Pack.EngineAssociation}",
        FabLocalContentKind.Generic => "PORTABLE / MULTI-MOTEUR",
        _ when string.IsNullOrWhiteSpace(Pack.EngineAssociation) => "UE ?",
        _ => $"UE {Pack.EngineAssociation}",
    };

    public string Inventory => Pack.Kind switch
    {
        FabLocalContentKind.EpicVault =>
            $"{Pack.AssetCount:N0} asset(s) · {Pack.MapCount:N0} map(s) · " +
            $"{Pack.SupportedFileCount:N0} fichier(s) à reconstruire · {FormatBytes(Pack.SupportedBytes)} raw",
        FabLocalContentKind.Unity =>
            $"{Pack.AssetCount:N0} asset(s) · {Pack.MapCount:N0} scène(s) · " +
            $"{Pack.SupportedFileCount:N0} fichier(s) · {FormatBytes(Pack.SupportedBytes)}",
        FabLocalContentKind.Godot =>
            $"{Pack.AssetCount:N0} fichier(s) portable(s) · {FormatBytes(Pack.SupportedBytes)}",
        FabLocalContentKind.CryEngine =>
            $"{Pack.AssetCount:N0} fichier(s) portable(s) · {FormatBytes(Pack.SupportedBytes)}",
        FabLocalContentKind.Generic =>
            $"{Pack.AssetCount:N0} asset(s) portable(s) · {FormatBytes(Pack.SupportedBytes)}",
        _ =>
            $"{Pack.AssetCount:N0} asset(s) · {Pack.MapCount:N0} map(s) · " +
            $"{Pack.SupportedFileCount:N0} fichier(s) · {FormatBytes(Pack.SupportedBytes)}",
    };

    public string LastModified => $"Modifié {Pack.LastModifiedAt.ToLocalTime():g}";

    public string Health => Pack.Health switch
    {
        FabLocalContentPackHealth.Ready => "PRÊT",
        FabLocalContentPackHealth.Partial => "PARTIEL",
        _ => "À VÉRIFIER",
    };

    public string IssueSummary => string.Join(" · ", Pack.Issues.Take(3));

    public bool HasIssues => Pack.Issues.Count > 0;

    public IReadOnlyList<string> PersonalCollections => _personalCollections;

    public bool HasCollections => _personalCollections.Length > 0;

    public string CollectionsLabel => HasCollections
        ? string.Join(" · ", _personalCollections)
        : "Aucune collection";

    public void SetCollections(string[] collections)
    {
        _personalCollections = collections;
        OnPropertyChanged(nameof(PersonalCollections));
        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(CollectionsLabel));
    }

    public ICommand UseForImportCommand { get; }

    public ICommand EditCollectionsCommand { get; }

    public ICommand OpenFolderCommand { get; }

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
}

public sealed class RemoteFabAssetRowViewModel : IDisposable
{
    private readonly RemoteFabAsset[] _assets;
    private readonly Func<RemoteFabAsset, RemoteFabAssetCardViewModel> _createCard;
    private bool _isRealized;

    public RemoteFabAssetRowViewModel(
        IEnumerable<RemoteFabAsset> assets,
        Func<RemoteFabAsset, RemoteFabAssetCardViewModel> createCard)
    {
        _assets = assets.ToArray();
        _createCard = createCard;
    }

    public BulkObservableCollection<RemoteFabAssetCardViewModel> Cards { get; } = [];
    public int AssetCount => _assets.Length;

    public void Realize()
    {
        if (_isRealized) return;
        _isRealized = true;
        var cards = _assets.Select(_createCard).ToArray();
        Cards.ReplaceWith(cards);
        foreach (var card in cards) card.EnsureThumbnailLoaded();
    }

    public void Recycle()
    {
        if (!_isRealized) return;
        _isRealized = false;
        var cards = Cards.ToArray();
        Cards.Clear();
        foreach (var card in cards) card.Dispose();
    }

    public void Dispose() => Recycle();
}

public sealed class RemoteFabAssetCardViewModel : ObservableObject, IDisposable
{
    private readonly CancellationTokenSource _thumbnailCancellation = new();
    private readonly Func<Uri?, CancellationToken, Task<Bitmap?>> _loadThumbnail;
    private Bitmap? _thumbnail;
    private bool _isThumbnailLoading;
    private bool _thumbnailLoadStarted;
    private bool _galleryLoadStarted;
    private bool _isGalleryLoading;
    private string _fullDescription;
    private IReadOnlyList<Uri> _galleryUris;
    private Bitmap? _selectedGalleryImage;
    private double _cardWidth = 310;
    private double _cardHeight = 285;
    private double _imageHeight = 150;
    private bool _disposed;
    private bool _isHidden;
    private bool _isStoredLocally;
    private string[] _personalCollections = [];

    public RemoteFabAssetCardViewModel(
        RemoteFabAsset asset,
        LibraryItemOrganizationViewModel organization,
        Action<RemoteFabAssetCardViewModel> openDetails,
        Func<Uri?, CancellationToken, Task<Bitmap?>> loadThumbnail,
        Func<RemoteFabAssetCardViewModel, Task>? openListing = null,
        Func<RemoteFabAssetCardViewModel, Task>? download = null,
        Action<RemoteFabAssetCardViewModel>? openInstall = null,
        Action<RemoteFabAssetCardViewModel>? openCollections = null,
        bool isHidden = false)
    {
        Asset = asset;
        Organization = organization;
        _loadThumbnail = loadThumbnail;
        _fullDescription = asset.Description;
        _galleryUris = asset.GalleryUris;
        _isHidden = isHidden;
        OpenDetailsCommand = new RelayCommand(() => openDetails(this));
        OpenListingCommand = new AsyncRelayCommand(
            () => openListing?.Invoke(this) ?? Task.CompletedTask,
            () => openListing is not null && HasListingUri);
        DownloadCommand = new AsyncRelayCommand(
            () => download?.Invoke(this) ?? Task.CompletedTask,
            () => download is not null && CanDownload);
        OpenInstallCommand = new RelayCommand(
            () => openInstall?.Invoke(this),
            () => openInstall is not null && Asset.PreferredProjectVersion is not null);
        OpenCollectionsCommand = new RelayCommand(
            () => openCollections?.Invoke(this),
            () => openCollections is not null);
    }

    public RemoteFabAsset Asset { get; }

    public LibraryItemOrganizationViewModel Organization { get; }

    public string Name => Organization.DisplayName;

    public string OriginalName => Asset.Title;

    public string Summary => Asset.Description;

    public string Description
    {
        get => _fullDescription;
        private set => SetProperty(ref _fullDescription, value);
    }

    public string Source => string.IsNullOrWhiteSpace(Asset.Source) ? "Fab" : Asset.Source;

    public string Distribution => string.IsNullOrWhiteSpace(Asset.DistributionMethod)
        ? "Distribution non précisée"
        : Asset.DistributionMethod;

    public string CategoryLabel => Asset.Categories.Count == 0
        ? "Sans catégorie"
        : Asset.Categories[0];

    public string Categories => Asset.Categories.Count == 0
        ? "Sans catégorie"
        : string.Join(" · ", Asset.Categories);

    public string Format
    {
        get
        {
            var hasRaw = Asset.ProjectVersions.Any(version =>
                version.ArtifactId.StartsWith("quixel-raw:", StringComparison.OrdinalIgnoreCase));
            var hasUAsset = Asset.ProjectVersions.Any(version =>
                version.ArtifactId.StartsWith("quixel-tier:", StringComparison.OrdinalIgnoreCase));
            if (hasRaw && hasUAsset) return "UASSET + RAW DISPONIBLES";
            if (hasRaw) return "RAW MULTI-MOTEUR DISPONIBLE";
            return Asset.ProjectVersions.Count > 0
                ? "UNREAL · MANIFEST DISPONIBLE"
                : "MÉTADONNÉES UNIQUEMENT";
        }
    }

    public string EngineVersions => string.Join(
        " · ",
        Asset.ProjectVersions
            .SelectMany(version => version.EngineVersions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5));

    public string AllEngineVersions
    {
        get
        {
            var versions = Asset.ProjectVersions
                .SelectMany(version => version.EngineVersions)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(version => version, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return versions.Length == 0 ? "Non précisé" : string.Join(" · ", versions);
        }
    }

    public string Platforms
    {
        get
        {
            var platforms = Asset.ProjectVersions
                .SelectMany(version => version.Platforms)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(platform => platform, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return platforms.Length == 0 ? "Non précisé" : string.Join(" · ", platforms);
        }
    }

    public string ArtifactSummary
    {
        get
        {
            if (Asset.WebLibraryItemId is not null)
                return Asset.WebCanDownload ? "Acquis sur Fab · formats à choisir sur la page officielle" : "Acquis sur Fab · téléchargement indisponible";
            if (Asset.ProjectVersions.Count == 0) return "Aucun téléchargement disponible";
            var rawCount = Asset.ProjectVersions.Count(version =>
                version.ArtifactId.StartsWith("quixel-raw:", StringComparison.OrdinalIgnoreCase));
            var uassetCount = Asset.ProjectVersions.Count - rawCount;
            if (rawCount > 0 && uassetCount > 0)
            {
                return $"{rawCount:N0} variante(s) RAW · {uassetCount:N0} variante(s) UAsset";
            }
            if (rawCount > 0) return $"{rawCount:N0} variante(s) RAW multi-moteur";
            return $"{Asset.ProjectVersions.Count:N0} version(s) de projet · {Asset.ProjectVersions.Sum(version => version.Builds.Count):N0} build(s)";
        }
    }

    public string Namespace => Asset.Namespace;

    public Uri? ListingUri => Asset.ListingUri;

    public bool HasListingUri => ListingUri is not null;

    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set
        {
            if (SetProperty(ref _thumbnail, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(PreviewImage));
                OnPropertyChanged(nameof(HasPreviewImage));
            }
        }
    }

    public bool HasThumbnail => Thumbnail is not null;

    public Bitmap? PreviewImage => SelectedGalleryImage ?? Thumbnail;

    public bool HasPreviewImage => PreviewImage is not null;

    public bool IsThumbnailLoading
    {
        get => _isThumbnailLoading;
        private set => SetProperty(ref _isThumbnailLoading, value);
    }

    public bool CanDownload => Asset.PreferredProjectVersion is not null || Asset.WebCanDownload;
    public bool CanInstall => Asset.PreferredProjectVersion is not null;

    public LibraryPackageContentKind ContentKind =>
        RemoteFabAssetClassifier.Classify(Asset).Kind;

    public string ContentKindLabel => ContentKind switch
    {
        LibraryPackageContentKind.ContentPack => "Pack de contenu",
        LibraryPackageContentKind.CompleteProject => "Projet complet",
        LibraryPackageContentKind.CodePlugin => "Plugin code",
        LibraryPackageContentKind.EnginePackage => "Package moteur",
        LibraryPackageContentKind.RawAsset => "Asset RAW multi-moteur",
        _ => "Type non déterminé",
    };

    public string ContentKindReason => RemoteFabAssetClassifier.Classify(Asset).Reason;

    public string InstallModeGuidance => ContentKind switch
    {
        LibraryPackageContentKind.CodePlugin => "Installation possible dans un projet ou directement dans un moteur Unreal.",
        LibraryPackageContentKind.EnginePackage => "Installation moteur, ou plugin local dans un projet si le pack contient un .uplugin valide.",
        LibraryPackageContentKind.CompleteProject => "Projet complet : téléchargement autonome; ne sera pas injecté dans un projet existant.",
        LibraryPackageContentKind.RawAsset => "Sources RAW : import dans les projets compatibles après téléchargement.",
        LibraryPackageContentKind.ContentPack => "Pack de contenu : installation dans les projets Unreal compatibles.",
        _ => "Type incertain : installation limitée aux projets après validation.",
    };

    public bool IsPluginAsset => ContentKind == LibraryPackageContentKind.CodePlugin;

    public DateTimeOffset? AcquiredAt => Asset.AcquiredAt;

    public string AcquisitionLabel => Asset.AcquiredAt is null
        ? "Date d’acquisition inconnue"
        : $"Acquis le {Asset.AcquiredAt.Value.ToLocalTime():d}";

    public bool IsHidden
    {
        get => _isHidden;
        private set => SetProperty(ref _isHidden, value);
    }

    public bool IsStoredLocally
    {
        get => _isStoredLocally;
        private set
        {
            if (SetProperty(ref _isStoredLocally, value))
            {
                OnPropertyChanged(nameof(LocalStatusLabel));
                OnPropertyChanged(nameof(LocalStatusForeground));
            }
        }
    }

    public string LocalStatusLabel => IsStoredLocally ? "✓ LOCAL" : "○ DISTANT";

    public IBrush LocalStatusForeground => IsStoredLocally ? Brushes.LightGreen : Brushes.Gray;

    public IReadOnlyList<string> PersonalCollections => _personalCollections;

    public int CollectionCount => _personalCollections.Length;

    public bool HasCollections => CollectionCount > 0;

    public string CollectionCountLabel => $"{CollectionCount:N0}";

    public string CollectionSummary => HasCollections
        ? string.Join(" · ", PersonalCollections)
        : Organization.FolderLabel;

    public ObservableCollection<FabEngineVersionBadgeViewModel> EngineVersionBadges { get; } = [];

    public double CardWidth
    {
        get => _cardWidth;
        private set => SetProperty(ref _cardWidth, value);
    }

    public double CardHeight
    {
        get => _cardHeight;
        private set => SetProperty(ref _cardHeight, value);
    }

    public double ImageHeight
    {
        get => _imageHeight;
        private set => SetProperty(ref _imageHeight, value);
    }

    public ObservableCollection<FabGalleryImageViewModel> GalleryImages { get; } = [];

    public bool HasGalleryImages => GalleryImages.Count > 0;

    public Bitmap? SelectedGalleryImage
    {
        get => _selectedGalleryImage;
        private set
        {
            if (SetProperty(ref _selectedGalleryImage, value))
            {
                OnPropertyChanged(nameof(HasSelectedGalleryImage));
                OnPropertyChanged(nameof(PreviewImage));
                OnPropertyChanged(nameof(HasPreviewImage));
            }
        }
    }

    public bool HasSelectedGalleryImage => SelectedGalleryImage is not null;

    public bool IsGalleryLoading
    {
        get => _isGalleryLoading;
        private set => SetProperty(ref _isGalleryLoading, value);
    }

    public ICommand OpenDetailsCommand { get; }

    public ICommand OpenListingCommand { get; }

    public ICommand DownloadCommand { get; }

    public ICommand OpenInstallCommand { get; }

    public ICommand OpenCollectionsCommand { get; }

    public void SetHidden(bool hidden) => IsHidden = hidden;

    public void SetStoredLocally(bool storedLocally) => IsStoredLocally = storedLocally;

    public void SetCollections(IEnumerable<string> collections)
    {
        _personalCollections = collections
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        OnPropertyChanged(nameof(PersonalCollections));
        OnPropertyChanged(nameof(CollectionCount));
        OnPropertyChanged(nameof(HasCollections));
        OnPropertyChanged(nameof(CollectionCountLabel));
        OnPropertyChanged(nameof(CollectionSummary));
    }

    public void UpdateInstalledEngineVersions(IReadOnlySet<string> installedVersions)
    {
        EngineVersionBadges.Clear();
        foreach (var version in Asset.ProjectVersions
                     .SelectMany(item => item.EngineVersions)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(ParseEngineVersion))
        {
            EngineVersionBadges.Add(new FabEngineVersionBadgeViewModel(
                version,
                installedVersions.Contains(NormalizeEngineVersion(version))));
        }
    }

    private static Version ParseEngineVersion(string value)
    {
        var normalized = NormalizeEngineVersion(value);
        return Version.TryParse(normalized, out var parsed) ? parsed : new Version();
    }

    private static string NormalizeEngineVersion(string value)
    {
        var normalized = value.Trim().Replace("UE_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("UE", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimStart('_', '-', ' ');
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : normalized;
    }

    public void UpdatePresentation(bool compact)
    {
        CardWidth = compact ? 230 : 310;
        CardHeight = compact ? 220 : 285;
        ImageHeight = compact ? 105 : 150;
    }

    public void EnsureThumbnailLoaded()
    {
        if (_disposed || _thumbnailLoadStarted || Asset.ThumbnailUri is null)
        {
            return;
        }

        _thumbnailLoadStarted = true;
        _ = LoadThumbnailAsync();
    }

    public void EnsureGalleryLoaded()
    {
        if (_disposed || _galleryLoadStarted)
        {
            return;
        }
        _galleryLoadStarted = true;
        EnsureThumbnailLoaded();
        _ = LoadGalleryAsync();
    }

    public void ApplyProductDetails(RemoteFabProductDetails details)
    {
        Description = string.IsNullOrWhiteSpace(details.Description)
            ? Asset.Description
            : details.Description;
        _galleryUris = details.ImageUris.Count > 0
            ? details.ImageUris
            : Asset.GalleryUris;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _thumbnailCancellation.Cancel();
        _thumbnailCancellation.Dispose();
        Thumbnail?.Dispose();
        Thumbnail = null;
        foreach (var image in GalleryImages)
        {
            image.Image.Dispose();
        }
        GalleryImages.Clear();
        SelectedGalleryImage = null;
    }

    private async Task LoadThumbnailAsync()
    {
        if (Asset.ThumbnailUri is null)
        {
            return;
        }

        IsThumbnailLoading = true;
        try
        {
            var bitmap = await _loadThumbnail(
                Asset.ThumbnailUri,
                _thumbnailCancellation.Token);
            if (_disposed)
            {
                bitmap?.Dispose();
                return;
            }
            Thumbnail = bitmap;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!_disposed)
            {
                IsThumbnailLoading = false;
            }
        }
    }

    private async Task LoadGalleryAsync()
    {
        IsGalleryLoading = true;
        try
        {
            foreach (var uri in _galleryUris.Take(16))
            {
                var bitmap = await _loadThumbnail(uri, _thumbnailCancellation.Token);
                if (_disposed)
                {
                    bitmap?.Dispose();
                    return;
                }
                if (bitmap is not null)
                {
                    var item = new FabGalleryImageViewModel(bitmap, SelectGalleryImage);
                    GalleryImages.Add(item);
                    SelectedGalleryImage ??= bitmap;
                    OnPropertyChanged(nameof(HasGalleryImages));
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (!_disposed)
            {
                IsGalleryLoading = false;
            }
        }
    }

    private void SelectGalleryImage(FabGalleryImageViewModel image) =>
        SelectedGalleryImage = image.Image;
}

public sealed class FabGalleryImageViewModel
{
    public FabGalleryImageViewModel(Bitmap image, Action<FabGalleryImageViewModel> select)
    {
        Image = image;
        SelectCommand = new RelayCommand(() => select(this));
    }

    public Bitmap Image { get; }

    public ICommand SelectCommand { get; }
}

public sealed class BuildPresetCardViewModel : ObservableObject
{
    private bool _isDeleteConfirmationVisible;

    public BuildPresetCardViewModel(
        UnrealBuildPreset preset,
        Func<UnrealBuildPreset, Task> run,
        Action<UnrealBuildPreset> edit,
        Func<UnrealBuildPreset, Task> delete,
        Action<Exception> onError)
    {
        Preset = preset;
        RunCommand = new AsyncRelayCommand(() => run(preset), onError: onError);
        EditCommand = new RelayCommand(() => edit(preset));
        RequestDeleteCommand = new RelayCommand(() => IsDeleteConfirmationVisible = true);
        CancelDeleteCommand = new RelayCommand(() => IsDeleteConfirmationVisible = false);
        ConfirmDeleteCommand = new AsyncRelayCommand(
            () => delete(preset),
            () => IsCustom,
            onError);
    }

    public UnrealBuildPreset Preset { get; }

    public string Name => Preset.Name;

    public string Kind => Preset.Kind == UnrealBuildPresetKind.Target ? "UBT Target" : "UAT Package";

    public string Summary => Preset.Kind == UnrealBuildPresetKind.Target
        ? $"{Preset.Target} · {Preset.Platform} · {Preset.Configuration} · {Preset.Action}"
        : $"{Preset.Platform} · {Preset.Configuration}" +
          (string.IsNullOrWhiteSpace(Preset.CookFlavor) ? string.Empty : $" · {Preset.CookFlavor}");

    public bool IsCustom => !UnrealBuildPresetDefaults.IsBuiltIn(Preset.Id);

    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        private set => SetProperty(ref _isDeleteConfirmationVisible, value);
    }

    public ICommand RunCommand { get; }

    public ICommand EditCommand { get; }

    public ICommand RequestDeleteCommand { get; }

    public ICommand CancelDeleteCommand { get; }

    public ICommand ConfirmDeleteCommand { get; }
}

public sealed class GitRepositoryCardViewModel
{
    public GitRepositoryCardViewModel(
        GitRepositorySnapshot snapshot,
        Func<string, Task> openFolder,
        Func<GitRepositorySnapshot, GitRemote, Task> fetch,
        Action<Exception> onError)
    {
        Snapshot = snapshot;
        OpenFolderCommand = new AsyncRelayCommand(
            () => openFolder(snapshot.RepositoryRoot),
            onError: onError);
        Remotes = snapshot.Remotes
            .Select(remote => new GitRemoteItemViewModel(
                remote,
                () => fetch(snapshot, remote),
                onError))
            .ToArray();
    }

    public GitRepositorySnapshot Snapshot { get; }

    public string DisplayName
    {
        get
        {
            var names = Snapshot.Owners.Select(owner => owner.DisplayName).Take(3).ToArray();
            var suffix = Snapshot.Owners.Count > names.Length
                ? $" +{Snapshot.Owners.Count - names.Length}"
                : string.Empty;
            return string.Join(" · ", names) + suffix;
        }
    }

    public string OwnerKinds => string.Join(
        " · ",
        Snapshot.Owners.Select(owner => owner.Kind).Distinct());

    public string RepositoryRoot => Snapshot.RepositoryRoot;

    public string Branch => Snapshot.Branch;

    public string Commit => Snapshot.HeadCommit.Length > 12
        ? Snapshot.HeadCommit[..12]
        : string.IsNullOrWhiteSpace(Snapshot.HeadCommit) ? "—" : Snapshot.HeadCommit;

    public string Upstream => Snapshot.Upstream ?? "Aucun upstream";

    public string Divergence => $"↑ {Snapshot.Ahead}  ↓ {Snapshot.Behind}";

    public string Changes =>
        $"{Snapshot.WorkingTree.Staged} staged · " +
        $"{Snapshot.WorkingTree.Modified} modifié(s) · " +
        $"{Snapshot.WorkingTree.Untracked} non suivi(s) · " +
        $"{Snapshot.WorkingTree.Conflicted} conflit(s)";

    public string LfsSummary
    {
        get
        {
            if (!Snapshot.Lfs.IsInstalled)
            {
                return "LFS non détecté";
            }

            var prefix = Snapshot.Lfs.IsTruncated ? "≥ " : string.Empty;
            return $"LFS {prefix}{Snapshot.Lfs.ObjectCount:N0} objet(s) · " +
                   $"{prefix}{FormatBytes(Snapshot.Lfs.StorageBytes)} · " +
                   $"{Snapshot.Lfs.TrackedPatterns.Count} pattern(s)";
        }
    }

    public string Error => Snapshot.Error ?? Snapshot.Lfs.Error ?? string.Empty;

    public bool HasError => !string.IsNullOrWhiteSpace(Error);

    public IReadOnlyList<GitRemoteItemViewModel> Remotes { get; }

    public ICommand OpenFolderCommand { get; }

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
}

public sealed class GitRemoteItemViewModel
{
    public GitRemoteItemViewModel(
        GitRemote remote,
        Func<Task> fetch,
        Action<Exception> onError)
    {
        Name = remote.Name;
        Url = remote.Url;
        FetchCommand = new AsyncRelayCommand(fetch, onError: onError);
    }

    public string Name { get; }

    public string Url { get; }

    public ICommand FetchCommand { get; }
}

public sealed class GitLogItemViewModel
{
    public GitLogItemViewModel(ProcessOutputLine line)
    {
        Timestamp = line.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Channel = line.Channel == ProcessOutputChannel.StandardError ? "ERR" : "OUT";
        Text = line.Text;
    }

    public string Timestamp { get; }

    public string Channel { get; }

    public string Text { get; }
}

public sealed class TaskItemViewModel : ObservableObject
{
    private HubTaskSnapshot _snapshot;

    public TaskItemViewModel(HubTaskSnapshot snapshot, Action<Guid> cancel, Action<HubTaskLink> navigate)
    {
        _snapshot = snapshot;
        Id = snapshot.Id;
        Category = snapshot.Category;
        Title = snapshot.Title;
        CancelCommand = new RelayCommand(() => cancel(snapshot.Id), () => CanCancel);
        Links = snapshot.Links.Select(link => new TaskLinkViewModel(link, navigate)).ToArray();
    }

    public IReadOnlyList<TaskLinkViewModel> Links { get; }

    public void ApplySnapshot(HubTaskSnapshot snapshot)
    {
        if (_snapshot == snapshot) return;
        _snapshot = snapshot;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(Duration));
        OnPropertyChanged(nameof(CanCancel));
        ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
    }

    public Guid Id { get; }

    public string Category { get; }

    public string Title { get; }

    public string Status => _snapshot.Status switch
    {
        HubTaskStatus.Queued => "En attente",
        HubTaskStatus.Running => "En cours",
        HubTaskStatus.Completed => "Terminé",
        HubTaskStatus.Failed => "Échec",
        HubTaskStatus.Canceled => "Annulé",
        _ => _snapshot.Status.ToString(),
    };

    public string Message => _snapshot.Error ?? _snapshot.Message ?? string.Empty;

    public double ProgressPercent => (_snapshot.Progress ?? 0) * 100;

    public string Duration => FormatDuration(_snapshot);

    public bool CanCancel => _snapshot.Status is HubTaskStatus.Queued or HubTaskStatus.Running;

    public ICommand CancelCommand { get; }

    private static string FormatDuration(HubTaskSnapshot snapshot)
    {
        if (snapshot.StartedAt is null)
        {
            return "En attente";
        }

        var end = snapshot.FinishedAt ?? DateTimeOffset.UtcNow;
        var duration = end - snapshot.StartedAt.Value;
        return duration.TotalMinutes >= 1
            ? $"{(int)duration.TotalMinutes}m {duration.Seconds:D2}s"
            : $"{duration.TotalSeconds:F1}s";
    }
}

public sealed class BuildLogItemViewModel
{
    public BuildLogItemViewModel(CyEngineHub.Build.Models.BuildLogLine line)
    {
        Timestamp = line.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Severity = line.Severity.ToString();
        Text = line.Text;
    }

    public string Timestamp { get; }

    public string Severity { get; }

    public string Text { get; }
}
