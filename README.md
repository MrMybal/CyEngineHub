# CyEngineHub — Alpha

**English** · [Français](README.fr.md)

A local-first, multi-engine development hub for Windows. Unreal Engine has the deepest
integration; Unity, Godot and CRYENGINE have local providers.

**Published version: `2.0.0-alpha.33`.** Back up projects before trying installation,
move or asset-conversion operations.

Recent changes are summarized in the [release notes](docs/releases/v2.0.0-alpha.33.md).

Alpha.33 fixes Unreal patch component selection and temporary-space planning,
adds verified local-block reuse and bounded chunk caching, and improves C++ project
creation, plugin templates, build navigation and Fab descriptions/image galleries.
The optional CyToolsCore MCP repair workflow still requires separate consent for
agent code execution and target validation before installation.
See the [MCP setup and limitations](extensions/builtin/cyenginehub.unreal-version-bridge/mcp/README.md).

This preview adds reusable pack groups, per-pack script chains, Cosmos project
destinations, reorganized settings and engine identity/Git controls. Native Unreal
5.7 conversion was validated on a complete 54-asset pack to 5.5 and 5.3, including
meshes and two levels. This is not universal asset compatibility: see the
[validated scope and Alpha limits](docs/native-57-bridge-validation.md).

<a id="interface-officielle-v2"></a>

## Official V2 interface

The hub opens directly on Engines; the overview has been removed. Engine detail pages have
three tabs: General, Installation options and Build / cross-compilation. Ready checks are
green, warnings orange and missing prerequisites red. Right-click one or more engines to run
a targeted, read-only, cancellable size analysis. Partial measurements are explicitly marked;
other measurements are retained.

Selected cards have a contrasting frame, tinted background and checkmark; selected rows are
highlighted across their full width. Products and projects expose actions through right-click.
Ctrl+click toggles a card; Shift+click selects a range in display order. Right-click preserves
the selected group, with grouped favorites and size analysis; individual actions require a
single selection. Favorites persist. One framed star button toggles the favorites filter
before pagination. Catalog size analysis sits beside Refresh; filters, display modes and the
star share the toolbar, with pagination aligned right. Engines and Projects group display modes
and the star on one line in aligned frames, with a centered count beside them. Actions remain
on a separate line and wrap when space is limited.

Engines, projects and packs have two size analyses: fill missing measurements or recheck
everything on disk. Declared pack-manifest sizes are reused without a full disk scan;
rechecking includes associated files. Each downloaded local version, whether from a provider
or derived, has an Install action that prepares that exact version without a replacement
download. In Activity, Actions groups installations, builds and analyses; Downloads is reserved
for transfers. Each entry has an icon and two counters: running and queued. Completed tasks
retain their original queue.

Dear ImGui is now CyEngineHub's official default interface. The software remains on the alpha
channel. The interface uses a medium-gray/orange palette inspired by CyDAW, compact lists with
resizable columns, a side inspector, search and tabbed forms. Density is adjustable and saved.
Downloads, profiles, installation scripts, connections and MCP tools use the existing services.

The main Tools tab groups Unreal utilities, the toolchain inventory and detailed compatibility
diagnostics. Select an engine to find its prerequisite installer, Unreal Version Selector,
Unreal Insights and Swarm Agent when present. Lists are searchable and sortable; the executable
and its purpose are shown before an explicitly confirmed launch. An Unreal installer being
present does not mean its prerequisites are installed. Official .NET, Visual Studio/C++,
Windows SDK and Unreal resources open in the browser without automatic installation.
Scans remain manual. Missing or unverified prerequisites offer contextual help in Tools,
including the required version and official downloads. Repair a Gradle wrapper through its
engine, rather than an arbitrary global version. .NET runtimes and Unreal wrappers are tied
to their own installation; standard Linux toolchains and environment variables are considered.
Launch/build actions are green, removal actions red and primary actions blue; orange is
reserved for selection and warnings.

New project and Add engine buttons are green. Project-specific options are in its detail page
under Project settings; Projects retains creation, profiles and project import. Engines and
Projects use the full width without a side inspector and offer the same four presentations as
marketplaces: detailed cards, image and name, rows without images and rows with thumbnails.
Engine, project and marketplace preferences are saved separately. Double-click always opens
the detail page without launching the editor. Columns remain sortable in both row modes.
Catalog filters are buttons with a selected state; filters, display options and pagination
have separate frames that adapt to the width. Product-detail actions use full text buttons
grouped by purpose. The four display modes use vector-icon buttons with tooltips, and the
active mode has a distinct color and outline. Their height exactly matches standard buttons
in both compact and comfortable density, with scaled, centered icons.

Engines and Projects retain their actions and selection. Profiles are prepared in Settings;
plugins are managed from project or engine detail pages. Storage and Activity navigation is
anchored at the bottom. Library sources share one row, with help below; display options sit
under search and adapt to available width. Libraries retain four views: detailed cards,
Image + name, rows without images and rows with thumbnails. Click data columns to toggle
ascending/descending sorting. Fab/Megascans sorting applies to all filtered results before
pagination. Empty facets disappear and return automatically when other filters allow matches.
Windows keyboard input passes characters, accents and AltGr to ImGui text fields.

Official Unreal installations are marked green. With Epic/Fab connected, the hub checks
revisions on the same branch and reports available updates without automatically downloading
or replacing the engine. Missing folders are excluded after catalog validation; unavailable
drives remain marked as unavailable. Ctrl/Shift+click selects multiple engines in cards or
lists. Right-click verifies installations, checks official updates or removes catalog entries
with confirmation, without deleting their files. The manifest reader accepts large Unreal
binary catalogs with bounded decompression.

An official engine download closes its form and immediately creates tracking entries in
Engines and Downloads. Transfers continue during navigation, respect download concurrency,
and can be canceled while queued or running from either view. An active destination cannot
be added twice. Completion adds the validated engine without changing pages. Download variants
show Unreal labels, platforms and RAW/Megascans formats; either the text or checkbox selects one.

Official engines and components download chunks in parallel: 12 connections per engine by
default, configurable from 1 to 32 in Settings → Downloads. The queue's Download settings
button opens these settings directly, distinguishing simultaneous transfers from connections
per engine. This setting is separate from simultaneous packs and applies to future transfers.
Chunks are written progressively, incomplete files are not reused, and temporary network errors
receive bounded retries. Aggregate throughput excludes bytes reused from cache; network progress
updates at most four times per second. Actual throughput also depends on the CDN, connection,
disk and CPU.

Reconstruction reuses decompressed chunks in a per-transfer LRU memory cache limited to
128 MiB and calculates integrity while writing, without rereading new files. Progress updates
at most four times per second, plus the start and end of each phase. Selecting a deleted or
inaccessible folder shows an error without exiting. Truncated chunks, network interruptions
and timeouts are retried up to six times with cancellable progressive delays. After failure
or cancellation, Resume is available in Downloads, engine cards/rows and the context menu.
It preserves the original version and destination and revalidates cached chunks before fetching
missing ones. For requests created by an older hub version, select the same official version
and target folder again to reuse the adjacent cache.

Downloads accept Epic chunks whose header moves from v2 to v3 with four additional bytes,
without mistaking that difference for truncation. HTTP size, header payload, GUID, decompression
and reconstructed file hashes remain checked on resume. Fab uses the same parallel chunk
pipeline, validated cache and retries. Settings → Downloads has an independent 1–32 connections
setting per Fab/Megascans download (default: 12). Megascans downloads multiple signed files
in parallel and segments large files when the server supports byte ranges with a strong
validator; otherwise it uses one connection per file. Incomplete files are not published in
the library. These settings do not change active transfers. The log shows significant events
by default: starts, completions, cancellations, warnings and errors. Full log reveals progress
and technical details, which remain in the log file.

Engines and projects retain separate Cards/List modes. Asset cards keep a fixed size;
one click selects the whole card, double-click opens details, and action buttons remain
independent. Sources and Settings panels use the full height and highlight the active tab.
Fab, Megascans and connector product pages offer an explicit return to the catalog and omit
list-presentation options. Clicking the active source also returns from a product to its catalog.
Downloaded variants have separate frames, with compatibility/sizes above and variant actions
below. Unreal extensions/plugins use a list with inspector. Thumbnails load and decode in the
background; GPU uploads per rendered frame are bounded to preserve navigation. The cached
Megascans catalog remains browsable offline without replacing authorized search configuration.

Avalonia provides native windows, GPU rendering, file pickers and WebView2 hosting.
Web pages remain in WebView2; hub controls are drawn with ImGui. For graphics problems,
`--classic` temporarily starts the previous interface.

<a id="télécharger"></a>

## Download

[Official releases](https://github.com/MrMybal/CyEngineHub/releases)

- `CyEngineHub-…-Setup-win-x64.exe`: per-user installer without administrator rights.
- `CyEngineHub-…-win-x64.zip`: self-contained version; extract everything before running `CyEngineHub.Desktop.exe`.
- `SHA256SUMS.txt`: hashes for both downloads.

Windows 10/11 x64. Packages include the .NET runtime; using the application requires no SDK.
This initial Alpha has no Authenticode signature, so Windows may display a warning.

<a id="fonctionnalités-de-cette-alpha"></a>

## Features in this Alpha

- Discover and launch installed engines/projects; Unreal build and packaging tools.
- Fab and legacy Quixel Megascans libraries: user login, cached catalog, asynchronous thumbnails,
  search, filters and virtualized display.
- Megascans Collections tab: official Quixel selections, search, cached covers and access to
  their assets present in your personal catalog.
- Unreal and RAW downloads according to formats actually available to the account.
- Configurable ZIP compression per marketplace, separately accessible JSON metadata,
  cache/download folders and multiple libraries with quotas and priority order.
- Collections, local packages and installation scripts configurable per target.
- Fab/Unreal derived-package workshops: temporary project with a selected engine version,
  fixes in the editor, `Package Data`, installable local versions and controlled removal.
- Unreal Blueprint/C++ project creation, editable profiles, feature packs, engine plugin
  selection and INI override preview/editing.
- Independent profile editor in Settings, also accessible from Projects.
- Built-in Asset Bridge extensions, Unreal installation through relocation into a temporary
  project, and experimental conversion between Unreal versions.
- Logs, background downloads, Windows notification area and optional automatic startup.
- Updates through official GitHub releases with release notes and SHA-256 verification.

<a id="limites-importantes"></a>

## Important limitations

Fab/Quixel connectors use community compatibility interfaces that may change. Access requires
your account and asset rights. They bypass neither marketplace licenses nor permissions.
Unity Asset Store does not yet have an equivalent complete remote workflow.

Converting to an older Unreal version is **not** a universal resave: experimental extensions
export/reimport supported types using installed engines. Blueprints, code, materials and
unsupported features may require manual intervention.

Installation/post-creation scripts must come from trusted sources. CyEngineHub is independent
of Epic Games, Quixel, Unity and the other publishers mentioned.

<a id="données-et-mises-à-jour"></a>

## Data and updates

Fab login offers an embedded Epic window through WebView2 on Windows with automatic JSON-code
retrieval. **Manual mode** in an external browser remains available, including when WebView2
is absent or rejected by the login provider. See [Epic / Fab login](docs/fab-login.md).

Settings, catalogs and logs default to `%LOCALAPPDATA%\CyEngineHub`. Packages and caches can
use folders configured in Settings. An empty `portable.flag` beside the executable enables
a local `Data` folder. Installing or uninstalling does not delete libraries or projects.
In portable mode, prefer manual ZIP updates while preserving `Data` and `portable.flag`:
the installer targets a per-user Windows installation.

The default repository is `MrMybal/CyEngineHub`. Under **Settings → Advanced → Updates**,
disable startup checks or prereleases if desired. The Alpha/Beta channel is enabled by default
for this Alpha. Downloads are verified before the application closes and launches the installer externally.

<a id="compiler-et-tester"></a>

## Build and test

- .NET SDK 9.0.306, or a revision compatible with `global.json`.
- .NET 8 runtime target; Dear ImGui interface hosted by Avalonia.

```powershell
dotnet restore .\CyEngineHub.sln
dotnet build .\CyEngineHub.sln -c Release
dotnet test .\CyEngineHub.sln -c Release
dotnet run --project .\src\CyEngineHub.Desktop\CyEngineHub.Desktop.csproj
```

Create Windows packages with Inno Setup 7 installed:

```powershell
.\tools\release\build-native-adapters.ps1 -EngineRoots $UnrealEngineRoots -OutputRoot $NewAdapters
.\tools\release\build-windows.ps1 -InnoCompiler 'C:\path\Inno Setup 7\ISCC.exe' -NativeAdapterRoot $NewAdapters
```

The script creates a new folder under `artifacts`, runs tests, publishes self-contained
executables (application, updater and tools), bundles extensions, compiles the installer
and generates the ZIP and hashes. It publishes nothing to GitHub and contains no credentials.

Sources include no user caches, marketplace downloads, tokens, annotation exports or personal
project files. Dependencies retain their respective licenses: see
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) and notices bundled in binaries.

## Documentation

- [Epic / Fab login: automatic and manual](docs/fab-login.md)
- [Official Megascans collections](docs/megascan-collections.md)
- [Derived Unreal packages](docs/derived-packages.md)
- [Profiles and project creation](docs/project-creation-profiles.md)
- [Updates and publishing](docs/application-updates.md)
- [Extensions](docs/extensions.md)
- [Package safety](docs/package-install-safety.md)
- [Plugin safety](docs/plugin-safety.md)
- [Storage](docs/storage-safety.md)
- [Architecture](docs/architecture.md)

Some historical design notes describe earlier Alpha stages. Current release limitations
are specified in its release notes.

## Optional Megascans catalog access

See [local catalog configuration](docs/quixel-local-configuration.md).
Catalog search is disabled until an authorized key is configured locally.
