using System.Numerics;
using Hexa.NET.ImGui;
using Gui = Hexa.NET.ImGui.ImGui;

namespace CyEngineHub.Desktop.ImGui;

// Semantic adapter only: the authoritative palette, metrics and fonts are CyUI Core.
internal static unsafe class ImGuiTheme
{
    private static readonly Dictionary<nint, string> Applied = [];
    private static string _theme = "mid-grey", _locale = "en";
    private static bool _compact = true;
    public static Vector4 Accent => CyUiNative.Colour(CyUiRole.Accent);
    public static Vector4 Muted => CyUiNative.Colour(CyUiRole.Dim);
    public static Vector4 Primary => CyUiNative.Colour(CyUiRole.AccentDim);
    public static Vector4 Selected => CyUiNative.Colour(CyUiRole.Selection);
    public static Vector4 Panel => CyUiNative.Colour(CyUiRole.Panel);
    public static Vector4 Input => CyUiNative.Colour(CyUiRole.Field);
    public static Vector4 Success => CyUiNative.Colour(CyUiRole.Success);
    public static Vector4 OfficialRow => Vector4.Lerp(Panel, Success, .18f);
    public static Vector4 Warning => CyUiNative.Colour(CyUiRole.Warning);
    internal static Vector4 ProgressColor(OperationProgressTone tone) => CyUiNative.Colour(tone switch
    { OperationProgressTone.Completed => CyUiRole.Success, OperationProgressTone.Failed => CyUiRole.Error,
      OperationProgressTone.Canceled => CyUiRole.Warning, _ => CyUiRole.Info });
    internal static CyUiVariant ActionVariant(string? command, bool primary, bool danger = false) => ActionColorPolicy.Resolve(command, primary, danger) switch
    { ActionTone.Start => CyUiVariant.Success, ActionTone.Danger => CyUiVariant.Danger, ActionTone.Primary => CyUiVariant.Primary, _ => CyUiVariant.Secondary };
    internal static int PushActionColors(string? command, bool primary, bool danger = false)
    {
        var variant = ActionVariant(command, primary, danger);
        if (variant == CyUiVariant.Secondary) return 0;
        var colour = CyUiNative.Colour(variant == CyUiVariant.Success ? CyUiRole.Success : variant == CyUiVariant.Danger ? CyUiRole.Error : CyUiRole.Accent);
        Gui.PushStyleColor(ImGuiCol.Button, colour);
        Gui.PushStyleColor(ImGuiCol.ButtonHovered, Vector4.Lerp(colour, CyUiNative.Colour(CyUiRole.Text), .15f));
        Gui.PushStyleColor(ImGuiCol.ButtonActive, Vector4.Lerp(colour, CyUiNative.Colour(CyUiRole.Ground), .2f));
        Gui.PushStyleColor(ImGuiCol.Text, CyUiNative.Colour(CyUiRole.AccentText));
        return 4;
    }
    // Compatibility for semantic color hints in existing XML/binding data. These never
    // define a palette: convert old UI hints to a role in the selected CyUI theme.
    internal static Vector4 Rgb(uint hint) => CyUiNative.Colour(hint switch
    {
        0xF28A2E or 0xF5A623 or 0xE0A04D or 0xEBC488 or 0xF0A43A => CyUiRole.Warning,
        0x815022 or 0x976031 or 0xAA6A30 => CyUiRole.Selection,
        0x5CD26E or 0x52D1A2 or 0x69C9B5 => CyUiRole.Success,
        0xF06474 or 0x872D40 or 0x882F43 or 0xE58C83 or 0xE8747C => CyUiRole.Error,
        0x246E45 => CyUiRole.Success,
        0x77BAFF or 0x56A8EC or 0x6684A4 or 0x315E82 => CyUiRole.Info,
        0xFFFFFF or 0xEAEFF5 or 0xE4E4E4 or 0xF5F5F5 => CyUiRole.Text,
        0xB4B4B4 or 0xBDC8D6 or 0xD5D5D5 => CyUiRole.Dim,
        0x565656 or 0x9A9A9A => CyUiRole.StrongLine,
        0x414141 or 0x4B4B4B => CyUiRole.Raised,
        0x2E2E2E or 0x181B20 or 0x080808 => CyUiRole.Field,
        _ => CyUiRole.Dim,
    });
    internal static Vector4 Rgb(int hint) => Rgb((uint)hint);
    internal static string ThemeKey(string theme) => theme switch
    { "Gris" => "mid-grey", "Sombre" => "navy", "Contraste" => "amber-night", _ => theme };
    internal static void Apply() => Apply(_theme, _compact, _locale);
    internal static void Apply(string theme, bool compact, string locale)
    {
        _theme = theme; _compact = compact; _locale = locale;
        var context = (nint)Gui.GetCurrentContext().Handle;
        var key = ThemeKey(theme) + ":" + compact + ":" + UiCardGrid.InterfaceScale + ":" + locale;
        if (Applied.GetValueOrDefault(context) == key) return;
        CyUiNative.Apply(ThemeKey(theme), compact, UiCardGrid.InterfaceScale, locale);
        Applied[context] = key;
    }
    internal static void Release() => Applied.Remove((nint)Gui.GetCurrentContext().Handle);
}
