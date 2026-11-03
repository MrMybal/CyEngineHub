#pragma once
#include <imgui.h>
#include <imgui_internal.h>
// CyUI's current Core unconditionally mentions 1.93 localization keys even though
// its public minimum is 1.92. Do not copy/patch its implementation: filter only
// entries for menus absent from this pinned 1.92.1 ABI. Existing keys keep their translations.
#if IMGUI_VERSION_NUM < 19297
inline constexpr ImGuiLocKey ImGuiLocKey_TableReset = static_cast<ImGuiLocKey>(-1);
inline constexpr ImGuiLocKey ImGuiLocKey_TableResetVisibility = static_cast<ImGuiLocKey>(-2);
namespace ImGui {
inline void CyHubLocalizeRegisterEntries(const ImGuiLocEntry* entries, int count) {
    for (int index = 0; index < count; ++index)
        if (entries[index].Key >= 0 && entries[index].Key < ImGuiLocKey_COUNT)
            LocalizeRegisterEntries(entries + index, 1);
}
}
#define LocalizeRegisterEntries CyHubLocalizeRegisterEntries
// CyUI's optional docking style mentions a newer ImGuiStyle member. The Hub uses
// ordinary windows/tables, not ImGui dock spaces. Headers/layouts are already parsed
// above, preserving the exact docking ABI used by cimgui and the managed binding.
#undef IMGUI_HAS_DOCK
#endif
