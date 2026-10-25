# Third-party notices

The Hub's own license is GPL-3.0-only, with separate MIT licenses for our two
native Unreal plugins. See [LICENSING.md](LICENSING.md) for exact scope. The notices
below concern third parties and do not relicense their code under the Hub license.

## Dear ImGui user interface

- Dear ImGui / cimgui, via ImGui.NET 1.91.6.1 — MIT License. https://github.com/ocornut/imgui and https://github.com/ImGuiNET/ImGui.NET
- Silk.NET.OpenGLES 2.23.0 — MIT License. https://github.com/dotnet/Silk.NET

The application uses Dear ImGui for its controls and Avalonia for native windows,
graphics surfaces, file dialogs and WebView2 integration. Package license texts
are included under `Licenses` in the Windows distribution.

## Microsoft Edge WebView2

The Windows embedded sign-in window uses the Microsoft.Web.WebView2 SDK.
Copyright Microsoft Corporation. The SDK and the separately installed WebView2
Runtime are subject to Microsoft's respective license terms, not the egs-api-rs
MIT license below. SDK license metadata/text is included in packaged build notices.

- SDK: https://www.nuget.org/packages/Microsoft.Web.WebView2
- Runtime and distribution: https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution

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
