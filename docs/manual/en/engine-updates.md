# Updating an official Unreal engine

Engine patches are separate from [CyEngineHub application updates](help:app-updates). Official Epic installations use Epic distribution metadata; source/Git engines must use their repository workflow. A newer revision label is not proof that a patch was installed.

## Apply a patch

1. Authenticate Epic/Fab, open the exact engine sheet and check the official revision. Use the context-menu update action or the sheet button when a newer revision is available.
2. Close editors and compilers using that installation. Verify the displayed existing directory, not a second installation path.
3. Confirm the operation. Follow Actions and the patch plan in the engine sheet.
4. After success, refresh discovery and check Build.version/the engine version. Reopen the editor only after the task finishes.

:::flow
Read target manifest and hash installed files
Reuse verified local chunks; download missing chunks through a bounded cache
Prepare changed byte ranges plus original bytes for undo
Verify target hashes, lock originals and publish the recovery journal
Apply ranges; commit Build.version last
Success: clean temporary data / Failure: restore journaled bytes
:::

## Understanding disk estimates

The updater no longer reconstructs every modified binary as a complete second file. It prepares differences and rollback bytes. The initial figure is explicitly a minimum reserve before those differences are measured. Free space is checked during preparation; the measured plan separates network transfer, reversible deltas, bounded chunk cache, file growth, metadata and safety margin. Already prepared verified deltas can be reused.

A patch can still contain substantial new or entirely changed data. Its transfer size is not its extra disk requirement, and neither equals the whole installation size. Existing component tags are used to avoid adding absent optional platforms. New components belong to an explicit installation choice.

## Interruption and restrictions

Secure in-place patching currently requires Windows. Original fingerprints and exclusive file locks prevent committing against changed/in-use binaries; multiple hard links are refused. Cancellation or commit failure attempts journal-based restoration. The next update attempt recovers an interrupted transaction before new work. Do not manually delete a staging folder containing a journal: it may hold the only rollback bytes. A leftover lock file alone is not an active update or installed revision.

Use [troubleshooting](help:troubleshooting) if space, locking or recovery fails. Keep independent backups; automated tests are not a universal validation of every commercial engine patch.
