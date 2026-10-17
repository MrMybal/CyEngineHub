using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Silk.NET.OpenGLES;
using SkiaSharp;

namespace CyEngineHub.Desktop.ImGui;

// Explicit test instrumentation. No network endpoint, background capture or global hooks.
public static class ImGuiDiagnostics
{
    private static ImGuiHost Host(Window window) => window.GetVisualDescendants().OfType<ImGuiHost>().First(host => host.IsEffectivelyVisible);
    public static bool IsHosted(Window window) => window.GetVisualDescendants().OfType<ImGuiHost>().Any();
    public static IReadOnlyList<string> Elements(Window window) => window.GetVisualDescendants().OfType<ImGuiHost>()
        .Where(host => host.IsEffectivelyVisible).SelectMany(host => (host.Surface.Workspace?.Elements ?? host.Surface.NativeElements).Keys).ToArray();
    public static string BrowserText(Window window) => string.Join('\n', window.GetVisualDescendants().OfType<ImGuiHost>()
        .SelectMany(host => host.Surface.DrawnBrowserText));
    public static int VisibleRows(Window window, string binding) => Host(window).Surface.Workspace?.VisibleRows.GetValueOrDefault(binding) ?? 0;
    public static Vector4 ElementBounds(Window window, string element) => Host(window).Surface.Workspace!.Elements[element];
    public static float FontSize(Window window) => Host(window).Surface.Workspace?.RenderedFontSize ?? 0;
    public static int ImageUploads(Window window) => Host(window).Surface.LastImageUploads;
    public static int TotalImageUploads(Window window) => Host(window).Surface.TotalImageUploads;
    public static void ScrollListToEnd(Window window, string binding) => Host(window).Surface.Workspace?.ScrollToEnd(binding);
    public static async Task FramesAsync(Window window, int frames = 2)
    {
        var surface = Host(window).Surface;
        var target = surface.Frames + frames;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (surface.Frames < target)
        {
            if (!window.IsVisible) return;
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(30, timeout.Token).ConfigureAwait(true);
        }
        if (surface.RenderFailure is { } failure) throw new InvalidOperationException("ImGui render failed.", failure);
    }
    public static async Task ClickAsync(Window window, string element, bool leadingEdge = false, int clicks = 1, float horizontal = .5f, float vertical = .5f, bool rightButton = false, KeyModifiers modifiers = KeyModifiers.None)
    {
        await FramesAsync(window).ConfigureAwait(true);
        var host = window.GetVisualDescendants().OfType<ImGuiHost>().FirstOrDefault(host => host.IsEffectivelyVisible &&
            (host.Surface.Workspace?.Elements ?? host.Surface.NativeElements).Keys.Any(key => key == element || key.StartsWith(element + "/", StringComparison.Ordinal)))
            ?? throw new InvalidOperationException("ImGui element not visible: " + element);
        var elements = host.Surface.Workspace?.Elements ?? host.Surface.NativeElements;
        var key = elements.Keys.FirstOrDefault(key => key == element || key.StartsWith(element + "/", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("ImGui element not visible: " + element);
        var rect = elements[key];
        var position = new Point(leadingEdge ? rect.X + 1 : rect.X + (rect.Z - rect.X) * horizontal, rect.Y + (rect.W - rect.Y) * vertical);
        if (!ReferenceEquals(host.InputHitTest(position), host))
            throw new InvalidOperationException("The GPU host does not receive pointer hit tests.");
        host.Focus();
        host.Surface.Input(io => { io.AddKeyEvent(ImGuiNET.ImGuiKey.ModCtrl, modifiers.HasFlag(KeyModifiers.Control)); io.AddKeyEvent(ImGuiNET.ImGuiKey.ModShift, modifiers.HasFlag(KeyModifiers.Shift)); });
        // Let hover/overlap ownership settle after a modal or grid reflow, as with a real pointer move.
        host.Surface.Input(io => io.AddMousePosEvent((float)position.X, (float)position.Y));
        await FramesAsync(window, 2).ConfigureAwait(true);
        if (clicks > 1) await Task.Delay(400).ConfigureAwait(true);
        for (var click = 0; click < clicks; click++)
        {
            host.Surface.Input(io => { io.AddFocusEvent(true); io.AddMousePosEvent((float)position.X, (float)position.Y); io.AddMouseButtonEvent(rightButton ? 1 : 0, true); });
            await FramesAsync(window, clicks > 1 ? 1 : 3).ConfigureAwait(true);
            host.Surface.Input(io => io.AddMouseButtonEvent(rightButton ? 1 : 0, false));
            await FramesAsync(window, clicks > 1 ? 1 : 3).ConfigureAwait(true);
        }
        host.Surface.Input(io => { io.AddKeyEvent(ImGuiNET.ImGuiKey.ModCtrl, false); io.AddKeyEvent(ImGuiNET.ImGuiKey.ModShift, false); });
        await FramesAsync(window).ConfigureAwait(true);
    }
    public static async Task TextAsync(Window window, string text, KeyModifiers modifiers = KeyModifiers.None)
    {
        // Exercise the Avalonia host, including Win32's handled-KeyDown gate,
        // rather than bypassing it with direct ImGui text injection.
        var host = Host(window);
        var down = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.A, KeyModifiers = modifiers };
        host.RaiseEvent(down);
        if (down.Handled) throw new InvalidOperationException("Printable KeyDown suppresses Windows text input.");
        host.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        host.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.A });
        await FramesAsync(window, 3).ConfigureAwait(true);
    }
    public static async Task KeyAsync(Window window, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var host = Host(window);
        host.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        await FramesAsync(window, 2).ConfigureAwait(true);
        host.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key });
        await FramesAsync(window, 2).ConfigureAwait(true);
    }
    public static async Task EscapeAsync(Window window)
    {
        Host(window).Surface.Input(io => io.AddKeyEvent(ImGuiNET.ImGuiKey.Escape, true));
        await FramesAsync(window).ConfigureAwait(true);
        Host(window).Surface.Input(io => io.AddKeyEvent(ImGuiNET.ImGuiKey.Escape, false));
        await FramesAsync(window).ConfigureAwait(true);
    }
    public static async Task CaptureAsync(Window window, string path)
    {
        var surface = Host(window).Surface;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        surface.CaptureRequested = (gl, width, height) =>
        {
            try { Capture(gl, width, height, path); completion.TrySetResult(); }
            catch (Exception exception) { completion.TrySetException(exception); }
        };
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(true); }
        finally { surface.CaptureRequested = null; }
    }
    private static unsafe void Capture(GL gl, int width, int height, string path)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, (void*)bitmap.GetPixels());
        using var flipped = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(flipped)) { canvas.Translate(0, height); canvas.Scale(1, -1); canvas.DrawBitmap(bitmap, 0, 0); }
        using var output = File.Create(path);
        flipped.Encode(output, SKEncodedImageFormat.Png, 100);
    }
}
