using System.IO.Compression;
using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Desktop.Views;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Engines;
using CyEngineHub.Extensions;
using CyEngineHub.Infrastructure.AppData;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Unreal.Integration;
using CyEngineHub.Unreal.Registration;
using Microsoft.Web.WebView2.Core;

sealed class TestApp : Application
{
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
}
sealed class Registration : IUnrealEngineRegistrationService
{
    public UnrealEngineRegistration? FindByRoot(string path) => null;
    public UnrealEngineRegistration Register(string path) => throw new NotSupportedException();
    public bool Unregister(string path) => throw new NotSupportedException();
}
static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly string Output = Path.GetFullPath("artifacts/cosmos-smoke-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
    static Task Call(object vm, string method, params object?[] args)
    {
        var info = vm.GetType().GetMethod(method, Private)!;
        var parameters = info.GetParameters();
        return (Task)info.Invoke(vm, parameters.Select((p, i) => i < args.Length ? args[i] : p.DefaultValue).ToArray())!;
    }
    static T Field<T>(object vm, string name) => (T)vm.GetType().GetField(name, Private)!.GetValue(vm)!;
    static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }

    [STAThread] static void Main()
    {
        Directory.CreateDirectory(Output);
        AppBuilder.Configure<TestApp>().UsePlatformDetect().SetupWithoutStarting();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(75));
        var finished = false;
        Dispatcher.UIThread.UnhandledException += (_, e) => { Console.WriteLine("UNHANDLED " + e.Exception); e.Handled = true; Environment.ExitCode = 1; stop.Cancel(); };
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Run(); }
            catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
            finally { finished = true; stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
        if (!finished) { Console.WriteLine("FAIL Smoke test aborted or timed out"); Environment.ExitCode = 1; }
    }

    static async Task Run()
    {
        // All data and WebView responses are isolated fixtures: no user DB or accounts.
        var paths = new AppDataPaths(Output, Path.Combine(Output, "qa.db"), Path.Combine(Output, "Logs"), true);
        var store = new SqliteCatalogStore(paths.DatabasePath);
        await store.InitializeAsync();
        await using var scheduler = new HubTaskScheduler(1);
        var ctor = typeof(MainWindowViewModel).GetConstructors().Single();
        var vm = (MainWindowViewModel)ctor.Invoke(ctor.GetParameters().Select(p => p.Name switch
        {
            "database" or "settings" or "engineCatalog" => (object)store,
            "engineModules" => new EngineModuleRegistry([new UnrealEngineModule()]),
            "engineRegistration" => new Registration(), "taskScheduler" => scheduler, "appData" => paths, _ => null
        }).ToArray());
        var manifest = await new HubExtensionManifestParser().ParseAsync(Path.GetFullPath("extensions/builtin/cyenginehub.marketplace-cosmos"));
        await Call(vm, "RebuildMarketplaceConnectorsAsync", (object)new[] { new HubExtensionSnapshot(manifest, Output, HubExtensionOrigin.BuiltIn, HubExtensionHealth.Ready, null) });
        var window = new MainWindow { DataContext = vm, Width = 1380, Height = 860, ShowInTaskbar = false, ShowActivated = false, Position = new PixelPoint(-3000, -3000) };
        window.Show();
        try
        {
            await Call(vm, "ShowConnectorLibraryAsync", manifest.Id, null);
            await Task.Delay(250);
            Check(vm.IsConnectorLibrarySelected && vm.SelectedConnectorLibrary!.IsEmpty, "Empty native Cosmos library renders");
            var file = Path.Combine(Output, "FixturePack.zip");
            using (var zip = ZipFile.Open(file, ZipArchiveMode.Create)) zip.CreateEntry("Content/Mesh.uasset");
            await Call(vm, "RegisterConnectorDownloadAsync", manifest.Id, file);
            await Call(vm, "ShowConnectorLibraryAsync", manifest.Id, file);
            await Task.Delay(250);
            Check(vm.SelectedConnectorLibrary!.IsProductVisible, "Imported file opens native product detail");
            if (ImGuiDiagnostics.IsHosted(window))
            {
                await ImGuiDiagnostics.FramesAsync(window);
                Check(ImGuiDiagnostics.Elements(window).Contains("section-selected/source/" + manifest.Id), "Connector source has the same selected-tab indicator");
                Check(!ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("library-view/", StringComparison.Ordinal)), "Connector product hides catalog presentation settings");
                await ImGuiDiagnostics.ClickAsync(window, "command/BackCommand");
                Check(vm.SelectedConnectorLibrary.IsListVisible, "Explicit product Back returns to the connector catalog");
            }
            else vm.SelectedConnectorLibrary.BackCommand.Execute(null);
            await Task.Delay(100);
            if (ImGuiDiagnostics.IsHosted(window))
            {
                await ImGuiDiagnostics.FramesAsync(window);
                Check(ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("list/Assets/", StringComparison.Ordinal)), "Downloaded file rendered in ImGui list");
                await ImGuiDiagnostics.ClickAsync(window, "library-view/images");
                Check(ImGuiDiagnostics.Elements(window).Contains("minimal/card/Assets/0"), "Local connector catalogs support image and name cards");
                await ImGuiDiagnostics.ClickAsync(window, "minimal/image/Assets/0", clicks: 2);
                Check(vm.SelectedConnectorLibrary.IsProductVisible, "Connector minimal card double-click opens its native product");
                await ImGuiDiagnostics.ClickAsync(window, "section-tab/source/" + manifest.Id);
                await ImGuiDiagnostics.ClickAsync(window, "library-view/rows");
                Check(ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("library-local-row/", StringComparison.Ordinal)), "Connector catalogs support sortable text rows");
                await ImGuiDiagnostics.ClickAsync(window, "library-view/thumbs");
                Check(ImGuiDiagnostics.Elements(window).Any(key => key.StartsWith("library-row-image/Assets/", StringComparison.Ordinal)), "Connector catalogs support thumbnail rows");
                await ImGuiDiagnostics.ClickAsync(window, "library-view/large");
            }
            else
            {
                var panel = window.GetVisualDescendants().OfType<ConnectorLibraryPanel>().Single();
                Check(panel.IsEffectivelyVisible && panel.GetVisualDescendants().OfType<ListBoxItem>().Count() == 1, "Downloaded file rendered in real ListBox");
            }
            vm.ShowLibraryFabCommand.Execute(null);
            await Call(vm, "ShowConnectorLibraryAsync", manifest.Id, null);
            await Task.Delay(100);
            Check(vm.SelectedConnectorLibrary.IsListVisible, "Return to Cosmos after Fab");
            await BrowserSmoke(vm, window, manifest.Id);
        }
        finally { window.Close(); }
        Console.WriteLine("OUTPUT " + Output);
    }

    static async Task BrowserSmoke(MainWindowViewModel vm, Window owner, string id)
    {
        var fixture = new Uri("https://cosmos-fixture.invalid/inventory");
        var rejectDownload = false;
        var request = new MarketplaceBrowserRequestEventArgs(id, "Cosmos fixture", fixture, Path.Combine(Output, "webview"),
            () => rejectDownload ? throw new IOException("Fixture unavailable destination") : Path.Combine(Output, "downloads"), () => Task.CompletedTask, () => Task.CompletedTask,
            path => Call(vm, "RegisterConnectorDownloadAsync", id, path), path => Call(vm, "ShowConnectorLibraryAsync", id, path));
        var browserWindow = new MarketplaceBrowserWindow(request) { ShowInTaskbar = false, ShowActivated = false };
        var browser = Field<MarketplaceWebView>(browserWindow, "_browser");
        var hooked = false;
        var loaded = new TaskCompletionSource();
        var downloaded = new TaskCompletionSource<string>();
        var canceled = new TaskCompletionSource();
        using var archiveBytes = new MemoryStream();
        using (var zip = new ZipArchive(archiveBytes, ZipArchiveMode.Create, true)) zip.CreateEntry("Content/Downloaded.uasset");
        browser.LocalDownloadCompleted += (_, path) => downloaded.TrySetResult(path);
        browser.StatusChanged += (_, message) =>
        {
            Console.WriteLine("WEB " + message);
            if (message.StartsWith("Téléchargement annulé", StringComparison.Ordinal)) canceled.TrySetResult();
            var controller = Field<CoreWebView2Controller?>(browser, "_controller");
            if (hooked || controller is null) return;
            hooked = true;
            var web = controller.CoreWebView2;
            web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
            web.WebResourceRequested += (_, args) => args.Response = args.Request.Uri.EndsWith("/download", StringComparison.Ordinal)
                ? web.Environment.CreateWebResourceResponse(new MemoryStream(archiveBytes.ToArray()), 200, "OK",
                    "Content-Type: application/zip\r\nContent-Disposition: attachment; filename=DownloadedPack.zip")
                : web.Environment.CreateWebResourceResponse(
                    new MemoryStream(Encoding.UTF8.GetBytes("<html><body>Offline Cosmos download fixture</body></html>")), 200, "OK", "Content-Type: text/html");
            web.NavigationCompleted += (_, args) => { if (args.IsSuccess) loaded.TrySetResult(); };
        };
        browserWindow.Show(owner);
        try
        {
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(25));
            Check(hooked, "Native browser initialized with offline website");
            browser.Navigate(new Uri(fixture, "/download"));
            var path = await downloaded.Task.WaitAsync(TimeSpan.FromSeconds(25));
            Check(File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(archiveBytes.ToArray()), "Real WebView download completed, bytes preserved");
            Check(Path.GetDirectoryName(Path.GetDirectoryName(path)) == Path.Combine(Output, "downloads"), "Browser download uses configured root");
            Check(vm.SelectedConnectorLibrary!.Assets.Count == 2, "Completed WebView download creates native record");
            await Task.Delay(150);
            if (ImGuiDiagnostics.IsHosted(browserWindow)) await ImGuiDiagnostics.FramesAsync(browserWindow);
            var stats = ImGuiDiagnostics.IsHosted(browserWindow) ? ImGuiDiagnostics.BrowserText(browserWindow)
                : string.Join("\n", browserWindow.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
            Check(stats.Contains("%") && stats.Contains("Vitesse :") && stats.Contains("Restant estimé :") && stats.Contains("Écoulé :"),
                "Native browser renders percentage, size, speed, ETA and elapsed time");
            if (ImGuiDiagnostics.IsHosted(browserWindow)) await ImGuiDiagnostics.ClickAsync(browserWindow, "native/Voir la fiche du dernier téléchargement");
            else
            {
                var detail = browserWindow.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Voir la fiche du dernier téléchargement"));
                Check(detail.IsEnabled, "Completed download enables native detail button"); detail.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await Task.Delay(500);
            Check(vm.SelectedConnectorLibrary.SelectedAsset?.Asset.FilePath == path, "Browser button opens downloaded asset detail");
            if (ImGuiDiagnostics.IsHosted(browserWindow)) await ImGuiDiagnostics.ClickAsync(browserWindow, "native/Mes assets téléchargés");
            else
            {
                var back = browserWindow.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Mes assets téléchargés"));
                back.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await Task.Delay(500);
            Check(vm.IsConnectorLibrarySelected, "Browser toolbar returns to native library");
            rejectDownload = true;
            browser.Navigate(new Uri(fixture, "/download"));
            await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(250);
            Check(browser.ActiveDownloads == 0 && vm.SelectedConnectorLibrary.Assets.Count == 2, "Unavailable destination cancels without crash or phantom asset");
        }
        finally { browserWindow.ForceClose(); }
    }
}
