using System.Numerics;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

// CyDAW's default Mid Grey palette: lighter neutral panels with orange selections.
internal static class ImGuiTheme
{
    public static readonly Vector4 Accent = Rgb(0xF28A2E);
    public static readonly Vector4 Muted = Rgb(0xB4B4B4);
    public static readonly Vector4 Primary = Rgb(0x7D4A1D);
    public static readonly Vector4 Selected = Rgb(0x76512D);
    public static readonly Vector4 Panel = Rgb(0x373737);
    public static readonly Vector4 Input = Rgb(0x2E2E2E);
    public static readonly Vector4 Success = Rgb(0x5CD26E);
    public static readonly Vector4 OfficialRow = Rgb(0x334C3A);
    public static readonly Vector4 Warning = Rgb(0xF5A623);
    internal static int PushActionColors(string? command, bool primary, bool danger = false)
    {
        var tone = ActionColorPolicy.Resolve(command, primary, danger);
        if (tone is ActionTone.Start or ActionTone.Danger)
        {
            Gui.PushStyleColor(ImGuiCol.Button, Rgb(tone == ActionTone.Start ? 0x287346u : 0x872D40u));
            Gui.PushStyleColor(ImGuiCol.ButtonHovered, Rgb(tone == ActionTone.Start ? 0x348D57u : 0xA63B50u));
            Gui.PushStyleColor(ImGuiCol.ButtonActive, Rgb(tone == ActionTone.Start ? 0x205D38u : 0x6B2232u));
            Gui.PushStyleColor(ImGuiCol.Text, Rgb(0xFFFFFF));
            return 4;
        }
        if (!primary) return 0;
        Gui.PushStyleColor(ImGuiCol.Button, Rgb(0x315E82));
        Gui.PushStyleColor(ImGuiCol.ButtonHovered, Rgb(0x4079A3));
        Gui.PushStyleColor(ImGuiCol.ButtonActive, Rgb(0x254865));
        return 3;
    }
    internal static Vector4 Rgb(uint hex) => new(((hex >> 16) & 255) / 255f, ((hex >> 8) & 255) / 255f, (hex & 255) / 255f, 1);
    public static void Apply()
    {
        Gui.StyleColorsDark();
        var s = Gui.GetStyle();
        s.WindowPadding = new(10, 8); s.FramePadding = new(7, 4);
        s.ItemSpacing = new(7, 5); s.ItemInnerSpacing = new(5, 4); s.CellPadding = new(8, 6);
        s.WindowRounding = 3; s.ChildRounding = 3; s.FrameRounding = 3;
        s.PopupRounding = 4; s.ScrollbarRounding = 3; s.GrabRounding = 2;
        s.ScrollbarSize = 11; s.WindowBorderSize = 1; s.ChildBorderSize = 1; s.FrameBorderSize = 0;
        s.Colors[(int)ImGuiCol.WindowBg] = Rgb(0x2E2E2E);
        s.Colors[(int)ImGuiCol.ChildBg] = Panel;
        s.Colors[(int)ImGuiCol.PopupBg] = Rgb(0x414141);
        s.Colors[(int)ImGuiCol.Border] = Rgb(0x565656);
        s.Colors[(int)ImGuiCol.Text] = Rgb(0xE4E4E4); s.Colors[(int)ImGuiCol.TextDisabled] = Muted;
        s.Colors[(int)ImGuiCol.FrameBg] = Input;
        s.Colors[(int)ImGuiCol.FrameBgHovered] = Rgb(0x4B4B4B);
        s.Colors[(int)ImGuiCol.FrameBgActive] = Rgb(0x565656);
        s.Colors[(int)ImGuiCol.Button] = Rgb(0x4B4B4B);
        s.Colors[(int)ImGuiCol.ButtonHovered] = Rgb(0x626262);
        s.Colors[(int)ImGuiCol.ButtonActive] = Primary;
        s.Colors[(int)ImGuiCol.Header] = Rgb(0x414141);
        s.Colors[(int)ImGuiCol.HeaderHovered] = Rgb(0x555555);
        s.Colors[(int)ImGuiCol.HeaderActive] = Rgb(0x626262);
        s.Colors[(int)ImGuiCol.CheckMark] = Accent; s.Colors[(int)ImGuiCol.SliderGrab] = Accent;
        s.Colors[(int)ImGuiCol.PlotHistogram] = Rgb(0x52D1A2);
        s.Colors[(int)ImGuiCol.Tab] = Rgb(0x414141);
        s.Colors[(int)ImGuiCol.TabSelected] = Selected;
        s.Colors[(int)ImGuiCol.Separator] = Rgb(0x565656);
        s.Colors[(int)ImGuiCol.TableHeaderBg] = Rgb(0x4B4B4B);
        s.Colors[(int)ImGuiCol.TableRowBgAlt] = new(.7f, .7f, .7f, .045f);
        s.Colors[(int)ImGuiCol.ModalWindowDimBg] = new(.025f, .03f, .04f, .82f);
        s.ScaleAllSizes(UiCardGrid.InterfaceScale);
    }
}
