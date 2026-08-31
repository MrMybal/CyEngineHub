# Storage safety

Storage tooling is useful only if it is more predictable than deleting folders
manually. CyEngineHub therefore separates inventory, planning and deletion.

## Manual and bounded inventory

Startup reads only the last SQLite snapshot. The filesystem is visited after the
user selects **Analyser le stockage**. CyEngineHub measures known engine, project,
DDC, Zen and Vault paths; it does not scan every drive.

Each path is limited to 250,000 entries or five seconds, and an inventory has a
two-minute global budget. Cancellation is checked throughout enumeration.
Directories that cannot be measured completely are marked partial and are never
eligible for Safe Clean. Reparse points, symbolic links and junctions are counted
as skipped boundaries and are not traversed.

Git LFS values come from the existing bounded Git snapshot. Storage analysis does
not enumerate the same object store a second time.

Engine/project footprint totals include their generated subdirectories. DDC and
Safe Clean totals are separate category views and must not be added to footprint
totals as if they were independent disk usage.

## Two-phase Safe Clean

The first action creates a plan showing the exact folder, estimated bytes and
file count. A separate confirmation is required before deletion. Plans expire
after 30 minutes so stale estimates cannot be reused indefinitely.

Execution revalidates the absolute owner root, category and exact expected path.
It rejects volume roots, path escapes, alternate folder names and a target that
has become a reparse point. Nested reparse points are removed as links only and
never followed.

The current allow-list is:

- `<Project>/Intermediate`;
- `<Project>/Binaries`;
- `<Project>/DerivedDataCache`;
- `<Project>/Saved/Logs`;
- `<Project>/Saved/Cooked`;
- `<Project>/Saved/StagedBuilds`;
- `<Engine>/Engine/DerivedDataCache`.

These folders contain Unreal-generated output. Their next build, cook or editor
session can be slower because data must be regenerated.

## Permanent deletion and cancellation

Safe Clean is a permanent deletion, not a move to the Recycle Bin. The UI states
this before confirmation. The operation runs through the task queue and supports
cancellation between entries. Cancellation can leave a partially cleaned target;
the user should run the inventory again before taking another action.

## Read-only categories

CyEngineHub does not currently delete or prune:

- whole engine or project roots;
- global or shared DDC and Zen stores;
- Epic Vault Cache;
- Git LFS objects;
- Git object databases;
- arbitrary user-selected paths.

Those operations require their own dry-run/reference protection and recovery
design before they can be exposed safely.
