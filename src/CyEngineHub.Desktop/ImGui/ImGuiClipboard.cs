using System.Runtime.InteropServices;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

internal sealed class ImGuiClipboard : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint GetText(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetText(nint context, nint text);
    private readonly GetText _get;
    private readonly SetText _set;
    private nint _utf8;
    private readonly nint _window;

    public ImGuiClipboard(nint window)
    {
        _window = window;
        _get = Read;
        _set = Write;
        var platform = Gui.GetPlatformIO();
        platform.Platform_GetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_get);
        platform.Platform_SetClipboardTextFn = Marshal.GetFunctionPointerForDelegate(_set);
    }

    private nint Read(nint context)
    {
        string text = "";
        if (OperatingSystem.IsWindows() && OpenClipboard(0))
        {
            try
            {
                var handle = GetClipboardData(13); // CF_UNICODETEXT, requested only on user paste.
                var pointer = handle == 0 ? 0 : GlobalLock(handle);
                if (pointer != 0)
                {
                    text = Marshal.PtrToStringUni(pointer) ?? "";
                    _ = GlobalUnlock(handle);
                }
            }
            finally { _ = CloseClipboard(); }
        }
        if (_utf8 != 0) Marshal.FreeCoTaskMem(_utf8);
        return _utf8 = Marshal.StringToCoTaskMemUTF8(text);
    }

    private void Write(nint context, nint utf8)
    {
        if (!OperatingSystem.IsWindows() || !OpenClipboard(_window)) return;
        nint handle = 0;
        try
        {
            var text = Marshal.PtrToStringUTF8(utf8) ?? "";
            handle = GlobalAlloc(2, (nuint)((text.Length + 1) * 2));
            if (handle == 0) return;
            var data = GlobalLock(handle);
            if (data == 0) return;
            var characters = (text + '\0').ToCharArray();
            Marshal.Copy(characters, 0, data, characters.Length);
            _ = GlobalUnlock(handle);
            if (EmptyClipboard() && SetClipboardData(13, handle) != 0) handle = 0; // Ownership passes to Windows.
        }
        finally
        {
            if (handle != 0) _ = GlobalFree(handle);
            _ = CloseClipboard();
        }
    }

    public void Dispose()
    {
        if (_utf8 != 0) Marshal.FreeCoTaskMem(_utf8);
        _utf8 = 0;
        GC.KeepAlive(_get);
        GC.KeepAlive(_set);
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern nint SetClipboardData(uint format, nint data);
    [DllImport("kernel32.dll")] private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalFree(nint memory);
}
