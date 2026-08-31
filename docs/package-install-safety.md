# Local package installation safety

CyEngineHub installs only packages that the user has already downloaded and
extracted into a chosen local directory. It does not download archives, query a
remote registry or execute package code during preparation or installation.

## Supported targets

- Unity: a package root containing a bounded valid `package.json` is copied as an
  embedded package to `<project>/Packages/<package-name>`. The reverse-domain ID,
  target project and optional minimum `unity` version are validated. CyEngineHub
  does not edit `Packages/manifest.json` or `packages-lock.json`; Unity documents
  that a folder directly beneath `Packages` is automatically treated as embedded.
- Godot: either an addon root containing `plugin.cfg`, or an extracted repository
  containing exactly one `addons/<id>/plugin.cfg`, is copied to
  `<project>/addons/<id>`. The declared script must exist inside that addon. A new
  addon is not added to the enabled list in `project.godot`; activation remains an
  explicit action in Godot Project Settings.
- CRYENGINE: inventory only. Copying binaries and changing `require.plugins` are
  not enabled because engine/ABI compatibility needs a stronger provider-specific
  model.

The layouts follow Unity’s official
[embedded dependency](https://docs.unity3d.com/Manual/upm-embed.html) and Godot’s
[editor plugin installation](https://docs.godotengine.org/en/stable/tutorials/plugins/editor/installing_plugins.html)
documentation.

## Two-phase transaction

Preparation is read-only and creates an expiring plan:

1. validate the engine project marker and provider ownership;
2. validate the package descriptor, identity and contained entry script;
3. inventory at most 25,000 files and 2 GiB;
4. reject symbolic links, junctions, traversal and volume roots;
5. compute SHA-256 for every source file;
6. fingerprint any existing destination;
7. display file/byte counts, replacement state and code-loading risk.

Execution accepts only a confirmed, non-expired plan. It recomputes the complete
source inventory and existing-target fingerprint. Any change after confirmation
aborts before mutation. Files are copied to
`<project>/.cyenginehub/PackageOperations/<Engine>/staging`, parsed again there,
and compared with the source inventory.

For replacement, the installed directory is first moved to the operation’s
`backups` directory. If the final staging move fails, that backup is restored
immediately. On success the backup is retained and its exact location is shown in
the UI. Only the unique unused staging directory is cleaned automatically.

The commit includes a schema-versioned JSON receipt below the project-owned
`receipts` directory. Receipt creation is part of the transaction: if atomic
persistence fails, the newly installed directory is removed from the destination
and the former version is restored. Invalid, oversized or linked receipt files are
ignored by the bounded history loader.

## Confirmed rollback

The Plugins page reloads installation receipts after project discovery and displays
their active or rolled-back state. Preparing an annulment is read-only and verifies:

- the persisted receipt still equals the selected history entry;
- the currently installed tree still matches its installation fingerprint;
- a previous-version backup, when present, still matches its original fingerprint;
- the recovery path is unused and remains below the project workspace.

After a second explicit confirmation, the installed tree is moved to `rolled-back`.
For a replacement, the retained backup is moved back to the engine-native package
destination. For a new installation, the destination is simply left absent. The
removed version is never deleted automatically and its recovery path is persisted
in the updated receipt. If receipt updating fails, the filesystem permutation is
reversed before the operation reports failure.

## Confirmed removal and restoration

The separate `ILocalEnginePackageManagementProvider` contract exposes removal only
for project-owned layouts that do not require editing an engine manifest:

- Unity: only standalone embedded packages reported as `Package embarqué` beneath
  `Packages/<id>`. A package also declared as a direct `manifest.json` dependency
  remains read-only;
- Godot: direct children of `addons/<id>` only after an editor plugin has been
  disabled. CyEngineHub does not leave a knowingly active `enabled_plugins` entry
  pointing to a missing directory;
- CRYENGINE: no removal capability.

Preparation inventories and fingerprints the complete installed tree and creates a
15-minute plan. Confirmation revalidates that exact inventory, then moves the
directory beneath the project-owned `removed` workspace. Nothing is recursively
deleted. A schema-versioned receipt is written atomically beneath
`removal-receipts`; if persistence fails, the directory is moved immediately back
to its native destination.

The Plugins page reloads removal receipts after restart. Restoration requires a
second expiring plan and is refused when the native destination is occupied, the
receipt changed, or the recoverable tree no longer has its removal fingerprint.
After confirmation the directory is moved back and the receipt is marked restored.
If that receipt update fails, the filesystem move is reversed.

## Trust boundary

Descriptor validation does not make third-party code trustworthy. Unity packages
can contain C# or native code, and Godot editor plugins execute inside the editor
after activation. The confirmation panel states this risk. Users should inspect
the source and use version control before opening the target project.
