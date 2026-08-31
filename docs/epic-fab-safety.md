# Epic and Fab safety

CyEngineHub’s Epic/Fab page separates three different states that must not be
confused: a browser session owned by Epic/Fab, Epic Games Launcher running as a
separate process, and local installation metadata read by CyEngineHub.

## Authentication boundary

- sign-in opens `https://www.epicgames.com/id/login` in the system browser;
- account management opens the official Epic account page;
- Fab and My Library open official `fab.com` pages;
- CyEngineHub does not embed a password form or intercept redirects;
- CyEngineHub does not read browser cookies, EGL tokens, cached passwords or client
  secrets;
- the UI never labels the account as connected because CyEngineHub cannot verify the
  external session.

## Local inventory boundary

Inventory is manual and bounded. The parser accepts at most 5,000 `.item`
manifests, rejects a manifest larger than 2 MB and rejects
`LauncherInstalled.dat` above 10 MB. It reads only these installation fields:

- artifact/application identifiers;
- display name and version;
- installation location;
- catalog item/namespace identifiers;
- main Unreal application association used for classification.

All other JSON fields are ignored. The snapshot cache contains only those typed
fields, launcher presence/path, local folder counts, diagnostics and the refresh
timestamp. No raw manifest is cached.

Vault Cache and `%LOCALAPPDATA%\FabPlugins` are counted only at their top level,
up to 10,000 directories. The inventory does not modify, download, verify or
delete their contents.

## Process and network boundary

- EGL is launched only after an explicit button press and only from a detected
  executable path;
- only absolute HTTP/HTTPS addresses supplied by the official backend contracts
  can be opened;
- inventory refresh performs no network request;
- startup loads the last inventory snapshot and performs no filesystem scan;
- no private Epic/Fab API endpoint is present in the implementation.

Direct Epic/Fab downloads remain behind the research gate in
[Epic/Fab feasibility](epic-fab-feasibility.md).

## Unreal source download

- repositories must use HTTPS on `github.com`, contain no embedded credentials,
  and target `UnrealEngine` or `UnrealEngine.git`;
- the branch and destination are validated and passed through structured process
  arguments rather than a shell command;
- the destination must be absent or empty and cannot be a volume root or reparse
  point;
- clone runs in the cancelable task queue with terminal prompts disabled and a
  persistent log;
- a shallow depth-1 clone is optional;
- successful output must contain `Setup.bat` and an `Engine` directory;
- CyEngineHub retains a failed/canceled partial clone for inspection and never runs
  `Setup.bat` automatically.

Access to Epic’s repository requires the user to have linked their Epic and
GitHub accounts as described in the official
[source download guide](https://dev.epicgames.com/documentation/en-us/unreal-engine/downloading-source-code-in-unreal-engine).

## Fab content import

The preferred path is the official Fab window in Unreal Editor. CyEngineHub can also
import an already acquired and extracted Unreal content pack when it contains a
`Content` directory with `.uasset` or `.umap` files.

- the user selects an existing CyEngineHub project target;
- source and target must be separate trees and no reparse point is followed;
- inventory is capped at 100,000 supported files and 100 GB;
- executable/script extensions are not copied;
- when both `.uproject` versions are numeric, content from a newer engine is
  blocked from an older target;
- the 30-minute plan reports bytes, skipped files and collisions before explicit
  confirmation;
- all files are staged below target `Saved` before modification;
- replaced target files are retained below `Saved\CyEngineHubFabBackups`;
- cancellation or failure triggers rollback of created/replaced files;
- success writes an atomic receipt below `Saved\CyEngineHubFabImports`, including
  SHA-256 fingerprints for installed files and collision backups;
- the desktop reloads these receipts as a per-project operation history;
- a later rollback requires a fresh 30-minute plan and a second confirmation;
- rollback stages the current imported files first, restores replaced files,
  deletes files created by the import and marks the receipt as rolled back;
- any missing or modified installed file/backup blocks the whole rollback, so
  edits made after import are never silently discarded;
- raw Vault Cache chunks, Fab entitlements and the plugin’s undocumented local
  socket are never consumed.

### Extracted Unity asset packs

The Unity workflow is a separate provider-specific importer. It accepts a chosen
directory, or the `Assets` directory of an extracted Unity project, and requires
the target to contain both `Assets` and `ProjectSettings/ProjectVersion.txt`.

- inventory is capped at 50,000 supported files and 20 GiB;
- `.meta` files are copied unchanged so existing Unity GUIDs are preserved;
- C# scripts, assembly definitions, shaders, native plugins, archives containing
  executable code and shell scripts are rejected rather than silently copied;
- unknown non-executable formats are reported and skipped;
- the destination is restricted to
  `Assets/CyEngineHubImports/<sanitized-pack-name>`;
- staging, backups and receipts live below `.cyenginehub/FabImports/Unity`, never
  below `Assets`, so Unity does not import operational metadata;
- replacement swaps the whole isolated pack directory only after every staged
  file passes SHA-256 verification;
- rollback accepts Unity-generated `.meta` sidecars but refuses a modified,
  missing or unexpected asset;
- the receipt must match the persisted project-local receipt and cannot redirect
  rollback to another project directory;
- target sidecar removal, pack restoration and receipt update are transactional.

This imports only content already present on disk. It does not enumerate Fab
entitlements, download products, parse Vault Cache chunks or invoke Fab’s private
localhost export protocol.

### Extracted Godot-compatible asset packs

The Godot workflow targets a recognized project containing `project.godot` and
installs each pack below `CyEngineHubImports/<sanitized-pack-name>`.

- inventory is capped at 50,000 supported files and 20 GiB;
- portable models, textures, audio and text/data files use a strict allow-list;
- GDScript/C# code, Godot shaders and native extensions, executable binaries and
  shell scripts reject the pack;
- native Godot scenes/resources such as `.tscn` and `.tres` are reported and
  skipped by this first conservative route;
- staging, backups and receipts live below `.cyenginehub/FabImports/Godot`;
- staged copies and replacement backups are fingerprinted before the atomic
  directory swap;
- rollback validates the persisted project-local receipt plus both imported and
  backup tree fingerprints, and refuses missing, modified or unexpected content.

Like the Unity route, this imports only content already present on disk and never
acts as a Fab entitlement, download or private export client.

### Extracted CryEngine-compatible asset packs

The CryEngine workflow requires one bounded, valid `.cryproject` descriptor. It
resolves the first relative directory declared in `content/assets` and installs
the pack below `<declared-assets>/CyEngineHubImports/<sanitized-pack-name>`.

- inventory is capped at 50,000 supported files and 20 GiB;
- portable models, textures, audio and text/data files use a strict allow-list;
- C/C++/C#/Lua code, scripts, shaders, native plug-ins and binaries reject the pack;
- CryEngine-native resources and metadata are reported and skipped;
- staging, backups and receipts live below `.cyenginehub/FabImports/CryEngine`;
- the descriptor, assets path, source inventory, target and backup fingerprints
  are revalidated around the atomic directory replacement;
- rollback requires the persisted project-local receipt and refuses missing,
  modified or unexpected imported/backup content.

This route also consumes only content already present on disk.

## Local extracted-pack library

This library is a convenience index over folders chosen by the user. It is not
the Fab account library and does not prove ownership or entitlement.

- scanning is manual; startup loads only the last metadata-only cache;
- whole volume roots and root reparse points are rejected;
- discovery is capped at depth 6, 50,000 directories per configured root and 500
  packs;
- each pack inventory is capped at 100,000 supported files and 100 GB and is
  marked partial when a limit or inaccessible subtree is encountered;
- Unreal packs require `.uasset` or `.umap` below `Content`; Unity packs require
  at least one supported data asset below `Assets`; Godot packs require
  `project.godot` and at least one portable data asset; CryEngine packs require a
  unique `.cryproject`, a contained declared assets directory and portable data;
- package directories containing a `.uplugin` are excluded from the Unreal
  content route and remain the responsibility of the plugin workflow;
- Unity, Godot and CryEngine code, scripts, shaders and binaries are counted and displayed as blockers;
  they are never classified as transferable data;
- selecting a pack routes its source to the Unreal, Unity, Godot or CryEngine planner; it never
  bypasses compatibility analysis or explicit import confirmation.
