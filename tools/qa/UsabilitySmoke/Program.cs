using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
using CyEngineHub.Infrastructure.AppData;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Projects.Models;
using CyEngineHub.Storage.Models;
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
    public UnrealEngineRegistration? FindByRoot(string path) => null;
    public UnrealEngineRegistration Register(string path) => throw new NotSupportedException();
    public bool Unregister(string path) => throw new NotSupportedException();
}
sealed class RecordingLauncher : IPlatformLauncher
{
    public List<(string Executable, IReadOnlyList<string> Arguments)> Launches { get; } = [];
    public Task<int?> StartDetachedAsync(string fileName, IReadOnlyList<string>? arguments = null, string? workingDirectory = null, CancellationToken cancellationToken = default)
    { Launches.Add((fileName, arguments ?? [])); return Task.FromResult<int?>(123); }
    public Task OpenFolderAsync(string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task OpenUriAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string Output = Path.GetFullPath("artifacts/usability-qa-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
    static void Call(object vm, string method, params object[] args) => vm.GetType().GetMethod(method, Private)!.Invoke(vm, args);
    static void Check(bool result, string message) { if (!result) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }
    [STAThread] static void Main(string[] args)
    {
        Directory.CreateDirectory(Output);
        AppBuilder.Configure<FixtureApp>().UsePlatformDetect().SetupWithoutStarting();
        using var stop = new CancellationTokenSource(args.Contains("--interactive") ? TimeSpan.FromMinutes(20) : TimeSpan.FromSeconds(60));
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
            "appData" => paths, "platformLauncher" => launcher, _ => null,
        }).ToArray());
        var engineRoot = Path.Combine(Output, "UE_5.5");
        var editor = Path.Combine(engineRoot, "Engine", "Binaries", "Win64", "UnrealEditor.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(editor)!);
        await File.WriteAllTextAsync(editor, "fixture - not executable");
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
             new(ToolchainKind.LinuxToolchain, "Linux", ToolchainHealth.Missing, "fixture", "absent", "SDK absent")], [])], [], [], [], DateTimeOffset.UtcNow));
        var window = new MainWindow { DataContext = vm, Title = "CyEngineHub — Interface QA (données fictives)", Width = 1280, Height = 820,
            ShowActivated = false, ShowInTaskbar = false, Position = new PixelPoint(-3000, -3000) };
        window.Show();
        if (ImGuiDiagnostics.IsHosted(window))
        {
            await TestImGui(window, vm, launcher);
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
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-home.png"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
        Check(vm.IsEnginesVisible, "ImGui pointer input activates engine navigation");
        await ImGuiDiagnostics.ClickAsync(window, "row/Unreal 5.5 · QA");
        await ImGuiDiagnostics.ClickAsync(window, "action/LaunchCommand");
        Check(launcher.Launches.Count == 1 && launcher.Launches[0].Arguments.SequenceEqual(["-norecentproject"]), "ImGui engine launch uses real command binding");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-engines.png"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Projets");
        Check(vm.IsProjectsVisible, "ImGui pointer input activates project navigation");
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
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-settings.png"));
        await ImGuiDiagnostics.ClickAsync(window, "nav/Vue d’ensemble");
        Check(vm.IsHomeVisible, "ImGui navigation remains functional after settings and tabs");
        await ImGuiDiagnostics.ClickAsync(window, "action/ToggleProjectCreatorCommand");
        Check(vm.IsProjectCreatorVisible, "Project creation modal opens through ImGui button");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs", leadingEdge: true);
        Check(vm.IsProjectCreatorVisible && vm.IsHomeVisible, "Modal blocks clicks on the uncovered backdrop navigation");
        await ImGuiDiagnostics.ClickAsync(window, "field/NewProjectName");
        await ImGuiDiagnostics.TextAsync(window, "ImGuiFixtureProject");
        if (vm.NewProjectName != "ImGuiFixtureProject")
        {
            Console.WriteLine("PROJECT NAME " + vm.NewProjectName);
            await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-input-debug.png"));
        }
        Check(vm.NewProjectName == "ImGuiFixtureProject", "Project form text input updates the creation model");
        await ImGuiDiagnostics.ClickAsync(window, "tab/Configuration INI");
        Check(vm.ProjectCreatorTabIndex == 3, "Project creation tabs update the INI editor selection");
        await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, "imgui-project-modal.png"));
        await ImGuiDiagnostics.ClickAsync(window, "command/CloseProjectCreatorCommand");
        Check(!vm.IsProjectCreatorVisible, "Project modal close button executes its command");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Paramètres");
        Check(vm.IsSettingsVisible, "Closed ImGui modal releases navigation input");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Téléchargements");
        Check(vm.IsTasksVisible, "Download queue remains accessible through ImGui navigation");
        await ImGuiDiagnostics.ClickAsync(window, "nav/Moteurs");
        await ImGuiDiagnostics.ClickAsync(window, "action/ToggleEngineImporterCommand");
        Check(vm.IsEngineImporterVisible, "Add engine action exposes the complete engine importer");
        Console.WriteLine("OUTPUT " + Output);
    }
    static async Task Capture(Window window, string name)
    {
        await Task.Delay(150);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(Output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
