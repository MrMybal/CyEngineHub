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
    private static bool IsCreationCommand(string? command) => command is "ToggleProjectCreatorCommand" or "ToggleEngineImporterCommand";
    internal static int PushActionColors(string? command, bool primary)
    {
        if (IsCreationCommand(command))
        {
            Gui.PushStyleColor(ImGuiCol.Button, Rgb(0x287346));
            Gui.PushStyleColor(ImGuiCol.ButtonHovered, Rgb(0x348D57));
            Gui.PushStyleColor(ImGuiCol.ButtonActive, Rgb(0x205D38));
            Gui.PushStyleColor(ImGuiCol.Text, Rgb(0xFFFFFF));
            return 4;
        }
        if (!primary) return 0;
        Gui.PushStyleColor(ImGuiCol.Button, Primary);
        return 1;
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
        s.Colors[(int)ImGuiCol.Header] = Selected;
        s.Colors[(int)ImGuiCol.HeaderHovered] = Rgb(0x625444);
        s.Colors[(int)ImGuiCol.HeaderActive] = Primary;
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
