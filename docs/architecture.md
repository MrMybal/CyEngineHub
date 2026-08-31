# Architecture

## Direction

CyEngineHub is a modular monolith. Modules are separate .NET assemblies with explicit
references, while the desktop project remains the only composition root. This
keeps startup and deployment simple now without preventing a future CLI, build
agent or extension host.

    CyEngineHub.Desktop
      ├─ CyEngineHub.Infrastructure
      ├─ CyEngineHub.Engines
      ├─ CyEngineHub.Extensions
      ├─ CyEngineHub.Unreal
      ├─ CyEngineHub.Unity
      ├─ CyEngineHub.Godot
      ├─ CyEngineHub.CryEngine
      ├─ CyEngineHub.Build
      ├─ CyEngineHub.Git
      ├─ CyEngineHub.Plugins
      ├─ CyEngineHub.Toolchains
      ├─ CyEngineHub.Storage
      ├─ CyEngineHub.Epic
      ├─ CyEngineHub.Fab
      ├─ CyEngineHub.Projects
      └─ CyEngineHub.Core

    CyEngineHub.Unity ───────┐
    CyEngineHub.Godot ───────┼─> CyEngineHub.Engines
    CyEngineHub.CryEngine ───┤
    CyEngineHub.Unreal ──────┘

    CyEngineHub.Extensions
      ├─ CyEngineHub.Engines
      └─ CyEngineHub.Core

    CyEngineHub.AssetBridge.Tool (isolated first-party process)

    CyEngineHub.Infrastructure
      ├─ CyEngineHub.Unreal
      ├─ CyEngineHub.Projects
      └─ CyEngineHub.Core

    CyEngineHub.Build ──────> CyEngineHub.Unreal
             └─────────> CyEngineHub.Projects
             └─────────> CyEngineHub.Core

    CyEngineHub.Git ────────> CyEngineHub.Core

    CyEngineHub.Plugins ─────> CyEngineHub.Unreal
               └────────> CyEngineHub.Projects
               └────────> CyEngineHub.Core

    CyEngineHub.Toolchains ───> CyEngineHub.Unreal
                  └──────> CyEngineHub.Core

    CyEngineHub.Storage ───────> CyEngineHub.Unreal
              ├───────────> CyEngineHub.Projects
              ├───────────> CyEngineHub.Git
              └───────────> CyEngineHub.Core

    CyEngineHub.Epic ──────────> CyEngineHub.Core

    CyEngineHub.Fab (independent web-backend contract)

    CyEngineHub.Unreal ─────┐
    CyEngineHub.Projects ───┴─> CyEngineHub.Core

Epic and Fab are isolated feature assemblies. Epic depends on Core for settings;
neither feature assembly depends on Desktop. Engine providers depend inward on
the neutral Engines contract and never on Desktop or another provider.

## Existing modules

### Engines and provider modules

- neutral engine family, status, capability and project-marker descriptors;
- validated registry with one module per engine family;
- dedicated assemblies for Unreal, Unity, Godot and CryEngine;
- explicit capability reporting so planned providers cannot appear functional;
- a neutral local-provider contract for installations, projects and launch
  requests, plus a shared bounded project-directory scanner;
- an optional `ILocalEnginePackageProvider` contract for bounded, read-only
  provider-native package inventories without claiming mutation support;
- a distinct `ILocalEnginePackageInstallationProvider` contract and shared
  SHA-256 transaction engine for providers that can safely install an extracted
  local package;
- a `Custom` family seam for future engines without changing the desktop shell.

Unreal owns the complete implemented vertical slice. Unity, Godot and CryEngine are `Active`
for tested local discovery, version parsing, editor/project launch and native package
inventory. Their compact catalogues remain separate from the Unreal-specific Build, Plugins and
Toolchains models. See
[Multi-engine providers](multi-engine-providers.md).

### Extensions

- a schema-versioned `extension.json` contract separate from Unreal `.uplugin`;
- bounded discovery across built-in and user roots with duplicate-ID protection;
- manifest validation for engine families, capabilities, formats, HTTPS links,
  placeholders and contained executable paths;
- two-phase installation with SHA-256 inventory, staging, retained backup and
  source revalidation after confirmation;
- external-process plans with expiration and executable revalidation;
- execution allow-list currently restricted to the first-party Asset Bridge;
- declarative HTTPS extensions for integrations such as the third-party Fab
  Asset Downgrader listing;
- a versioned JSON Schema and declarative example package for extension authors.

The Asset Bridge performs bounded source inspection across Unreal, Unity, Godot and
CryEngine. It resolves provider-owned destinations beneath each validated project,
then revalidates the plan before execution. It copies only
interoperable exported files and refuses collisions. It does not parse native
engine assets, translate scripts/Blueprints or recreate engine-native materials.
See [Extensions](extensions.md).

### Core

- structured process requests using ProcessStartInfo.ArgumentList, with an
  explicit raw-argument escape hatch for Windows batch wrappers;
- async stdout/stderr streaming with a bounded capture buffer;
- cancellation that terminates the launched process tree;
- bounded-concurrency task scheduling and progress snapshots;
- settings and logging contracts;
- detached application/folder launcher abstraction.

### Unreal provider

- discovery source abstraction;
- Windows Unreal registration discovery;
- local Epic .item manifest discovery;
- configured path discovery;
- engine root normalization and Build.version parsing;
- source fork, source, installed build and Epic binary classification;
- health checks for Editor, UBT and AutomationTool.
- per-user Windows engine registration behind a testable store abstraction;
- idempotent registration and root-scoped unregistration.
- validated and confirmed GitHub source-engine clone requests, with cancelable
  process execution, optional shallow history and post-clone structure checks.

### Projects

- current implementation belongs to the Unreal provider transition path;
- discovery source abstraction;
- Unreal recent-project extraction;
- bounded breadth-first scanning of configured roots;
- explicit exclusions for .git, Content, Saved, Intermediate, Binaries,
  DerivedDataCache and engine trees;
- tolerant .uproject parsing with typed errors.

### Infrastructure

- SQLite catalog with WAL and schema versioning;
- JSONL rolling logs;
- system process runner and platform launcher;
- installed or portable data paths.

### Build

- typed requests for GenerateProjectFiles, UBT Build/Clean/Rebuild and UAT
  BuildCookRun packaging;
- typed Setup.bat execution for source engines;
- built-in and custom UBT/UAT presets persisted through the settings contract;
- validated Windows batch command construction with paths containing spaces;
- live warning/error/fatal classification and common toolchain diagnostics;
- bounded in-memory output plus persistent UTF-8 operation logs;
- shared process execution and process-tree cancellation through Core.

### Git

- Git CLI integration independent of any hosting provider;
- streamed porcelain-v2 parsing that retains counts rather than file lists;
- repository-root resolution and deduplication across engine/project owners;
- branch, commit, upstream, ahead/behind and arbitrary remote snapshots;
- bounded Git LFS storage analysis with explicit partial-result semantics;
- sanitized remote URLs and SQLite-backed snapshot cache;
- fetch-only remote updates with cancellation and persistent operation logs.

### Plugins

- bounded discovery beneath engine, project and configured library roots;
- tolerant `.uplugin` parsing for versions, modules, platforms, content and
  dependencies;
- stable cache identities and diagnostics for malformed descriptors, duplicate
  plugin names and missing required dependencies;
- compatibility rules that distinguish rebuildable source plugins from
  incompatible binary-only builds;
- two-phase installation planning and confirmation;
- transactional staging, post-copy descriptor verification, retained previous
  versions and rollback if the final directory swap fails;
- typed `BuildPlugin` requests routed through UAT with isolated output safety.

The Unreal workflow above remains the only install/build manager. Separately, the
neutral package-inventory contract lets Unity merge UPM manifest/lock state, Godot
read `plugin.cfg` plus GDExtension descriptors, and CRYENGINE read plugins declared
by `.cryproject`. These scans are manual and bounded. Unity and Godot additionally
implement the separate local-package installation contract: a two-phase transaction
  copies a validated extracted folder through project-contained staging and retains
the prior destination as a backup. Versioned JSON receipts support restart-safe,
fingerprint-validated rollback while preserving the removed version for recovery. See
[Package installation safety](package-install-safety.md).

Unity and Godot also implement the neutral package-management extension for
provider-approved project-owned folders. Removal is another expiring two-phase
transaction: it fingerprints the installed tree, moves it into project-contained
recovery, atomically persists a distinct receipt and can restore it after restart.
Occupied destinations, changed recovery content, stale receipts and active Godot
editor plugins are rejected; Unity manifest dependencies are never rewritten.

### Toolchains

- read-only detection for Visual Studio, MSVC, Windows SDK, .NET, CMake, Ninja,
  Clang, Android SDK/NDK/JDK/Gradle/ADB and Unreal Linux cross-toolchains;
- modern Unreal `*_SDK.json` parsing with bounded legacy source fallbacks;
- per-engine compatibility evaluation for exact SDK components, valid ranges,
  preferred MSVC versions and explicitly banned compiler ranges;
- process/user/system snapshots for Android, Java, NDK and Linux variables;
- bounded `adb devices -l` parsing with offline state and Meta Quest recognition;
- SQLite-backed snapshots, typed errors and no automatic inventory at startup.

### Storage

- targeted discovery of engine/project footprints, generated project folders,
  engine/project/global/shared DDC, Zen data and local Epic Vault Cache;
- reuse of cached Git LFS measurements instead of a second repository scan;
- bounded directory sizing (250,000 entries or five seconds per path and a
  two-minute global budget), with cancellation and explicit partial results;
- reparse-point exclusion, volume capacity snapshots and low-space warnings;
- SQLite-backed snapshots loaded without filesystem work at startup;
- two-phase Safe Clean with exact path/category validation, a 30-minute plan,
  permanent-delete confirmation and typed failures;
- strict cleanup allow-list limited to generated project folders and engine DDC.

### Epic

- official browser authentication boundary that never claims to verify the
  external session;
- Windows EGL discovery and optional explicit launch;
- bounded parsing of allow-listed local `.item` and `LauncherInstalled.dat`
  installation fields;
- local Vault Cache and Fab plugin package counts without content mutation;
- SQLite-backed inventory snapshot containing no password, cookie or token;
- replaceable authentication, launcher-location and local-catalog contracts.

### Fab

- replaceable backend contract;
- official Fab home and My Library web surface;
- explicit capability flags showing that remote library enumeration and direct
  downloads are not supported by the current backend.
- selected-project handoff to the official Fab window in Unreal Editor;
- local extracted-content import with bounded inventory, version compatibility,
  staging, collision backups and immediate-failure rollback;
- persistent project-local import receipts and confirmed post-import rollback
  guarded by SHA-256 validation of installed files and backups.
- Unity-specific extracted-asset import into an isolated
  `Assets/CyEngineHubImports` directory, with a strict data allow-list, atomic
  directory replacement and receipts/backups below `.cyenginehub`;
- Godot-specific portable-data import into an isolated `CyEngineHubImports`
  directory, with active-content rejection, atomic directory replacement and
  receipts/backups below `.cyenginehub/FabImports/Godot`;
- CryEngine-specific portable-data import into `CyEngineHubImports` below the
  descriptor-declared assets directory, with code/binary rejection, atomic
  directory replacement and receipts/backups below `.cyenginehub/FabImports/CryEngine`;
- manually refreshed local extracted-pack catalog with configured roots, bounded
  traversal, Unreal/Unity/Godot/CryEngine classification, metadata cache and handoff to the
  matching import planner.

### Desktop

- composition root;
- engine-provider registry composition without engine-specific dependencies in
  the registry itself;
- MVVM view models;
- Avalonia 12 XAML with compiled bindings;
- dashboard, engine, project, Git, plugin, Epic/Fab, toolchain, storage,
  build-output, task and settings views;
- source engine and project actions for GPF, Editor builds and Win64 packages;
- confirmed engine unregistration and an inline build-preset editor;
- roadmap placeholders that preserve the intended navigation.

## Data and performance rules

- no whole-disk scan at startup;
- discovered metadata is cached in SQLite;
- configured project scans have a maximum depth;
- heavy Unreal/generated directories are excluded;
- command output retained in memory is bounded;
- full build output is streamed to a per-operation file instead of being held in
  memory;
- Git status runs only on explicit refresh and its streamed file records are
  reduced to counters;
- LFS analysis stops after 250,000 objects or five seconds and marks partial
  results;
- plugin discovery runs only on explicit refresh and skips generated directories
  and reparse points;
- plugin installation inventories at most 250,000 files, never follows reparse
  points and preserves a replaced version;
- Hub extension packages are capped at 2,000 files and 512 MiB, reject reparse
  points, and are fingerprinted again after user confirmation;
- installed user-extension executables are not runnable; only the exact bundled
  Asset Bridge identity and executable name pass the current execution policy;
- Asset Bridge inspection is bounded to 50,000 entries, 10,000 transferable files
  and 20 GiB, never follows reparse points, and targets recognized project roots;
- toolchain inventory runs only on explicit refresh; startup loads its last
  snapshot without launching `vswhere`, `dotnet` or `adb`;
- toolchain metadata reads are bounded and ADB device enumeration is the only
  device-facing command in this milestone;
- storage analysis runs only on explicit refresh, skips reparse points and never
  exposes partial measurements to Safe Clean;
- storage cleanup is queued, cancelable and restricted to exact regenerated
  Unreal paths selected from a fresh snapshot;
- Epic/Fab startup loads only a cached snapshot; local manifest inventory is an
  explicit, bounded action and performs no network request;
- Epic/Fab browser actions use only absolute official HTTPS locations and do not
  expose browser or EGL credentials to CyEngineHub;
- source-engine clones accept only credential-free GitHub HTTPS UnrealEngine URLs
  and never run Setup automatically;
- Fab project imports never parse Vault chunks or call the export plugin’s private
  localhost protocol, and require a fresh confirmed plan before writing Content;
- Fab import rollback uses a second expiring plan, refuses files changed since
  import and stages the applied state before restoring or deleting anything;
- Unity/Fab, Godot/Fab and CryEngine/Fab rollback additionally validate their persisted receipts
  and confine deletion to their provider-owned `CyEngineHubImports` destination;
- the local Fab pack catalog rejects volume roots/reparse points, never scans at
  startup and never interprets raw Vault Cache or entitlement data;
- discovery and external processes do not run on the UI thread;
- the task scheduler defaults to one heavyweight operation at a time;
- SQLite uses one short-lived connection per operation and WAL mode.

## Extension seams

Every engine begins with an `IEngineModule` and an `EngineModuleDescriptor` in
the neutral `CyEngineHub.Engines` assembly. Provider-specific discovery, launch,
build, package, plugin and asset services stay inside their provider assembly.
Only implemented and tested features are added to a provider's capability flags.
This prevents the UI from treating a project marker as proof that a complete
workflow exists.

Epic and Fab integrations remain behind replaceable contracts such as:

    IEpicAuthenticationService
    IEpicLocalCatalogService
    IFabBackend

Entitlement, manifest-download and content-download contracts will be added only
if supported public integration surfaces pass the research gate.

The same rule applies to Git hosting providers. Core Git support will target the
Git CLI and arbitrary remotes; GitHub-specific features, if any, stay optional.

## Deliberate non-decisions

- Unity, Godot and CryEngine are operational only for the capability flags they
  expose; package/addon/plugin inventory is implemented, local extracted-package
  installation is active for Unity/Godot, while remote acquisition/removal, Godot
  exports and CRYENGINE build/plugin installation remain staged.
- Existing Build, Projects, Plugins, Toolchains and Storage implementations are
  the active Unreal vertical slice; only installation/project discovery metadata
  and launch requests have been extracted after Unity, Godot and CryEngine proved that
  common contract.

- No direct Epic/Fab authentication, entitlement, catalog or download protocol
  is implemented; official pages are handed off to the system browser.
- Engine registration intentionally targets only the current-user Unreal registry
  key; machine-wide Epic installations are never modified.
- Clone/update workflows for source repositories are not implemented yet.
- Pull, checkout, merge, rebase, reset, prune and garbage collection are not
  exposed in the first Git milestone.
- No database ORM is used; the small schema is explicit and versioned.
- No global recursive watcher is installed.
- No SDK/toolchain installer, environment-variable writer, SetupAndroid action or
  ADB device mutation is exposed yet.
- No Vault Cache deletion, global/shared DDC clearing, Git LFS prune, Git GC or
  whole-root storage deletion is exposed yet.
- No extension ABI is frozen before the core feature boundaries stabilize.

## Technology references

- [Avalonia desktop getting started](https://docs.avaloniaui.net/docs/get-started/)
- [Avalonia compiled bindings](https://docs.avaloniaui.net/docs/data-binding/compiled-bindings)
- [Avalonia performance guidance](https://docs.avaloniaui.net/docs/app-development/performance)
- [.NET release support](https://learn.microsoft.com/dotnet/core/releases-and-support)
