#include <cyui/Core.h>
#include <cyui/modules/DocumentViews.h>
#include <cyui/modules/Diagnostics.h>
#include <cyui/modules/Inspector.h>
#include <cyui/modules/Images.h>
#include <imgui_internal.h>
#include <cstring>
#include <cmath>
#include <memory>

#if defined(_WIN32)
#define HUB_API extern "C" __declspec(dllexport)
#else
#define HUB_API extern "C" __attribute__((visibility("default")))
#endif
namespace {
thread_local std::string returned;
struct DocumentView { cyui::Document model; cyui::DocumentState view; float contentHeight = 0, width = 0; };
struct ImageView { cyui::ImageState view; ImTextureID previous = 0; double fitZoom = 1; ImVec2 size{}; };
struct Logs { cyui::LogBuffer model; cyui::LogState view; };
struct Properties { cyui::InspectorState view; };
const char* result(const std::string& value) { returned = value; return returned.c_str(); }
}
HUB_API int cyhub_ui_abi() { return 2; }
HUB_API const char* cyhub_ui_imgui_version() { return ImGui::GetVersion(); }
HUB_API const char* cyhub_ui_revision() { return "ff023e646c6a3038b3dfbfd53f6d2b4a2f5955e3"; }
HUB_API void cyhub_ui_apply(const char* theme, int comfortable, float scale, const char* locale) {
    cyui::Config config;
    config.themeKey = theme;
    config.density = comfortable ? cyui::Density::comfortable : cyui::Density::compact;
    config.dpiScale = scale;
    cyui::apply(config);
    cyui::setLocale(locale);
}
HUB_API void cyhub_ui_fonts(const char* fallback) {
    cyui::FontConfig fonts;
    if (fallback) fonts.fallbackFolder = fallback;
    cyui::loadFonts(fonts);
}
HUB_API unsigned cyhub_ui_colour(int role) {
    const auto& fields = cyui::paletteFields();
    if (role < 0 || static_cast<std::size_t>(role) >= fields.size()) return cyui::palette().text;
    return cyui::palette().*(fields[role].member);
}
HUB_API float cyhub_ui_dp(float value) { return cyui::dp(value); }
HUB_API void cyhub_ui_notify(const char* text, int tone) { cyui::notify(text, static_cast<cyui::Tone>(tone)); }
HUB_API void cyhub_ui_notifications() { cyui::renderNotifications(); }
HUB_API int cyhub_ui_button(const char* label, int variant, float width, float height) {
    return cyui::button(label, static_cast<cyui::Variant>(variant), {width, height});
}
HUB_API void cyhub_ui_progress(float fraction, float width, float height, int tone) {
    cyui::progressBar(fraction, {width, height}, "", static_cast<cyui::Tone>(tone));
}
HUB_API void cyhub_ui_heading(const char* text, int level) { cyui::heading(text, level); }
HUB_API void cyhub_ui_tooltip(const char* text) { cyui::tooltip(text); }
HUB_API const char* cyhub_ui_tr(const char* key, const char* fallback, int label) {
    return result(label ? cyui::trLabel(key, fallback) : cyui::tr(key, fallback));
}
HUB_API void cyhub_ui_translation(const char* locale, const char* key, const char* value) {
    cyui::registerTranslations(locale, {{key, value}});
}
HUB_API void* cyhub_document_create() { return new DocumentView; }
HUB_API float cyhub_document_content_height(void* pointer) { return static_cast<DocumentView*>(pointer)->contentHeight; }
HUB_API void cyhub_document_destroy(void* pointer) { delete static_cast<DocumentView*>(pointer); }
HUB_API void cyhub_document_set(void* pointer, const char* markdown) {
    auto& document = *static_cast<DocumentView*>(pointer);
    document.model.setMarkdown(markdown); document.contentHeight = 0;
}
HUB_API const char* cyhub_document_draw(void* pointer, const char* id, float width, float height) {
    auto& document = *static_cast<DocumentView*>(pointer);
    const auto availableWidth = width > 0 ? width : ImGui::GetContentRegionAvail().x;
    if (document.width != availableWidth) { document.width = availableWidth; document.contentHeight = 0; }
    // Negative height requests natural flow inside the application's outer reader.
    // Keep spare space for rounding/padding so this child cannot capture wheel input.
    const bool flow = height < 0;
    const auto viewportHeight = flow ? std::max(cyui::dp(80), document.contentHeight + cyui::dp(16)) :
        height > 0 && document.contentHeight > 0 ? std::min(height, document.contentHeight) : height;
    if (flow) ImGui::SetNextWindowScroll({0, 0});
    auto* parent = ImGui::GetCurrentWindow();
    auto action = cyui::documentView(id, document.view, document.model, {width, viewportHeight});
    // Observe the shared renderer's actual child-content extent. This is viewport
    // sizing, not another Markdown layout implementation. Short descriptions must
    // not reserve a full page and push installed variants out of the fiche.
    if (!parent->DC.ChildWindows.empty()) {
        const auto* child = parent->DC.ChildWindows.back();
        document.contentHeight = std::max(cyui::dp(24), child->DC.CursorMaxPos.y - child->DC.CursorStartPos.y + child->WindowPadding.y * 2);
    }
    // Host-owned clipboard action; rendering and hit testing stay in DocumentViews.
    // CyUI currently exposes a clicked URL, but no link context-menu callback.
    ImGui::PushID(id);
    if (ImGui::BeginPopupContextItem("document-links")) {
        for (const auto& block : document.model.blocks())
            for (const auto& run : block.inlines)
                if (run.kind == cyui::DocumentInlineKind::link) {
                    ImGui::PushID(run.target.c_str());
                    if (ImGui::MenuItem((cyui::tr("Copy link address") + " · " + run.text).c_str()))
                        ImGui::SetClipboardText(run.target.c_str());
                    ImGui::PopID();
                }
        ImGui::EndPopup();
    }
    ImGui::PopID();
    return result(action.link);
}
HUB_API void* cyhub_image_create() { return new ImageView; }
HUB_API void cyhub_image_destroy(void* pointer) { delete static_cast<ImageView*>(pointer); }
HUB_API void cyhub_image_reset(void* pointer) { static_cast<ImageView*>(pointer)->view = {}; }
HUB_API void cyhub_image_zoom(void* pointer, float factor) {
    auto& image = *static_cast<ImageView*>(pointer);
    image.view.canvas.view.zoomAt({image.size.x / 2, image.size.y / 2}, std::max(0.01, image.view.canvas.view.zoom * factor));
    image.view.fitPending = false;
}
HUB_API float cyhub_image_relative_zoom(void* pointer) {
    auto& image = *static_cast<ImageView*>(pointer);
    return static_cast<float>(image.view.canvas.view.zoom / std::max(0.00001, image.fitZoom));
}
HUB_API void cyhub_image_pan(void* pointer, float* x, float* y) {
    const auto& origin = static_cast<ImageView*>(pointer)->view.canvas.view.origin;
    *x = static_cast<float>(origin.x); *y = static_cast<float>(origin.y);
}
HUB_API int cyhub_image_draw(void* pointer, const char* id, unsigned long long texture, int width, int height, float sizeX, float sizeY) {
    auto& image = *static_cast<ImageView*>(pointer);
    if (image.previous != texture) { image.view = {}; image.previous = texture; }
    if ((image.size.x != sizeX || image.size.y != sizeY) &&
        std::abs(image.view.canvas.view.zoom / std::max(0.00001, image.fitZoom) - 1.0) < 0.001)
        image.view.fitPending = true;
    const bool fitting = image.view.fitPending;
    image.size = {sizeX, sizeY};
    const auto clicked = cyui::imageViewport(id, image.view, {texture, width, height}, {sizeX, sizeY}).clicked;
    if (fitting || ImGui::IsItemHovered() && ImGui::IsMouseDoubleClicked(0)) image.fitZoom = image.view.canvas.view.zoom;
    // Preserve the Hub's existing left-drag gesture in addition to CyUI's middle/space pan.
    if (ImGui::IsItemHovered() && ImGui::IsMouseDragging(0) && !ImGui::IsKeyDown(ImGuiKey_Space)) {
        const auto delta = ImGui::GetIO().MouseDelta;
        image.view.canvas.view.origin.x -= delta.x / image.view.canvas.view.zoom;
        image.view.canvas.view.origin.y -= delta.y / image.view.canvas.view.zoom;
    }
    return clicked;
}
HUB_API void* cyhub_logs_create() { return new Logs; }
HUB_API void cyhub_logs_destroy(void* pointer) { delete static_cast<Logs*>(pointer); }
HUB_API void cyhub_logs_clear(void* pointer) { static_cast<Logs*>(pointer)->model.clear(); }
HUB_API void cyhub_logs_append(void* pointer, int level, const char* source, const char* text) {
    static_cast<Logs*>(pointer)->model.append(static_cast<cyui::LogLevel>(level), text, source);
}
HUB_API unsigned long long cyhub_logs_draw(void* pointer, const char* id, float width, float height) {
    auto& logs = *static_cast<Logs*>(pointer);
    return cyui::logView(id, logs.view, logs.model, {width, height});
}
HUB_API void* cyhub_inspector_create() { return new Properties; }
HUB_API void cyhub_inspector_destroy(void* pointer) { delete static_cast<Properties*>(pointer); }
// One host property at a time: app validation/persistence remain outside the native view.
HUB_API const char* cyhub_inspector_text(void* pointer, const char* id, unsigned long long propertyId,
    const char* label, const char* help, const char* value, int flags, unsigned long long revision) {
    auto& state = *static_cast<Properties*>(pointer);
    cyui::Property property;
    property.id = propertyId; property.label = label; property.help = help; property.value = std::string(value);
    property.readOnly = (flags & 1) != 0; property.password = (flags & 2) != 0; property.multiline = (flags & 4) != 0;
    auto edits = cyui::inspector(id, state.view, {&property, 1}, revision);
    for (const auto& edit : edits) if (edit.edit.committed()) return result(std::get<std::string>(edit.edit.value));
    return nullptr;
}
