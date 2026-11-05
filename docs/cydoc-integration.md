# CyDoc integration and migration

CyEngineHub distributes an offline CyDoc reader, pinned to Git revision
`774f2a7eabf7dd181a98ffc5add115b580b67f42` (CyDoc 0.1.0, MIT).
The application and its manual retain GPL-3.0-only; the reader's separate MIT
notice is included. No modification of the CyDoc repository is required.

## Build and distribution

The same HTML reader is also published on GitHub Pages:
[English](https://mrmybal.github.io/CyEngineHub/#/en/welcome) and
[Français](https://mrmybal.github.io/CyEngineHub/#/fr/welcome).
The `Documentation Pages` workflow regenerates the export on relevant pushes to
main and supports manual dispatch. It checks out the exact CyDoc revision from
dependency.json, uses its npm lockfile, and deploys only artifacts/pages via the
official GitHub Pages actions. CyDoc is currently public, so no cross-repository
credential or Actions secret is needed. A future visibility change must provide
an appropriately scoped read credential; it must never publish private sources
as a workaround.

`node tools/docs/prepare-pages.mjs` stages byte-identical HTML, scripts, styles,
the image module, licenses, manifest, translation report and only media declared
by the exported pages. It does not copy browser profiles, application databases,
logs, credentials, local user assets, build caches or the source checkout. Build
diagnostics are not published. The HTTPS site uses the reader's existing hash
routes and relative resources; no website-only renderer or Markdown rewrite is
introduced. The original file:// export and native help integration are unchanged.

Prerequisites: Git with access to the private CyDoc repository, Node 22+, npm,
and the application's normal .NET SDK. Never put credentials in a configuration
file, generated documentation or command argument.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/docs/build.ps1
# Recompile the pinned dependency and regenerate regardless of the input cache:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/docs/build.ps1 -Force
node tools/docs/verify.mjs
dotnet build src/CyEngineHub.Desktop/CyEngineHub.Desktop.csproj -c Release
```

The first generation uses authenticated Git to create an isolated ignored
`artifacts/cydoc-source-774f2a7` checkout. `npm ci` honors CyDoc's package lock;
TypeScript compilation builds the library, not its demo site. CyDoc is not
installed from the public npm registry. Its upstream development dependencies
include mesh types/Three.js; they stay in the ignored dependency checkout and
are **not** dependencies of the shipped application or reader.

`tools/docs/generate.mjs` stages authoring input, invokes the supported CyDoc CLI
exporter, and bundles an application bootstrap through the public `cydoc/viewer`
API. The generated asset-provider catalog and selected module bundles are used
without fetch, an HTTP server, a CDN or a service worker. The bootstrap adds
the correct host license labeling, theme persistence and reopening scroll state.
It does not repair or rewrite compiled page data.

The complete `artifacts/cydoc/help` tree is copied as `Help` by Desktop's build
and publish target. Do not copy only index.html. `tools/release/build-windows.ps1`
uses that target, then verifies every help file in the distribution ZIP against
the tested export by SHA-256. Node and the private checkout are build-time only.
Non-Windows builders can run the Node generator against a previously compiled
pinned CyDoc checkout; the .NET build refuses a missing export.

## Content, media and navigation

All 25 existing chapter IDs are retained in English and French. Existing
`help:<id>` authoring links are translated to `doc:<id>`. Old local hash routes
`#help:<id>`/`#help/<id>` and native `help:<id>#anchor` requests remain accepted.
The checked-in `tools/docs/manual-map.json` fixes shared heading IDs across both
languages; editing a heading must not silently change a deep link.

Only **image** is enabled: three actual demo-data application screenshots need
zoom, pan and optional annotations. Nine workflows per language compile to
accessible static SVG, requiring no graph module. No video, GIF, gallery, mesh,
graph bundle or Three.js ships. Image controls use the minimal presentation and
enlargement stays within the reader window.

Technical Markdown, release notes, example code and schemas are indexed as
separate reference pages, preserving their original text and code. Old validation
results, research and roadmap pages are marked historical. Artifact paths are
retained as text, not converted into fabricated evidence or copied private user
content. `migration-report.json` records source hashes, original-language
references and unavailable local research links. Screenshots retain their
provenance in docs/screenshots/README.md. No new screenshots of private accounts
or asset libraries are collected.

## Languages and translation maintenance

English is the default. The 25 user chapters have French counterparts with the
same page and section IDs. English revisions derive from source hashes;
`manual-map.json` stores the English revision actually reviewed for French.
Update `frenchSourceRevision` only after reviewing/updating that translation.
An English-only edit is reported outdated, not automatically declared translated.
CyDoc's translation-report.json and its visible report page show missing,
outdated and untracked translations. Missing French technical references fall
back to the default-locale container, explicitly labeled original-language text;
some older references are themselves French. They are not claimed as newly
translated English prose. API annotation extraction currently lacks revision
tags, so extracted bilingual API pages remain untracked in the upstream report.

The native help language is saved in `documentation.language.v1`, separate from
the partial application UI localization. CyDoc's locale selector updates it.
Annotations, reader preferences and bookmarks remain in a dedicated persistent
DocumentationWebView profile, never an Epic/Fab authentication profile.

## Source documentation

The explicit bilingual `@doc` annotations on `IAssetSourceConverter` and
`IFabInstallationScript` use CyDoc's actual language-neutral extractor. C# XML
documentation is not claimed to be automatically understood by CyDoc.
Only the two allowlisted public SDK contract files are staged under source-sdk;
the lexical source index includes those distributed sources, not the entire
application, user projects, credentials, artifacts or private dependency code.
It is not a compiler-backed semantic API browser. Examples/schemas remain
separate readable code pages. See the source index for exact signatures/lines.

## Native host and security

Help / Documentation opens welcome. The header's help action and F1 open the
current screen's guide. The classic Avalonia sidebar exposes both entries too.
The application screen, task queue and selection are never replaced by help.
A reusable owned window provides Back, Forward and Return to app.
`DocumentationLocation` resolves manifest-backed page/section/media IDs and
creates properly encoded file URLs, including Unicode and spaces in Windows paths.

Windows uses the already referenced WebView2 Core runtime. Native objects,
messages, permissions and automatic network requests are disabled; local paths
must remain inside the installed Help directory. CSP blocks network resources.
Only explicit user-initiated HTTPS links leave the reader through the system
browser. Annotation/bookmark exports remain user-initiated local JSON downloads.
If WebView2 is unavailable, the window offers the same file URL to the installed
system browser; it never downloads a runtime or starts a server. On other
platforms this browser fallback is the supported integration path.

## Verification and remaining limits

Local Windows verification on 2026-11-05: 950 solution tests passed, plus 70
real WebView2 reader checks covering all 50 localized user articles, image
decoding/enlargement, keyboard zoom/search, emulated touch, narrow navigation,
language fallback and persisted theme/scroll. The separate native UI fixture
checks sidebar/F1/return, context routing and saved help language. No real
Unreal installation, project, marketplace account or credential is used.

`tools/docs/verify.mjs` checks both locales, shared anchors, image-only bundles,
resource existence, CSP, license files and bounded source indexing. The CyDoc
exporter validates page/section/media links. .NET tests cover native routing,
fallback and resource boundaries. CyDocSmoke exercises the actual Windows
WebView2 file reader; UsabilitySmoke --manual-only exercises sidebar/F1/return.
The package gate checks byte-identical complete help payloads.

The library is an early 0.1 release: all page data loads together; source indexing
is lexical; notes do not synchronize between machines or browser profiles.
System-browser storage behavior can differ by platform. Actual touch hardware,
screen-reader use and macOS/Linux native execution require separate validation.
Do not claim these checks from DOM keyboard/pointer tests on Windows alone.

There are 82 original-language reference/example/license containers without a
separate French translation container, and three revision-untracked entries
(the existing bilingual repository README and two extracted API annotations).
The 25 user guides have no missing or outdated French chapters. Original French
engineering text is explicitly identified, including its article language for
assistive technology; it is not presented as a new English translation.
