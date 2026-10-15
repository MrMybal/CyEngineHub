using System.Numerics;
using ImGuiNET;
using Gui = ImGuiNET.ImGui;

namespace CyEngineHub.Desktop.ImGui;

// CyDAW 2's dense charcoal panels and Shaderixel's restrained violet/cyan accents.
internal static class ImGuiTheme
{
    public static readonly Vector4 Accent = Rgb(0x9B89F5);
    public static readonly Vector4 Muted = Rgb(0x929BAA);
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
        s.Colors[(int)ImGuiCol.WindowBg] = Rgb(0x16181D);
        s.Colors[(int)ImGuiCol.ChildBg] = Rgb(0x1C1F25);
        s.Colors[(int)ImGuiCol.PopupBg] = Rgb(0x24282F);
        s.Colors[(int)ImGuiCol.Border] = Rgb(0x343943);
        s.Colors[(int)ImGuiCol.Text] = Rgb(0xDCE1E8); s.Colors[(int)ImGuiCol.TextDisabled] = Muted;
        s.Colors[(int)ImGuiCol.FrameBg] = Rgb(0x13161B);
        s.Colors[(int)ImGuiCol.FrameBgHovered] = Rgb(0x30323F);
        s.Colors[(int)ImGuiCol.FrameBgActive] = Rgb(0x3C3656);
        s.Colors[(int)ImGuiCol.Button] = Rgb(0x30343D);
        s.Colors[(int)ImGuiCol.ButtonHovered] = Rgb(0x48415D);
        s.Colors[(int)ImGuiCol.ButtonActive] = Rgb(0x615080);
        s.Colors[(int)ImGuiCol.Header] = Rgb(0x3A344D);
        s.Colors[(int)ImGuiCol.HeaderHovered] = Rgb(0x4C425F);
        s.Colors[(int)ImGuiCol.HeaderActive] = Rgb(0x5A4C73);
        s.Colors[(int)ImGuiCol.CheckMark] = Accent; s.Colors[(int)ImGuiCol.SliderGrab] = Accent;
        s.Colors[(int)ImGuiCol.PlotHistogram] = Rgb(0x52D1A2);
        s.Colors[(int)ImGuiCol.Tab] = Rgb(0x23262D);
        s.Colors[(int)ImGuiCol.TabSelected] = Rgb(0x3A344D);
        s.Colors[(int)ImGuiCol.Separator] = Rgb(0x343943);
        s.Colors[(int)ImGuiCol.ModalWindowDimBg] = new(.025f, .03f, .04f, .82f);
        s.ScaleAllSizes(UiCardGrid.InterfaceScale);
    }
}
