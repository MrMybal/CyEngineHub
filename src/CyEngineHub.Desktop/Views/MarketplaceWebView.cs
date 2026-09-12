using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Web.WebView2.Core;
using CyEngineHub.Core.Marketplaces;
using System.Diagnostics;
using System.Text.Json;
using CyEngineHub.Fab.Remote;

namespace CyEngineHub.Desktop.Views;

/// <summary>Official website rendered locally; scoped read-only Fab library transport, no credential export or host commands.</summary>
public sealed class MarketplaceWebView : NativeControlHost, IDisposable
{
    public async Task<string> ReadFabLibraryPageAsync(string path, CancellationToken cancellationToken)
    {
        if (!FabWebLibraryClient.IsAllowedRequest(path)) throw new ArgumentException("Requête Fab refusée.");
        var web = await Ready;
        if (web is null || !Uri.TryCreate(web.Source, UriKind.Absolute, out var source)
            || source.Scheme != "https" || source.Host != "www.fab.com")
            throw new InvalidOperationException("Connectez-vous sur Fab dans cette fenêtre, puis relancez la synchronisation.");
        var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nonce = Guid.NewGuid().ToString("N");
        void Received(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!Uri.TryCreate(args.Source, UriKind.Absolute, out var origin) || origin.Scheme != "https" || origin.Host != "www.fab.com") return;
            try
            {
                var raw = args.WebMessageAsJson;
                if (raw.Length > 8_000_000) return;
                using var json = JsonDocument.Parse(raw);
                var root = json.RootElement;
                if (!root.TryGetProperty("nonce", out var marker) || marker.GetString() != nonce) return;
                if (root.GetProperty("ok").GetBoolean()) completion.TrySetResult(root.GetProperty("body").GetString()!);
                else completion.TrySetException(new InvalidOperationException("Fab demande une connexion ou une vérification, ou refuse la requête. Utilisez la page officielle puis réessayez."));
            }
            catch (Exception) { completion.TrySetException(new InvalidDataException("Réponse Fab invalide.")); }
        }
        web.Settings.IsWebMessageEnabled = true;
        web.WebMessageReceived += Received;
        try
        {
            // Fixed read-only endpoint; no cookies, tokens, scripts or commands leave the browser.
            var script = "(async()=>{const nonce=" + JsonSerializer.Serialize(nonce) + ";try{"
                + "const r=await fetch(" + JsonSerializer.Serialize(path) + ",{method:'GET',credentials:'same-origin',redirect:'error',headers:{Accept:'application/json'}});"
                + "const ok=r.ok && (r.headers.get('content-type')||'').includes('application/json');"
                + "const body=ok?await r.text():'';window.chrome.webview.postMessage({nonce,ok:ok&&body.length<8000000,body});"
                + "}catch{window.chrome.webview.postMessage({nonce,ok:false,body:''});}})();";
            await web.ExecuteScriptAsync(script);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(45), cancellationToken);
        }
        finally
        {
            if (!_disposed)
            {
                web.WebMessageReceived -= Received;
                web.Settings.IsWebMessageEnabled = false;
            }
        }
    }
    private readonly Uri? _initialUri;
    private readonly string _profileDirectory;
    private readonly Func<string> _getDownloadDirectory;
    private readonly Func<string, Task> _registerDownload;
    private int _preparingDownloads;
    private readonly CoreWebView2Environment? _sharedEnvironment;
    private readonly TaskCompletionSource<CoreWebView2?> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<CoreWebView2DownloadOperation> _downloads = [];
    private readonly Dictionary<CoreWebView2DownloadOperation, DownloadTelemetry> _telemetry = [];
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _progressTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private CoreWebView2Controller? _controller;
    private IntPtr _window;
    private bool _disposed;

    public MarketplaceWebView(Uri? initialUri, string profileDirectory, Func<string> getDownloadDirectory,
        Func<string, Task> registerDownload, CoreWebView2Environment? environment = null)
    {
        _initialUri = initialUri;
        _profileDirectory = profileDirectory;
        _getDownloadDirectory = getDownloadDirectory;
        _registerDownload = registerDownload;
        _sharedEnvironment = environment;
        _progressTimer.Tick += (_, _) => PublishActiveDownloads();
        Focusable = true;
        SizeChanged += (_, _) => Dispatcher.UIThread.Post(UpdateBounds);
    }

    public Task<CoreWebView2?> Ready => _ready.Task;
    public int ActiveDownloads => _preparingDownloads + _downloads.Count(item => item.State == CoreWebView2DownloadState.InProgress || item.CanResume);
    public bool CanGoBack => _controller?.CoreWebView2.CanGoBack == true;
    public bool CanGoForward => _controller?.CoreWebView2.CanGoForward == true;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? BrowserStateChanged;
    public event EventHandler? CloseRequested;
    public event EventHandler<string>? LocalDownloadCompleted;
    public event EventHandler<DownloadTelemetrySnapshot>? DownloadProgressChanged;
    public Func<CoreWebView2Environment, Task<CoreWebView2?>>? CreatePopupAsync { get; set; }

    public void Back() { if (CanGoBack) _controller!.CoreWebView2.GoBack(); }
    public void Forward() { if (CanGoForward) _controller!.CoreWebView2.GoForward(); }
    public void Reload() => _controller?.CoreWebView2.Reload();
    public void Navigate(Uri uri)
    {
        if (IsAllowedNavigation(uri.AbsoluteUri)) _controller?.CoreWebView2.Navigate(uri.AbsoluteUri);
    }
    public void ShowDownloads() => _controller?.CoreWebView2.OpenDefaultDownloadDialog();

    private static bool IsAllowedNavigation(string value) => MarketplaceBrowserPolicy.CanNavigate(value);

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Le navigateur intégré nécessite Windows.");
        _window = NativeMethods.CreateWindowEx(0, "static", "", 0x56010000,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new InvalidOperationException("Création de la fenêtre web impossible.");
        var handle = _window;
        Dispatcher.UIThread.Post(() => _ = InitializeAsync(handle));
        return new PlatformHandle(handle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        StopBrowser();
        if (OperatingSystem.IsWindows()) NativeMethods.DestroyWindow(control.Handle);
        _window = IntPtr.Zero;
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
    }

    private async Task InitializeAsync(IntPtr handle)
    {
        try
        {
            if (_disposed) return;
            var environment = _sharedEnvironment ?? await CoreWebView2Environment.CreateAsync(userDataFolder: _profileDirectory);
            if (_disposed || _window != handle) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = "Marketplace";
            options.IsInPrivateModeEnabled = false;
            var controller = await environment.CreateCoreWebView2ControllerAsync(handle, options);
            if (_disposed || _window != handle) { controller.Close(); return; }
            _controller = controller;
            var web = controller.CoreWebView2;
            web.Settings.AreHostObjectsAllowed = false;
            web.Settings.IsWebMessageEnabled = false;
            web.Settings.AreDevToolsEnabled = false;
            web.Settings.AreDefaultContextMenusEnabled = false;
            web.Settings.IsStatusBarEnabled = false;
            web.Settings.IsPasswordAutosaveEnabled = false;
            web.Settings.IsGeneralAutofillEnabled = false;
            web.NavigationStarting += OnNavigationStarting;
            web.NavigationCompleted += OnNavigationCompleted;
            web.HistoryChanged += OnHistoryChanged;
            web.NewWindowRequested += OnNewWindowRequested;
            web.DownloadStarting += OnDownloadStarting;
            web.WindowCloseRequested += OnWindowCloseRequested;
            web.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            web.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            web.ProcessFailed += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                StopBrowser();
                Report("Le navigateur a rencontré une erreur. Fermez puis rouvrez cette fenêtre.");
                BrowserStateChanged?.Invoke(this, EventArgs.Empty);
            });
            UpdateBounds();
            controller.IsVisible = true;
            _ready.TrySetResult(web);
            if (_initialUri is not null) Navigate(_initialUri);
            BrowserStateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Never log authentication URLs, cookies, responses or exception details.
            _ready.TrySetResult(null);
            StopBrowser();
            Report("Navigateur indisponible. Vérifiez Microsoft Edge WebView2 Runtime puis fermez et rouvrez cette fenêtre.");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (!MarketplaceBrowserPolicy.CanNavigate(args.Uri, allowBlank: _sharedEnvironment is not null))
        {
            args.Cancel = true;
            Report("Navigation bloquée : seules les pages HTTPS sont autorisées.");
            return;
        }
        // Only show the origin: OAuth query strings and callback paths may contain credentials.
        Report("Chargement · " + MarketplaceBrowserPolicy.DisplayOrigin(args.Uri));
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        var source = _controller?.CoreWebView2.Source;
        Report(args.IsSuccess && source is not null
            ? MarketplaceBrowserPolicy.DisplayOrigin(source)
            : "La page n’a pas pu être chargée. Vous pouvez actualiser ou revenir à l’inventaire.");
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnHistoryChanged(object? sender, object args) => BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    private void OnWindowCloseRequested(object? sender, object args) =>
        Dispatcher.UIThread.Post(() => CloseRequested?.Invoke(this, EventArgs.Empty));

    private async void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (!args.IsUserInitiated || !MarketplaceBrowserPolicy.CanNavigate(args.Uri, allowBlank: true) || CreatePopupAsync is null || sender is not CoreWebView2 web)
        {
            Report("Fenêtre automatique ou lien non HTTPS bloqué.");
            return;
        }
        var deferral = args.GetDeferral();
        try
        {
            // A real child WebView keeps window.opener and OAuth popup semantics intact.
            var popup = await CreatePopupAsync(web.Environment);
            if (!_disposed && popup is not null) args.NewWindow = popup;
        }
        catch (Exception) { Report("Impossible d’ouvrir la fenêtre de connexion interne."); }
        finally { ReleaseDeferral(deferral); }
    }

    private async void OnDownloadStarting(object? sender, CoreWebView2DownloadStartingEventArgs args)
    {
        var deferral = args.GetDeferral();
        args.Handled = true;
        _preparingDownloads++;
        try
        {
            // Capture the configured root on the UI thread; filesystem work stays off it.
            var root = _getDownloadDirectory();
            var suggested = args.ResultFilePath;
            var path = await Task.Run(() => MarketplaceDownloadStorage.PrepareDestination(root, suggested));
            if (_disposed) { args.Cancel = true; return; }
            args.ResultFilePath = path;
            var download = args.DownloadOperation;
            _downloads.Add(download);
            _telemetry.Add(download, new DownloadTelemetry());
            download.StateChanged += OnDownloadStateChanged;
            PublishDownload(download, "Téléchargement");
            _progressTimer.Start();
            Report("Téléchargement vers : " + path);
        }
        catch (Exception)
        {
            try { args.Cancel = true; }
            catch (COMException) { }
            Report("Téléchargement annulé : dossier indisponible ou marketplace désactivée. Vérifiez Paramètres → Marketplaces. Aucun repli vers Téléchargements Windows.");
        }
        finally
        {
            _preparingDownloads--;
            ReleaseDeferral(deferral);
            if (!_disposed) BrowserStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private static void ReleaseDeferral(CoreWebView2Deferral deferral)
    {
        // Dispose completes the event itself. Complete + Dispose completes it twice,
        // causing E_ILLEGAL_METHOD_CALL (0x8000000E) to escape the async event handler.
        try { deferral.Dispose(); }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            // The native controller may have closed while the async operation was pending.
        }
    }

    private async void OnDownloadStateChanged(object? sender, object args)
    {
        if (sender is not CoreWebView2DownloadOperation download) return;
        PublishDownload(download, download.State == CoreWebView2DownloadState.Completed ? "Création de la fiche" :
            download.State == CoreWebView2DownloadState.InProgress ? "Téléchargement" : download.CanResume ? "En pause / interrompu" : "Annulé / échoué");
        if (download.State == CoreWebView2DownloadState.Completed ||
            download.State == CoreWebView2DownloadState.Interrupted && !download.CanResume)
        {
            download.StateChanged -= OnDownloadStateChanged;
            _downloads.Remove(download);
            if (_downloads.Count == 0) _progressTimer.Stop();
        }
        if (download.State == CoreWebView2DownloadState.Completed)
        {
            var path = download.ResultFilePath;
            _preparingDownloads++;
            Report("Téléchargement terminé · création de la fiche locale…");
            try
            {
                await _registerDownload(path);
                PublishDownload(download, "Terminé");
                Report("Téléchargement terminé et fiche ajoutée à Mes assets téléchargés : " + path);
            }
            catch (Exception)
            {
                PublishDownload(download, "Téléchargé · fiche à réessayer");
                Report("Fichier téléchargé, mais fiche non enregistrée. Cliquez sur Voir la fiche pour réessayer, ou ajoutez le fichier depuis la bibliothèque locale.");
            }
            finally { _preparingDownloads--; }
            _telemetry.Remove(download);
            if (!_disposed) LocalDownloadCompleted?.Invoke(this, path);
        }
        else Report("Téléchargement en cours ou interrompu : consultez Téléchargements.");
        if (!_downloads.Contains(download)) _telemetry.Remove(download);
        BrowserStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PublishActiveDownloads()
    {
        foreach (var download in _downloads.ToArray())
            PublishDownload(download, download.State == CoreWebView2DownloadState.InProgress ? "Téléchargement" : "En pause / interrompu");
    }

    private void PublishDownload(CoreWebView2DownloadOperation download, string state)
    {
        if (_disposed || !_telemetry.TryGetValue(download, out var telemetry)) return;
        try
        {
            DownloadProgressChanged?.Invoke(this, telemetry.Update(download.ResultFilePath, download.BytesReceived,
                download.TotalBytesToReceive is { } total && total <= long.MaxValue ? (long)total : null, _clock.Elapsed, state));
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException) { }
    }

    private void Report(string message)
    {
        if (!_disposed) StatusChanged?.Invoke(this, message);
    }

    private void UpdateBounds()
    {
        if (_disposed || _controller is null || _window == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
        if (NativeMethods.GetClientRect(_window, out var rect))
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom));
    }

    private void StopBrowser()
    {
        _progressTimer.Stop();
        _telemetry.Clear();
        var controller = _controller;
        _controller = null;
        if (controller is null) return;
        try
        {
            foreach (var download in _downloads) download.StateChanged -= OnDownloadStateChanged;
            _downloads.Clear();
            var web = controller.CoreWebView2;
            web.NavigationStarting -= OnNavigationStarting;
            web.NavigationCompleted -= OnNavigationCompleted;
            web.HistoryChanged -= OnHistoryChanged;
            web.NewWindowRequested -= OnNewWindowRequested;
            web.DownloadStarting -= OnDownloadStarting;
            web.WindowCloseRequested -= OnWindowCloseRequested;
            controller.Close();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ready.TrySetResult(null);
        StopBrowser();
        GC.SuppressFinalize(this);
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetClientRect(IntPtr window, out Rect rect);
    }
}
