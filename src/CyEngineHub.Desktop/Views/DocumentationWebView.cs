using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using CyEngineHub.Desktop.Documentation;
using Microsoft.Web.WebView2.Core;

namespace CyEngineHub.Desktop.Views;

/// <summary>Offline-only CyDoc host. No native bridge, authentication or network resources.</summary>
public sealed class DocumentationWebView : NativeControlHost, IDisposable
{
    private readonly DocumentationLocation _location;
    private readonly string _profile;
    private CoreWebView2Controller? _controller;
    private IntPtr _handle;
    private Uri _uri;
    private bool _disposed;
    public DocumentationWebView(DocumentationLocation location, Uri uri, string profile)
    {
        _location = location; _uri = uri; _profile = profile; Focusable = true;
        SizeChanged += (_, _) => Dispatcher.UIThread.Post(UpdateBounds);
    }
    public event EventHandler<string>? Failed;
    public event EventHandler<Uri>? ExternalLinkRequested;
    public event EventHandler<Uri>? SourceChanged;
    public event EventHandler? Ready;
    public CoreWebView2? Browser => _controller?.CoreWebView2;
    public void Navigate(Uri uri)
    {
        if (!_location.AllowsResource(uri)) throw new ArgumentException("Help must remain within its installed directory.", nameof(uri));
        _uri = uri; Browser?.Navigate(uri.AbsoluteUri);
    }
    public void GoBack() { if (Browser?.CanGoBack == true) Browser.GoBack(); }
    public void GoForward() { if (Browser?.CanGoForward == true) Browser.GoForward(); }
    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Use the local-browser fallback on this platform.");
        _handle = NativeMethods.CreateWindowEx(0, "static", "", 0x56010000, 0, 0, 1, 1, parent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (_handle == IntPtr.Zero) throw new InvalidOperationException("Cannot create the documentation view.");
        var handle = _handle; Dispatcher.UIThread.Post(() => _ = InitializeAsync(handle));
        return new PlatformHandle(handle, "HWND");
    }
    protected override void DestroyNativeControlCore(IPlatformHandle control)
    { Stop(); if (OperatingSystem.IsWindows()) NativeMethods.DestroyWindow(control.Handle); _handle = IntPtr.Zero; }
    protected override void OnGotFocus(FocusChangedEventArgs e)
    { base.OnGotFocus(e); _controller?.MoveFocus(CoreWebView2MoveFocusReason.Programmatic); }
    private async Task InitializeAsync(IntPtr handle)
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _profile);
            if (_disposed || _handle != handle) return;
            var controller = await environment.CreateCoreWebView2ControllerAsync(handle);
            if (_disposed || _handle != handle) { controller.Close(); return; }
            _controller = controller;
            var web = controller.CoreWebView2;
            web.Settings.AreHostObjectsAllowed = false; web.Settings.IsWebMessageEnabled = false;
            web.Settings.IsPasswordAutosaveEnabled = false; web.Settings.IsGeneralAutofillEnabled = false;
            web.Settings.IsStatusBarEnabled = false; web.Settings.AreDevToolsEnabled = false;
            web.NavigationStarting += (_, args) =>
            {
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && _location.AllowsResource(uri)) return;
                args.Cancel = true;
                if (args.IsUserInitiated && Uri.TryCreate(args.Uri, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0)
                    Dispatcher.UIThread.Post(() => ExternalLinkRequested?.Invoke(this, uri));
            };
            web.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            web.WebResourceRequested += (_, args) =>
            {
                if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) && (_location.AllowsResource(uri) || uri.Scheme is "data" or "blob")) return;
                args.Response = environment.CreateWebResourceResponse(null, 403, "Offline documentation", "");
            };
            web.NewWindowRequested += (_, args) =>
            {
                args.Handled = true;
                if (args.IsUserInitiated && Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.UserInfo.Length == 0)
                    Dispatcher.UIThread.Post(() => ExternalLinkRequested?.Invoke(this, uri));
            };
            web.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            // Only CyDoc's user-initiated annotation/bookmark JSON exports can download.
            web.DownloadStarting += (_, args) =>
            {
                var download = args.DownloadOperation.Uri;
                // Chromium file origins may serialize blob URLs as blob:null/.
                // Accept those only from this trusted local reader, never from a
                // remote page or as an arbitrary filesystem download.
                if (!Uri.TryCreate(web.Source, UriKind.Absolute, out var source) || !_location.AllowsResource(source) ||
                    !(download.StartsWith("blob:file:", StringComparison.OrdinalIgnoreCase) || download.StartsWith("blob:null/", StringComparison.OrdinalIgnoreCase))) args.Cancel = true;
            };
            web.SourceChanged += (_, _) => { if (Uri.TryCreate(web.Source, UriKind.Absolute, out var uri) && _location.AllowsResource(uri)) SourceChanged?.Invoke(this, uri); };
            web.NavigationCompleted += (_, args) => { if (args.IsSuccess) Ready?.Invoke(this, EventArgs.Empty); else Failed?.Invoke(this, "The local documentation could not be opened. Use Open in browser."); };
            web.ProcessFailed += (_, _) => Dispatcher.UIThread.Post(() => Failed?.Invoke(this, "The documentation view stopped. Use Open in browser."));
            controller.AcceleratorKeyPressed += (_, args) => { if (args.VirtualKey == 0x70) args.Handled = true; };
            UpdateBounds(); controller.IsVisible = true; web.Navigate(_uri.AbsoluteUri);
        }
        catch (Exception error) when (error is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or UnauthorizedAccessException)
        { if (!_disposed) Dispatcher.UIThread.Post(() => Failed?.Invoke(this, "WebView2 is unavailable. Open the same offline manual in your browser; no download is required.")); }
    }
    private void UpdateBounds()
    { if (_controller is not null && _handle != IntPtr.Zero && NativeMethods.GetClientRect(_handle, out var r)) _controller.Bounds = new(0, 0, Math.Max(1,r.Right), Math.Max(1,r.Bottom)); }
    private void Stop() { var controller = _controller; _controller = null; try { controller?.Close(); } catch (COMException) { } }
    public void Dispose() { if (_disposed) return; _disposed = true; Stop(); GC.SuppressFinalize(this); }
    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern IntPtr CreateWindowEx(int exStyle,string className,string name,int style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
        [DllImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool DestroyWindow(IntPtr window);
        [DllImport("user32.dll")] [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] [return:MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(IntPtr window,out Rect rect);
    }
}
