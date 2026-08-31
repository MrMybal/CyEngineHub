# Multi-engine providers

CyEngineHub separates product-wide concerns from engine-specific behavior. The
desktop composes a validated registry from independent provider assemblies:

| Provider | Status | Project markers | Current scope |
| --- | --- | --- | --- |
| Unreal Engine | Active | `.uproject`, `.uplugin` | Discovery, launch, source acquisition, build/package, plugins and guarded Fab import |
| Unity | Active | `ProjectSettings/ProjectVersion.txt`, `Assets/` | Bounded discovery/launch, UPM inventory, confirmed embedded-package install/removal/restore, Asset Bridge and guarded Fab import |
| Godot Engine | Active | `project.godot` | Bounded discovery/launch, addon/GDExtension inventory, confirmed local editor-plugin install/removal/restore, Asset Bridge and guarded Fab import |
| CryEngine | Active | `.cryproject`, `.cryengine` | Bounded configured/Launcher/official-registry discovery, required-engine matching, read-only declared-plugin inventory and Sandbox launch |

## Contract rules

`CyEngineHub.Engines` owns only neutral metadata: engine family, stable provider
ID, vendor, status, project markers and capability flags. It does not reference
Unreal, Unity, Godot, CryEngine, Epic or Fab.

Each provider owns its SDK assumptions, filesystem conventions, launcher
integration and compatibility rules. A provider must not add a capability flag
until the corresponding operation is implemented, cancellation-aware and covered
by provider tests. Planned modules therefore report `None` even though their
roadmap is visible in the UI. Unity, Godot and CryEngine report only the discovery,
version, launch, package-inventory and safe local package-management capabilities that
are implemented and covered by provider tests. CRYENGINE does not expose package
installation or removal.

## Active local discovery

Unity checks configured locations and the conventional Unity Hub Editor roots.
It recognizes a project only when both `Assets/` and
`ProjectSettings/ProjectVersion.txt` exist, then requires the exact declared
Editor version before launching with `-projectPath`.

Godot checks configured portable locations, conventional program locations and
exact editor names available through `PATH`. It reads a bounded `project.godot`,
uses `config/name` and `config/features`, and selects an editor with the same
declared major/minor before launching with `--editor --path`.

CryEngine checks configured roots, conventional Launcher locations and the
documented `%PROGRAMDATA%/Crytek/CRYENGINE/cryengine.json` registry. It recognizes
bounded `.cryengine` and `.cryproject` JSON descriptors, matches the required engine
identity or compatible version, then launches Sandbox with `-project`.

## Native package inventory

`ILocalEnginePackageProvider` is an optional neutral, read-only contract. It does
not imply that CyEngineHub can install, remove or update a package. A manual refresh
on the Plugins page asks each active provider to inspect only its already discovered
projects:

- Unity merges direct dependencies from `Packages/manifest.json` with the resolved
  graph from `Packages/packages-lock.json`, including depth, source and dependencies;
- Godot inspects direct children of `addons`, reads bounded `[plugin]` metadata from
  `plugin.cfg`, reports the editor activation marker and recognizes bounded
  `.gdextension`/legacy `.gdnlib` descriptors;
- CRYENGINE reads the bounded `require.plugins` array of `.cryproject`, preserving
  native/managed type and the declared lookup path.

File sizes, traversal depth, directory counts and result counts are capped. Reparse
points are refused. Unity package manifests are never edited, external local-package
paths are not followed, and a missing CRYENGINE project-local path is not treated as
an error because the engine searches its binary plugin directory second. This follows
the official [Unity project manifest](https://docs.unity3d.com/Manual/upm-manifestPrj.html),
[Unity lock file](https://docs.unity3d.com/Manual/upm-conflicts-auto.html),
[Godot plugin](https://docs.godotengine.org/en/stable/tutorials/plugins/editor/making_plugins.html)
and [CRYENGINE project/plugin](https://www.cryengine.com/docs/static/engines/cryengine-5/categories/23756813/pages/26874870)
formats.

`ILocalEnginePackageInstallationProvider` is a separate optional contract. Unity
and Godot currently implement it only for a user-selected directory already present
on disk:

- Unity validates `package.json`, its safe reverse-domain ID and its minimum `unity`
  version, then embeds the package beneath `Packages/<id>` without editing the
  Package Manager-owned project manifest;
- Godot accepts one validated editor addon containing `plugin.cfg` and a contained
  script, copies it beneath `addons/<id>`, and deliberately does not enable it in
  `project.godot`.

Both providers use the same expiring two-phase transaction, SHA-256 inventories,
target fingerprint, isolated staging and retained replacement backup. A versioned
receipt is persisted per project. The Plugins page reloads these receipts after a
restart and can prepare a second confirmed transaction that restores the backup—or
removes a new installation—only if installed and backup fingerprints still match.
The removed version is retained under `rolled-back` for manual recovery. See
[Package installation safety](package-install-safety.md).

`ILocalEnginePackageManagementProvider` adds a distinct removal/restoration
transaction. Unity exposes it only for standalone embedded packages that can be
removed without rewriting `manifest.json`. Godot exposes it only for project-owned
addons that are not currently enabled. The complete tree is fingerprinted, moved
to a project-local recovery directory after confirmation, recorded in a persistent
receipt and restorable after restart only while both destination and fingerprint
remain safe.

All project scanners are manual, bounded to 25,000 directories, eight levels
and 500 projects. Generated directories and reparse points are skipped. The
official command-line references are the
[Unity Editor command-line manual](https://docs.unity3d.com/6000.0/Documentation/Manual/EditorCommandLineArguments.html)
the [Godot command-line tutorial](https://docs.godotengine.org/en/stable/tutorials/editor/command_line_tutorial.html),
and CRYENGINE’s official [project descriptor documentation](https://www.cryengine.com/docs/static/engines/cryengine-5/categories/23756813/pages/26874870)
and [command-line reference](https://www.cryengine.com/docs/static/engines/cryengine-5/categories/23756816/pages/25535471).

## Activation order

For each new engine, implementation proceeds capability by capability:

1. bounded installation and project discovery;
2. editor and project launch;
3. version and compatibility model;
4. packages/plugins/addons;
5. build and export/package workflows;
6. local and remote asset routing.

Fab entries are not globally installable by default. The target provider must
recognize the content format and validate the selected project before an import
action is enabled. Unreal's transactional importer remains provider-specific.
Unity has its own data-only transactional importer for already extracted packs:
it validates the project marker, preserves `.meta` GUIDs, rejects active code and
keeps receipts outside `Assets`. Godot has a parallel portable-data importer that
validates `project.godot`, rejects scripts/shaders/native extensions and keeps
receipts below `.cyenginehub/FabImports/Godot`. CryEngine has the equivalent
strict portable-data importer:
it resolves `content/assets` from a unique `.cryproject`, rejects code/binaries and
keeps receipts below `.cyenginehub/FabImports/CryEngine`. Asset Bridge destinations remain portable-file
staging surfaces, not Fab entitlement or download clients.

## Transition of the current code

The existing `Projects`, `Build`, `Plugins`, `Toolchains` and `Storage` assemblies
form the working Unreal vertical slice. They are not automatically treated as
cross-engine APIs. Common contracts will be extracted into neutral assemblies
only after a second real provider demonstrates that the semantics match; this
avoids encoding Unreal concepts under generic names.
