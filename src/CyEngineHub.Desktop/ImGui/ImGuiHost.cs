using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using ImGuiNET;
using Silk.NET.OpenGLES;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

// The border owns a real, transparent hit-test surface. The GPU visual is not
// relied upon for input hit testing; that is independent of its drawing backend.
internal sealed class ImGuiHost : Border
{
    internal ImGuiSurface Surface { get; }
    public ImGuiHost(string layout, Action<string>? nativeAction = null)
    {
        Background = Brushes.Transparent;
        Focusable = true;
        ClipToBounds = true;
        Surface = new ImGuiSurface(layout, nativeAction);
        Child = Surface;
        DataContextChanged += (_, _) => Surface.Model = DataContext;
        PointerMoved += (_, args) => MousePosition(args);
        PointerPressed += (_, args) =>
        {
            Focus(); MousePosition(args);
            var point = args.GetCurrentPoint(this);
            var button = Button(point.Properties.PointerUpdateKind);
            if (button >= 0) Surface.Input(io => io.AddMouseButtonEvent(button, true));
            args.Pointer.Capture(this);
            args.Handled = true;
        };
        PointerReleased += (_, args) =>
        {
            MousePosition(args);
            var button = Button(args.GetCurrentPoint(this).Properties.PointerUpdateKind);
            if (button >= 0) Surface.Input(io => io.AddMouseButtonEvent(button, false));
            args.Pointer.Capture(null);
            args.Handled = true;
        };
        PointerCaptureLost += (_, _) => ReleaseButtons();
        PointerWheelChanged += (_, args) =>
        {
            var delta = args.Delta;
            MousePosition(args);
            Surface.Input(io => io.AddMouseWheelEvent((float)delta.X, (float)delta.Y));
            args.Handled = true;
        };
        KeyDown += (_, args) => Key(args, true);
        KeyUp += (_, args) => Key(args, false);
        TextInput += (_, args) =>
        {
            var text = args.Text;
            if (!string.IsNullOrEmpty(text)) Surface.Input(io => io.AddInputCharactersUTF8(text));
            args.Handled = true;
        };
        GotFocus += (_, _) => Surface.Input(io => io.AddFocusEvent(true));
        LostFocus += (_, _) => { ReleaseButtons(); Surface.Input(io => io.AddFocusEvent(false)); };
    }
    private static int Button(PointerUpdateKind kind) => kind switch
    {
        PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => 0,
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => 1,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => 2,
        _ => -1,
    };
    private void ReleaseButtons() => Surface.Input(io =>
    { io.AddMouseButtonEvent(0, false); io.AddMouseButtonEvent(1, false); io.AddMouseButtonEvent(2, false); });
    private void MousePosition(PointerEventArgs args)
    {
        var position = args.GetPosition(this);
        Surface.Input(io => io.AddMousePosEvent((float)position.X, (float)position.Y));
    }
    private void Key(KeyEventArgs args, bool down)
    {
        var key = MapKey(args.Key); var mods = args.KeyModifiers;
        Surface.Input(io =>
        {
            io.AddKeyEvent(ImGuiKey.ModCtrl, mods.HasFlag(KeyModifiers.Control));
            io.AddKeyEvent(ImGuiKey.ModShift, mods.HasFlag(KeyModifiers.Shift));
            io.AddKeyEvent(ImGuiKey.ModAlt, mods.HasFlag(KeyModifiers.Alt));
            io.AddKeyEvent(ImGuiKey.ModSuper, mods.HasFlag(KeyModifiers.Meta));
            if (key != ImGuiKey.None) io.AddKeyEvent(key, down);
        });
        args.Handled = true;
    }
    internal static ImGuiKey MapKey(Key key)
    {
        if (key is >= Avalonia.Input.Key.A and <= Avalonia.Input.Key.Z) return ImGuiKey.A + (key - Avalonia.Input.Key.A);
        if (key is >= Avalonia.Input.Key.D0 and <= Avalonia.Input.Key.D9) return ImGuiKey._0 + (key - Avalonia.Input.Key.D0);
        if (key is >= Avalonia.Input.Key.F1 and <= Avalonia.Input.Key.F12) return ImGuiKey.F1 + (key - Avalonia.Input.Key.F1);
        return key switch
        {
            Avalonia.Input.Key.Tab => ImGuiKey.Tab, Avalonia.Input.Key.Left => ImGuiKey.LeftArrow, Avalonia.Input.Key.Right => ImGuiKey.RightArrow,
            Avalonia.Input.Key.Up => ImGuiKey.UpArrow, Avalonia.Input.Key.Down => ImGuiKey.DownArrow,
            Avalonia.Input.Key.PageUp => ImGuiKey.PageUp, Avalonia.Input.Key.PageDown => ImGuiKey.PageDown,
            Avalonia.Input.Key.Home => ImGuiKey.Home, Avalonia.Input.Key.End => ImGuiKey.End,
            Avalonia.Input.Key.Delete => ImGuiKey.Delete, Avalonia.Input.Key.Back => ImGuiKey.Backspace,
            Avalonia.Input.Key.Insert => ImGuiKey.Insert, Avalonia.Input.Key.Space => ImGuiKey.Space,
            Avalonia.Input.Key.Enter => ImGuiKey.Enter, Avalonia.Input.Key.Escape => ImGuiKey.Escape,
            Avalonia.Input.Key.LeftCtrl => ImGuiKey.LeftCtrl, Avalonia.Input.Key.RightCtrl => ImGuiKey.RightCtrl,
            Avalonia.Input.Key.LeftShift => ImGuiKey.LeftShift, Avalonia.Input.Key.RightShift => ImGuiKey.RightShift,
            Avalonia.Input.Key.LeftAlt => ImGuiKey.LeftAlt, Avalonia.Input.Key.RightAlt => ImGuiKey.RightAlt,
            _ => ImGuiKey.None,
        };
    }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "GL resources are released with their native context in OnOpenGlDeinit.")]
internal sealed class ImGuiSurface : OpenGlControlBase
{
    private readonly string _layout;
    private readonly Action<string>? _nativeAction;
    private readonly ConcurrentQueue<Action<ImGuiIOPtr>> _input = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _timer;
    private ImGuiGlRenderer? _renderer;
    private ImGuiWorkspace? _workspace;
    private ImGuiClipboard? _clipboard;
    private nint _context;
    private double _previous;
    private double _fontScale = 1;
    private long _lastInput;
    internal object? Model { get; set; }
    internal ImGuiWorkspace? Workspace => _workspace;
    internal Control? NativeSource { get; set; }
    internal List<string> DrawnBrowserText { get; } = [];
    internal Dictionary<string, Vector4> NativeElements { get; } = new(StringComparer.Ordinal);
    internal Action? CustomDraw { get; set; }
    internal Action<GL, int, int>? CaptureRequested { get; set; }
    internal Action? FrameCompleted { get; set; }
    internal Exception? RenderFailure { get; private set; }
    internal int Frames { get; private set; }
    public ImGuiSurface(string layout, Action<string>? nativeAction)
    {
        _layout = layout; _nativeAction = nativeAction;
        IsHitTestVisible = false;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(33), DispatcherPriority.Background, (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window { IsVisible: false } or Window { WindowState: WindowState.Minimized }) return;
            _timer!.Interval = TimeSpan.FromMilliseconds(Environment.TickCount64 - _lastInput < 1600 ? 16 : 100);
            RequestNextFrameRendering();
        });
        AttachedToVisualTree += (_, _) => _timer.Start();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }
    internal void Input(Action<ImGuiIOPtr> input)
    { _input.Enqueue(input); _lastInput = Environment.TickCount64; RequestNextFrameRendering(); }
    protected override unsafe void OnOpenGlInit(GlInterface gl)
    {
        _context = Gui.CreateContext(); Gui.SetCurrentContext(_context);
        var io = Gui.GetIO(); io.NativePtr->IniFilename = null; io.NativePtr->LogFilename = null;
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        LoadFont(TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        ImGuiTheme.Apply();
        _clipboard = new ImGuiClipboard(TopLevel.GetTopLevel(this)?.TryGetPlatformHandle()?.Handle ?? 0);
        _renderer = new ImGuiGlRenderer(GL.GetApi(gl.GetProcAddress));
        if (CustomDraw is null) _workspace = new ImGuiWorkspace(_layout, _renderer.Image, _nativeAction);
    }
    private void LoadFont(double scale)
    {
        _fontScale = scale;
        var io = Gui.GetIO(); io.Fonts.Clear(); io.FontGlobalScale = 1 / (float)scale;
        var font = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf");
        if (File.Exists(font))
        {
            ushort[] ranges = [0x20, 0x17F, 0x2000, 0x27FF, 0];
            var pinned = GCHandle.Alloc(ranges, GCHandleType.Pinned);
            try { io.Fonts.AddFontFromFileTTF(font, 14 * (float)scale, default, pinned.AddrOfPinnedObject()); io.Fonts.Build(); }
            finally { pinned.Free(); }
        }
        else { io.Fonts.AddFontDefault(); io.FontGlobalScale = 1; }
    }
    protected override void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (_renderer is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        using var saved = new GlState(_renderer.Api);
        Gui.SetCurrentContext(_context);
        var io = Gui.GetIO(); var now = _clock.Elapsed.TotalSeconds;
        io.DeltaTime = (float)Math.Clamp(now - _previous, .001, .2); _previous = now;
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        if (Math.Abs(scale - _fontScale) > .01) { LoadFont(scale); _renderer.UpdateFont(); }
        io.DisplaySize = new((float)Bounds.Width, (float)Bounds.Height);
        io.DisplayFramebufferScale = new((float)scale);
        while (_input.TryDequeue(out var input)) input(io);
        Gui.NewFrame();
        try
        {
            if (CustomDraw is { } draw)
            {
                Gui.SetNextWindowPos(Vector2.Zero); Gui.SetNextWindowSize(io.DisplaySize);
                Gui.Begin("###browser-chrome", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings);
                draw(); Gui.End();
            }
            else _workspace?.Draw(Model, io.DisplaySize);
        }
        catch (Exception exception)
        {
            RenderFailure = exception;
            // Keep diagnostics local; authentication payloads are never displayed here.
        }
        Gui.Render();
        _renderer.Render(Gui.GetDrawData(), fb, (int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
        Frames++;
        CaptureRequested?.Invoke(_renderer.Api, (int)(Bounds.Width * scale), (int)(Bounds.Height * scale));
        FrameCompleted?.Invoke();
    }
    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        Gui.SetCurrentContext(_context);
        _renderer?.Dispose(); _renderer = null;
        _workspace?.Dispose(); _workspace = null;
        _clipboard?.Dispose(); _clipboard = null;
        Gui.DestroyContext(_context); _context = 0;
    }
}
