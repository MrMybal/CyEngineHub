# Modular CyUI integration

## Scope and dependency lock

The desktop interface uses CyUI 1.2.0 at revision
`ff023e646c6a3038b3dfbfd53f6d2b4a2f5955e3`, not a floating branch.
`native/CyEngineHub.CyUI/dependencies.lock.json` pins CyUI, cimgui, the ImGui
submodule and the managed binding together. No upstream sources are modified.
There was no previous CyUI integration: the old frontend used ImGui.NET 1.91.6.1
and application-owned renderers.

| Feature | Selected target | Migration |
| --- | --- | --- |
| Theme, density, fonts, action buttons, headings, progress | `CyUI::Core` | Replaces the local palette/font setup and progress renderer. Existing saved theme names map to CyUI themes. |
| Fab descriptions | `CyUI::DocumentViews` | Replaces the local rich-text renderer. The host still normalizes untrusted HTML/Markdown, validates URLs, opens links and retains original catalogue data. |
| Build/Git/raw journals | `CyUI::Diagnostics` | Bounded log buffers and native filtering, scrolling and copy controls. The host retains raw files, filters and persistence. |
| Profile/property forms | `CyUI::Inspector` | Native drafts and revision-aware begin/update/commit/cancel. Host setters run on commit; validation and save commands remain application-owned. |
| Enlarged product gallery | `CyUI::Images` | Shared fit, zoom, pan and canvas. Host owns images, texture lifetime, lazy loading, retries, thumbnails, selection and keyboard gallery navigation. |

Code and Canvas are automatic dependencies of DocumentViews and Images. Code
brings its pinned Lexilla language support. This is not CodeEditor or Scintilla UI.
The build links explicit module targets; production never requests `All` or
`CyUI::CyUI`. Demo, Audio, Gallery, tests belonging to upstream CyUI, and WebCEF
are explicitly disabled. Hub integration contract tests remain enabled.

Assets is not selected: Hub catalogue paging, collections, multi-target installs,
platform/build variants and transactional imports remain application adapters,
using the shared Core style. There is no audio editor, timeline, plot, node editor,
viewport, code editor, diff view or command palette to port.

## One native ImGui implementation

ImGui.NET is removed. Hexa.NET.ImGui **2.2.8.5** targets ImGui **1.92.1**, with
32-bit characters. Its prebuilt native assets are excluded. The application builds
one `cimgui` shared library containing the C API, ImGui and selected CyUI modules.
`CYUI_IMGUI_TARGET=cyhub_imgui` prevents CyUI building another ImGui copy.
The host checks the native ABI/revision/version before drawing.

The OpenGL backend supports ImGui 1.92's dynamic RGBA32 font texture lifecycle
(create, subrectangle updates, destroy), rather than the removed static font atlas
upload. Avalonia continues to own native windows, input, clipboard and folder
dialogs. Each host creates/destroys its own ImGui context; native view state and
textures are released with that context. CyUI's context shutdown hook releases its
theme/font state. Theme preferences propagate to secondary browser chrome.

## Reproducible build

Requirements: .NET SDK compatible with `global.json`, Git, CMake 3.21+, and a
C++17 compiler. Windows uses Visual Studio 2022 C++ tools/Windows SDK; the script
finds its CMake installation when it is not on PATH. No machine-specific source
directory is encoded in the project. The first build requires access to the pinned
Git repositories; subsequent builds reuse the verified local dependency cache.

```powershell
./tools/ui/build-cyui-native.ps1
dotnet build ./CyEngineHub.sln -c Release
dotnet test ./CyEngineHub.sln -c Release
dotnet run --project ./tools/qa/UsabilitySmoke/UsabilitySmoke.csproj -c Release -- --cyui-only
```

Ordinary Windows desktop builds call the native preparation automatically. It
checks source revisions/cleanliness and runs the native contract tests. Output is
under `artifacts/cyui-native`; checkouts are under
`artifacts/cyui-migration-dependencies`, never installed user libraries.
Use `-SourcesRoot`, `-OutputRoot` and `-CMake` to choose other local locations.
When supplying a separately built matching runtime, set both
`-p:SkipBuildCyUiNative=true` and `-p:CyUiNativeRuntime=<runtime-file>`.
Do not substitute the NuGet runtime or upgrade only one side of the ABI lock.

On Linux/macOS, run the script with PowerShell 7 and CMake/compiler on PATH before
the .NET build. Expected output is `runtime/cimgui.so` or `runtime/cimgui.dylib`;
override `CyUiNativeRuntime` if using a multi-configuration generator. These paths
are supported by the project, but Linux/macOS execution has **not** been validated
in this Windows migration. The existing Windows-only WebView2 integration remains
Windows-only. No CEF runtime is downloaded or linked.

Native dependency licenses are kept verbatim in `LICENCES` and copied to normal
build/publish output. See `THIRD_PARTY_NOTICES.md`. When distributing binaries,
include the corresponding application source and the exact native dependency
sources (including ImGui and CyUI's syntax dependencies), not just an inaccessible
repository reference. Do not include dependency `.git` directories or build caches.

## Compatibility boundaries and retained adapters

- The pinned CyUI Core currently references two localization enum values and a
  docking-style field newer than its advertised ImGui 1.92 minimum. The host's
  forced-include `ImGui192Compatibility.h` filters only those absent localization
  entries and skips that unused docking styling. ImGui structs are parsed before
  the guard; its docking ABI is unchanged. Hub uses no dockspaces. This adapter
  applies only to CyUI Core compilation, not cimgui or the managed binding.
- Inspector's API has a two-column property layout, not a compact standalone
  input. Narrow/already-labelled creation fields and immediate search filters
  retain themed Core inputs. Profile name/description and wide property rows use
  Inspector. There is no new application-wide Undo stack: input Undo/Redo is
  preserved, one committed edit reaches existing host setters, and Escape cancels
  the draft before dismissing its containing modal.
- DocumentViews renders a documented Markdown subset, not arbitrary HTML/CSS.
  The host's existing safe HTML conversion remains necessary. Nested inline
  formatting/long single link labels are limited by that shared subset. A host
  context menu retains copying document link addresses; CyUI currently returns
  clicked URLs but has no per-link context-menu callback.
- CyUI Images uses middle/Space pan; the adapter additionally retains Hub's
  left-drag gesture. Thumbnails/inline previews remain host catalogue presentation.
- The component locale setting controls CyUI's English/French built-in strings,
  not a newly claimed complete translation of Hub's French business UI. Stable
  host/property IDs do not use translated captions. User names, paths, content
  and log messages are not translated.
- The Unreal Editor plugin remains the existing Slate integration for **4.27–5.8**.
  CyUI's current official Unreal packaging supports **5.5+**, so replacing it would
  remove required older-engine support. No second ImGui instance or unofficial
  CyUI copy has been added to Unreal. The app migration does not claim to port
  that Slate plugin; this remains a separate compatibility gap.
- `--classic` remains the existing Avalonia diagnostic fallback, not the default
  CyUI frontend. WebView2 content remains a native child surface; its authentication
  and navigation services are unchanged. No unused Web module is linked.

## Validation (Windows x64)

The native contract covers ABI, independent contexts, five theme palettes,
compact/comfortable densities, dp scaling at 1/1.5/2 and localized stable labels.
GPU smoke tests cover all five themes/two densities, Inspector draft cancellation,
keyboard commit and text Undo/Redo, appearance persistence, real description link
clicks, gallery fit/wheel/pan/arrows/Escape and 1024/1440 layout checks. Hardware
monitor DPI switching is not claimed: the scale matrix is a native contract test.

The full solution suite contains 870 tests. Existing targeted UI fixtures also
exercise local package reuse, parallel imports, explicit forced installation,
project creation/build navigation, settings, engine aliases and editor bridge
requests. Fixtures use isolated fake catalogues/projects: no real engine, original
pack or credential is modified. Screenshots and disposable outputs stay in
`artifacts/usability-qa-*` and are not checked into source.
