using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CyEngineHub.Core.Tasks;
using CyEngineHub.Desktop.ViewModels;
using CyEngineHub.Desktop.Views;
using CyEngineHub.Desktop.ImGui;
using CyEngineHub.Engines;
using CyEngineHub.Fab.Remote;
using CyEngineHub.Infrastructure.AppData;
using CyEngineHub.Infrastructure.Persistence;
using CyEngineHub.Unreal.Integration;
using CyEngineHub.Unreal.Registration;
using Microsoft.Web.WebView2.Core;

// Windows smoke test: isolated database/profile, all web responses and tokens are fixtures.
// No account, real credentials, user browser profile, external navigation or download is used.
sealed class PreviewRegistration : IUnrealEngineRegistrationService
{
    public UnrealEngineRegistration? FindByRoot(string path) => null;
    public UnrealEngineRegistration Register(string path) => throw new NotSupportedException();
    public bool Unregister(string path) => throw new NotSupportedException();
}

sealed class PreviewApp : Application
{
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
}

sealed class FakeFab : IDirectFabClient
{
    public const string Code = "only-a-fake-authorization-code";
    public static readonly Uri Callback = new("https://www.epicgames.com/id/api/redirect?clientId=fixture&responseType=code");
    public Uri AuthorizationUri { get; } = new("https://www.epicgames.com/id/login?redirectUrl=" + Uri.EscapeDataString(Callback.AbsoluteUri));
    public int ConnectCount;
    public int EstimateCount;
    public bool UnknownEstimate;
    public Task<DirectFabDownloadEstimate?> EstimateDownloadAsync(RemoteFabAsset asset, RemoteFabProjectVersion version, CancellationToken cancellationToken = default)
    {
        EstimateCount++;
        return Task.FromResult(UnknownEstimate ? null : new DirectFabDownloadEstimate(1024, 2048, 2));
    }
    public bool Fail;
    public string? Received;
    public Task<DirectFabStatus> GetStatusAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DirectFabStatus(DirectFabConnectionState.Disconnected, "Fixture"));
    public Task<DirectFabStatus> ConnectAsync(string code, CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        Received = code;
        if (Fail) throw new InvalidOperationException("Do not expose: " + Code);
        return Task.FromResult(new DirectFabStatus(DirectFabConnectionState.Connected, "Fixture connected", "fixture", "Demo"));
    }
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<IReadOnlyList<RemoteFabAsset>> GetLibraryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RemoteFabAsset>>([]);
    public Task<RemoteFabProductDetails> GetProductDetailsAsync(RemoteFabAsset asset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DirectFabDownloadResult> DownloadAsync(RemoteFabAsset asset, string root, IProgress<DirectFabDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<DirectFabDownloadResult> DownloadAsync(RemoteFabAsset asset, RemoteFabProjectVersion version, string root, IProgress<DirectFabDownloadProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

static class Program
{
    static readonly string Output = Path.GetFullPath("artifacts/fab-login-qa-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T? Field<T>(object value, string field) => (T?)value.GetType().GetField(field, Private)!.GetValue(value);
    static Task Call(object vm, string method) => (Task)vm.GetType().GetMethod(method, Private)!.Invoke(vm, null)!;
    static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException(message); Console.WriteLine("PASS " + message); }

    [STAThread]
    static void Main()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows/WebView2 smoke test");
        Directory.CreateDirectory(Output);
        AppBuilder.Configure<PreviewApp>().UsePlatformDetect().SetupWithoutStarting();
        using var stop = new CancellationTokenSource();
        Dispatcher.UIThread.Post(async () =>
        {
            try { await Run(); }
            catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
            finally { stop.Cancel(); }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
    }

    static async Task Run()
    {
        var paths = new AppDataPaths(Output, Path.Combine(Output, "test.db"), Path.Combine(Output, "Logs"), true);
        var store = new SqliteCatalogStore(paths.DatabasePath);
        await store.InitializeAsync();
        await using var scheduler = new HubTaskScheduler(1);
        var fake = new FakeFab();
        var quixel = new FakeQuixel();
        var ctor = typeof(MainWindowViewModel).GetConstructors().Single();
        MainWindowViewModel MakeVm() => (MainWindowViewModel)ctor.Invoke(ctor.GetParameters().Select(p => p.Name switch
        {
            "database" or "settings" or "engineCatalog" => (object)store,
            "engineModules" => new EngineModuleRegistry([new UnrealEngineModule()]),
            "engineRegistration" => new PreviewRegistration(), "taskScheduler" => scheduler,
            "appData" => paths, "directFabClient" => fake, "quixelClient" => quixel, _ => null
        }).ToArray());

        var vm = MakeVm();
        await Call(vm, "OpenEmbeddedFabAuthorizationAsync");
        Check(vm.IsFabManualSignInExpanded && fake.ConnectCount == 0, "Missing dialog falls back to manual");
        vm.IsFabManualSignInExpanded = false;
        FabWebAuthorizationRequestEventArgs? pending = null;
        vm.FabWebAuthorizationRequested += (_, request) => pending = request;
        var login = Call(vm, "OpenEmbeddedFabAuthorizationAsync");
        Check(vm.IsFabWebLoginActive && !vm.OpenEmbeddedFabAuthorizationCommand.CanExecute(null), "Busy guard prevents another login");
        Check(pending!.ProfileDirectory.StartsWith(Output, StringComparison.OrdinalIgnoreCase), "Dedicated browser profile under app data");
        Check(!await vm.TryConnectDirectFabFromClipboardAsync(FakeFab.Code), "Old clipboard capture cannot compete with embedded login");
        pending.Complete(new FabWebAuthorizationResult { UseManualMode = true });
        await login;
        Check(vm.IsFabManualSignInExpanded && !vm.IsFabWebLoginActive && !vm.IsEpicBusy && fake.ConnectCount == 0, "Manual fallback releases busy state without exchanging a token");
        vm.IsFabManualSignInExpanded = false;
        login = Call(vm, "OpenEmbeddedFabAuthorizationAsync");
        pending!.Complete(new FabWebAuthorizationResult());
        await login;
        Check(!vm.IsFabWebLoginActive && !vm.IsFabManualSignInExpanded && fake.ConnectCount == 0, "Cancel has no authentication side effects");
        fake.Fail = true;
        login = Call(vm, "OpenEmbeddedFabAuthorizationAsync");
        pending!.Complete(new FabWebAuthorizationResult { Code = FakeFab.Code });
        await login;
        Check(vm.IsFabManualSignInExpanded && !vm.IsEpicBusy && !vm.IsFabWebLoginActive, "Failed exchange leaves manual mode usable");
        Check(!vm.DirectFabStatusSummary.Contains(FakeFab.Code) && !vm.StatusMessage.Contains(FakeFab.Code), "Authentication exception data is not exposed");
        fake.Fail = false;
        login = Call(vm, "OpenEmbeddedFabAuthorizationAsync");
        pending!.Complete(new FabWebAuthorizationResult { Code = FakeFab.Code });
        await login;
        Check(vm.IsDirectFabConnected && fake.Received == FakeFab.Code && vm.FabAuthorizationCode == "", "Automatic exchange does not fill manual textbox");
        Check(!vm.IsFabWebLoginActive && !vm.IsEpicBusy, "Successful exchange releases busy state");
        var quixelLogin = Call(vm, "OpenEmbeddedQuixelAuthorizationAsync");
        Check(pending!.ProviderDisplayName.Contains("Quixel") && pending.ProfileDirectory.EndsWith("Quixel"), "Quixel uses its own provider label and browser profile");
        Check(!vm.ConnectDirectFabCommand.CanExecute(null), "Manual Fab cannot interrupt Quixel login");
        pending.Complete(new FabWebAuthorizationResult { UseManualMode = true });
        await quixelLogin;
        Check(vm.IsQuixelManualSignInExpanded && !vm.IsFabWebLoginActive, "Quixel manual fallback releases shared auth guard");
        quixel.Fail = true;
        quixelLogin = Call(vm, "OpenEmbeddedQuixelAuthorizationAsync");
        pending!.Complete(new FabWebAuthorizationResult { Code = FakeFab.Code });
        await quixelLogin;
        Check(!vm.QuixelStatusSummary.Contains(FakeFab.Code) && !vm.IsEpicBusy, "Quixel failed exchange is redacted and releases busy state");
        quixel.Fail = false;
        quixelLogin = Call(vm, "OpenEmbeddedQuixelAuthorizationAsync");
        pending!.Complete(new FabWebAuthorizationResult { Code = FakeFab.Code });
        await quixelLogin;
        Check(vm.IsQuixelConnected && quixel.Received == FakeFab.Code && vm.IsDirectFabConnected, "Quixel login preserves separate existing Fab session");
        var variant = new RemoteFabProjectVersion("fixture-variant", [], ["5.5"], ["Windows"]);
        var asset = new RemoteFabAsset("fixture-asset", "fixture", "Fixture pack", "", "Fab", "ASSET_PACK", null, null, [], [variant]);
        typeof(MainWindowViewModel).GetField("_selectedRemoteFabAsset", Private)!.SetValue(vm, asset);
        vm.FabInstallVersions.Add(new FabInstallVersionOptionViewModel(variant, true));
        await Call(vm, "EstimatePackDownloadsAsync");
        Check(vm.PackDownloadEstimateSummary.Contains("1 KB") && vm.PackDownloadEstimateSummary.Contains("2 KB"), "Pack preflight shows transfer and uncompressed sizes");
        await Call(vm, "EstimatePackDownloadsAsync");
        Check(fake.EstimateCount == 1, "Repeated preflight reuses cached manifest estimate");
        Check(vm.Engines.Count == 0 && vm.DownloadSelectedFabVersionsCommand.CanExecute(null), "Download is enabled without an installed engine");
        vm.FabInstallVersions.Clear();
        await Call(vm, "EstimatePackDownloadsAsync");
        Check(vm.PackDownloadEstimateSummary.Contains("Sélectionnez"), "Empty selection provides an actionable preflight message");
        fake.UnknownEstimate = true;
        vm.FabInstallVersions.Add(new FabInstallVersionOptionViewModel(variant with { ArtifactId = "fixture-unknown" }, true));
        await Call(vm, "EstimatePackDownloadsAsync");
        Check(vm.PackDownloadEstimateSummary.Contains("inconnue") && vm.PackDownloadEstimateSummary.Contains("incomplet"), "Unknown sizes are not presented as a complete zero-byte estimate");

        var manualVm = MakeVm();
        manualVm.FabAuthorizationCode = FakeFab.Code;
        await Call(manualVm, "ConnectDirectFabAsync");
        Check(manualVm.IsDirectFabConnected && manualVm.FabAuthorizationCode == "", "Existing manual code connection still works");

        var preview = new MainWindow { DataContext = MakeVm(), Width = 1380, Height = 870, ShowInTaskbar = false,
            ShowActivated = false, Position = new PixelPoint(-3000, -3000) };
        preview.Show();
        var previewVm = (MainWindowViewModel)preview.DataContext!;
        previewVm.ShowFabConnectionSettingsCommand.Execute(null);
        await ScrollToSignIn(preview);
        await Capture(preview, "fab-settings");
        previewVm.IsFabManualSignInExpanded = true;
        await ScrollToSignIn(preview);
        await Capture(preview, "fab-manual");
        previewVm.ShowEpicConnectionSettingsCommand.Execute(null);
        await ScrollToSignIn(preview);
        await Capture(preview, "epic-manual");

        await BrowserSmoke(preview, fake.AuthorizationUri, false);
        await BrowserSmoke(preview, fake.AuthorizationUri, true);
        preview.Close();
        Console.WriteLine("OUTPUT " + Output);
    }

    static async Task BrowserSmoke(Window owner, Uri authorizationUri, bool cancel)
    {
        var request = new FabWebAuthorizationRequestEventArgs(authorizationUri, Path.Combine(Output, cancel ? "cancel-webview" : "success-webview"));
        var window = new FabAuthorizationWindow(request) { ShowInTaskbar = false, ShowActivated = false };
        var hooked = false;
        var responseCount = 0;
        var reachedPage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EpicAuthorizationWebView? browser = null;
        CoreWebView2Controller? controller = null;
        window.Opened += (_, _) =>
        {
            browser = Field<EpicAuthorizationWebView>(window, "_browser")!;
            browser.StatusChanged += (_, message) =>
            {
                Console.WriteLine("WEB " + message);
                controller = Field<CoreWebView2Controller>(browser, "_controller");
                if (hooked || controller is null) return;
                hooked = true;
                var web = controller.CoreWebView2;
                Check(web.Profile.IsInPrivateModeEnabled && !web.Settings.AreHostObjectsAllowed && !web.Settings.IsWebMessageEnabled,
                    "Real WebView2 is private, without a native page bridge");
                web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
                web.WebResourceRequested += (_, args) =>
                {
                    responseCount++;
                    var callback = args.Request.Uri == FakeFab.Callback.AbsoluteUri;
                    var payload = callback ? "{\"authorizationCode\":\"" + FakeFab.Code + "\"}" : "<html><body>Offline Epic login fixture</body></html>";
                    args.Response = web.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(payload)),
                        200, "OK", callback ? "Content-Type: application/json" : "Content-Type: text/html");
                };
                web.NavigationCompleted += (_, args) =>
                {
                    if (!args.IsSuccess) return;
                    reachedPage.TrySetResult();
                    if (!cancel && web.Source.Contains("/id/login", StringComparison.Ordinal))
                        Dispatcher.UIThread.Post(() => web.Navigate(FakeFab.Callback.AbsoluteUri));
                };
            };
        };
        var resultTask = window.ShowDialog<FabWebAuthorizationResult?>(owner);
        try
        {
            await reachedPage.Task.WaitAsync(TimeSpan.FromSeconds(25));
            Check(controller!.Bounds.Width > 400 && controller.Bounds.Height > 200, "Native browser has a visible content area");
            if (cancel)
            {
                window.Width = 760;
                window.Height = 580;
                await Task.Delay(300);
                Check(controller.Bounds.Width < 800 && controller.Bounds.Height > 100, "Native browser follows window resize");
                // Simulate an unavailable runtime/browser, then use the actual manual button.
                typeof(FabAuthorizationWindow).GetMethod("ShowFailure", Private)!.Invoke(window, ["Fixture: navigateur indisponible"]);
                if (ImGuiDiagnostics.IsHosted(window))
                    await ImGuiDiagnostics.ClickAsync(window, "native/Mode manuel");
                else
                {
                    var manualButton = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Mode manuel"));
                    Check(manualButton.IsEffectivelyEnabled, "Manual button stays available after browser failure");
                    manualButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
            }
            var result = await resultTask.WaitAsync(TimeSpan.FromSeconds(25));
            Check(cancel ? result?.UseManualMode == true : result?.Code == FakeFab.Code,
                cancel ? "Dialog returns manual fallback" : "Real WebView2 automatically captures fixture JSON and closes dialog");
            Check(hooked && responseCount > 0 && Field<CoreWebView2Controller>(browser!, "_controller") is null,
                "Native browser is disposed after dialog closes");
        }
        finally { if (window.IsVisible) window.Close(new FabWebAuthorizationResult()); }
    }

    static async Task ScrollToSignIn(Window window)
    {
        if (ImGuiDiagnostics.IsHosted(window)) { await ImGuiDiagnostics.FramesAsync(window); return; }
        await Task.Delay(150);
        window.GetVisualDescendants().OfType<FabSignInPanel>().Single(panel => panel.IsEffectivelyVisible).BringIntoView();
        await Task.Delay(150);
    }

    static async Task Capture(Window window, string name)
    {
        if (ImGuiDiagnostics.IsHosted(window)) { await ImGuiDiagnostics.CaptureAsync(window, Path.Combine(Output, name + ".png")); return; }
        await Task.Delay(250);
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(Output, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
