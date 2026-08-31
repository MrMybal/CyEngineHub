# Plugin safety and lifecycle

Plugin discovery and installation are deliberately separate operations.

## Discovery

- the last plugin catalogue is read from SQLite at startup;
- filesystem discovery starts only after **Analyser les plugins** or after adding
  a local library;
- scans are limited by depth and directory count;
- `.git`, `.vs`, `Binaries`, `Intermediate`, `Saved`, `DerivedDataCache` and
  `Content` are not traversed while looking for descriptors;
- reparse points, symbolic links and junctions are not followed;
- malformed descriptors are retained as broken catalogue entries with typed
  diagnostics instead of aborting the complete scan.

Git status is still a separate manual action. Valid plugin roots are added as Git
repository candidates only when the user requests a Git analysis.

## Compatibility

CyEngineHub compares a descriptor's `EngineVersion`, `SupportedTargetPlatforms` and
module platform allow/deny lists with the selected target:

- a source plugin from another UE major/minor is allowed with a rebuild warning;
- a binary-only plugin from another UE major/minor is rejected;
- an explicitly unsupported target platform is rejected;
- missing engine-version metadata is shown but is not guessed as incompatible.

These checks are a preflight guard, not a promise that arbitrary plugin source
will compile against a newer Unreal API.

## Transactional installation

Pressing **Préparer l’installation** only calculates a plan. No target is changed
until **Confirmer l’installation** is pressed.

The confirmed workflow is:

1. inventory the source and re-check compatibility;
2. copy into a unique `.cyenginehub-staging-*` directory inside the target's Plugins
   directory;
3. omit generated folders and omit `Binaries` when source code is available;
4. parse the staged `.uplugin` again;
5. move an existing destination to `.cyenginehub-backups/<plugin>-<timestamp>-<id>`;
6. atomically move staging to the final plugin directory;
7. restore the backup if the final move fails.

Old versions are retained; CyEngineHub does not automatically delete plugin backups.
Source/destination overlap and link-based target/source directories are rejected.

## BuildPlugin

`RunUAT.bat BuildPlugin` can clear an existing package directory. CyEngineHub therefore
requires the output to be nonexistent or empty and rejects any output that
overlaps the engine or plugin source. The desktop uses a unique path below:

    <CyEngineHub data>\PluginPackages\<Plugin>\UE-<major.minor>\Win64-<timestamp>-<id>

The operation uses the shared cancellable task runner and persistent UAT logs.
