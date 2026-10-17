using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Input;
using CyEngineHub.Fab.Packaging;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Fab.Storage;
using CyEngineHub.Projects.Models;
using CyEngineHub.Unreal.Models;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public const string FabDerivedWorkspacesSetting = "library.fab.derivedWorkspaces.v1";
    private static readonly JsonSerializerOptions DerivedProjectJsonOptions = new()
    {
        WriteIndented = true,
    };
    private readonly List<FabDerivedWorkspaceRecord> _fabDerivedWorkspaces = [];
    private FabStoredDownloadViewModel? _selectedDerivedSource;
    private EngineCardViewModel? _selectedDerivedEngine;
    private DerivedContentKindOptionViewModel? _selectedDerivedContentKind;
    private FabDerivedWorkspaceViewModel? _pendingDerivedWorkspaceDeletion;
    private string _derivedName = string.Empty;
    private string _derivedNotes = string.Empty;
    private bool _isDerivedWorkspaceCreatorVisible;
    private bool _isDerivedWorkspaceDeletionVisible;
    private bool _isDerivedWorkspaceBusy;
    private bool _isDerivedFolderImporterVisible;
    private bool _isPackTestCreatorVisible;
    private string _derivedSourceFolder = string.Empty;
    private FabStoredDownloadViewModel? _selectedTestSource;
    private EngineCardViewModel? _selectedTestEngine;
    private string? _activeDerivedInstallArtifactId;

    public ObservableCollection<FabDerivedWorkspaceViewModel> SelectedFabDerivedWorkspaces { get; } = [];
    public ObservableCollection<DerivedContentKindOptionViewModel> DerivedContentKinds { get; } = [];
    public ICommand OpenDerivedWorkspaceCreatorCommand { get; private set; } = null!;
    public ICommand CloseDerivedWorkspaceCreatorCommand { get; private set; } = null!;
    public ICommand CreateDerivedWorkspaceCommand { get; private set; } = null!;
    public ICommand CancelDerivedWorkspaceDeletionCommand { get; private set; } = null!;
    public ICommand ConfirmDerivedWorkspaceDeletionCommand { get; private set; } = null!;
    public ICommand OpenDerivedFolderImporterCommand { get; private set; } = null!;
    public ICommand CloseDerivedFolderImporterCommand { get; private set; } = null!;
    public ICommand ImportDerivedFolderCommand { get; private set; } = null!;
    public ICommand ClosePackTestCreatorCommand { get; private set; } = null!;
    public ICommand CreatePackTestCommand { get; private set; } = null!;

    public bool IsDerivedFolderImporterVisible
    {
        get => _isDerivedFolderImporterVisible;
        private set { if (SetProperty(ref _isDerivedFolderImporterVisible, value)) NotifyFabOptionsModalState(); }
    }

    public bool IsPackTestCreatorVisible
    {
        get => _isPackTestCreatorVisible;
        private set { if (SetProperty(ref _isPackTestCreatorVisible, value)) NotifyFabOptionsModalState(); }
    }

    public string DerivedSourceFolder
    {
        get => _derivedSourceFolder;
        set => SetProperty(ref _derivedSourceFolder, value);
    }

    public FabStoredDownloadViewModel? SelectedTestSource
    {
        get => _selectedTestSource;
        set => SetProperty(ref _selectedTestSource, value);
    }

    public EngineCardViewModel? SelectedTestEngine
    {
        get => _selectedTestEngine;
        set => SetProperty(ref _selectedTestEngine, value);
    }

    public bool IsDerivedWorkspaceCreatorVisible
    {
        get => _isDerivedWorkspaceCreatorVisible;
        private set
        {
            if (SetProperty(ref _isDerivedWorkspaceCreatorVisible, value)) NotifyFabOptionsModalState();
        }
    }

    public bool IsDerivedWorkspaceDeletionVisible
    {
        get => _isDerivedWorkspaceDeletionVisible;
        private set
        {
            if (SetProperty(ref _isDerivedWorkspaceDeletionVisible, value)) NotifyFabOptionsModalState();
        }
    }

    public bool IsDerivedWorkspaceBusy
    {
        get => _isDerivedWorkspaceBusy;
        private set => SetProperty(ref _isDerivedWorkspaceBusy, value);
    }

    public bool HasSelectedFabDerivedWorkspaces => SelectedFabDerivedWorkspaces.Count > 0;
    public bool HasActiveDerivedInstall => !string.IsNullOrWhiteSpace(_activeDerivedInstallArtifactId);

    public FabStoredDownloadViewModel? SelectedDerivedSource
    {
        get => _selectedDerivedSource;
        set => SetProperty(ref _selectedDerivedSource, value);
    }

    public EngineCardViewModel? SelectedDerivedEngine
    {
        get => _selectedDerivedEngine;
        set => SetProperty(ref _selectedDerivedEngine, value);
    }

    public DerivedContentKindOptionViewModel? SelectedDerivedContentKind
    {
        get => _selectedDerivedContentKind;
        set => SetProperty(ref _selectedDerivedContentKind, value);
    }

    public string DerivedName
    {
        get => _derivedName;
        set => SetProperty(ref _derivedName, value);
    }

    public string DerivedNotes
    {
        get => _derivedNotes;
        set => SetProperty(ref _derivedNotes, value);
    }

    private void InitializeDerivedWorkspaces()
    {
        DerivedContentKinds.Add(new(LibraryPackageContentKind.ContentPack, "AssetPack Unreal"));
        DerivedContentKinds.Add(new(LibraryPackageContentKind.CodePlugin, "Plugin code Unreal"));
        DerivedContentKinds.Add(new(LibraryPackageContentKind.EnginePackage, "Package moteur Unreal"));
        DerivedContentKinds.Add(new(LibraryPackageContentKind.CompleteProject, "Projet complet Unreal"));
        DerivedContentKinds.Add(new(LibraryPackageContentKind.RawAsset, "Fichiers RAW"));
        SelectedDerivedContentKind = DerivedContentKinds[0];
        OpenDerivedWorkspaceCreatorCommand = new RelayCommand(() => OpenDerivedWorkspaceCreator(null));
        CloseDerivedWorkspaceCreatorCommand = new RelayCommand(() => IsDerivedWorkspaceCreatorVisible = false);
        CreateDerivedWorkspaceCommand = new AsyncRelayCommand(CreateDerivedWorkspaceAsync, () => !IsDerivedWorkspaceBusy, ShowError);
        CancelDerivedWorkspaceDeletionCommand = new RelayCommand(() =>
        {
            _pendingDerivedWorkspaceDeletion = null;
            IsDerivedWorkspaceDeletionVisible = false;
        });
        ConfirmDerivedWorkspaceDeletionCommand = new AsyncRelayCommand(DeleteDerivedWorkspaceAsync, onError: ShowError);
        OpenDerivedFolderImporterCommand = new RelayCommand(OpenDerivedFolderImporter);
        CloseDerivedFolderImporterCommand = new RelayCommand(() => IsDerivedFolderImporterVisible = false);
        ImportDerivedFolderCommand = new AsyncRelayCommand(ImportDerivedFolderAsync, () => !IsDerivedWorkspaceBusy, ShowError);
        ClosePackTestCreatorCommand = new RelayCommand(() => IsPackTestCreatorVisible = false);
        CreatePackTestCommand = new AsyncRelayCommand(CreatePackTestAsync, () => !IsDerivedWorkspaceBusy, ShowError);
    }

    private void OpenDerivedFolderImporter()
    {
        if (SelectedRemoteFabAsset is null) return;
        DerivedSourceFolder = string.Empty;
        SelectedDerivedEngine = Engines.FirstOrDefault();
        SelectedDerivedContentKind = DerivedContentKinds[0];
        DerivedName = $"{SelectedRemoteFabAsset.Title} · dérivé importé";
        DerivedNotes = string.Empty;
        IsDerivedFolderImporterVisible = true;
    }

    private void OpenPackTestCreator(FabStoredDownloadViewModel source)
    {
        SelectedTestSource = source;
        SelectedTestEngine = Engines.FirstOrDefault(item => source.EngineVersions.Contains(
            $"{item.Engine.Version.Major}.{item.Engine.Version.Minor}", StringComparer.OrdinalIgnoreCase)) ?? Engines.FirstOrDefault();
        IsPackTestCreatorVisible = true;
    }

    private async Task ImportDerivedFolderAsync()
    {
        var asset = SelectedRemoteFabAsset ?? throw new InvalidOperationException("Aucun produit n’est ouvert.");
        if (!Directory.Exists(DerivedSourceFolder)) throw new DirectoryNotFoundException("Sélectionnez un dossier existant.");
        if (string.IsNullOrWhiteSpace(DerivedName)) throw new InvalidOperationException("Donnez un nom au dérivé.");
        var engineVersion = SelectedDerivedEngine is null
            ? string.Empty
            : $"{SelectedDerivedEngine.Engine.Version.Major}.{SelectedDerivedEngine.Engine.Version.Minor}";
        var kind = SelectedDerivedContentKind?.Value ?? LibraryPackageContentKind.ContentPack;
        var artifactId = $"derived:{Guid.NewGuid():N}";
        IsDerivedWorkspaceBusy = true;
        try
        {
            var sourceVersion = new RemoteFabProjectVersion(
                "external-folder", [], string.IsNullOrEmpty(engineVersion) ? [] : [engineVersion], ["Windows"]);
            var result = await DerivedLibraryPackageService.CreateAsync(new DerivedLibraryPackageRequest(
                asset, sourceVersion, DerivedSourceFolder,
                Path.Combine(ResolveMarketplaceStorageProfile(asset).DownloadDirectory, "Derived"),
                DerivedName.Trim(), DerivedNotes.Trim(), kind, ResolveMarketplaceStorageMode(asset), artifactId)).ConfigureAwait(true);
            result = await MarketplaceStorageRouter.PlaceAsync(result, ResolveMarketplaceStorageProfile(asset)).ConfigureAwait(true);
            _fabStoredDownloads.Add(new FabStoredDownloadRecord(
                asset.AssetId, DerivedName.Trim(), artifactId, result.DestinationDirectory, result.DownloadedBytes,
                result.CompletedAt, NormalizeAssetSource(asset), kind.ToString(), result.StorageMode, result.ManifestPath,
                result.UncompressedBytes, result.StoredBytes, asset.Title, DerivedName.Trim(), DerivedNotes.Trim(),
                "external-folder", string.IsNullOrEmpty(engineVersion) ? [] : [engineVersion]));
            await PersistStoredFabDownloadsAsync().ConfigureAwait(true);
            RebuildSelectedFabStoredDownloads(asset.AssetId);
            IsDerivedFolderImporterVisible = false;
            StatusMessage = $"Dossier importé comme dérivé « {DerivedName.Trim()} » sans modifier la source.";
        }
        finally { IsDerivedWorkspaceBusy = false; }
    }

    private async Task CreatePackTestAsync()
    {
        var source = SelectedTestSource ?? throw new InvalidOperationException("Sélectionnez une version locale à tester.");
        var engine = SelectedTestEngine ?? throw new InvalidOperationException("Sélectionnez une version d’Unreal Engine.");
        SelectedDerivedSource = source;
        SelectedDerivedEngine = engine;
        var sourceKind = ParseContentKind(source.PackageKind);
        SelectedDerivedContentKind = DerivedContentKinds.FirstOrDefault(item => item.Value ==
            (sourceKind == LibraryPackageContentKind.CompleteProject ? LibraryPackageContentKind.ContentPack : sourceKind)) ?? DerivedContentKinds[0];
        DerivedName = $"Test · {source.Version}";
        DerivedNotes = "Projet temporaire de test de cette version locale.";
        await CreateDerivedWorkspaceCoreAsync(isTestOnly: true).ConfigureAwait(true);
        IsPackTestCreatorVisible = false;
        StatusMessage = $"Projet temporaire de test créé et ouvert avec Unreal Engine {engine.Engine.Version.Major}.{engine.Engine.Version.Minor}.";
    }

    private void OpenDerivedWorkspaceCreator(FabStoredDownloadViewModel? source)
    {
        if (SelectedRemoteFabAsset is null) return;
        SelectedDerivedSource = source ?? SelectedFabStoredDownloads.FirstOrDefault();
        SelectedDerivedEngine = Engines.FirstOrDefault();
        var sourceKind = ParseContentKind(SelectedDerivedSource?.PackageKind);
        SelectedDerivedContentKind = DerivedContentKinds.FirstOrDefault(option => option.Value ==
            (sourceKind == LibraryPackageContentKind.CompleteProject
                ? LibraryPackageContentKind.ContentPack
                : sourceKind)) ?? DerivedContentKinds[0];
        DerivedName = $"{SelectedRemoteFabAsset.Title} · dérivé";
        DerivedNotes = string.Empty;
        IsDerivedWorkspaceCreatorVisible = true;
    }

    private Task CreateDerivedWorkspaceAsync() => CreateDerivedWorkspaceCoreAsync(isTestOnly: false);

    private async Task CreateDerivedWorkspaceCoreAsync(bool isTestOnly)
    {
        var asset = SelectedRemoteFabAsset ?? throw new InvalidOperationException("Aucun produit n’est ouvert.");
        var source = SelectedDerivedSource ?? throw new InvalidOperationException("Sélectionnez une version locale source.");
        var engine = SelectedDerivedEngine?.Engine ?? throw new InvalidOperationException("Sélectionnez une version d’Unreal Engine.");
        var kind = SelectedDerivedContentKind?.Value ?? LibraryPackageContentKind.ContentPack;
        if (string.IsNullOrWhiteSpace(DerivedName)) throw new InvalidOperationException("Donnez un nom au dérivé.");
        var workspaceName = DerivedName.Trim();
        var workspaceNotes = DerivedNotes.Trim();
        IsDerivedWorkspaceBusy = true;
        string? newWorkspaceRoot = null;
        try
        {
            var id = Guid.NewGuid();
            var root = Path.Combine(DataDirectory, "DerivedWorkspaces", id.ToString("N"));
            newWorkspaceRoot = root;
            var projectName = "CyHubDerived_" + id.ToString("N")[..8];
            Directory.CreateDirectory(Path.Combine(root, "Content"));
            var projectPath = Path.Combine(root, projectName + ".uproject");
            var association = $"{engine.Version.Major}.{engine.Version.Minor}";
            var descriptor = new
            {
                FileVersion = 3,
                EngineAssociation = association,
                Category = "CyEngineHub Derived Workspace",
                Description = $"Atelier temporaire pour {asset.Title}",
            };
            await File.WriteAllTextAsync(
                projectPath,
                JsonSerializer.Serialize(descriptor, DerivedProjectJsonOptions));

            var materialized = Path.Combine(DataDirectory, "DerivedSources", id.ToString("N"));
            try
            {
                await DerivedLibraryPackageService.MaterializeAsync(source.Path, materialized, ParseContentKind(source.PackageKind));
                if (Directory.EnumerateDirectories(materialized, "Content", SearchOption.AllDirectories).Any() ||
                    Directory.Exists(Path.Combine(materialized, "Content")))
                {
                    var plan = await _fabProjectContentImport.PrepareAsync(materialized, projectPath).ConfigureAwait(true);
                    await _fabProjectContentImport.ExecuteAsync(plan).ConfigureAwait(true);
                }
                else if (Directory.EnumerateFiles(materialized, "*.uplugin", SearchOption.AllDirectories).Any())
                {
                    await DerivedLibraryPackageService.MaterializeAsync(
                        materialized,
                        Path.Combine(root, "Plugins"),
                        LibraryPackageContentKind.CodePlugin).ConfigureAwait(true);
                }
                else
                {
                    await DerivedLibraryPackageService.MaterializeAsync(
                        materialized,
                        Path.Combine(root, "Content", "DerivedRaw"),
                        LibraryPackageContentKind.RawAsset).ConfigureAwait(true);
                }
            }
            finally
            {
                DeleteOwnedDerivedDirectory(materialized, "DerivedSources");
            }

            var record = new FabDerivedWorkspaceRecord(
                id,
                asset.AssetId,
                asset.Title,
                NormalizeAssetSource(asset),
                source.ArtifactId,
                source.Path,
                workspaceName,
                workspaceNotes,
                association,
                engine.Id,
                projectPath,
                kind,
                DateTimeOffset.UtcNow,
                IsTestOnly: isTestOnly);
            _fabDerivedWorkspaces.Add(record);
            await PersistDerivedWorkspacesAsync().ConfigureAwait(true);
            // A launcher failure must never erase a successfully prepared workspace.
            newWorkspaceRoot = null;
            RebuildSelectedDerivedWorkspaces(asset.AssetId);
            IsDerivedWorkspaceCreatorVisible = false;
            await _platformLauncher.StartDetachedAsync(engine.EditorPath, [projectPath], engine.RootPath).ConfigureAwait(true);
            StatusMessage = $"Atelier {record.Name} créé et ouvert avec Unreal Engine {association}.";
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(newWorkspaceRoot))
                DeleteOwnedDerivedDirectory(newWorkspaceRoot, "DerivedWorkspaces");
            throw;
        }
        finally
        {
            IsDerivedWorkspaceBusy = false;
        }
    }

    private async Task OpenDerivedWorkspaceAsync(FabDerivedWorkspaceViewModel workspace)
    {
        var record = FindWorkspace(workspace.Id);
        var engine = ResolveEngine(record.EngineVersion)
            ?? throw new InvalidOperationException($"Unreal Engine {record.EngineVersion} n’est plus disponible.");
        if (!File.Exists(record.ProjectPath)) throw new FileNotFoundException("Le projet temporaire est introuvable.", record.ProjectPath);
        await _platformLauncher.StartDetachedAsync(engine.EditorPath, [record.ProjectPath], engine.RootPath).ConfigureAwait(true);
    }

    private async Task PackageDerivedWorkspaceAsync(FabDerivedWorkspaceViewModel workspace)
    {
        var record = FindWorkspace(workspace.Id);
        if (record.IsTestOnly) throw new InvalidOperationException("Un projet de test ne peut pas être empaqueté comme dérivé.");
        var asset = SelectedRemoteFabAsset ?? throw new InvalidOperationException("Le produit d’origine n’est plus ouvert.");
        if (!File.Exists(record.ProjectPath)) throw new FileNotFoundException("Le projet temporaire est introuvable.", record.ProjectPath);
        var artifactId = $"derived:{record.Id:N}:{DateTime.UtcNow:yyyyMMddHHmmss}";
        var sourceVersion = new RemoteFabProjectVersion(
            record.SourceArtifactId,
            [],
            [record.EngineVersion],
            ["Windows"]);
        IsDerivedWorkspaceBusy = true;
        try
        {
            var result = await DerivedLibraryPackageService.CreateAsync(
                new DerivedLibraryPackageRequest(
                    asset,
                    sourceVersion,
                    Path.GetDirectoryName(record.ProjectPath)!,
                    Path.Combine(ResolveMarketplaceStorageProfile(asset).DownloadDirectory, "Derived"),
                    record.Name,
                    record.Notes,
                    record.ContentKind,
                    ResolveMarketplaceStorageMode(asset),
                    artifactId)).ConfigureAwait(true);
            result = await MarketplaceStorageRouter.PlaceAsync(
                result,
                ResolveMarketplaceStorageProfile(asset)).ConfigureAwait(true);
            _fabStoredDownloads.Add(new FabStoredDownloadRecord(
                asset.AssetId,
                record.Name,
                artifactId,
                result.DestinationDirectory,
                result.DownloadedBytes,
                result.CompletedAt,
                record.Source,
                record.ContentKind.ToString(),
                result.StorageMode,
                result.ManifestPath,
                result.UncompressedBytes,
                result.StoredBytes,
                asset.Title,
                record.Name,
                record.Notes,
                record.SourceArtifactId,
                [record.EngineVersion]));
            await PersistStoredFabDownloadsAsync().ConfigureAwait(true);
            RebuildSelectedFabStoredDownloads(asset.AssetId);
            StatusMessage = $"Dérivé {record.Name} empaqueté et ajouté à la fiche produit.";
        }
        finally
        {
            IsDerivedWorkspaceBusy = false;
        }
    }

    private void RequestDeleteDerivedWorkspace(FabDerivedWorkspaceViewModel workspace)
    {
        _pendingDerivedWorkspaceDeletion = workspace;
        IsDerivedWorkspaceDeletionVisible = true;
    }

    private async Task DeleteDerivedWorkspaceAsync()
    {
        var workspace = _pendingDerivedWorkspaceDeletion
            ?? throw new InvalidOperationException("Aucun atelier n’est sélectionné.");
        var record = FindWorkspace(workspace.Id);
        DeleteOwnedDerivedDirectory(Path.GetDirectoryName(record.ProjectPath)!, "DerivedWorkspaces");
        _fabDerivedWorkspaces.RemoveAll(item => item.Id == record.Id);
        await PersistDerivedWorkspacesAsync().ConfigureAwait(true);
        RebuildSelectedDerivedWorkspaces(record.AssetId);
        _pendingDerivedWorkspaceDeletion = null;
        IsDerivedWorkspaceDeletionVisible = false;
        StatusMessage = $"Projet temporaire {record.Name} supprimé. Les packages dérivés sont conservés.";
    }

    private void DeleteOwnedDerivedDirectory(string path, string group)
    {
        var root = Path.GetFullPath(Path.Combine(DataDirectory, group));
        var target = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(root, target);
        if (relative == "." || Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("Suppression refusée hors du stockage temporaire CyEngineHub.");
        if (!Directory.Exists(target)) return;
        foreach (var directory in Directory.EnumerateDirectories(target, "*", SearchOption.AllDirectories).Prepend(target))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Suppression refusée : le projet temporaire contient une jonction.");
        }
        Directory.Delete(target, recursive: true);
    }

    private async Task OpenDerivedForInstallationAsync(FabStoredDownloadViewModel derived)
    {
        var asset = SelectedRemoteFabAsset ?? throw new InvalidOperationException("Aucun produit n’est ouvert.");
        if (!_fabStoredDownloads.Any(item => item.AssetId == asset.AssetId && item.ArtifactId == derived.ArtifactId && item.Path == derived.Path) || !Directory.Exists(derived.Path))
            throw new DirectoryNotFoundException("La version locale n’est plus disponible. Actualisez la bibliothèque ; aucun téléchargement ne sera lancé.");
        _activeDerivedInstallArtifactId = derived.ArtifactId;
        var version = new RemoteFabProjectVersion(
            derived.ArtifactId,
            [],
            derived.EngineVersions,
            ["Windows"]);
        FabInstallVersions.Clear();
        FabInstallVersions.Add(new FabInstallVersionOptionViewModel(version, true));
        RebuildFabInstallTargets(
            SelectedRemoteFabAssetCard ?? throw new InvalidOperationException("Fiche produit absente."),
            ParseContentKind(derived.PackageKind),
            [version]);
        IsFabDownloadOptionsVisible = false;
        IsFabInstallOptionsVisible = true;
        OnPropertyChanged(nameof(HasActiveDerivedInstall));
        OnPropertyChanged(nameof(FabInstallContentKindLabel));
        OnPropertyChanged(nameof(FabInstallContentKindReason));
        OnPropertyChanged(nameof(FabInstallModeGuidance));
        await Task.CompletedTask;
    }

    public string FabInstallContentKindLabel => HasActiveDerivedInstall
        ? FormatDerivedKind(ParseContentKind(_fabStoredDownloads.LastOrDefault(item =>
            item.ArtifactId.Equals(_activeDerivedInstallArtifactId, StringComparison.OrdinalIgnoreCase) &&
            item.AssetId.Equals(SelectedRemoteFabAsset?.AssetId, StringComparison.OrdinalIgnoreCase))?.PackageKind))
        : SelectedRemoteFabAssetCard?.ContentKindLabel ?? "Type inconnu";

    public string FabInstallContentKindReason => HasActiveDerivedInstall
        ? "Type défini dans le manifeste de la version locale"
        : SelectedRemoteFabAssetCard?.ContentKindReason ?? string.Empty;

    public string FabInstallModeGuidance => HasActiveDerivedInstall
        ? "Cette installation utilise la version locale sélectionnée sans retélécharger ni modifier les fichiers du pack."
        : SelectedRemoteFabAssetCard?.InstallModeGuidance ?? string.Empty;

    private static string FormatDerivedKind(LibraryPackageContentKind kind) => kind switch
    {
        LibraryPackageContentKind.ContentPack => "AssetPack",
        LibraryPackageContentKind.CompleteProject => "Projet complet",
        LibraryPackageContentKind.CodePlugin => "Plugin code",
        LibraryPackageContentKind.EnginePackage => "Package moteur",
        LibraryPackageContentKind.RawAsset => "Asset RAW",
        _ => "Package local",
    };

    private void RebuildSelectedDerivedWorkspaces(string assetId)
    {
        SelectedFabDerivedWorkspaces.Clear();
        foreach (var record in _fabDerivedWorkspaces
                     .Where(item => item.AssetId.Equals(assetId, StringComparison.OrdinalIgnoreCase))
                     .Where(item => File.Exists(item.ProjectPath))
                     .OrderByDescending(item => item.CreatedAt))
        {
            SelectedFabDerivedWorkspaces.Add(new FabDerivedWorkspaceViewModel(
                record.Id, record.Name, record.AssetTitle, record.EngineVersion, record.ProjectPath,
                record.ContentKind,
                record.IsTestOnly ? "Projet temporaire prêt pour le test" : "Projet temporaire prêt à être modifié",
                !record.IsTestOnly,
                record.IsTestOnly ? "TEST DU PACK" : "ATELIER DE DÉRIVÉ",
                OpenDerivedWorkspaceAsync, PackageDerivedWorkspaceAsync,
                RequestDeleteDerivedWorkspace, ShowError));
        }
        OnPropertyChanged(nameof(HasSelectedFabDerivedWorkspaces));
    }

    private async Task LoadDerivedWorkspacesAsync()
    {
        var json = await _settings.GetAsync(FabDerivedWorkspacesSetting).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var records = JsonSerializer.Deserialize<FabDerivedWorkspaceRecord[]>(json) ?? [];
            _fabDerivedWorkspaces.Clear();
            _fabDerivedWorkspaces.AddRange(records.Where(item => File.Exists(item.ProjectPath)).TakeLast(1_000));
        }
        catch (JsonException)
        {
            AppendRawLog("WARN", "Library/Derived", "Ateliers dérivés ignorés : données invalides.");
        }
    }

    private Task PersistDerivedWorkspacesAsync() => _settings.SetAsync(
        FabDerivedWorkspacesSetting,
        JsonSerializer.Serialize(_fabDerivedWorkspaces.TakeLast(1_000)));

    private FabDerivedWorkspaceRecord FindWorkspace(Guid id) =>
        _fabDerivedWorkspaces.SingleOrDefault(item => item.Id == id)
        ?? throw new InvalidOperationException("Cet atelier dérivé n’existe plus.");

    private static LibraryPackageContentKind ParseContentKind(string? value) =>
        Enum.TryParse<LibraryPackageContentKind>(value, true, out var parsed)
            ? parsed
            : LibraryPackageContentKind.Unknown;

    private sealed record FabDerivedWorkspaceRecord(
        Guid Id,
        string AssetId,
        string AssetTitle,
        string Source,
        string SourceArtifactId,
        string SourcePath,
        string Name,
        string Notes,
        string EngineVersion,
        string EngineId,
        string ProjectPath,
        LibraryPackageContentKind ContentKind,
        DateTimeOffset CreatedAt,
        bool IsTestOnly = false);
}
