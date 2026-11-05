# Third-party notices

The Hub's own license is GPL-3.0-only, with separate MIT licenses for our two
native Unreal plugins. See [LICENSING.md](LICENSING.md) for exact scope. The notices
below concern third parties and do not relicense their code under the Hub license.

## Dear ImGui user interface

- CyUI 1.2.0, revision `ff023e646c6a3038b3dfbfd53f6d2b4a2f5955e3` — MIT. https://github.com/MrMybal/CyUI
- Dear ImGui 1.92.1 and cimgui — MIT. https://github.com/ocornut/imgui and https://github.com/JunaMeinhold/cimgui
- Hexa.NET.ImGui 2.2.8.5 — MIT. https://github.com/HexaEngine/Hexa.NET.ImGui
- Lexilla, Scintilla interface headers and SciTE language data, brought in by CyUI DocumentViews/Code — permissive Neil Hodgson licenses. No Scintilla editor is linked.
- Silk.NET.OpenGLES 2.23.0 — MIT License. https://github.com/dotnet/Silk.NET

The application uses Dear ImGui for its controls and Avalonia for native windows,
graphics surfaces, file dialogs and WebView2 integration. Package license texts
are included under `Licenses` in the Windows distribution.
Native dependency notices are also retained under `LICENCES/CyUI`,
`LICENCES/DearImGui`, `LICENCES/cimgui` and `LICENCES/Hexa.NET.ImGui`.
The exact source/ABI revisions and selected modules are recorded in
`native/CyEngineHub.CyUI/dependencies.lock.json`. ImGui.NET is no longer used.

## Microsoft Edge WebView2

The same already referenced Core SDK also hosts the offline CyDoc help window.
It opens local files only and has no access to marketplace authentication profiles.

The Windows embedded sign-in window uses the Microsoft.Web.WebView2 SDK.
Copyright Microsoft Corporation. The SDK and the separately installed WebView2
Runtime are subject to Microsoft's respective license terms, not the egs-api-rs
MIT license below. SDK license metadata/text is included in packaged build notices.

- SDK: https://www.nuget.org/packages/Microsoft.Web.WebView2
- Runtime and distribution: https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution

## CyDoc offline documentation reader

- CyDoc 0.1.0, revision `774f2a7eabf7dd181a98ffc5add115b580b67f42` — MIT.
  https://github.com/MrMybal/CyDoc
- The complete MIT license is distributed in `Help/CYDOC-LICENSE.txt`.
- Host documentation retains GPL-3.0-only (`Help/CYENGINEHUB-LICENSE.txt`).
- Only the image module is bundled. No Three.js, mesh, graph, GIF, gallery or
  video runtime is distributed with this reader. CyDoc's compiler dependencies
  are build tools, not part of the installed reader.

## egs-api-rs

Parts of the Epic manifest/chunk format support and the Fab compatibility flow were
adapted from `AchetaGames/egs-api-rs`.

MIT License

Copyright (c) 2021 acheta-games

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
