#include "cimgui.h"
#include <cassert>
#include <cstring>
#include <string>
#include <initializer_list>
#if defined(_WIN32)
#define HUB_IMPORT extern "C" __declspec(dllimport)
#else
#define HUB_IMPORT extern "C"
#endif
HUB_IMPORT int cyhub_ui_abi();
HUB_IMPORT const char* cyhub_ui_imgui_version();
HUB_IMPORT void cyhub_ui_apply(const char*, int, float, const char*);
HUB_IMPORT unsigned cyhub_ui_colour(int);
HUB_IMPORT float cyhub_ui_dp(float);
HUB_IMPORT const char* cyhub_ui_tr(const char*, const char*, int);
HUB_IMPORT void* cyhub_document_create();
HUB_IMPORT void cyhub_document_set(void*, const char*);
HUB_IMPORT void cyhub_document_destroy(void*);
int main() {
    if (cyhub_ui_abi() != 1) return 1;
    if (std::strcmp(cyhub_ui_imgui_version(), "1.92.1") != 0) return 2;
    auto first = igCreateContext(nullptr);
    cyhub_ui_apply("mid-grey", 0, 1, "en");
    auto before = cyhub_ui_colour(0);
    auto second = igCreateContext(nullptr);
    igSetCurrentContext(second);
    cyhub_ui_apply("light-grey", 1, 1.5f, "fr");
    if (cyhub_ui_colour(0) == before) return 3;
    igSetCurrentContext(first);
    if (cyhub_ui_colour(0) != before) return 4;
    for (const auto* theme : {"mid-grey", "light-grey", "amber-night", "violet", "navy"})
        for (int comfortable = 0; comfortable != 2; ++comfortable)
            for (float dpi : {1.f, 1.5f, 2.f}) {
                cyhub_ui_apply(theme, comfortable, dpi, "en");
                if (cyhub_ui_dp(20) != dpi * 20) return 5;
                if (cyhub_ui_colour(0) == cyhub_ui_colour(10)) return 6;
            }
    cyhub_ui_apply("mid-grey", 0, 1, "en");
    const std::string englishLabel = cyhub_ui_tr("Copy", "Copy", 1);
    cyhub_ui_apply("mid-grey", 0, 1, "fr");
    const std::string frenchLabel = cyhub_ui_tr("Copy", "Copy", 1);
    if (englishLabel.substr(englishLabel.find("###")) != frenchLabel.substr(frenchLabel.find("###"))) return 7;
    auto document = cyhub_document_create();
    cyhub_document_set(document, "# Heading\n\nA **bold** [link](https://example.invalid)\n\n```cpp\nint x;\n```");
    cyhub_document_destroy(document);
    igDestroyContext(second); igDestroyContext(first);
    return 0;
}
