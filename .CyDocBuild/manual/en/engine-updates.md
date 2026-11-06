---
id: engine-updates
title: Updating an official Unreal engine
description: Apply an official Unreal patch and understand its disk requirements.
section: Engines and projects
parent: engines
order: 3
revision: 2
---
# Updating an official Unreal engine {#updating-an-official-unreal-engine}

Engine patches are separate from [CyEngineHub application updates](doc:app-updates). Official Epic installations use Epic distribution metadata; source/Git engines must use their repository workflow. A newer revision label is not proof that a patch was installed.

## Apply a patch {#apply-a-patch}

1. Authenticate Epic/Fab, open the exact engine sheet and check the official revision. Use the context-menu update action or the sheet button when a newer revision is available.
2. Close editors and compilers using that installation. Verify the displayed existing directory, not a second installation path.
3. Confirm the operation. Follow Actions and the patch plan in the engine sheet.
4. After success, refresh discovery and check Build.version/the engine version. Reopen the editor only after the task finishes.

```diagram
{"type":"flow","title":"Workflow steps","nodes":[{"id":"step-0","label":"Read target manifest and hash installed files","x":20,"y":20,"width":648},{"id":"step-1","label":"Reuse verified local chunks; download missing chunks through a bounded cache","x":20,"y":130,"width":648},{"id":"step-2","label":"Prepare changed byte ranges plus original bytes for undo","x":20,"y":240,"width":648},{"id":"step-3","label":"Verify target hashes, lock originals and publish the recovery journal","x":20,"y":350,"width":648},{"id":"step-4","label":"Apply ranges; commit Build.version last","x":20,"y":460,"width":648},{"id":"step-5","label":"Success: clean temporary data / Failure: restore journaled bytes","x":20,"y":570,"width":648}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"},{"from":"step-4","to":"step-5"}]}
```

## Understanding disk estimates {#understanding-disk-estimates}

The updater no longer reconstructs every modified binary as a complete second file. It prepares differences and rollback bytes. The initial figure is explicitly a minimum reserve before those differences are measured. Free space is checked during preparation; the measured plan separates network transfer, reversible deltas, bounded chunk cache, file growth, metadata and safety margin. Already prepared verified deltas can be reused.

A patch can still contain substantial new or entirely changed data. Its transfer size is not its extra disk requirement, and neither equals the whole installation size. Existing component tags are used to avoid adding absent optional platforms. New components belong to an explicit installation choice.

## Interruption and restrictions {#interruption-and-restrictions}

Secure in-place patching currently requires Windows. Original fingerprints and exclusive file locks prevent committing against changed/in-use binaries; multiple hard links are refused. Cancellation or commit failure attempts journal-based restoration. The next update attempt recovers an interrupted transaction before new work. Do not manually delete a staging folder containing a journal: it may hold the only rollback bytes. A leftover lock file alone is not an active update or installed revision.

Use [troubleshooting](doc:troubleshooting) if space, locking or recovery fails. Keep independent backups; automated tests are not a universal validation of every commercial engine patch.
