using System.Collections.ObjectModel;
using System.Windows.Input;
using CyEngineHub.Epic.Local;
using CyEngineHub.Toolchains.Detection;

namespace CyEngineHub.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private EngineCardViewModel? _selectedToolsEngine;
    private UnrealUtility? _selectedUnrealUtility;
    private UnrealUtility? _pendingUtilityLaunch;
    private string _unrealToolsStatus = "Sélectionnez un moteur pour retrouver ses utilitaires.";
    private int _unrealToolsGeneration;
    private int _toolsTabIndex;
    public int ToolsTabIndex { get => _toolsTabIndex; set => SetProperty(ref _toolsTabIndex, Math.Clamp(value, 0, 2)); }
    public ObservableCollection<UnrealUtility> UnrealUtilities { get; } = [];
    public bool IsToolsNavigationActive => IsToolchainsVisible;
    public EngineCardViewModel? SelectedToolsEngine
    {
        get => _selectedToolsEngine;
        set { if (SetProperty(ref _selectedToolsEngine, value)) _ = RefreshUnrealToolsAsync(); }
    }
    public UnrealUtility? SelectedUnrealUtility
    {
        get => _selectedUnrealUtility;
        set => SetProperty(ref _selectedUnrealUtility, value);
    }
    public string UnrealToolsStatus { get => _unrealToolsStatus; private set => SetProperty(ref _unrealToolsStatus, value); }
    public bool IsUtilityLaunchConfirmationVisible => _pendingUtilityLaunch is not null;
    public string UtilityLaunchConfirmationText => _pendingUtilityLaunch is { } tool
        ? $"{tool.Name}\n\n{tool.Description}\n\nExécutable : {tool.ExecutablePath}\n\nLancer cet outil local ? Aucun argument automatique ne sera ajouté." : "";
    private ICommand? _refreshUnrealToolsCommand, _prepareUnrealUtilityCommand, _confirmUtilityLaunchCommand,
        _cancelUtilityLaunchCommand, _openUnrealUtilityFolderCommand, _openDotNetDownloadsCommand,
        _openCppToolsDownloadsCommand, _openWindowsSdkDownloadsCommand, _openUnrealPrerequisitesGuideCommand;
    public ICommand RefreshUnrealToolsCommand => _refreshUnrealToolsCommand ??= new AsyncRelayCommand(RefreshUnrealToolsAsync, onError: ShowError);
    public ICommand PrepareUnrealUtilityCommand => _prepareUnrealUtilityCommand ??= new RelayCommand(() =>
    {
        if (SelectedUnrealUtility is not { IsAvailable: true } tool) return;
        _pendingUtilityLaunch = tool;
        OnPropertyChanged(nameof(IsUtilityLaunchConfirmationVisible));
        OnPropertyChanged(nameof(UtilityLaunchConfirmationText));
        OnPropertyChanged(nameof(IsAnyFabModalVisible));
    }, () => SelectedUnrealUtility?.IsAvailable == true);
    public ICommand CancelUtilityLaunchCommand => _cancelUtilityLaunchCommand ??= new RelayCommand(CancelUtilityLaunch);
    public ICommand ConfirmUtilityLaunchCommand => _confirmUtilityLaunchCommand ??= new AsyncRelayCommand(async () =>
    {
        var tool = _pendingUtilityLaunch;
        CancelUtilityLaunch();
        if (tool?.ExecutablePath is not { } path || !File.Exists(path))
            throw new FileNotFoundException("L’outil n’existe plus. Actualisez la liste avant de réessayer.");
        await _platformLauncher.StartDetachedAsync(path, [], Path.GetDirectoryName(path)).ConfigureAwait(true);
        StatusMessage = $"{tool.Name} lancé. Les éventuelles demandes de l’assistant restent à confirmer.";
        AppendRawLog("INFO", "Tools/Unreal", $"Lancement confirmé : {tool.Name}");
    }, () => _pendingUtilityLaunch is not null, onError: ShowError);
    public ICommand OpenUnrealUtilityFolderCommand => _openUnrealUtilityFolderCommand ??= new AsyncRelayCommand(async () =>
    {
        if (SelectedUnrealUtility?.ExecutablePath is not { } path || !File.Exists(path))
            throw new FileNotFoundException("Actualisez les utilitaires : l’exécutable n’est plus disponible.");
        await OpenFolderAsync(Path.GetDirectoryName(path)!).ConfigureAwait(true);
    }, () => SelectedUnrealUtility?.IsAvailable == true, onError: ShowError);
    public ICommand OpenDotNetDownloadsCommand => _openDotNetDownloadsCommand ??= OfficialToolLink("https://dotnet.microsoft.com/en-us/download", ".NET");
    public ICommand OpenCppToolsDownloadsCommand => _openCppToolsDownloadsCommand ??= OfficialToolLink("https://visualstudio.microsoft.com/downloads/", "Visual Studio / outils C++");
    public ICommand OpenWindowsSdkDownloadsCommand => _openWindowsSdkDownloadsCommand ??= OfficialToolLink("https://learn.microsoft.com/en-us/windows/apps/windows-sdk/", "Windows SDK");
    public ICommand OpenUnrealPrerequisitesGuideCommand => _openUnrealPrerequisitesGuideCommand ??= OfficialToolLink(
        "https://dev.epicgames.com/documentation/en-us/unreal-engine/hardware-and-software-specifications-for-unreal-engine", "Prérequis Unreal");
    private AsyncRelayCommand OfficialToolLink(string url, string name) => new(() => OpenOfficialUriAsync(new Uri(url), name), onError: ShowError);
    private void CancelUtilityLaunch()
    {
        _pendingUtilityLaunch = null;
        OnPropertyChanged(nameof(IsUtilityLaunchConfirmationVisible));
        OnPropertyChanged(nameof(UtilityLaunchConfirmationText));
        OnPropertyChanged(nameof(IsAnyFabModalVisible));
    }
    private void OpenTools()
    {
        ShowSection(PageSection.Toolchains);
        var engine = SelectedToolsEngine is { } previous ? Engines.FirstOrDefault(item => item.Engine.Id == previous.Engine.Id) : null;
        engine ??= Engines.FirstOrDefault(item => item.Engine.Id == SelectedEngineDetails?.Engine.Id) ?? Engines.FirstOrDefault();
        if (ReferenceEquals(engine, SelectedToolsEngine)) _ = RefreshUnrealToolsAsync();
        else SelectedToolsEngine = engine;
    }
    private async Task RefreshUnrealToolsAsync()
    {
        var generation = ++_unrealToolsGeneration;
        var root = SelectedToolsEngine?.Engine.RootPath;
        var selectedId = SelectedUnrealUtility?.Id;
        SelectedUnrealUtility = null;
        UnrealUtilities.Clear();
        UnrealToolsStatus = "Recherche des utilitaires dans les emplacements connus…";
        try
        {
            var utilities = await Task.Run(() =>
            {
                var launcherRoots = new List<string>();
                // The existing locator also handles custom Launcher installation locations.
                string? executable = null;
                try { executable = new EpicLauncherLocator().FindLauncherExecutable(); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    // A restricted registry entry must not hide the selected engine's own utilities.
                }
                if (executable is not null)
                {
                    var launcherRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "../../.."));
                    launcherRoots.Add(launcherRoot);
                }
                foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
                {
                    var programFiles = Environment.GetFolderPath(folder);
                    if (!string.IsNullOrEmpty(programFiles)) launcherRoots.Add(Path.Combine(programFiles, "Epic Games", "Launcher"));
                }
                return UnrealUtilityCatalog.Discover(root, launcherRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            }).ConfigureAwait(true);
            if (generation != _unrealToolsGeneration) return;
            foreach (var utility in utilities) UnrealUtilities.Add(utility);
            SelectedUnrealUtility = utilities.FirstOrDefault(item => item.Id == selectedId) ?? (utilities.Count > 0 ? utilities[0] : null);
            UnrealToolsStatus = $"{utilities.Count(item => item.IsAvailable)} utilitaire(s) trouvé(s) · aucun outil lancé par la détection.";
        }
        catch (Exception error)
        {
            if (generation == _unrealToolsGeneration) UnrealToolsStatus = "Recherche interrompue : " + error.Message;
        }
    }
}
