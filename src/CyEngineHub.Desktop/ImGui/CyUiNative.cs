using System.Numerics;
using System.Runtime.InteropServices;

namespace CyEngineHub.Desktop.ImGui;

internal enum CyUiRole { Ground, Panel, Raised, Field, Hover, Active, Line, StrongLine, Grid, StrongGrid, Text, Dim, Faint, Accent, AccentHover, AccentDim, AccentText, Selection, Success, Warning, Error, Info }
internal enum CyUiTone { Neutral, Accent, Success, Warning, Error, Info }
internal enum CyUiVariant { Secondary, Primary, Ghost, Outline, Danger, Success }

// Application-owned ABI adapter. All presentation implementations remain in pinned CyUI.
internal static partial class CyUiNative
{
    private const string Library = "cimgui";
    internal static void VerifyAbi()
    {
        if (Abi() != 2 || Marshal.PtrToStringUTF8(Revision()) != "ff023e646c6a3038b3dfbfd53f6d2b4a2f5955e3" ||
            Marshal.PtrToStringUTF8(ImGuiVersion()) != "1.92.1")
            throw new InvalidOperationException("CyUI/ImGui runtime does not match the application's pinned ABI. Rebuild the native dependency.");
    }
    internal static Vector4 Colour(CyUiRole role)
    {
        var packed = ColourNative((int)role);
        return new((packed & 255) / 255f, ((packed >> 8) & 255) / 255f, ((packed >> 16) & 255) / 255f, (packed >> 24) / 255f);
    }
    internal static string Translate(string english, string? fallback = null, bool label = false) =>
        Marshal.PtrToStringUTF8(TranslateNative(english, fallback ?? english, label ? 1 : 0)) ?? english;
    internal static void Apply(string theme, bool compact, float scale, string locale) => ApplyNative(theme, compact ? 0 : 1, scale, locale);
    internal static void LoadFonts()
    {
        RegisterTranslation("fr", "Copy link address", "Copier l’adresse du lien");
        Fonts("");
    }
    internal static bool Button(string label, CyUiVariant variant = CyUiVariant.Secondary, Vector2 size = default) => ButtonNative(label, (int)variant, size.X, size.Y) != 0;
    internal static void Progress(float fraction, Vector2 size, CyUiTone tone) => ProgressNative(fraction, size.X, size.Y, (int)tone);
    internal static float Dp(float value) => DpNative(value);
    internal static string? DrawDocument(nint handle, string id, Vector2 size) => Marshal.PtrToStringUTF8(DocumentDraw(handle, id, size.X, size.Y));
    internal static bool DrawImage(nint handle, string id, nint texture, int width, int height, Vector2 size) => ImageDraw(handle, id, (ulong)texture, width, height, size.X, size.Y) != 0;
    internal static ulong DrawLogs(nint handle, string id, Vector2 size) => LogsDraw(handle, id, size.X, size.Y);
    internal static string? Inspector(nint handle, string id, ulong propertyId, string label, string help, string value, int flags, ulong revision) =>
        Marshal.PtrToStringUTF8(InspectorText(handle, id, propertyId, label, help, value, flags, revision));

    [LibraryImport(Library, EntryPoint = "cyhub_ui_abi", StringMarshalling = StringMarshalling.Utf8)] private static partial int Abi();
    [LibraryImport(Library, EntryPoint = "cyhub_ui_revision", StringMarshalling = StringMarshalling.Utf8)] private static partial nint Revision();
    [LibraryImport(Library, EntryPoint = "cyhub_ui_imgui_version", StringMarshalling = StringMarshalling.Utf8)] private static partial nint ImGuiVersion();
    [LibraryImport(Library, EntryPoint = "cyhub_ui_colour", StringMarshalling = StringMarshalling.Utf8)] private static partial uint ColourNative(int role);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_dp", StringMarshalling = StringMarshalling.Utf8)] private static partial float DpNative(float value);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_apply", StringMarshalling = StringMarshalling.Utf8)] private static partial void ApplyNative(string theme, int comfortable, float scale, string locale);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_fonts", StringMarshalling = StringMarshalling.Utf8)] private static partial void Fonts(string fallback);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_tr", StringMarshalling = StringMarshalling.Utf8)] private static partial nint TranslateNative(string key, string fallback, int label);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_translation", StringMarshalling = StringMarshalling.Utf8)] internal static partial void RegisterTranslation(string locale, string key, string value);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_notifications", StringMarshalling = StringMarshalling.Utf8)] internal static partial void Notifications();
    [LibraryImport(Library, EntryPoint = "cyhub_ui_notify", StringMarshalling = StringMarshalling.Utf8)] internal static partial void Notify(string text, CyUiTone tone);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_button", StringMarshalling = StringMarshalling.Utf8)] private static partial int ButtonNative(string label, int variant, float width, float height);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_progress", StringMarshalling = StringMarshalling.Utf8)] private static partial void ProgressNative(float fraction, float width, float height, int tone);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_heading", StringMarshalling = StringMarshalling.Utf8)] internal static partial void Heading(string text, int level);
    [LibraryImport(Library, EntryPoint = "cyhub_ui_tooltip", StringMarshalling = StringMarshalling.Utf8)] internal static partial void Tooltip(string text);
    [LibraryImport(Library, EntryPoint = "cyhub_document_create", StringMarshalling = StringMarshalling.Utf8)] internal static partial nint CreateDocument();
    [LibraryImport(Library, EntryPoint = "cyhub_document_content_height")] internal static partial float DocumentContentHeight(nint handle);
    [LibraryImport(Library, EntryPoint = "cyhub_document_destroy", StringMarshalling = StringMarshalling.Utf8)] internal static partial void DestroyDocument(nint handle);
    [LibraryImport(Library, EntryPoint = "cyhub_document_set", StringMarshalling = StringMarshalling.Utf8)] internal static partial void SetDocument(nint handle, string markdown);
    [LibraryImport(Library, EntryPoint = "cyhub_document_draw", StringMarshalling = StringMarshalling.Utf8)] private static partial nint DocumentDraw(nint handle, string id, float width, float height);
    [LibraryImport(Library, EntryPoint = "cyhub_image_create", StringMarshalling = StringMarshalling.Utf8)] internal static partial nint CreateImage();
    [LibraryImport(Library, EntryPoint = "cyhub_image_destroy", StringMarshalling = StringMarshalling.Utf8)] internal static partial void DestroyImage(nint handle);
    [LibraryImport(Library, EntryPoint = "cyhub_image_reset", StringMarshalling = StringMarshalling.Utf8)] internal static partial void ResetImage(nint handle);
    [LibraryImport(Library, EntryPoint = "cyhub_image_zoom", StringMarshalling = StringMarshalling.Utf8)] internal static partial void ZoomImage(nint handle, float factor);
    [LibraryImport(Library, EntryPoint = "cyhub_image_relative_zoom")] internal static partial float ImageZoom(nint handle);
    [LibraryImport(Library, EntryPoint = "cyhub_image_pan")] internal static partial void ImagePan(nint handle, out float x, out float y);
    [LibraryImport(Library, EntryPoint = "cyhub_image_draw", StringMarshalling = StringMarshalling.Utf8)] private static partial int ImageDraw(nint handle, string id, ulong texture, int width, int height, float sizeX, float sizeY);
    [LibraryImport(Library, EntryPoint = "cyhub_logs_create", StringMarshalling = StringMarshalling.Utf8)] internal static partial nint CreateLogs();
    [LibraryImport(Library, EntryPoint = "cyhub_logs_destroy", StringMarshalling = StringMarshalling.Utf8)] internal static partial void DestroyLogs(nint handle);
    [LibraryImport(Library, EntryPoint = "cyhub_logs_clear", StringMarshalling = StringMarshalling.Utf8)] internal static partial void ClearLogs(nint handle);
    [LibraryImport(Library, EntryPoint = "cyhub_logs_append", StringMarshalling = StringMarshalling.Utf8)] internal static partial void AppendLog(nint handle, int level, string source, string text);
    [LibraryImport(Library, EntryPoint = "cyhub_logs_draw", StringMarshalling = StringMarshalling.Utf8)] private static partial ulong LogsDraw(nint handle, string id, float width, float height);
    [LibraryImport(Library, EntryPoint = "cyhub_inspector_create", StringMarshalling = StringMarshalling.Utf8)] internal static partial nint CreateInspector();
    [LibraryImport(Library, EntryPoint = "cyhub_inspector_destroy", StringMarshalling = StringMarshalling.Utf8)] internal static partial void DestroyInspector(nint handle);
    [LibraryImport(Library, EntryPoint = "cyhub_inspector_text", StringMarshalling = StringMarshalling.Utf8)] private static partial nint InspectorText(nint handle, string id, ulong propertyId, string label, string help, string value, int flags, ulong revision);
}
