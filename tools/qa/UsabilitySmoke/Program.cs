using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyEngineHub.Core.Processes;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Desktop.Views;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Engines;
using CyEngineHub.Extensions;
using CyEngineHub.Epic.Acquisition;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Plugins.Models;
using CyEngineHub.Infrastructure.AppData;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Projects.Models;
using CyEngineHub.Storage.Models;
using CyEngineHub.Storage.Persistence;
using CyEngineHub.Toolchains.Models;
using CyEngineHub.Unreal.Integration;
using CyEngineHub.Unreal.Models;
using CyEngineHub.Unreal.Registration;

sealed class FixtureApp : Application
{
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
}
sealed class FixtureRegistration : IUnrealEngineRegistrationService
{
    private readonly UnrealEngineRegistrationService _service = new(new FixtureRegistrationStore());
    public UnrealEngineRegistration? FindByRoot(string path) => _service.FindByRoot(path);
    public UnrealEngineRegistration Register(string path) => _service.Register(path);
    public UnrealEngineRegistration SetAssociation(string path, string identifier) => _service.SetAssociation(path, identifier);
    public bool Unregister(string path) => _service.Unregister(path);
    private sealed class FixtureRegistrationStore : IUnrealEngineRegistrationStore
    {
        private readonly List<UnrealEngineRegistration> _values = [];
        public IReadOnlyList<UnrealEngineRegistration> ReadAll() => _values.ToArray();
        public void Write(UnrealEngineRegistration value) { _values.RemoveAll(item => item.Identifier.Equals(value.Identifier, StringComparison.OrdinalIgnoreCase)); _values.Add(value); }
        public bool Delete(string identifier) => _values.RemoveAll(item => item.Identifier == identifier) > 0;
    }
}
sealed class RecordingLauncher : IPlatformLauncher
{
    public List<(string Executable, IReadOnlyList<string> Arguments)> Launches { get; } = [];
    public List<Uri> OpenedUris { get; } = [];
    public Task<int?> StartDetachedAsync(string fileName, IReadOnlyList<string>? arguments = null, string? workingDirectory = null, CancellationToken cancellationToken = default)
    { Launches.Add((fileName, arguments ?? [])); return Task.FromResult<int?>(123); }
    public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default) { OpenedUris.Add(uri); return Task.CompletedTask; }
}
sealed class FixtureFab : IDirectFabClient
{
    public int DownloadConnections { get; private set; } = 12;
    public void SetDownloadConnections(int connections) => DownloadConnections = connections;
    public Uri AuthorizationUri => new("https://example.invalid/fixture");
    public Task<DirectFabStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DirectFabStatus(DirectFabConnectionState.Connected, "Fixture"));
    public Task<DirectFabStatus> ConnectAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<RemoteFabAsset>> GetLibraryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteFabAsset>>([]);
    public Task<RemoteFabProductDetails> GetProductDetailsAsync(RemoteFabAsset asset, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteFabProductDetails("Fixture", []));
    public Task<DirectFabDownloadResult> DownloadAsync(RemoteFabAsset asset, string root, IProgress<DirectFabDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DirectFabDownloadResult> DownloadAsync(RemoteFabAsset asset, RemoteFabProjectVersion version, string root, IProgress<DirectFabDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
sealed class FixtureQuixel : IQuixelMegascansClient
{
    public int DownloadConnections { get; private set; } = 12;
    public void SetDownloadConnections(int connections) => DownloadConnections = connections;
    public Uri AuthorizationUri => new("https://example.invalid/fixture");
    public Task<QuixelStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new QuixelStatus(QuixelConnectionState.Connected, "Fixture"));
    public Task<QuixelStatus> ConnectAsync(string code, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<QuixelStatus> ConnectFromEpicSessionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<QuixelLibraryResult> GetLibraryAsync(CancellationToken cancellationToken = default) => Task.FromResult(new QuixelLibraryResult(0, [], 0));
    public Task<RemoteFabProductDetails> GetProductDetailsAsync(RemoteFabAsset asset, CancellationToken cancellationToken = default) => Task.FromResult(new RemoteFabProductDetails("Fixture", []));
    public Task<DirectFabDownloadResult> DownloadAsync(RemoteFabAsset asset, RemoteFabProjectVersion version, string root, IProgress<DirectFabDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException("No real asset downloads permitted in UI fixtures.");
}
sealed class FixtureEngineBackend : IEpicBinaryEngineBackend
{
    public int ChunkConcurrency { get; private set; } = 12;
    public void SetChunkConcurrency(int concurrency) => ChunkConcurrency = concurrency;
    public Func<EpicBinaryEngineRelease, string, IProgress<EpicBinaryEngineDownloadProgress>?, CancellationToken, Task<EpicBinaryEngineDownloadResult>>? DownloadHandler { get; set; }
    public Uri InstallationGuideUri => new("https://example.invalid/engine");
    public TaskCompletionSource<EpicEngineBuild?>? Gate { get; set; }
    public int Checks { get; private set; }
    public Task<EpicBinaryEngineBackendStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new EpicBinaryEngineBackendStatus(EpicBinaryEngineBackendState.DirectDownloadReady, "Fixture", true));
    public Task<IReadOnlyList<EpicBinaryEngineRelease>> GetAvailableReleasesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<EpicBinaryEngineRelease>>([new("fixture", "UE_5.5", "5.5", "Fixture")]);
    public Task<EpicEngineBuild?> GetLatestBuildAsync(EpicBinaryEngineRelease release, CancellationToken cancellationToken = default)
    { Checks++; return Gate?.Task.WaitAsync(cancellationToken) ?? Task.FromResult<EpicEngineBuild?>(EpicEngineBuild.Parse("5.5.4-200+++UE5+Release-5.5")); }
    public Task<EpicBinaryEngineDownloadResult> DownloadAsync(EpicBinaryEngineRelease release, string destinationDirectory, IProgress<EpicBinaryEngineDownloadProgress>? progress = null, CancellationToken cancellationToken = default) =>
        DownloadHandler?.Invoke(release, destinationDirectory, progress, cancellationToken) ?? throw new NotSupportedException("No real engine download permitted in UI fixtures.");
    public Task<EpicBinaryEngineComponentUpdateResult> UpdateComponentsAsync(EpicBinaryEngineRelease release, string existingDirectory, IReadOnlyCollection<string> components, IProgress<EpicBinaryEngineDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException("No engine modification permitted in UI fixtures.");
}
static partial class Program
{
    static bool DownloadsOnly;
    static bool VariantsOnly;
    static bool ToolsOnly;
    static bool ActivityOnly;
    static bool AnnotationsOnly;
    static bool ResourcesOnly;
    static bool ResourceLayoutOnly;
    static bool GroupsOnly;
    static bool SettingsOnly;
    static bool EngineIdentityOnly;
    static readonly FixtureEngineBackend EngineBackend = new();
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string Output = Path.GetFullPath("artifacts/usability-qa-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
    static void Call(object vm, string method, params object[] args) => vm.GetType().GetMethod(method, Private)!.Invoke(vm, args);
    static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
    [STAThread] static void Main(string[] args)
    {
        DownloadsOnly = args.Contains("--engine-downloads-only");
        VariantsOnly = args.Contains("--variants-only");
        ToolsOnly = args.Contains("--tools-only");
        ActivityOnly = args.Contains("--activity-only");
        AnnotationsOnly = args.Contains("--annotations-only");
        ResourcesOnly = args.Contains("--resources-only");
        ResourceLayoutOnly = args.Contains("--resource-layout-only");
        GroupsOnly = args.Contains("--groups-only");
        SettingsOnly = args.Contains("--settings-only");
        EngineIdentityOnly = args.Contains("--engine-identity-only");
        Directory.CreateDirectory(Output);
        AppBuilder.Configure<FixtureApp>().UsePlatformDetect().SetupWithoutStarting();
        using var stop = new CancellationTokenSource(args.Contains("--interactive") ? TimeSpan.FromMinutes(20) : TimeSpan.FromSeconds(240));
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Run(args.Contains("--interactive"), stop); }
            catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
        Environment.Exit(Environment.ExitCode);
    }
    static async Task Run(bool interactive, CancellationTokenSource stop)
    {
        var paths = new AppDataPaths(Output, Path.Combine(Output, "qa.db"), Path.Combine(Output, "Logs"), true);
        var store = new SqliteCatalogStore(paths.DatabasePath);
        await store.InitializeAsync();
        await using var scheduler = new HubTaskScheduler(1);
        var launcher = new RecordingLauncher();
        var ctor = typeof(MainWindowViewModel).GetConstructors().Single();
        var vm = (MainWindowViewModel)ctor.Invoke(ctor.GetParameters().Select(p => p.Name switch
        {
            "database" or "settings" or "engineCatalog" => (object)store,
            "engineModules" => new EngineModuleRegistry([new UnrealEngineModule()]),
            "engineRegistration" => new FixtureRegistration(), "taskScheduler" => scheduler,
            "processRunner" when EngineIdentityOnly => new CyEngineHub.Infrastructure.Processes.SystemProcessRunner(),
            "appData" => paths, "platformLauncher" => launcher,
            "fabProjectContentImport" => new CyEngineHub.Fab.Import.FabProjectContentImportService(),
            "storageSnapshotCache" => new StorageSnapshotCache(store),
            "directFabClient" => new FixtureFab(), "quixelClient" => new FixtureQuixel(), "epicBinaryEngineBackend" => EngineBackend, _ => null,
        }).ToArray());
        var engineRoot = Path.Combine(Output, "UE_5.5");
        var editor = Path.Combine(engineRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(editor)!);
        await File.WriteAllTextAsync(editor, "fixture - not executable");
        Directory.CreateDirectory(Path.Combine(engineRoot, "Engine", "Build"));
        await File.WriteAllTextAsync(Path.Combine(engineRoot, "Engine", "Build", "Build.version"), "{\"MajorVersion\":5,\"MinorVersion\":5,\"PatchVersion\":0}");
        var engine = new UnrealEngineInstallation("fixture:engine", "fixture-association", "Unreal 5.5 · QA", engineRoot, editor,
            new(5, 5, 0), EngineInstallationType.EpicBinary, null, null, null, "QA", EngineHealth.Healthy, [], DateTimeOffset.UtcNow);
        var projectRoot = Path.Combine(Output, "Project");
        Directory.CreateDirectory(projectRoot);
        var marker = Path.Combine(projectRoot, "Fixture.uproject");
        await File.WriteAllTextAsync(marker, "{}");
        var project = new UnrealProject("fixture:project", "Fixture", marker, projectRoot, "fixture-association", null, null, [], [], ProjectHealth.Healthy, [], DateTimeOffset.UtcNow);
        Call(vm, "ReplaceCatalog", new[] { engine }, new[] { project });
        var library = Path.Combine(Output, "Library");
        Directory.CreateDirectory(library);
        vm.FabLibraryLocations.Clear();
        vm.AddFabLibraryLocationCommand.Execute(null);
        vm.FabLibraryLocations[0].Path = library;
        vm.FabLibraryLocations[0].MaximumGigabytes = "1";
        StorageEntry Entry(StorageCategory category, string path, long size) => new(path, category, StorageEntryRole.Footprint,
            "Fixture", "Fixture", path, path, size, 1, 1, null, StorageHealth.Ready, false, "Données de démonstration");
        Call(vm, "ReplaceStorageSnapshot", new StorageSnapshot([Entry(StorageCategory.EngineInstallation, engineRoot, 200),
            Entry(StorageCategory.Project, projectRoot, 100), Entry(StorageCategory.MarketplaceLibrary, library, 150)],
            [new("D:\\", "QA (simulation)", 10000, 500, 500)], [], DateTimeOffset.UtcNow));
        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
        vm.FabLibraryLocations[0].MaximumGigabytes = "0,5";
        Call(vm, "RefreshStorageOverview");
        Check(vm.StorageLibraryCapacities.First().Quota.Contains("512"), "French decimal quota matches marketplace settings parser");
        vm.FabLibraryLocations[0].MaximumGigabytes = "invalid";
        Call(vm, "RefreshStorageOverview");
        Check(vm.StorageLibraryCapacities.First().Quota.Contains("invalide"), "Invalid quota is explicit, never interpreted as unlimited");
        vm.FabLibraryLocations[0].MaximumGigabytes = "1";
        System.Globalization.CultureInfo.CurrentCulture = originalCulture;
        Call(vm, "RefreshStorageOverview");
        Call(vm, "ReplaceToolchainSnapshot", new ToolchainSnapshot([], [new(engine.Id, engine.DisplayName, "5.5.0",
            [new(ToolchainKind.DotNet, ".NET", ToolchainHealth.Ready, "fixture", "fixture", ""),
             new(ToolchainKind.LinuxToolchain, "Linux", ToolchainHealth.Missing, "fixture", "absent", "SDK absent"),
             new(ToolchainKind.WindowsSdk, "Windows SDK", ToolchainHealth.Warning, "fixture", "fixture", "Version recommandée absente")], [])], [], [], [], DateTimeOffset.UtcNow));
        var window = new MainWindow { DataContext = vm, Title = "CyEngineHub — Interface QA (données fictives)", Width = 1280, Height = 820,
            ShowActivated = false, ShowInTaskbar = false, Position = new PixelPoint(-3000, -3000) };
        window.Show();
        if (ImGuiDiagnostics.IsHosted(window))
        {
            if (EngineIdentityOnly) { await ImGuiDiagnostics.FramesAsync(window, 4); await TestEngineIdentity(window, vm); Console.WriteLine("OUTPUT " + Output); }
            else if (SettingsOnly) { await ImGuiDiagnostics.FramesAsync(window, 4); await TestOrganizedSettings(window, vm); Console.WriteLine("OUTPUT " + Output); }
            else if (GroupsOnly) { await ImGuiDiagnostics.FramesAsync(window, 4); await TestPackGroups(window, vm); Console.WriteLine("OUTPUT " + Output); }
            else if (ResourceLayoutOnly) { await ImGuiDiagnostics.FramesAsync(window, 4); await TestResourceToolbarLayout(window, vm); Console.WriteLine("OUTPUT " + Output); }
            else if (VariantsOnly) { await ImGuiDiagnostics.FramesAsync(window, 4); await TestVariantLabels(window, vm); Console.WriteLine("OUTPUT " + Output); }
            else if (ToolsOnly) { await ImGuiDiagnostics.FramesAsync(window, 4); await TestTools(window, vm, launcher); Console.WriteLine("OUTPUT " + Output); }
            else if (ActivityOnly) { await ImGuiDiagnostics.FramesAsync(window, 4); await TestEngineDetailsAndActivity(window, vm); Console.WriteLine("OUTPUT " + Output); }
            else if (AnnotationsOnly) { await ImGuiDiagnostics.FramesAsync(window, 4); await TestResourcePresentations(window, vm, launcher); await TestAsyncImages(window, vm); await TestAssetCards(window, vm); Console.WriteLine("OUTPUT " + Output); }
            else if (ResourcesOnly) { await ImGuiDiagnostics.FramesAsync(window, 4); await TestAsyncImages(window, vm); await TestAssetCards(window, vm); await TestMultiSelectionToolbar(window, vm, launcher); await TestResourceActions(window, vm, launcher); Console.WriteLine("OUTPUT " + Output); }
            else if (DownloadsOnly) { vm.ShowEnginesCommand.Execute(null); await ImGuiDiagnostics.FramesAsync(window, 4); await TestEngineDownloadFlow(window, vm); Console.WriteLine("OUTPUT " + Output); }
            else await TestImGui(window, vm, launcher);
            window.Close(); stop.Cancel(); return;
        }
        Click(window, vm.ShowEnginesCommand);
        await Task.Delay(150);
        Check(vm.IsEnginesVisible, "Engine navigation binding");
        vm.Engines.Single().OpenDetailsCommand.Execute(null);
        await Task.Delay(150);
        var launchButton = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Lancer · accueil Unreal"));
        typeof(Button).GetMethod("OnClick", Private)!.Invoke(launchButton, null);
        await Task.Delay(100);
        Check(launcher.Launches.Count == 1 && launcher.Launches[0].Arguments.SequenceEqual(["-norecentproject"]), "Bound engine launch sends project-browser arguments");
        Check(vm.CrossCompileProjects.Count == 1 && !vm.RunCrossCompileCommand.CanExecute(null), "Only associated projects; missing Linux SDK prevents launch");
        await Capture(window, "engine");
        var compileButton = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Compilation / Linux / Android"));
        typeof(Button).GetMethod("OnClick", Private)!.Invoke(compileButton, null);
        await Capture(window, "engine-compilation");
        Click(window, vm.ShowStorageCommand);
        await Task.Delay(150);
        var tabs = window.FindControl<TabControl>("StorageOverviewTabs")!;
        tabs.SelectedIndex = 3;
        Check(vm.StorageOverviewTab == 3 && vm.FilteredStorageEntries.Count == 1, "Packs tab filters storage through two-way binding");
        tabs.SelectedIndex = 1;
        Check(vm.FilteredStorageEntries.Single().Entry.Category == StorageCategory.EngineInstallation, "Engine storage tab isolated from packs/projects");
        tabs.SelectedIndex = 0;
        await Capture(window, "storage");
        Click(window, vm.ShowProjectsCommand);
        await Task.Delay(150);
        Check(vm.IsProjectsVisible, "Project navigation binding");
        await Capture(window, "projects");
        Click(window, vm.ShowSettingsCommand);
        await Task.Delay(150);
        Check(vm.IsSettingsVisible, "Settings navigation binding");
        await Capture(window, "settings");
        Console.WriteLine("OUTPUT " + Output);
        if (interactive)
        {
            Console.WriteLine("INTERACTIVE fixture ready; launch/folder actions are recorded only.");
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            await closed.Task;
        }
        else window.Close();
        stop.Cancel();
    }
    static void Click(Window window, System.Windows.Input.ICommand command)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Classes.Contains("nav") && ReferenceEquals(button.Command, command));
        Check(button.IsEffectivelyEnabled, "Navigation button enabled");
        typeof(Button).GetMethod("OnClick", Private)!.Invoke(button, null);
    }
    static async Task TestImGui(Window window, MainWindowViewModel vm, RecordingLauncher launcher)
    {
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(!ImGuiDiagnostics.Elements(window).Contains("density") && !ImGuiDiagnostics.Elements(window).Contains("action/NavigateBackCommand"),
            "Startup has neither redundant history arrows nor a density checkbox");
        Check(vm.IsEnginesVisible && !ImGuiDiagnostics.Elements(window).Contains("nav/Vue d’ensemble") &&
            ImGuiDiagnostics.Elements(window).Contains("creation-action/ToggleEngineImporterCommand"), "Startup opens engines directly and overview navigation is removed");
        Check(!ImGuiDiagnostics.Elements(window).Contains("nav/Plugins"), "Plugins are reached through project and engine details, not the main navigation");
        var footer = ImGuiDiagnostics.ElementBounds(window, "nav/Stockage");
        Check(footer.Y > window.Height * .65 && ImGuiDiagnostics.ElementBounds(window, "nav/Paramètres").W < window.Height,
            "Storage and activity navigation are anchored visibly near the bottom");
        Check(footer.Y > ImGuiDiagnostics.ElementBounds(window, "nav/activity-heading").W, "Storage is inside the Activity group below its heading");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-home.png"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
        Check(vm.IsEnginesVisible, "ImGui pointer input activates engine navigation");
        Check(!ImGuiDiagnostics.Elements(window).Contains("resource-tools") &&
            !ImGuiDiagnostics.Elements(window).Contains("action/ShowProjectProfilesCommand"), "Resource toolbars omit the removed global options");
        Check(ImGuiDiagnostics.Elements(window).Contains("creation-action/ToggleEngineImporterCommand"), "Add Engine has the same dedicated creation color");
        Check(ImGuiDiagnostics.FontSize(window) >= 16, "Global text size increased from 14 by 15 percent");
        Check(ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("resource/card/", StringComparison.Ordinal)), "Engines default to fixed cards");
        var fullWidth = ImGuiDiagnostics.ElementBounds(window, "resources/full-width");
        Check(fullWidth.Z - fullWidth.X > window.Width - 270, "Resource cards use the full workspace without an inspector");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Liste");
        Check(!vm.EngineCardsView && !ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("resource/card/", StringComparison.Ordinal)), "Engine list mode remains available");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Cartes");
        await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/Unreal 5.5 · QA", horizontal: .8f, vertical: .82f);
        Check(ImGuiDiagnostics.Elements(window).Contains("card/selected/resource/Unreal 5.5 · QA") && !vm.IsEngineDetailsVisible,
            "Engine card whitespace selects without opening or launching");
        await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/Unreal 5.5 · QA", clicks: 2, horizontal: .8f, vertical: .82f);
        Check(vm.IsEngineDetailsVisible && launcher.Launches.Count == 0, "Engine double-click opens its details, never launches the engine");
        vm.ShowEnginesCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "action/LaunchCommand");
        Check(launcher.Launches.Count == 1 && launcher.Launches[0].Arguments.SequenceEqual(["-norecentproject"]), "ImGui engine launch uses real command binding");
        await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/Unreal 5.5 · QA", clicks: 2, vertical: .2f);
        Check(vm.IsEngineDetailsVisible, "Double-clicking an engine image also opens its details");
        vm.ShowEnginesCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engines.png"));
        await TestResourcePresentations(window, vm, launcher);
        await TestResourceToolbarLayout(window, vm);
        await TestEngineDetailsAndActivity(window, vm);
        await TestEngineSortingAndUpdates(window, vm);
        await TestEngineSelection(window, vm);
        await ImGuiDiagnostics.ClickAsync(window, "nav/Projets");
        Check(vm.IsProjectsVisible, "ImGui pointer input activates project navigation");
        Check(ImGuiDiagnostics.Elements(window).Contains("creation-action/ToggleProjectCreatorCommand") &&
            !ImGuiDiagnostics.Elements(window).Contains("resource-tools") &&
            ImGuiDiagnostics.Elements(window).Contains("action/ToggleManualProjectAddCommand"), "Projects expose colored creation and import, not global per-project options");
        await ImGuiDiagnostics.ClickAsync(window, "action/ToggleManualProjectAddCommand");
        Check(vm.IsManualProjectAddVisible, "Project import remains directly accessible from the project toolbar");
        await ImGuiDiagnostics.ClickAsync(window, "command/ToggleManualProjectAddCommand");
        Check(!vm.IsManualProjectAddVisible && vm.IsProjectsVisible, "Closing import returns to the project list");
        await ImGuiDiagnostics.ClickAsync(window, "action/ToggleProjectCreatorCommand");
        Check(vm.IsProjectCreatorVisible, "Colored project creation action opens the creator from the project list");
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseProjectCreatorCommand");
        Check(vm.ProjectCardsView && ImGuiDiagnostics.Elements(window).Contains("resource/card/Fixture"), "Projects have their own card mode");
        await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/Fixture", horizontal: .8f, vertical: .82f);
        Check(ImGuiDiagnostics.Elements(window).Contains("card/selected/resource/Fixture") && !vm.IsProjectDetailsVisible, "Project whitespace uses the same selection behavior");
        await ImGuiDiagnostics.ClickAsync(window, "action/OpenDetailsCommand");
        Check(vm.IsProjectDetailsVisible && vm.SelectedProjectDetails?.Name == "Fixture", "Project settings open the selected project's own details");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-project-settings.png"));
        vm.ShowProjectsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/Fixture", clicks: 2, horizontal: .8f, vertical: .82f);
        Check(vm.IsProjectDetailsVisible && launcher.Launches.Count == 1, "Project double-click opens its details, never launches the editor");
        vm.ShowProjectsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-project-cards.png"));
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Liste");
        Check(!vm.ProjectCardsView && vm.EngineCardsView, "Project and engine display preferences are independent");
        await vm.FlushMarketplaceSettingsWritesAsync();
        typeof(MainWindowViewModel).GetField("_engineCardsView", Private)!.SetValue(vm, false);
        typeof(MainWindowViewModel).GetField("_projectCardsView", Private)!.SetValue(vm, true);
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadInterfaceAsync", Private)!.Invoke(vm, null)!;
        Check(vm.EngineCardsView && !vm.ProjectCardsView, "Independent display modes restore from saved settings");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Cartes");
        await ImGuiDiagnostics.ClickAsync(window, "resource-search");
        await ImGuiDiagnostics.TextAsync(window, "nonexistent-project");
        Check(!ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("row/", StringComparison.Ordinal)), "ImGui text input filters the project list");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Stockage");
        Check(vm.IsStorageVisible, "ImGui storage navigation");
        await ImGuiDiagnostics.ClickAsync(window, "storage-tab/Packs");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-storage-debug.png"));
        Console.WriteLine($"STORAGE TAB {vm.StorageOverviewTab} COUNT {vm.FilteredStorageEntries.Count}");
        Check(vm.StorageOverviewTab == 3 && vm.FilteredStorageEntries.Count == 1, "ImGui storage tabs update bound filtering");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-storage.png"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Paramètres");
        Check(vm.IsSettingsVisible, "ImGui pointer input activates settings navigation");
        CheckFullHeight(window, "settings");
        var sidebar = ImGuiDiagnostics.ElementBounds(window, "pane/settings/navigation");
        window.Height = 1000;
        await ImGuiDiagnostics.FramesAsync(window, 3);
        CheckFullHeight(window, "settings");
        var taller = ImGuiDiagnostics.ElementBounds(window, "pane/settings/navigation");
        Check(taller.W - taller.Y > sidebar.W - sidebar.Y + 150, "Sidebar grows with the window instead of retaining a fixed list height");
        Check(ImGuiDiagnostics.ElementBounds(window, "nav/Stockage").Y > footer.Y + 150, "Bottom navigation follows window height changes");
        window.Height = 820;
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(!ImGuiDiagnostics.Elements(window).Contains("action/NavigateBackCommand"), "Persistent settings navigation has no redundant back button");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/project-profiles");
        Check(vm.IsSettingsProjectProfilesSelected && ImGuiDiagnostics.Elements(window).Contains("section-selected/settings/project-profiles"), "Settings tabs share a visible selected state");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-settings.png"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Projets");
        Check(vm.IsProjectsVisible, "ImGui navigation remains functional after settings and tabs");
        await ImGuiDiagnostics.ClickAsync(window, "action/ToggleProjectCreatorCommand");
        Check(vm.IsProjectCreatorVisible, "Project creation modal opens through ImGui button");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs", leadingEdge: true);
        Check(vm.IsProjectCreatorVisible && vm.IsProjectsVisible, "Modal blocks clicks on the uncovered backdrop navigation");
        await ImGuiDiagnostics.ClickAsync(window, "field/NewProjectName");
        await ImGuiDiagnostics.TextAsync(window, "ImGuiFixtureProject");
        if (vm.NewProjectName != "ImGuiFixtureProject")
        {
            Console.WriteLine("PROJECT NAME " + vm.NewProjectName);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-input-debug.png"));
        }
        Check(vm.NewProjectName == "ImGuiFixtureProject", "Project form text input updates the creation model");
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
        await ImGuiDiagnostics.TextAsync(window, "Projet été");
        Check(vm.NewProjectName == "Projet été", "Routed keyboard input supports accents and Ctrl+A replacement");
        await ImGuiDiagnostics.KeyAsync(window, Key.Back);
        Check(vm.NewProjectName == "Projet ét", "Routed Backspace edits text");
        await ImGuiDiagnostics.TextAsync(window, "@", KeyModifiers.Control | KeyModifiers.Alt);
        Check(vm.NewProjectName == "Projet ét@", "AltGr text is not swallowed as a shortcut");
        await ImGuiDiagnostics.ClickAsync(window, "tab/Configuration INI");
        Check(vm.ProjectCreatorTabIndex == 3, "Project creation tabs update the INI editor selection");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-project-modal.png"));
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseProjectCreatorCommand");
        Check(!vm.IsProjectCreatorVisible, "Project modal close button executes its command");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Paramètres");
        Check(vm.IsSettingsVisible, "Closed ImGui modal releases navigation input");
        await TestToolLists(window, vm);
        await TestTools(window, vm, launcher);
        await TestActivityLog(window, vm);
        await TestAsyncImages(window, vm);
        await ImGuiDiagnostics.ClickAsync(window, "nav/Téléchargements");
        Check(vm.IsTasksVisible, "Download queue remains accessible through ImGui navigation");
        await TestAssetCards(window, vm);
        await TestMultiSelectionToolbar(window, vm, launcher);
        await TestResourceActions(window, vm, launcher);
        await TestVariantLabels(window, vm);
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
        await ImGuiDiagnostics.ClickAsync(window, "action/ToggleEngineImporterCommand");
        Check(vm.IsEngineImporterVisible, "Add engine action exposes the complete engine importer");
        await TestEngineDownloadFlow(window, vm);
        Console.WriteLine("OUTPUT " + Output);
    }
    static async Task TestResourcePresentations(Window window, MainWindowViewModel vm, RecordingLauncher launcher)
    {
        var launches = launcher.Launches.Count;
        foreach (var engines in new[] { true, false })
        {
            if (engines) vm.ShowEnginesCommand.Execute(null); else vm.ShowProjectsCommand.Execute(null);
            await ImGuiDiagnostics.FramesAsync(window, 3);
            Check(new[] { "Cartes", "Image + nom", "Lignes", "Lignes + image" }.All(label => ImGuiDiagnostics.Elements(window).Contains("resource-view/" + label)),
                "Engines and projects expose the same four presentations as marketplaces");
            var iconPrefix = "view-mode-icon/" + (engines ? "engines/" : "projects/");
            Check(Enumerable.Range(0, 4).All(mode => ImGuiDiagnostics.Elements(window).Contains(iconPrefix + mode)),
                "Resource presentation controls draw four vector icons instead of text labels");
            var firstIcon = ImGuiDiagnostics.ElementBounds(window, iconPrefix + "0");
            var lastIcon = ImGuiDiagnostics.ElementBounds(window, iconPrefix + "3");
            Check(firstIcon.Z - firstIcon.X <= 40 && Math.Abs((firstIcon.Z - firstIcon.X) - (firstIcon.W - firstIcon.Y)) < 1 &&
                lastIcon.Z - firstIcon.X < 180, "Four square icon buttons occupy less than 180 pixels");
            foreach (var compact in new[] { true, false })
            {
                vm.UseCompactInterface = compact;
                await ImGuiDiagnostics.FramesAsync(window, 3);
                var standard = ImGuiDiagnostics.ElementBounds(window, engines ? "action/ToggleEngineImporterCommand" : "action/ToggleProjectCreatorCommand");
                Check(Enumerable.Range(0, 4).All(mode =>
                {
                    var icon = ImGuiDiagnostics.ElementBounds(window, iconPrefix + mode);
                    return Math.Abs((icon.W - icon.Y) - (standard.W - standard.Y)) < 1;
                }), "Resource icons match standard button height in " + (compact ? "compact" : "comfortable") + " density");
            }
            vm.UseCompactInterface = true;
            await ImGuiDiagnostics.FramesAsync(window, 3);
            var name = engines ? vm.Engines[0].DisplayName : vm.Projects[0].Name;
            var full = ImGuiDiagnostics.ElementBounds(window, "resource/card/" + name);
            await ImGuiDiagnostics.ClickAsync(window, "resource-view/Image + nom");
            Check(ImGuiDiagnostics.Elements(window).Contains("view-mode-tooltip/" + (engines ? "engines/1" : "projects/1")) &&
                ImGuiDiagnostics.Elements(window).Contains("resource-view-selected/1"), "Resource icons retain tooltips and a visible selected state");
            var minimal = ImGuiDiagnostics.ElementBounds(window, "resource/minimal/" + name);
            Check(minimal.Z - minimal.X < full.Z - full.X && minimal.W - minimal.Y < full.W - full.Y &&
                !ImGuiDiagnostics.Elements(window).Contains("action/LaunchCommand"), "Image and name resource cards are smaller and omit metadata/actions");
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, engines ? "imgui-engine-minimal-cards.png" : "imgui-project-minimal-cards.png"));
            await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/" + name, clicks: 2, vertical: .25f);
            Check((engines ? vm.IsEngineDetailsVisible : vm.IsProjectDetailsVisible) && launcher.Launches.Count == launches,
                "Double-clicking a minimal resource card opens details without launching an editor");
            if (engines) vm.ShowEnginesCommand.Execute(null); else vm.ShowProjectsCommand.Execute(null);
            await ImGuiDiagnostics.FramesAsync(window, 3);
            await ImGuiDiagnostics.ClickAsync(window, "resource-view/Lignes");
            Check(!ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("resource-row/image/", StringComparison.Ordinal)), "Plain resource rows contain no image");
            await ImGuiDiagnostics.ClickAsync(window, "resource-view/Lignes + image");
            Check(ImGuiDiagnostics.Elements(window).Contains("resource-row/image/" + name), "Resource thumbnail rows draw small images inside the selectable name cell");
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, engines ? "imgui-engine-thumbnail-rows.png" : "imgui-project-thumbnail-rows.png"));
            await ImGuiDiagnostics.ClickAsync(window, "table-header/resources/" + (engines ? "engines" : "projects") + "/1");
            await ImGuiDiagnostics.ClickAsync(window, "table-header/resources/" + (engines ? "engines" : "projects") + "/0");
            await ImGuiDiagnostics.ClickAsync(window, "row/" + name, clicks: 2, horizontal: .06f);
            Check((engines ? vm.IsEngineDetailsVisible : vm.IsProjectDetailsVisible) && launcher.Launches.Count == launches,
                "Thumbnail resource rows retain sortable columns and double-click details behavior");
            if (engines) vm.ShowEnginesCommand.Execute(null); else vm.ShowProjectsCommand.Execute(null);
            await ImGuiDiagnostics.FramesAsync(window, 3);
            await ImGuiDiagnostics.ClickAsync(window, "resource-view/Cartes");
        }
        vm.SetResourcePresentation(true, 1);
        vm.SetResourcePresentation(false, 3);
        var libraryRows = vm.IsLibraryRowsMode;
        await vm.FlushMarketplaceSettingsWritesAsync();
        foreach (var field in new[] { "_engineImageOnlyCards", "_projectRowImages" }) typeof(MainWindowViewModel).GetField(field, Private)!.SetValue(vm, false);
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadInterfaceAsync", Private)!.Invoke(vm, null)!;
        Check(vm.EngineCardsView && vm.EngineImageOnlyCards && !vm.ProjectCardsView && vm.ProjectRowImages && vm.IsLibraryRowsMode == libraryRows,
            "All resource presentations persist independently for engines, projects and marketplaces");
        vm.SetResourcePresentation(true, 0); vm.SetResourcePresentation(false, 0);
        vm.ShowEnginesCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
    }

    static async Task TestEngineDetailsAndActivity(Window window, MainWindowViewModel vm)
    {
        vm.Engines[0].OpenDetailsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(vm.EngineDetailsTab == 0 && ImGuiDiagnostics.Elements(window).Contains("engine-details/general") &&
            !ImGuiDiagnostics.Elements(window).Contains("action/BuildEditorCommand"), "Engine details open on a compact General tab without compilation controls");
        var content = ImGuiDiagnostics.ElementBounds(window, "pane/engine-details/content");
        Check(content.Y < 210 && content.W > window.Height - 65, "Engine header stays compact and tab content fills the remaining height");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engine-general-tabs.png"));
        await ImGuiDiagnostics.ClickAsync(window, "engine-detail-tab/1");
        Check(vm.EngineDetailsTab == 1 && ImGuiDiagnostics.Elements(window).Contains("engine-details/installation") &&
            !ImGuiDiagnostics.Elements(window).Contains("action/LaunchCommand"), "Installation options have their own tab and do not repeat General actions");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engine-installation-tab.png"));
        await ImGuiDiagnostics.ClickAsync(window, "engine-detail-tab/2");
        Check(vm.EngineDetailsTab == 2 && ImGuiDiagnostics.Elements(window).Contains("action/BuildEditorCommand") &&
            ImGuiDiagnostics.Elements(window).Contains("action/GenerateProjectFilesCommand"), "Compilation and cross-compilation share their own engine tab");
        Check(vm.SelectedEngineToolchains.SelectMany(card => card.Checks).Any(check => check.Health == "Ready" && check.HealthColor == "#5CD26E") &&
            vm.SelectedEngineToolchains.SelectMany(card => card.Checks).Any(check => check.Health == "Missing" && check.HealthColor == "#E8747C") &&
            vm.SelectedEngineToolchains.SelectMany(card => card.Checks).Any(check => check.Health == "Warning" && check.HealthColor == "#F5A623"), "Ready checks are green, warnings orange and missing requirements red");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engine-compilation-tab.png"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
        vm.Engines[0].OpenDetailsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(vm.EngineDetailsTab == 0, "Opening a new engine detail resets its selected tab to General");

        var scheduler = (HubTaskScheduler)typeof(MainWindowViewModel).GetField("_taskScheduler", Private)!.GetValue(vm)!;
        scheduler.SetDownloadConcurrency(1);
        HubTaskHandle Start(string name, bool download) => scheduler.Enqueue(new HubTaskDefinition("QA", name,
            async (context, token) => { context.Report(.25, "Synthetic operation, no executable launched"); await Task.Delay(Timeout.Infinite, token); }) { IsDownload = download });
        var build = Start("QA compiler un projet", false);
        var install = Start("QA installer un pack", false);
        var transfer = Start("QA transférer un moteur", true);
        var pending = Start("QA transférer un asset", true);
        async Task Wait(Func<bool> condition)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (!condition()) { if (DateTime.UtcNow > until) throw new TimeoutException("Activity queue did not settle"); await ImGuiDiagnostics.FramesAsync(window, 2); }
        }
        try
        {
            await Wait(() => vm.RunningActions == 1 && vm.QueuedActions == 1 && vm.RunningDownloads == 1 && vm.QueuedDownloads == 1);
            await ImGuiDiagnostics.ClickAsync(window, "nav/Actions");
            Check(vm.IsActionsVisible && ImGuiDiagnostics.Elements(window).Contains("task/" + build.Id) &&
                ImGuiDiagnostics.Elements(window).Contains("task/" + install.Id) && !ImGuiDiagnostics.Elements(window).Contains("task/" + transfer.Id), "Actions contains builds and installations but excludes downloads");
            Check(ImGuiDiagnostics.ElementBounds(window, "nav/Actions").Y < ImGuiDiagnostics.ElementBounds(window, "nav/Téléchargements").Y,
                "Actions appears immediately above Downloads in the activity navigation");
            Check(ImGuiDiagnostics.Elements(window).Contains("nav-count/actions/running/1") && ImGuiDiagnostics.Elements(window).Contains("nav-count/actions/queued/1") &&
                ImGuiDiagnostics.Elements(window).Contains("nav-count/downloads/running/1") && ImGuiDiagnostics.Elements(window).Contains("nav-count/downloads/queued/1"), "Both navigation entries show independent running and waiting counters");
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-actions-queue.png"));
            await ImGuiDiagnostics.ClickAsync(window, "task/cancel/" + install.Id);
            await Wait(() => vm.QueuedActions == 0);
            Check(vm.RunningDownloads == 1 && vm.QueuedDownloads == 1, "Canceling a queued installation leaves download counters unchanged");
            await ImGuiDiagnostics.ClickAsync(window, "task/cancel/" + build.Id);
            await Wait(() => vm.RunningActions == 0);
            await ImGuiDiagnostics.ClickAsync(window, "nav/Téléchargements");
            Check(vm.IsTasksVisible && ImGuiDiagnostics.Elements(window).Contains("task/" + transfer.Id) &&
                !ImGuiDiagnostics.Elements(window).Contains("task/" + build.Id), "Downloads shows only its own transfers and preserves their cancellation actions");
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-downloads-separated.png"));
            await ImGuiDiagnostics.ClickAsync(window, "task/cancel/" + pending.Id);
            await Wait(() => vm.QueuedDownloads == 0);
            await ImGuiDiagnostics.ClickAsync(window, "task/cancel/" + transfer.Id);
            await Wait(() => vm.RunningDownloads == 0);
            Check(vm.RunningActions == 0 && vm.QueuedActions == 0, "All counters clear after cancellation without moving terminal tasks to the other queue");
        }
        finally
        {
            foreach (var handle in new[] { build, install, transfer, pending }) handle.Cancel();
            await Task.WhenAll(build.Completion, install.Completion, transfer.Completion, pending.Completion);
            scheduler.SetDownloadConcurrency(3);
        }
        var localRoot = Directory.CreateDirectory(Path.Combine(Output, "GodotFixture")).FullName;
        var localEditor = Path.Combine(localRoot, "Godot.exe");
        await File.WriteAllTextAsync(localEditor, "synthetic editor - never executed");
        var organization = new LibraryItemOrganizationViewModel(CyEngineHub.Core.Settings.WorkspaceItemKind.Engine, localRoot, "Godot QA", null,
            (_, _, _, _) => Task.CompletedTask, null, error => throw error);
        var localCard = new LocalEngineInstallationCardViewModel(new(EngineFamily.Godot, "godot", "qa-godot", "Godot QA", "4.4", localRoot, localEditor, "QA"),
            organization, "Taille · à analyser", _ => throw new NotSupportedException("No local editor may run in fixtures"), _ => Task.CompletedTask,
            card => Call(vm, "OpenLocalEngineDetails", card), error => throw error);
        vm.LocalEngineInstallations.Add(localCard);
        localCard.OpenDetailsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(ImGuiDiagnostics.Elements(window).Contains("action/ShowSelectedEnginePluginsCommand"), "Other engine providers retain their plugin and tool actions in General");
        await ImGuiDiagnostics.ClickAsync(window, "engine-detail-tab/1");
        Check(ImGuiDiagnostics.Elements(window).Contains("field/SelectedLocalEngineDetails.Organization.Alias"), "Other engine providers retain their organization settings in installation options");
        await ImGuiDiagnostics.ClickAsync(window, "engine-detail-tab/0");
        await ImGuiDiagnostics.ClickAsync(window, "action/AnalyzeSelectedEngineSizeCommand");
        await Wait(() => !vm.IsEngineSelectionBusy);
        var measured = (StorageSnapshot)typeof(MainWindowViewModel).GetField("_currentStorageSnapshot", Private)!.GetValue(vm)!;
        Check(measured.Entries.Any(entry => entry.Category == StorageCategory.EngineInstallation && entry.OwnerRoot == localRoot && entry.Path == localRoot && entry.SizeBytes > 0),
            "Local provider size analysis measures its installation root rather than only its editor executable");
        vm.LocalEngineInstallations.Remove(localCard);
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
    }
    static async Task TestTools(Window window, MainWindowViewModel vm, RecordingLauncher launcher)
    {
        var root = vm.Engines[0].Engine.RootPath;
        Check(Path.GetFullPath(root).StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Tool fixtures stay inside the isolated output directory");
        var prerequisite = Path.Combine(root, "Engine", "Extras", "Redist", "en-us", "UEPrereqSetup_x64.exe");
        var selector = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealVersionSelector.exe");
        var insights = Path.Combine(root, "Engine", "Binaries", "Win64", "UnrealInsights.exe");
        foreach (var path in new[] { prerequisite, selector, insights })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, "synthetic fixture - never executable");
        }
        var launches = launcher.Launches.Count;
        await ImGuiDiagnostics.ClickAsync(window, "nav/Outils");
        var until = DateTime.UtcNow.AddSeconds(10);
        while (vm.UnrealUtilities.Count != 4)
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("Unreal tools inventory did not complete");
            await ImGuiDiagnostics.FramesAsync(window, 2);
        }
        Check(vm.IsToolchainsVisible && vm.IsToolsNavigationActive && vm.HeaderTitle == "Outils", "Outils is an independent highlighted main navigation page");
        Check(launcher.Launches.Count == launches && vm.UnrealUtilities.Count(item => item.IsAvailable) == 3, "Detecting tools never launches executables or fabricates missing files");
        Check(!ImGuiDiagnostics.Elements(window).Contains("action/NavigateBackCommand"), "The main Tools page has no redundant back arrow");
        await ImGuiDiagnostics.ClickAsync(window, "table-header/unreal-tools-list/0");
        Check(ImGuiDiagnostics.ElementBounds(window, "unreal-tool/prerequisites").Y < ImGuiDiagnostics.ElementBounds(window, "unreal-tool/version-selector").Y, "Unreal utility columns sort ascending");
        await ImGuiDiagnostics.ClickAsync(window, "table-header/unreal-tools-list/0");
        Check(ImGuiDiagnostics.ElementBounds(window, "unreal-tool/prerequisites").Y > ImGuiDiagnostics.ElementBounds(window, "unreal-tool/version-selector").Y, "Clicking a utility column again reverses sorting");
        await ImGuiDiagnostics.ClickAsync(window, "unreal-tool/prerequisites");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-tools-unreal.png"));
        await ImGuiDiagnostics.ClickAsync(window, "action/PrepareUnrealUtilityCommand");
        Check(vm.IsUtilityLaunchConfirmationVisible && launcher.Launches.Count == launches, "Prerequisite installer requires confirmation showing the exact executable");
        Check(vm.UtilityLaunchConfirmationText.Contains(prerequisite, StringComparison.Ordinal), "Confirmation identifies the selected engine's installer");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Projets");
        Check(vm.IsToolchainsVisible && vm.IsUtilityLaunchConfirmationVisible, "Tool confirmation blocks backdrop navigation");
        await ImGuiDiagnostics.ClickAsync(window, "command/CancelUtilityLaunchCommand");
        Check(!vm.IsUtilityLaunchConfirmationVisible && launcher.Launches.Count == launches, "Canceling a tool launch makes no system change");
        await ImGuiDiagnostics.ClickAsync(window, "unreal-tool/version-selector");
        await ImGuiDiagnostics.ClickAsync(window, "action/PrepareUnrealUtilityCommand");
        await ImGuiDiagnostics.ClickAsync(window, "command/ConfirmUtilityLaunchCommand");
        Check(launcher.Launches.Count == launches + 1 && launcher.Launches[^1].Executable == selector && launcher.Launches[^1].Arguments.Count == 0,
            "Explicit confirmation launches only the chosen selector, without silent registration arguments");
        await ImGuiDiagnostics.ClickAsync(window, "unreal-tool/swarm");
        Check(!vm.PrepareUnrealUtilityCommand.CanExecute(null) && !vm.OpenUnrealUtilityFolderCommand.CanExecute(null), "Missing utility exposes no launch or folder action");
        await ImGuiDiagnostics.ClickAsync(window, "tools-search");
        await ImGuiDiagnostics.TextAsync(window, "Insights");
        Check(ImGuiDiagnostics.Elements(window).Count(key => key.StartsWith("unreal-tool/", StringComparison.Ordinal)) == 1, "Tools search accepts keyboard text and filters utility rows");
        await ImGuiDiagnostics.ClickAsync(window, "tools-tab/1");
        var old = (ToolchainSnapshot)typeof(MainWindowViewModel).GetField("_currentToolchainSnapshot", Private)!.GetValue(vm)!;
        ToolchainInstallation Installation(string id, string name, string version, ToolchainKind kind) => new(id, kind, name, version, root, "QA", ToolchainHealth.Ready, [], new Dictionary<string, string>());
        Call(vm, "ReplaceToolchainSnapshot", old with { Installations = [Installation("dotnet8", ".NET", "8.0", ToolchainKind.DotNet), Installation("dotnet10", ".NET", "10.0", ToolchainKind.DotNet), Installation("msvc", "C++ / MSVC", "14.38", ToolchainKind.Msvc)] });
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(ImGuiDiagnostics.Elements(window).Count(key => key.StartsWith("toolchain-row/", StringComparison.Ordinal)) == 3, "Toolchains tab displays detected SDK and C++ installations as compact rows");
        await ImGuiDiagnostics.ClickAsync(window, "table-header/toolchain-list/1");
        Check(ImGuiDiagnostics.ElementBounds(window, "toolchain-row/dotnet8").Y < ImGuiDiagnostics.ElementBounds(window, "toolchain-row/dotnet10").Y, "Toolchain versions sort numerically instead of alphabetically");
        await ImGuiDiagnostics.ClickAsync(window, "toolchain-row/msvc");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-tools-toolchains.png"));
        await ImGuiDiagnostics.ClickAsync(window, "action/OpenDotNetDownloadsCommand");
        Check(launcher.OpenedUris[^1].Host == "dotnet.microsoft.com" && launcher.Launches.Count == launches + 1, "Official .NET link opens a browser page, not an installer");
        await ImGuiDiagnostics.ClickAsync(window, "tools-tab/2");
        Check(ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("command/RefreshToolchainsCommand/", StringComparison.Ordinal)), "Detailed compatibility and device diagnostics remain accessible");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-tools-diagnostics.png"));
        var engineChecks = vm.EngineToolchains[0].Checks;
        Check(engineChecks.Where(check => check.Health == "Ready").All(check => !check.HasRemediation) &&
            engineChecks.Where(check => check.Health != "Ready").All(check => check.HasRemediation),
            "Only missing or warning prerequisites offer a contextual remedy");
        await ImGuiDiagnostics.ClickAsync(window, "command/ObtainPrerequisiteCommand");
        Check(vm.IsToolchainsVisible && vm.ToolsTabIndex == 1 && vm.PrerequisiteCheck is not null && vm.SelectedToolsEngine?.Engine.Id == vm.Engines[0].Engine.Id,
            "Prerequisite remedy opens Tools for the correct engine");
        var opened = launcher.OpenedUris.Count;
        Check(vm.PrerequisiteRemediation?.DownloadPage is not null && launcher.OpenedUris.Count == opened, "Selecting a remedy never starts a download or installer automatically");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-tools-prerequisite-remedy.png"));
        await ImGuiDiagnostics.ClickAsync(window, "action/OpenPrerequisiteDownloadCommand");
        Check(launcher.OpenedUris.Count == opened + 1 && launcher.OpenedUris[^1] == vm.PrerequisiteRemediation!.DownloadPage && launcher.Launches.Count == launches + 1,
            "Explicit download action opens only the selected official prerequisite page");
        await ImGuiDiagnostics.ClickAsync(window, "action/ClosePrerequisiteCommand");
        Check(vm.PrerequisiteCheck is null, "Prerequisite help can be dismissed");
        await ImGuiDiagnostics.ClickAsync(window, "tools-tab/0");
        vm.RawLogSearchQuery = "";
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
        Check(vm.IsEnginesVisible && !vm.IsToolsNavigationActive, "Leaving tools clears its navigation highlight without changing the selected engine");
    }
    static async Task TestActivityLog(Window window, MainWindowViewModel vm)
    {
        await ImGuiDiagnostics.ClickAsync(window, "nav/Journal");
        var task = new HubTaskSnapshot(Guid.NewGuid(), "QA", "QA journal-test/lifecycle", HubTaskStatus.Queued, null, null, DateTimeOffset.UtcNow, null, null, null);
        void Send(HubTaskSnapshot snapshot) => Call(vm, "OnTaskChanged", vm, new HubTaskChangedEventArgs(snapshot));
        Send(task);
        task = task with { Status = HubTaskStatus.Running };
        Send(task);
        for (var i = 0; i < 100; i++) Send(task with { Progress = i / 100d, Message = "QA journal-test/chunk " + i });
        Send(task with { Status = HubTaskStatus.Completed, Progress = 1, Message = "QA terminé" });
        Call(vm, "AppendRawLog", "WARN", "QA", "QA journal-test/avertissement");
        Call(vm, "AppendRawLog", "ERROR", "QA", "QA journal-test/erreur\n   at QA.Technical.Stack()");
        vm.RawLogSearchQuery = "QA journal-test/";
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(!vm.ShowFullRawLog && vm.FilteredRawLogEntries.Count == 5, "Default journal shows lifecycle, warnings and errors, not chunk progress");
        Check(vm.FilteredRawLogEntries.All(entry => !entry.DisplayText.Contains("Stack()")), "Default journal summarizes multiline errors");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-journal-important.png"));
        await ImGuiDiagnostics.ClickAsync(window, "check/ShowFullRawLog");
        Check(vm.ShowFullRawLog && vm.FilteredRawLogEntries.Count == 105, "Complete journal reveals every technical progress entry");
        Check(vm.FilteredRawLogEntries.Any(entry => entry.DisplayText.Contains("Stack()")), "Complete journal retains multiline diagnostics");
        vm.RawLogSearchQuery = "QA journal-test/chunk 99";
        Check(vm.FilteredRawLogEntries.Count == 1, "Technical log search finds an individual chunk");
        vm.RefreshRawLogCommand.Execute(null);
        Check(vm.FilteredRawLogEntries.Count == 1, "Complete log remains searchable after reloading from disk");
        vm.RawLogSearchQuery = "";
        await ImGuiDiagnostics.ClickAsync(window, "check/ShowFullRawLog");
        Check(!vm.ShowFullRawLog && !vm.FilteredRawLogEntries.Any(entry => entry.Text.Contains("QA journal-test/chunk")), "Reload preserves default filtering of technical progress");
    }
    static async Task TestVariantLabels(Window window, MainWindowViewModel vm)
    {
        var versions = new[] {
            new RemoteFabProjectVersion("fixture-ue55", [], ["UE_5.5"], ["Windows"]),
            new RemoteFabProjectVersion("fixture-ue56", [], ["UE_5.6"], ["Windows"]),
            new RemoteFabProjectVersion("quixel-raw:fixture:4096:fbx", [], [], []),
            new RemoteFabProjectVersion("quixel-tier:fixture:2", [], [], []) };
        vm.FabInstallVersions.Clear();
        foreach (var version in versions) vm.FabInstallVersions.Add(new(version, false));
        vm.OpenFabDownloadOptionsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        foreach (var version in versions)
        {
            var key = "variant-label/" + version.ArtifactId;
            var bounds = ImGuiDiagnostics.ElementBounds(window, key);
            Check(bounds.Z - bounds.X > 90 && bounds.W - bounds.Y > 20, "Download variant renders both its version/format and artifact description: " + version.ArtifactId);
        }
        await ImGuiDiagnostics.ClickAsync(window, "variant-label/fixture-ue55");
        Check(vm.FabInstallVersions[0].IsSelected && !vm.FabInstallVersions[1].IsSelected, "Clicking a variant label toggles only its own checkbox");
        await ImGuiDiagnostics.ClickAsync(window, "variant-label/quixel-raw:fixture:4096:fbx");
        Check(vm.FabInstallVersions[0].IsSelected && vm.FabInstallVersions[2].IsSelected, "Unreal and Megascans RAW variants can be selected together");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-download-variants.png"));
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseFabDownloadOptionsCommand");
        Check(!vm.IsFabDownloadOptionsVisible, "Closing variant options releases the modal");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Téléchargements");
        await ImGuiDiagnostics.ClickAsync(window, "action/ShowDownloadSettingsCommand");
        Check(vm.IsSettingsDownloadsSelected && !ImGuiDiagnostics.Elements(window).Contains("field/EngineChunkConcurrency") &&
            ImGuiDiagnostics.Elements(window).Contains("field/DownloadConcurrency"), "Downloads page exposes global queue controls without Unreal-only settings");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/project-profiles");
        await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/unreal/downloads");
        await ImGuiDiagnostics.ClickAsync(window, "field/EngineChunkConcurrency", leadingEdge: true);
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
        await ImGuiDiagnostics.TextAsync(window, "24");
        await ImGuiDiagnostics.KeyAsync(window, Key.Enter);
        Check(vm.EngineChunkConcurrency == 24, "Keyboard configuration supports more than twelve engine connections");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/marketplaces");
        await ImGuiDiagnostics.ClickAsync(window, "settings-subtab/marketplaces/Fab");
        await ImGuiDiagnostics.ClickAsync(window, "field/FabDownloadConnections", leadingEdge: true);
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
        await ImGuiDiagnostics.TextAsync(window, "20");
        await ImGuiDiagnostics.KeyAsync(window, Key.Enter);
        Check(vm.AssetDownloadConnections == 20, "Fab and Megascans connections have their own editable setting");
        vm.QuixelDownloadConnections = 20;
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/downloads");
        await ImGuiDiagnostics.ClickAsync(window, "field/DownloadConcurrency", leadingEdge: true);
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
        await ImGuiDiagnostics.TextAsync(window, "6");
        await ImGuiDiagnostics.KeyAsync(window, Key.Enter);
        vm.ResumeDownloadsOnStartup = false; // Never resume a real user's queue from an isolated fixture.
        await ImGuiDiagnostics.ClickAsync(window, "action/SaveDownloadQueueSettingsCommand");
        await ImGuiDiagnostics.FramesAsync(window, 5);
        Check(EngineBackend.ChunkConcurrency == 24 && vm.DownloadConcurrency == 6, "Saving independent download settings configures the engine backend");
        var fab = (FixtureFab)typeof(MainWindowViewModel).GetField("_directFabClient", Private)!.GetValue(vm)!;
        Check(fab.DownloadConnections == 20, "Saving download settings configures the Fab transfer client");
        var quixel = (FixtureQuixel)typeof(MainWindowViewModel).GetField("_quixelClient", Private)!.GetValue(vm)!;
        Check(quixel.DownloadConnections == 20 && vm.DownloadQueueStatus.StartsWith("Enregistré", StringComparison.Ordinal), "Saving configures Megascans as well and finishes successfully");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-download-settings.png"));
        vm.EngineChunkConcurrency = 2;
        vm.AssetDownloadConnections = 1;
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadDownloadQueueAsync", Private)!.Invoke(vm, null)!;
        Check(vm.EngineChunkConcurrency == 24 && EngineBackend.ChunkConcurrency == 24 && vm.DownloadConcurrency == 6, "Chunk concurrency is restored from saved options independently of pack concurrency");
        Check(vm.AssetDownloadConnections == 20 && fab.DownloadConnections == 20 && quixel.DownloadConnections == 20, "Asset connections are restored independently from saved settings");
        var settings = (CyEngineHub.Core.Settings.ISettingsStore)typeof(MainWindowViewModel).GetField("_settings", Private)!.GetValue(vm)!;
        await settings.SetAsync("downloads.queue.options.v1", "{\"Concurrency\":3,\"Resume\":false}");
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadDownloadQueueAsync", Private)!.Invoke(vm, null)!;
        Check(vm.EngineChunkConcurrency == 12, "Older queue settings without a chunk field retain their values and default to twelve connections");
        Check(vm.AssetDownloadConnections == 12 && fab.DownloadConnections == 12, "Old settings default Fab and Megascans connections to twelve");
        vm.EngineChunkConcurrency = 12;
        vm.ResumeDownloadsOnStartup = true;
        vm.SaveDownloadQueueSettingsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 4);
    }
    static async Task TestEngineDownloadFlow(Window window, MainWindowViewModel vm)
    {
        var scheduler = (HubTaskScheduler)typeof(MainWindowViewModel).GetField("_taskScheduler", Private)!.GetValue(vm)!;
        scheduler.SetDownloadConcurrency(1);
        var gates = new System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource>();
        var syntheticFailures = 0;
        var started = new System.Collections.Concurrent.ConcurrentDictionary<string, CancellationToken>();
        EngineBackend.DownloadHandler = async (release, path, progress, token) =>
        {
            Check(Path.GetFullPath(path).StartsWith(Output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Simulated engine writes stay inside QA output");
            started[path] = token;
            var gate = gates.GetOrAdd(path, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
            progress?.Report(new(.25, "Téléchargement simulé", 64 * 1024 * 1024, 256 * 1024 * 1024));
            await gate.Task.WaitAsync(token);
            if (path.EndsWith("Fail", StringComparison.Ordinal) && Interlocked.Increment(ref syntheticFailures) == 1)
                throw new IOException("Synthetic engine transfer failure");
            Directory.CreateDirectory(Path.Combine(path, "Engine", "Build"));
            Directory.CreateDirectory(Path.Combine(path, "Engine", "Binaries", "Win64"));
            await File.WriteAllTextAsync(Path.Combine(path, "Engine", "Build", "Build.version"), "{\"MajorVersion\":5,\"MinorVersion\":4,\"PatchVersion\":1}", token);
            await File.WriteAllTextAsync(Path.Combine(path, "Engine", "Build", "InstalledBuild.txt"), "fixture", token);
            await File.WriteAllTextAsync(Path.Combine(path, "Engine", "Binaries", "Win64", "UnrealEditor.exe"), "fixture - not executable", token);
            return new(release, path, "5.4.1", 3, 100);
        };
        async Task Wait(Func<bool> predicate)
        {
            var until = DateTime.UtcNow.AddSeconds(15);
            while (!predicate()) { if (DateTime.UtcNow > until) throw new TimeoutException("Engine QA state timed out"); await ImGuiDiagnostics.FramesAsync(window, 2); }
        }
        async Task<EngineDownloadCardViewModel> Enqueue(string name)
        {
            vm.IsEngineImporterVisible = true;
            await Wait(() => !vm.IsEpicBusy);
            vm.SelectedEpicEngineRelease = new("fixture", "UE_5.4", "5.4", "QA " + name);
            vm.EpicEngineTargetDirectory = Path.Combine(Output, "Download" + name);
            await ImGuiDiagnostics.ClickAsync(window, "command/DownloadEpicEngineCommand");
            Check(!vm.IsEngineImporterVisible && vm.IsEnginesVisible && !vm.IsEpicBusy && vm.DownloadEpicEngineCommand.CanExecute(null), "Download click closes importer and immediately releases the command");
            return vm.EpicEngineDownloads.Single(item => item.Path == vm.EpicEngineTargetDirectory);
        }
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Cartes");
        var first = await Enqueue("First");
        await Wait(() => first.StatusLabel == "En téléchargement");
        Check(first.ProgressPercent == 25 && vm.Tasks.Any(task => task.Id == first.TaskId && task.CanCancel), "Engine immediately appears in the task queue with progress and cancellation");
        Check(ImGuiDiagnostics.Elements(window).Contains("engine-download/cancel/" + first.TaskId), "Active engine card exposes its Cancel action");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engine-downloading.png"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Projets");
        Check(vm.IsProjectsVisible && first.CanCancel, "Engine transfer does not block project navigation");
        var queued = await Enqueue("Queued");
        Check(queued.StatusLabel == "En attente" && !started.ContainsKey(queued.Path), "Engine downloads respect the configured download concurrency");
        vm.DownloadEpicEngineCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(vm.EpicEngineDownloads.Count == 2, "Double submission cannot enqueue the same destination twice");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engine-queued.png"));
        await ImGuiDiagnostics.ClickAsync(window, "engine-download/cancel/" + queued.TaskId);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engine-cancel.png"));
        await Wait(() => queued.StatusLabel == "Annulé");
        Check(!started.ContainsKey(queued.Path) && first.CanCancel, "Queued engine cancels without starting or affecting the active transfer");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Liste");
        await ImGuiDiagnostics.ClickAsync(window, "engine-download/cancel/" + first.TaskId);
        await Wait(() => first.StatusLabel == "Annulé");
        Check(started[first.Path].IsCancellationRequested && !Directory.Exists(first.Path), "Engine list cancellation reaches the backend token without creating an installation");
        var third = await Enqueue("TasksCancel");
        await Wait(() => third.StatusLabel == "En téléchargement");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Téléchargements");
        await ImGuiDiagnostics.ClickAsync(window, "task/cancel/" + third.TaskId);
        await Wait(() => third.StatusLabel == "Annulé");
        Check(started[third.Path].IsCancellationRequested, "Downloads page cancels the same engine transfer");
        var success = await Enqueue("Success");
        await Wait(() => started.ContainsKey(success.Path));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Projets");
        gates[success.Path].TrySetResult();
        await Wait(() => vm.Engines.Any(item => item.Path == success.Path) && !vm.EpicEngineDownloads.Contains(success));
        Check(vm.IsProjectsVisible && vm.Engines.Single(item => item.Path == success.Path).IsInstallationAvailable, "Completion replaces the pending entry with a validated engine without navigating away");
        var failed = await Enqueue("Fail");
        await Wait(() => started.ContainsKey(failed.Path));
        gates[failed.Path].TrySetResult();
        await Wait(() => failed.StatusLabel == "Échec");
        Check(!failed.CanCancel && !vm.Engines.Any(item => item.Path == failed.Path) && failed.Message.Contains("Synthetic", StringComparison.Ordinal), "Failed download remains visible with its error, never as an installed engine");
        await ImGuiDiagnostics.ClickAsync(window, "resource-search");
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
        await ImGuiDiagnostics.TextAsync(window, "QA Fail");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Cartes");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engine-retry-card.png"));
        Check(ImGuiDiagnostics.Elements(window).Contains("engine-download/retry/" + failed.TaskId), "Failed engine cards expose Resume beside their task action");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Liste");
        Check(ImGuiDiagnostics.Elements(window).Contains("engine-download/retry/" + failed.TaskId), "Failed engine rows expose the same Resume action");
        await ImGuiDiagnostics.ClickAsync(window, "resource-search");
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
        await ImGuiDiagnostics.KeyAsync(window, Key.Back);
        await ImGuiDiagnostics.ClickAsync(window, "nav/Téléchargements");
        gates[failed.Path] = new(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.SelectedEpicEngineRelease = new("different", "UE_OTHER", "9.9", "Do not use on retry");
        vm.EpicEngineTargetDirectory = Path.Combine(Output, "DoNotUseOnRetry");
        await ImGuiDiagnostics.ClickAsync(window, "task/retry/" + failed.TaskId);
        await Wait(() => vm.EpicEngineDownloads.Any(item => item.Path == failed.Path && item.CanCancel));
        var resumed = vm.EpicEngineDownloads.Single(item => item.Path == failed.Path);
        Check(resumed.TaskId != failed.TaskId && resumed.Name == failed.Name && vm.IsTasksVisible,
            "Resume uses the original release and directory, creates one new task and preserves navigation");
        failed.RetryCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(vm.EpicEngineDownloads.Count(item => item.Path == failed.Path) == 1, "Repeated resume cannot duplicate an active destination");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engine-resumed.png"));
        gates[failed.Path].TrySetResult();
        await Wait(() => vm.Engines.Any(item => item.Path == failed.Path) && !vm.EpicEngineDownloads.Contains(resumed));
        Check(vm.IsTasksVisible, "Resumed engine completes and registers without changing the active page");
        Check(Directory.EnumerateFiles(Path.Combine(Output, "DownloadSuccess"), "*", SearchOption.AllDirectories).Sum(path => new FileInfo(path).Length) < 4096, "No real engine payload was downloaded for UI tests");
        scheduler.SetDownloadConcurrency(3);
        EngineBackend.DownloadHandler = null;
    }
    static async Task TestEngineSortingAndUpdates(Window window, MainWindowViewModel vm)
    {
        var original = vm.Engines.Single().Engine;
        var projects = vm.Projects.Select(item => item.Project).ToArray();
        var older = original with { Id = "sort9", DisplayName = "Sort 5.9", RootPath = Path.Combine(Output, "Sort9"), Version = new(5, 9, 0), Type = EngineInstallationType.Source };
        var newer = original with { Id = "sort10", DisplayName = "Sort 5.10", RootPath = Path.Combine(Output, "Sort10"), Version = new(5, 10, 0) };
        Call(vm, "ReplaceCatalog", new[] { original, newer, older }, projects);
        vm.Engines.Single(item => item.Engine.Id == original.Id).UpdateEffectiveSize("Inconnue");
        vm.Engines.Single(item => item.Engine.Id == older.Id).UpdateEffectiveSize("2 GiB");
        vm.Engines.Single(item => item.Engine.Id == newer.Id).UpdateEffectiveSize("500 MiB");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Liste");
        await ImGuiDiagnostics.ClickAsync(window, "table-header/resources/engines/1");
        Check(ImGuiDiagnostics.ElementBounds(window, "row/Sort 5.9").Y < ImGuiDiagnostics.ElementBounds(window, "row/Sort 5.10").Y, "Version column sorts numerically, 5.9 before 5.10");
        await ImGuiDiagnostics.ClickAsync(window, "table-header/resources/engines/1");
        Check(ImGuiDiagnostics.ElementBounds(window, "row/Sort 5.9").Y > ImGuiDiagnostics.ElementBounds(window, "row/Sort 5.10").Y, "Clicking a column again reverses the order");
        await ImGuiDiagnostics.ClickAsync(window, "table-header/resources/engines/3");
        Check(ImGuiDiagnostics.ElementBounds(window, "row/Sort 5.10").Y < ImGuiDiagnostics.ElementBounds(window, "row/Sort 5.9").Y &&
            ImGuiDiagnostics.ElementBounds(window, "row/Sort 5.9").Y < ImGuiDiagnostics.ElementBounds(window, "row/" + original.DisplayName).Y,
            "Size sorting compares byte values and puts unknown measurements last");
        Check(ImGuiDiagnostics.Elements(window).Contains("official-engine/" + newer.RootPath) &&
            !ImGuiDiagnostics.Elements(window).Contains("official-engine/" + older.RootPath), "Only official installed engines receive the distinct official styling");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engines-sorted.png"));
        Call(vm, "ReplaceCatalog", new[] { original }, projects);
        EngineBackend.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Call(vm, "ApplyDirectFabStatus", new DirectFabStatus(DirectFabConnectionState.Connected, "Fixture update check"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Projets");
        Check(vm.IsCheckingOfficialEngineUpdates && vm.IsProjectsVisible, "Engine revision checks do not block navigation");
        EngineBackend.Gate.SetResult(EpicEngineBuild.Parse("5.5.4-200+++UE5+Release-5.5"));
        while (vm.IsCheckingOfficialEngineUpdates) await Task.Delay(20);
        EngineBackend.Gate = null;
        Check(vm.Engines.Single().OfficialUpdateAvailable && vm.Engines.Single().OfficialUpdateLabel.Contains("5.5.4", StringComparison.Ordinal), "New official revision is indicated without installing anything");
        var checks = EngineBackend.Checks;
        await (Task)typeof(MainWindowViewModel).GetMethod("CheckOfficialEngineUpdatesAsync", Private)!.Invoke(vm, [false, null])!;
        Check(EngineBackend.Checks == checks, "Repeated automatic checks reuse recent metadata");
        EngineBackend.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        EngineBackend.Gate.SetException(new HttpRequestException("Synthetic unavailable catalog"));
        await (Task)typeof(MainWindowViewModel).GetMethod("CheckOfficialEngineUpdatesAsync", Private)!.Invoke(vm, [true, null])!;
        Check(!vm.Engines.Single().OfficialUpdateAvailable && vm.Engines.Single().OfficialUpdateLabel == "Vérification indisponible", "An unavailable catalog is never reported as up to date");
        EngineBackend.Gate = null;
        await (Task)typeof(MainWindowViewModel).GetMethod("CheckOfficialEngineUpdatesAsync", Private)!.Invoke(vm, [true, null])!;
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Cartes");
    }
    static async Task TestEngineSelection(Window window, MainWindowViewModel vm)
    {
        var original = vm.Engines.Single().Engine;
        var projects = vm.Projects.Select(item => item.Project).ToArray();
        var catalog = (IEngineCatalog)typeof(MainWindowViewModel).GetField("_engineCatalog", Private)!.GetValue(vm)!;
        var fixtures = new List<UnrealEngineInstallation> { original };
        foreach (var label in new[] { "Selection B", "Selection C" })
        {
            var path = Path.Combine(Output, label);
            var editor = Path.Combine(path, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(editor)!);
            Directory.CreateDirectory(Path.Combine(path, "Engine", "Build"));
            await File.WriteAllTextAsync(editor, "fixture - must not launch");
            await File.WriteAllTextAsync(Path.Combine(path, "Engine", "Build", "Build.version"), "{\"MajorVersion\":5,\"MinorVersion\":5,\"PatchVersion\":0}");
            fixtures.Add(original with { Id = label, DisplayName = label, RootPath = path, EditorPath = editor,
                Type = label.EndsWith('B') ? EngineInstallationType.EpicBinary : EngineInstallationType.Source });
        }
        foreach (var engine in fixtures) await catalog.UpsertAsync(engine);
        foreach (var project in projects) await ((IProjectCatalog)catalog).UpsertAsync(project);
        await catalog.UpsertAsync(original with { Id = "missing-official", RootPath = Path.Combine(Output, "Missing5.2") });
        await catalog.UpsertAsync(original with { Id = "missing-source", RootPath = Path.Combine(Output, "MissingCustom"), Type = EngineInstallationType.Source });
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadCachedCatalogAsync", Private)!.Invoke(vm, null)!;
        Check(vm.Engines.Count == 3, "Cache loading omits nonexistent official and custom engine folders");
        while (vm.IsCheckingOfficialEngineUpdates) await Task.Delay(20);
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Liste");
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection B");
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection C", modifiers: KeyModifiers.Control);
        Check(ImGuiDiagnostics.Elements(window).Count(key => key.StartsWith("engine/selected/", StringComparison.Ordinal)) == 2, "Ctrl-click selects multiple engine rows");
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection B", rightButton: true);
        Check(ImGuiDiagnostics.Elements(window).Contains("engine-context/check") &&
            ImGuiDiagnostics.Elements(window).Count(key => key.StartsWith("engine/selected/", StringComparison.Ordinal)) == 2, "Right-click preserves the existing multi-selection");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engine-context.png"));
        Check(ImGuiDiagnostics.Elements(window).Contains("engine-context/size"), "Engine context menu exposes targeted size analysis for a multi-selection");
        await ImGuiDiagnostics.ClickAsync(window, "engine-context/size");
        while (vm.IsEngineSelectionBusy) await ImGuiDiagnostics.FramesAsync(window, 2);
        Check(fixtures.Skip(1).All(engine => { var label = vm.Engines.Single(item => item.Path == engine.RootPath).EffectiveSizeLabel; return !label.Contains("non mesurée", StringComparison.Ordinal) && !label.Contains("à analyser", StringComparison.Ordinal); }), "Selected engine sizes update after the targeted background scan");
        var sizes = (StorageSnapshot)typeof(MainWindowViewModel).GetField("_currentStorageSnapshot", Private)!.GetValue(vm)!;
        Check(fixtures.Skip(1).All(engine => sizes.Entries.Any(entry => entry.Category == StorageCategory.EngineInstallation && entry.OwnerRoot == engine.RootPath && entry.SizeBytes > 0)), "Targeted scan records measurements for both selected engines");
        Check(sizes.Entries.Any(entry => entry.Category == StorageCategory.Project && entry.SizeBytes == 100) &&
            sizes.Entries.Any(entry => entry.Category == StorageCategory.MarketplaceLibrary && entry.SizeBytes == 150), "Targeted size analysis preserves unrelated project and library measurements");
        var ordered = fixtures.OrderBy(engine => ImGuiDiagnostics.ElementBounds(window, "row/" + engine.DisplayName).Y).ToArray();
        await ImGuiDiagnostics.ClickAsync(window, "row/" + ordered[0].DisplayName);
        await ImGuiDiagnostics.ClickAsync(window, "row/" + ordered[^1].DisplayName, modifiers: KeyModifiers.Shift);
        Check(ImGuiDiagnostics.Elements(window).Count(key => key.StartsWith("engine/selected/", StringComparison.Ordinal)) == 3, "Shift selects a contiguous range in displayed sort order");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Cartes");
        await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/Selection B", vertical: .75f);
        await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/Selection C", vertical: .75f, modifiers: KeyModifiers.Control);
        await ImGuiDiagnostics.ClickAsync(window, "card/body/resource/Selection B", vertical: .75f, rightButton: true);
        Check(ImGuiDiagnostics.Elements(window).Count(key => key.StartsWith("engine/selected/", StringComparison.Ordinal)) == 2 &&
            ImGuiDiagnostics.Elements(window).Contains("engine-context/remove"), "Cards support the same multi-selection and context menu");
        await ImGuiDiagnostics.EscapeAsync(window);
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Liste");
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection B");
        vm.Engines.Single(engine => engine.Engine.Id == original.Id).ApplyOfficialUpdate("Unselected sentinel", false);
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection B", rightButton: true);
        await ImGuiDiagnostics.ClickAsync(window, "engine-context/updates");
        while (vm.IsEngineSelectionBusy || vm.IsCheckingOfficialEngineUpdates) await Task.Delay(20);
        Check(vm.Engines.Single(engine => engine.Engine.Id == original.Id).OfficialUpdateLabel == "Unselected sentinel" &&
            vm.Engines.Single(engine => engine.Engine.Id == "Selection B").OfficialUpdateAvailable, "Context update check targets selected official engines only");
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection B", rightButton: true);
        await ImGuiDiagnostics.ClickAsync(window, "engine-context/check");
        while (vm.IsEngineSelectionBusy || vm.IsCheckingOfficialEngineUpdates) await Task.Delay(20);
        Check(vm.Engines.Count == 3 && vm.StatusMessage.Contains("Vérification terminée", StringComparison.Ordinal), "Selected installation check does not restore stale cached engines");
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection B");
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection C", modifiers: KeyModifiers.Control);
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection C", rightButton: true);
        await ImGuiDiagnostics.ClickAsync(window, "engine-context/remove");
        await ImGuiDiagnostics.ClickAsync(window, "engine-remove/cancel");
        Check(vm.Engines.Count == 3, "Canceling removal keeps both selected installations visible");
        await ImGuiDiagnostics.ClickAsync(window, "row/Selection C", rightButton: true);
        await ImGuiDiagnostics.ClickAsync(window, "engine-context/remove");
        await ImGuiDiagnostics.ClickAsync(window, "engine-remove/confirm");
        while (vm.IsEngineSelectionBusy) await Task.Delay(20);
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadCachedCatalogAsync", Private)!.Invoke(vm, null)!;
        Check(vm.Engines.Count == 1 && fixtures.All(engine => File.Exists(engine.EditorPath)), "Confirmed multi-removal persists without deleting any engine file");
        await ImGuiDiagnostics.ClickAsync(window, "resource-view/Cartes");
    }
    static async Task TestToolLists(Window window, MainWindowViewModel vm)
    {
        var folder = Path.Combine(Output, "FixtureExtensions");
        foreach (var id in new[] { "cyenginehub.asset-bridge", "cyenginehub.marketplace-cosmos", "cyenginehub.marketplace-megascans-fab" })
        {
            var manifest = new HubExtensionManifest
            {
                Id = id, SchemaVersion = 1, Version = "1.0", Publisher = "Fixture", Description = "Synthetic UI fixture; no executable.",
                DisplayName = id.Contains("asset-bridge", StringComparison.Ordinal) ? "Asset Bridge fixture" : id.Contains("cosmos", StringComparison.Ordinal) ? "Cosmos fixture" : "Megascans fixture",
                Kind = HubExtensionKind.Tool, ExecutionKind = HubExtensionExecutionKind.WebLink, Website = "https://example.invalid/fixture"
            };
            vm.HubExtensions.Add(new HubExtensionCardViewModel(new(manifest, folder, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null)));
        }
        await ImGuiDiagnostics.ClickAsync(window, "nav/Extensions");
        Check(ImGuiDiagnostics.Elements(window).Count(key => key.StartsWith("extension-row/", StringComparison.Ordinal)) == 3,
            "Extensions render as compact list rows, not expanded forms");
        await ImGuiDiagnostics.ClickAsync(window, "table-header/extensions-list/0");
        await ImGuiDiagnostics.ClickAsync(window, "table-header/extensions-list/0");
        Check(ImGuiDiagnostics.ElementBounds(window, "extension-row/cyenginehub.asset-bridge").Y >
            ImGuiDiagnostics.ElementBounds(window, "extension-row/cyenginehub.marketplace-cosmos").Y, "Extension columns share the same ascending and descending sorting behavior");
        await ImGuiDiagnostics.ClickAsync(window, "extension-row/cyenginehub.asset-bridge");
        Check(vm.SelectedHubExtension?.Id == "cyenginehub.asset-bridge", "Extension row selects its inspector without running a tool");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-extensions-list.png"));
        await ImGuiDiagnostics.ClickAsync(window, "extensions-search");
        await ImGuiDiagnostics.TextAsync(window, "Cosmos");
        Check(ImGuiDiagnostics.Elements(window).Contains("extension-row/cyenginehub.marketplace-cosmos") &&
              !ImGuiDiagnostics.Elements(window).Contains("extension-row/cyenginehub.asset-bridge"), "Extension search filters list rows");
        var plugin = new UnrealPluginDescriptor("fixture-plugin", "FixturePlugin", "Fixture Plugin", "Fixture", "Tools", "Fixture.uplugin", "FixturePlugins",
            PluginSourceKind.LocalLibrary, "Fixture", null, 3, 1, "1.0", "5.5", true, false, false, [], [], [], PluginHealth.Healthy, [], DateTimeOffset.UtcNow);
        Call(vm, "ReplacePluginCatalog", (object)new[] { plugin });
        vm.Projects.Single().OpenDetailsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.ClickAsync(window, "command/ShowSelectedProjectPluginsCommand");
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(ImGuiDiagnostics.Elements(window).Contains("plugin-row/FixturePlugin"), "Unreal plugins render as a compact list with an inspector");
        await ImGuiDiagnostics.ClickAsync(window, "plugin-row/FixturePlugin");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-plugins-list.png"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Paramètres");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/settings/appearance");
        Check(ImGuiDiagnostics.Elements(window).Contains("interface-density"), "Density is configured in settings, not the navigation sidebar");
    }

    static async Task TestAsyncImages(Window window, MainWindowViewModel vm)
    {
        var cacheDirectory = Path.Combine(Output, "AsyncImages");
        Directory.CreateDirectory(cacheDirectory);
        var uri = new Uri("https://images-fixture.invalid/cached-large.png");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(uri.AbsoluteUri)));
        using (var bitmap = new SkiaSharp.SKBitmap(4096, 2048))
        {
            bitmap.Erase(SkiaSharp.SKColors.SteelBlue);
            using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var file = File.Create(Path.Combine(cacheDirectory, hash + ".image"));
            encoded.SaveTo(file);
        }
        using var gate = new ManualResetEventSlim();
        var cache = new RemoteImageCache(() =>
        {
            if (Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("Image file access ran on the UI thread.");
            if (!gate.Wait(TimeSpan.FromSeconds(8))) throw new TimeoutException();
            return cacheDirectory;
        });
        var pending = cache.LoadThumbnailAsync(uri);
        try
        {
            await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
            Check(vm.IsEnginesVisible && !pending.IsCompleted, "Navigation remains responsive while cached image work is blocked on a worker");
        }
        finally { gate.Set(); }
        using var thumbnail = await pending;
        using var preview = await cache.LoadAsync(uri);
        Check(thumbnail is not null && Math.Max(thumbnail.PixelSize.Width, thumbnail.PixelSize.Height) <= 640,
            "Cached thumbnails decode asynchronously with bounded dimensions");
        Check(preview is not null && preview.PixelSize.Width > thumbnail!.PixelSize.Width && preview.PixelSize.Width <= 2048,
            "Large previews keep a separate higher-resolution decode");
        Check(ImGuiDiagnostics.ImageUploads(window) <= 2, "GPU uploads are budgeted per frame");
        vm.FabCacheDirectory = Path.Combine(Output, "FabImageCache");
        var fabImages = Path.Combine(vm.FabCacheDirectory, "Images");
        Directory.CreateDirectory(fabImages);
        File.Copy(Path.Combine(cacheDirectory, hash + ".image"), Path.Combine(fabImages, hash + ".image"));
    }
    static void CheckFullHeight(Window window, string section)
    {
        var nav = ImGuiDiagnostics.ElementBounds(window, "pane/" + section + "/navigation");
        var content = ImGuiDiagnostics.ElementBounds(window, "pane/" + section + "/content");
        Check(Math.Abs(nav.Y - content.Y) < 2 && Math.Abs(nav.W - content.W) < 2 && nav.W - nav.Y > window.Height - 140,
            section + " navigation and content occupy the same full height");
    }
    static async Task TestAssetCards(Window window, MainWindowViewModel vm)
    {
        var assets = Enumerable.Range(0, 240).Select(index => new RemoteFabAsset("card-" + index, "fab",
            index.ToString("D3") + (index % 2 == 0 ? " Asset with a deliberately very long name to verify that a title never changes card dimensions" : " Short asset"),
            "Fixture", "fab", "assetPack", null, index < 12 ? new Uri("https://images-fixture.invalid/cached-large.png") : null, ["Simulation"],
            index % 2 == 0 ? [new("fixture", [], Enumerable.Range(0, 20).Select(n => "5." + n).ToArray(), ["Windows"])] : [])).ToArray();
        typeof(MainWindowViewModel).GetField("_remoteFabCatalog", Private)!.SetValue(vm, assets);
        Call(vm, "ApplyDirectFabStatus", new DirectFabStatus(DirectFabConnectionState.Connected, "Fixture catalog"));
        vm.ShowLibraryFabCommand.Execute(null);
        Call(vm, "ShowCachedRemoteFabPreview");
        var beforeUploads = ImGuiDiagnostics.TotalImageUploads(window);
        var maximumUploads = 0;
        for (var frame = 0; frame < 20; frame++)
        {
            await ImGuiDiagnostics.FramesAsync(window, 1);
            maximumUploads = Math.Max(maximumUploads, ImGuiDiagnostics.ImageUploads(window));
        }
        Check(maximumUploads <= 2, "Cached catalog frames respect the GPU upload budget");
        Check(ImGuiDiagnostics.TotalImageUploads(window) > beforeUploads + 1, "Multiple cached thumbnails upload progressively without synchronous file decoding");
        await ImGuiDiagnostics.FramesAsync(window, 4);
        CheckFullHeight(window, "library");
        var sourceTab = ImGuiDiagnostics.ElementBounds(window, "section-tab/source/Fab");
        Check(sourceTab.W - sourceTab.Y < 40, "Library sources are single-line tabs without subtitles");
        var help = ImGuiDiagnostics.ElementBounds(window, "library-source-help");
        Check(help.Y > window.Height * .8, "Library help is anchored at the bottom of the sources pane");
        var displayMode = ImGuiDiagnostics.ElementBounds(window, "library-view/images");
        var hiddenCheck = ImGuiDiagnostics.ElementBounds(window, "check/ShowHiddenFabAssets");
        var searchField = ImGuiDiagnostics.ElementBounds(window, "field/LibrarySearchQuery");
        Check(displayMode.Y > searchField.W && (hiddenCheck.X < displayMode.X || hiddenCheck.Y < displayMode.Y), "Library display and filter groups follow search without overlapping");
        foreach (var id in new[] { "library-filters", "library-display", "library-pagination" })
            Check(ImGuiDiagnostics.Elements(window).Contains("toolbar-group/" + id), "Catalog toolbar exposes a separate bordered group: " + id);
        foreach (var property in new[] { "ShowDownloadedLibraryAssets", "ShowUndownloadedLibraryAssets", "ShowHiddenFabAssets" })
        {
            var initial = (bool)typeof(MainWindowViewModel).GetProperty(property)!.GetValue(vm)!;
            await ImGuiDiagnostics.ClickAsync(window, "library-filter/" + property);
            Check((bool)typeof(MainWindowViewModel).GetProperty(property)!.GetValue(vm)! != initial, "Catalog filter buttons toggle their bound property: " + property);
            await ImGuiDiagnostics.ClickAsync(window, "library-filter/" + property);
            Check((bool)typeof(MainWindowViewModel).GetProperty(property)!.GetValue(vm)! == initial, "Filter buttons can restore the initial state: " + property);
        }
        window.Width = 1440;
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(ImGuiDiagnostics.Elements(window).Contains("library-view/rows") && ImGuiDiagnostics.Elements(window).Contains("library-view/thumbs"),
            "All four library presentations are available from the same toolbar");
        Check(new[] { "large", "images", "rows", "thumbs" }.All(id => ImGuiDiagnostics.Elements(window).Contains("view-mode-icon/library/" + id)),
            "Marketplaces share the same four vector presentation icons as projects and engines");
        var displayFrame = ImGuiDiagnostics.ElementBounds(window, "toolbar-group/library-display");
        Check(displayFrame.Z - displayFrame.X < 210, "Icon-only marketplace presentation group stays compact");
        foreach (var compact in new[] { true, false })
        {
            vm.UseCompactInterface = compact;
            await ImGuiDiagnostics.FramesAsync(window, 3);
            var standard = ImGuiDiagnostics.ElementBounds(window, "library-filter/ShowDownloadedLibraryAssets");
            Check(new[] { "large", "images", "rows", "thumbs" }.All(id =>
            {
                var icon = ImGuiDiagnostics.ElementBounds(window, "view-mode-icon/library/" + id);
                return Math.Abs((icon.W - icon.Y) - (standard.W - standard.Y)) < 1 && Math.Abs(icon.Y - standard.Y) < 1;
            }), "Marketplace icons match adjacent button height and vertical alignment in " + (compact ? "compact" : "comfortable") + " density");
        }
        vm.UseCompactInterface = true;
        await ImGuiDiagnostics.FramesAsync(window, 3);
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-library-wide-toolbar.png"));
        window.Width = 1050;
        await ImGuiDiagnostics.FramesAsync(window, 3);
        var libraryContent = ImGuiDiagnostics.ElementBounds(window, "pane/library/content");
        var advancedKey = ImGuiDiagnostics.Elements(window).First(key => key.StartsWith("command/ToggleAdvancedLibraryFilterCommand/", StringComparison.Ordinal));
        foreach (var key in new[] { "library-view/images", "check/ShowHiddenFabAssets", "action/NextLibraryPageCommand", advancedKey })
        {
            var control = ImGuiDiagnostics.ElementBounds(window, key);
            Check(control.X >= libraryContent.X && control.Z <= libraryContent.Z, "Narrow toolbar keeps " + key + " inside its content pane");
        }
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-library-narrow-toolbar.png"));
        Check(Math.Abs(ImGuiDiagnostics.ElementBounds(window, "action/PreviousLibraryPageCommand").Y -
            ImGuiDiagnostics.ElementBounds(window, "action/NextLibraryPageCommand").Y) < 8, "Pagination arrows stay grouped on narrow windows");
        window.Width = 1280;
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(ImGuiDiagnostics.Elements(window).Contains("section-selected/source/Fab"), "The selected marketplace is visibly highlighted");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/source/Local");
        Check(vm.IsLibraryLocalSelected && ImGuiDiagnostics.Elements(window).Contains("section-selected/source/Local"), "Marketplace tabs switch content and selected styling together");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/source/Fab");
        await ImGuiDiagnostics.FramesAsync(window, 4);
        var first = ImGuiDiagnostics.ElementBounds(window, "asset/card/card-0");
        var second = ImGuiDiagnostics.ElementBounds(window, "asset/card/card-1");
        Check(Math.Abs(first.Z - first.X - (second.Z - second.X)) < 1 && Math.Abs(first.W - first.Y - (second.W - second.Y)) < 1,
            "Long titles and twenty engine versions cannot change asset card dimensions");
        Check(first.W - first.Y < 410 && first.Z - first.X > 350, "Large asset cards keep bounded fixed dimensions");
        Check(ImGuiDiagnostics.VisibleRows(window, "PagedRemoteFabRows") is > 0 and < 10, "Catalog only realizes visible card rows");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-asset-cards.png"));
        await ImGuiDiagnostics.ClickAsync(window, "card/body/asset/card-0", horizontal: .9f, vertical: .67f);
        Check(!vm.IsRemoteFabProductVisible && ImGuiDiagnostics.Elements(window).Contains("card/selected/asset/card-0"), "Asset metadata selects the entire card without opening it");
        await ImGuiDiagnostics.ClickAsync(window, "card/body/asset/card-0", clicks: 2, horizontal: .9f, vertical: .67f);
        Check(vm.IsRemoteFabProductVisible && vm.SelectedRemoteFabAsset?.AssetId == "card-0", "Asset double-click follows the same details behavior");
        Check(new[] { "download", "install", "web", "collections", "visibility" }.All(id => ImGuiDiagnostics.Elements(window).Contains("product-action/" + id)) &&
            ImGuiDiagnostics.Elements(window).Contains("product-action-group/acquisition") && ImGuiDiagnostics.Elements(window).Contains("product-action-group/organization"),
            "Product actions use explicit full-width text buttons grouped by purpose");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-product-action-panel.png"));
        await ImGuiDiagnostics.ClickAsync(window, "product-action/download");
        Check(vm.IsFabDownloadOptionsVisible, "Product download button opens variant selection instead of downloading immediately");
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseFabDownloadOptionsCommand");
        await ImGuiDiagnostics.ClickAsync(window, "product-action/install");
        Check(vm.IsFabInstallOptionsVisible, "Product install button keeps the project installation dialog");
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseFabInstallOptionsCommand");
        await ImGuiDiagnostics.ClickAsync(window, "product-action/collections");
        Check(vm.IsFabCollectionEditorVisible, "Product collections text button opens the original collection editor");
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseFabCollectionEditorCommand");
        Check(ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("command/CloseRemoteFabProductCommand", StringComparison.Ordinal)), "Product details expose an explicit Back to library action");
        Check(!ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("library-view/", StringComparison.Ordinal)), "Product details hide all catalog view controls");
        var originalHeight = window.Height;
        window.Height = 1100;
        for (var row = 0; row < 2; row++)
            vm.SelectedFabStoredDownloads.Add(new("card-0", "UE : 5.0 · UE : 5.1 · UE : 5.2 · UE : 5.3 · UE : 5.4", "fixture-stored-" + row,
                Path.Combine(Output, "Stored" + row), 1024, DateTimeOffset.UtcNow, "Fab", "ContentPack", "ZipFastest", null, 2048, 1024,
                _ => throw new InvalidOperationException("No folder launch in fixture"), _ => throw new InvalidOperationException("No deletion in fixture"),
                _ => { }));
        await ImGuiDiagnostics.FramesAsync(window, 4);
        var storedFirst = ImGuiDiagnostics.ElementBounds(window, "stored-version/fixture-stored-0");
        var storedSecond = ImGuiDiagnostics.ElementBounds(window, "stored-version/fixture-stored-1");
        Check(storedFirst.W > storedFirst.Y && storedSecond.Y > storedFirst.W,
            "Downloaded versions have separate bordered cards with visible vertical spacing");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-product-stored-versions.png"));
        await ImGuiDiagnostics.ClickAsync(window, "stored-version-action/fixture-stored-0/RequestDeleteCommand");
        Check(vm.SelectedFabStoredDownloads[0].IsDeleteConfirmationVisible && !vm.SelectedFabStoredDownloads[1].IsDeleteConfirmationVisible,
            "Downloaded-version actions target only their own row and preserve delete confirmation");
        await ImGuiDiagnostics.ClickAsync(window, "stored-version-action/fixture-stored-0/CancelDeleteCommand");
        Check(!vm.SelectedFabStoredDownloads[0].IsDeleteConfirmationVisible, "Canceling row deletion keeps both downloaded variants");
        vm.SelectedFabStoredDownloads.Clear();
        window.Height = originalHeight;
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseRemoteFabProductCommand");
        Check(!vm.IsRemoteFabProductVisible && vm.IsRemoteFabCatalogVisible && ImGuiDiagnostics.Elements(window).Contains("library-view/large"),
            "Explicit Back restores the catalog and its presentation controls");
        await ImGuiDiagnostics.ClickAsync(window, "asset/image/card-0");
        Check(!vm.IsRemoteFabProductVisible, "Single-clicking an asset image follows the same select-only rule");
        await ImGuiDiagnostics.ClickAsync(window, "asset/image/card-0", clicks: 2);
        Check(vm.IsRemoteFabProductVisible, "Double-clicking an asset image opens its details");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/source/Fab");
        window.Width = 1050;
        await ImGuiDiagnostics.FramesAsync(window, 4);
        var resized = ImGuiDiagnostics.ElementBounds(window, "asset/card/card-0");
        Check(Math.Abs(resized.Z - resized.X - (first.Z - first.X)) < 1, "Resizing reflows columns without stretching asset cards");
        window.Width = 1280;
        vm.ShowCompactLibraryCardsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 4);
        var small = ImGuiDiagnostics.ElementBounds(window, "asset/card/card-0");
        Check(small.Z - small.X < first.Z - first.X && small.W - small.Y < first.W - first.Y, "Small card mode remains fixed and compact");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-asset-small-cards.png"));
        var detailedCount = ImGuiDiagnostics.Elements(window).Count(key => key.StartsWith("asset/card/", StringComparison.Ordinal));
        await ImGuiDiagnostics.ClickAsync(window, "library-view/images");
        Check(ImGuiDiagnostics.Elements(window).Contains("view-mode-tooltip/library/images") &&
            ImGuiDiagnostics.Elements(window).Contains("library-view-selected/images"), "Marketplace icons keep descriptive tooltips and their selected state");
        var minimal = ImGuiDiagnostics.ElementBounds(window, "asset/card/card-0");
        Check(vm.IsLibraryImageOnlyCards && minimal.W - minimal.Y < 190 &&
            ImGuiDiagnostics.Elements(window).Count(key => key.StartsWith("asset/card/", StringComparison.Ordinal)) > detailedCount,
            "Image and name mode shows more fixed cards per viewport");
        Check(!ImGuiDiagnostics.Elements(window).Contains("action/DownloadCommand") &&
            !ImGuiDiagnostics.Elements(window).Contains("asset/details/card-0"), "Minimal cards have neither metadata nor action buttons");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-asset-image-name.png"));
        await ImGuiDiagnostics.ClickAsync(window, "minimal/image/asset/card-0", clicks: 2);
        Check(vm.IsRemoteFabProductVisible && vm.SelectedRemoteFabAsset?.AssetId == "card-0", "Minimal image card double-click still opens its product");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/source/Fab");
        await vm.FlushMarketplaceSettingsWritesAsync();
        typeof(MainWindowViewModel).GetField("_libraryImageOnlyCards", Private)!.SetValue(vm, false);
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadInterfaceAsync", Private)!.Invoke(vm, null)!;
        Check(vm.IsLibraryImageOnlyCards, "Image and name mode is restored from saved preferences");
        await ImGuiDiagnostics.ClickAsync(window, "library-view/rows");
        Check(vm.IsLibraryRowsMode && !vm.IsLibraryRowImages && ImGuiDiagnostics.Elements(window).Contains("library-row/card-0") &&
            !ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("library-row-image/", StringComparison.Ordinal)), "Plain rows contain metadata but no thumbnails");
        await ImGuiDiagnostics.ClickAsync(window, "table-header/library-assets/0");
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        await ImGuiDiagnostics.ClickAsync(window, "table-header/library-assets/0");
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        await ImGuiDiagnostics.FramesAsync(window, 3);
        Check(vm.SelectedLibrarySort == "Nom · Z → A" && ImGuiDiagnostics.Elements(window).Contains("library-row/card-239"), "Library header sorting covers the entire catalog, beyond the first 120-item page");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-library-rows.png"));
        await ImGuiDiagnostics.ClickAsync(window, "library-view/thumbs");
        Check(vm.IsLibraryRowImages && ImGuiDiagnostics.Elements(window).Contains("library-row-image/asset/card-239"), "Thumbnail rows add only the small image");
        await ImGuiDiagnostics.ClickAsync(window, "library-row-image/asset/card-239", clicks: 2);
        Check(vm.IsRemoteFabProductVisible && vm.SelectedRemoteFabAsset?.AssetId == "card-239", "Double-clicking a row thumbnail opens the same product detail");
        await ImGuiDiagnostics.ClickAsync(window, "section-tab/source/Fab");
        await vm.FlushMarketplaceSettingsWritesAsync();
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadInterfaceAsync", Private)!.Invoke(vm, null)!;
        Check(vm.IsLibraryRowsMode && vm.IsLibraryRowImages, "The row presentation is persisted with its thumbnail choice");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-library-thumbnail-rows.png"));
        vm.SelectedLibrarySort = "Nom · A → Z";
        vm.ShowImageOnlyLibraryCardsCommand.Execute(null);
        await ImGuiDiagnostics.ClickAsync(window, "field/LibrarySearchQuery");
        await ImGuiDiagnostics.TextAsync(window, "Short");
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        Check(vm.LibrarySearchQuery == "Short" && vm.LibraryResultsSummary.StartsWith("120 /", StringComparison.Ordinal), "Fab search receives routed keyboard text and filters the catalog");
        Check(vm.VisibleLibraryEngineFacets.Count == 0 && vm.LibraryEngineFacets.Any(facet => facet.ResultCount == 0), "Facets at zero are hidden without deleting their selection state");
        await ImGuiDiagnostics.KeyAsync(window, Key.A, KeyModifiers.Control);
        await ImGuiDiagnostics.KeyAsync(window, Key.Back);
        await (Task)typeof(MainWindowViewModel).GetMethod("RefreshRemoteFabViewNowAsync", Private)!.Invoke(vm, null)!;
        Check(vm.LibrarySearchQuery.Length == 0 && vm.VisibleLibraryEngineFacets.Count > 0 &&
            vm.VisibleLibraryEngineFacets.All(facet => facet.HasResults) && vm.VisibleLibraryCategoryFacets.All(facet => facet.HasResults),
            "Clearing search restores useful facets automatically");
        vm.ShowCompactLibraryCardsCommand.Execute(null);
        await ImGuiDiagnostics.FramesAsync(window, 3);
        ImGuiDiagnostics.ScrollListToEnd(window, "PagedRemoteFabRows");
        await ImGuiDiagnostics.FramesAsync(window, 4);
        Check(ImGuiDiagnostics.Elements(window).Contains("asset/card/card-119"), "Fixed card catalog scrolls to the last asset");
        vm.ShowLargeLibraryCardsCommand.Execute(null);
    }
    static async Task Capture(Window window, string name)
    {
        await Task.Delay(150);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(Output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
