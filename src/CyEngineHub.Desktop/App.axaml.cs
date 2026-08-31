using System.Text.Json;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CyEngineHub.Core.Settings;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Build.Execution;
using CyEngineHub.Build.Presets;
using CyEngineHub.CryEngine.Integration;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Desktop.Views;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Epic.Authentication;
using CyEngineHub.Epic.Local;
using CyEngineHub.Epic.Persistence;
using CyEngineHub.Engines;
using CyEngineHub.Extensions;
using CyEngineHub.Fab;
using CyEngineHub.Fab.Import;
using CyEngineHub.Fab.Library;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Infrastructure.AppData;
using CyEngineHub.Infrastructure.Diagnostics;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Infrastructure.Processes;
using CyEngineHub.Git.Analysis;
using CyEngineHub.Git.Persistence;
using CyEngineHub.Godot.Integration;
using CyEngineHub.Plugins.Compatibility;
using CyEngineHub.Plugins.Discovery;
using CyEngineHub.Plugins.Installation;
using CyEngineHub.Plugins.Parsing;
using CyEngineHub.Plugins.Persistence;
using CyEngineHub.Projects.Detection;
using CyEngineHub.Projects.Parsing;
using CyEngineHub.Storage.Analysis;
using CyEngineHub.Storage.Cleanup;
using CyEngineHub.Storage.Persistence;
using CyEngineHub.Toolchains.Compatibility;
using CyEngineHub.Toolchains.Detection;
using CyEngineHub.Toolchains.Persistence;
using CyEngineHub.Unreal.Detection;
using CyEngineHub.Unreal.Acquisition;
using CyEngineHub.Unreal.Registration;
using CyEngineHub.Unreal.Integration;
using CyEngineHub.Unity.Integration;
using CyEngineHub.Updates;

namespace CyEngineHub.Desktop;

public sealed partial class App : Application, IDisposable
{
    private HubTaskScheduler? _taskScheduler;
    private JsonLineAppLogger? _logger;
    private HttpClient? _fabHttpClient;
    private HttpClient? _updateHttpClient;
    private DirectFabApiClient? _directFabClient;
    private QuixelMegascansApiClient? _quixelClient;
    private MainWindow? _mainWindow;
    private bool _isExplicitShutdown;
    private bool _disposed;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (OperatingSystem.IsWindows())
            {
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
            var appData = AppDataPaths.Resolve();
            _logger = new JsonLineAppLogger(appData.LogsDirectory);
            var store = new SqliteCatalogStore(appData.DatabasePath);
            _taskScheduler = new HubTaskScheduler(maximumConcurrency: 1);
            _fabHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30),
            };
            _updateHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30),
            };
            var directEpicSessionStore = new WindowsCredentialFabSessionStore();
            _directFabClient = new DirectFabApiClient(
                _fabHttpClient,
                directEpicSessionStore);
            _quixelClient = new QuixelMegascansApiClient(
                _fabHttpClient,
                new WindowsCredentialQuixelSessionStore(),
                directEpicSessionStore);
            var processRunner = new SystemProcessRunner();
            var hubExtensionManifestParser = new HubExtensionManifestParser();

            async Task<IReadOnlyCollection<string>> ReadPathsAsync(
                string key,
                CancellationToken cancellationToken)
            {
                var json = await ((ISettingsStore)store).GetAsync(key, cancellationToken)
                    .ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(json)
                    ? []
                    : JsonSerializer.Deserialize<string[]>(json) ?? [];
            }

            var engineDiscovery = new EngineDiscoveryService(
                [
                    new WindowsRegistryEngineDiscoverySource(),
                    new EpicManifestEngineDiscoverySource(),
                    new ConfiguredEngineDiscoverySource(
                        token => ReadPathsAsync(MainWindowViewModel.EngineRootsSetting, token)),
                ],
                new EngineInspector(),
                store,
                _logger);
            var projectDiscovery = new ProjectDiscoveryService(
                [
                    new RecentProjectDiscoverySource(),
                    new ConfiguredProjectRootDiscoverySource(
                        token => ReadPathsAsync(MainWindowViewModel.ProjectRootsSetting, token)),
                ],
                new UprojectDescriptorParser(),
                store,
                _logger);
            var pluginParser = new UpluginDescriptorParser();
            var pluginCompatibility = new PluginCompatibilityService();
            var toolchainRequirements = new UnrealToolchainRequirementParser();
            var toolchainCompatibility = new ToolchainCompatibilityService();
            var engineModules = new EngineModuleRegistry(
            [
                new UnrealEngineModule(),
                new UnityEngineModule(),
                new GodotEngineModule(),
                new CryEngineModule(),
            ]);
            var viewModel = new MainWindowViewModel(
                store,
                store,
                store,
                engineModules,
                new HubExtensionCatalogService(hubExtensionManifestParser),
                new HubExtensionInstallationService(hubExtensionManifestParser),
                new HubExtensionExecutionService(processRunner),
                engineDiscovery,
                projectDiscovery,
                _taskScheduler,
                new UnrealBuildExecutor(processRunner),
                new UnrealBuildPresetCatalog(store),
                new UnrealEngineRegistrationService(
                    new WindowsRegistryEngineRegistrationStore()),
                new GitRepositoryDiscoveryService(
                    new GitRepositoryInspector(processRunner)),
                new GitSnapshotCache(store),
                new PluginDiscoveryService(pluginParser),
                new PluginSnapshotCache(store),
                new PluginInstallationService(pluginCompatibility, pluginParser),
                new ToolchainInventoryService(
                    processRunner,
                    toolchainRequirements,
                    toolchainCompatibility),
                new ToolchainSnapshotCache(store),
                new StorageAnalysisService(),
                new StorageSnapshotCache(store),
                new StorageCleanupService(),
                new EpicLocalCatalogService(),
                new EpicSnapshotCache(store),
                new OfficialEpicWebAuthenticationService(),
                new OfficialFabWebBackend(),
                _directFabClient,
                _quixelClient,
                new OfficialEpicGamesLauncherEngineBackend(
                    _fabHttpClient,
                    _directFabClient,
                    directEpicSessionStore),
                new FabProjectContentImportService(),
                new UnityFabAssetImportService(),
                new GodotFabAssetImportService(),
                new CryEngineFabAssetImportService(),
                new FabLocalContentLibraryService(),
                new UnrealSourceDownloadService(processRunner),
                processRunner,
                new PlatformLauncher(),
                new GitHubReleaseUpdateService(_updateHttpClient),
                appData);
            viewModel.UpdateInstallerReady += OnUpdateInstallerReady;
            _mainWindow = new MainWindow
            {
                DataContext = viewModel,
            };
            _mainWindow.Closing += OnMainWindowClosing;
            desktop.MainWindow = _mainWindow;
            ConfigureTrayIcon(viewModel);
            if (OperatingSystem.IsWindows() && Environment.GetCommandLineArgs().Any(argument =>
                    string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase)))
            {
                _mainWindow.ShowInTaskbar = false;
                _mainWindow.Opened += HideMainWindowAfterStartup;
            }
            desktop.Exit += OnDesktopExit;
            _ = viewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs eventArgs)
    {
        Dispose();
    }

    private void ConfigureTrayIcon(MainWindowViewModel viewModel)
    {
        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons is null) return;
        foreach (var trayIcon in trayIcons)
        {
            trayIcon.IsVisible = OperatingSystem.IsWindows();
            trayIcon.ToolTipText = $"CyEngineHub Alpha · {viewModel.ApplicationVersion}";
        }
    }

    private void OnMainWindowClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (!OperatingSystem.IsWindows() ||
            _isExplicitShutdown ||
            eventArgs.CloseReason is WindowCloseReason.ApplicationShutdown or
                WindowCloseReason.OSShutdown)
        {
            return;
        }

        eventArgs.Cancel = true;
        _mainWindow?.Hide();
    }

    private void HideMainWindowAfterStartup(object? sender, EventArgs eventArgs)
    {
        if (_mainWindow is null) return;
        _mainWindow.Opened -= HideMainWindowAfterStartup;
        _mainWindow.Hide();
        _mainWindow.ShowInTaskbar = true;
    }

    private void OnTrayIconClicked(object? sender, EventArgs eventArgs) => ShowMainWindow();

    private void OnTrayOpenClicked(object? sender, EventArgs eventArgs) => ShowMainWindow();

    private void ShowMainWindow()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_mainWindow is null) return;
            _mainWindow.ShowInTaskbar = true;
            if (!_mainWindow.IsVisible) _mainWindow.Show();
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Activate();
        });
    }

    private void OnTrayExitClicked(object? sender, EventArgs eventArgs)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isExplicitShutdown = true;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        });
    }

    private void OnUpdateInstallerReady(object? sender, UpdateInstallerReadyEventArgs eventArgs)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                "L’installation automatique est actuellement disponible uniquement sous Windows.");
        }

        var sourceDirectory = Path.Combine(AppContext.BaseDirectory, "Updater");
        var sourceExecutable = Path.Combine(sourceDirectory, "CyEngineHub.Updater.exe");
        if (!File.Exists(sourceExecutable))
        {
            throw new FileNotFoundException(
                "Le lanceur externe de mise à jour est absent de cette installation.",
                sourceExecutable);
        }

        var stagingDirectory = Path.Combine(
            Path.GetTempPath(),
            "CyEngineHub",
            "Updater",
            Guid.NewGuid().ToString("N"));
        CopyDirectory(sourceDirectory, stagingDirectory);
        var stagedExecutable = Path.Combine(stagingDirectory, "CyEngineHub.Updater.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = stagedExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = stagingDirectory,
        };
        startInfo.ArgumentList.Add("--wait-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--installer");
        startInfo.ArgumentList.Add(Path.GetFullPath(eventArgs.InstallerPath));
        using var updaterProcess = Process.Start(startInfo) ??
            throw new InvalidOperationException("Impossible de démarrer le lanceur externe de mise à jour.");

        _isExplicitShutdown = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(
                sourceFile,
                Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)),
                overwrite: true);
        }

        foreach (var childDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(
                childDirectory,
                Path.Combine(destinationDirectory, Path.GetFileName(childDirectory)));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _isExplicitShutdown = true;
        var trayIcons = TrayIcon.GetIcons(this);
        if (trayIcons is not null)
        {
            foreach (var trayIcon in trayIcons)
            {
                trayIcon.IsVisible = false;
                trayIcon.Dispose();
            }
        }
        _taskScheduler?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _directFabClient?.Dispose();
        _fabHttpClient?.Dispose();
        _updateHttpClient?.Dispose();
        _logger?.Dispose();
    }
}
