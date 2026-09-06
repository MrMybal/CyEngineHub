using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using CyEngineHub.Fab.Remote;
using Microsoft.Web.WebView2.Core;

namespace CyEngineHub.Desktop.Views;

/// <summary>A dedicated private WebView, without any native bridge exposed to web pages.</summary>
public sealed class EpicAuthorizationWebView : NativeControlHost, IDisposable
{
    private readonly Uri _authorizationUri;
    private readonly string _profileDirectory;
    private readonly EpicAuthorizationResponse _responseReader;
    private readonly CancellationTokenSource _lifetime = new();
    private CoreWebView2Controller? _controller;
    private IntPtr _window;
    private Uri? _topLevelUri;
    private long _navigationGeneration;
    private bool _disposed;
    private bool _delivered;

    public EpicAuthorizationWebView(Uri authorizationUri, string profileDirectory)
    {
        _authorizationUri = authorizationUri;
        _profileDirectory = profileDirectory;
        _responseReader = new EpicAuthorizationResponse(authorizationUri);
        Focusable = true;
        SizeChanged += (_, _) => Dispatcher.UIThread.Post(UpdateBounds);
    }

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? Failed;
    public event EventHandler<string>? AuthorizationCodeReceived;
    public event EventHandler? CancelRequested;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("WebView2 nécessite Windows.");
        _window = NativeMethods.CreateWindowEx(0, "static", "", 0x56010000,
            0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new InvalidOperationException("La fenêtre web Windows n’a pas pu être créée.");
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
            StatusChanged?.Invoke(this, "Ouverture du navigateur sécurisé…");
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _profileDirectory);
            if (_disposed || _window != handle) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = "FabAuthorization";
            options.IsInPrivateModeEnabled = true;
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
            web.WebResourceResponseReceived += OnResponseReceived;
            web.NewWindowRequested += OnNewWindowRequested;
            web.DownloadStarting += (_, args) => args.Cancel = true;
            web.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            web.ServerCertificateErrorDetected += (_, args) => args.Action = CoreWebView2ServerCertificateErrorAction.Cancel;
            web.ProcessFailed += (_, _) => Fail("Le navigateur s’est arrêté. Fermez cette fenêtre, réessayez ou utilisez le mode manuel.");
            controller.AcceleratorKeyPressed += (_, args) =>
            {
                if (args.VirtualKey == 0x1B && args.KeyEventKind == CoreWebView2KeyEventKind.KeyDown)
                {
                    args.Handled = true;
                    Dispatcher.UIThread.Post(() => CancelRequested?.Invoke(this, EventArgs.Empty));
                }
            };
            UpdateBounds();
            controller.IsVisible = true;
            web.Navigate(_authorizationUri.AbsoluteUri);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            Fail("Microsoft Edge WebView2 Runtime est absent. Installez-le depuis Microsoft ou utilisez le mode manuel.");
        }
        catch (Exception)
        {
            // Do not log URLs, browser responses or exception data from the authentication flow.
            Fail("Impossible d’ouvrir le navigateur intégré. Vérifiez WebView2 ou utilisez le mode manuel.");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (_disposed || _delivered || !Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0)
        {
            args.Cancel = true;
            if (!_disposed && !_delivered) StatusChanged?.Invoke(this, "Navigation non HTTPS bloquée. Le mode manuel reste disponible.");
            return;
        }
        _topLevelUri = uri;
        _navigationGeneration++;
        // Show the destination origin, never an authorization query or token.
        StatusChanged?.Invoke(this, $"Connexion sur {uri.GetLeftPart(UriPartial.Authority)}");
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_disposed || _delivered) return;
        if (!args.IsSuccess)
            StatusChanged?.Invoke(this, "La page n’a pas pu être chargée. Réessayez ou passez en mode manuel.");
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        StatusChanged?.Invoke(this,
            "Ce fournisseur demande une autre fenêtre. Utilisez la connexion Epic directe ou le mode manuel avec votre navigateur.");
    }

    private async void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs args)
    {
        if (_disposed || _delivered || args.Request.Method != "GET" || args.Response.StatusCode != 200 ||
            _topLevelUri is null || !_responseReader.IsExpectedCallback(_topLevelUri) ||
            !Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) || !_responseReader.IsExpectedCallback(uri))
            return;
        var generation = _navigationGeneration;
        var cancellationToken = _lifetime.Token;
        try
        {
            using var body = await args.Response.GetContentAsync();
            if (body is null || _disposed || _delivered || generation != _navigationGeneration) return;
            var code = await _responseReader.ReadCodeAsync(uri, args.Response.StatusCode, body, cancellationToken);
            if (_disposed || _delivered || generation != _navigationGeneration) return;
            if (code is null)
            {
                StatusChanged?.Invoke(this, "La réponse Epic ne contient pas de code utilisable. Réessayez ou utilisez le mode manuel.");
                return;
            }
            _delivered = true;
            // Close native windows outside WebView2 callbacks to avoid re-entrancy.
            Dispatcher.UIThread.Post(() =>
            {
                if (!_disposed) AuthorizationCodeReceived?.Invoke(this, code);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_disposed) StatusChanged?.Invoke(this, "La réponse Epic n’a pas pu être récupérée. Le mode manuel reste disponible.");
        }
    }

    private void UpdateBounds()
    {
        if (_disposed || _controller is null || _window == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
        if (NativeMethods.GetClientRect(_window, out var rect))
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, Math.Max(1, rect.Right), Math.Max(1, rect.Bottom));
    }

    private void Fail(string message)
    {
        if (!_disposed) Dispatcher.UIThread.Post(() => { if (!_disposed) Failed?.Invoke(this, message); });
    }

    private void StopBrowser()
    {
        if (_controller is null) return;
        var controller = _controller;
        _controller = null;
        try
        {
            var web = controller.CoreWebView2;
            web.NavigationStarting -= OnNavigationStarting;
            web.NavigationCompleted -= OnNavigationCompleted;
            web.WebResourceResponseReceived -= OnResponseReceived;
            web.NewWindowRequested -= OnNewWindowRequested;
            controller.Close();
        }
        catch (Exception exception) when (exception is COMException or InvalidOperationException)
        {
            // A crashed browser may already have released its COM controller.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        StopBrowser();
        _lifetime.Dispose();
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
