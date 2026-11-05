# Storage, quotas and safe cleaning

Storage is the dedicated disk-usage workspace. Startup reads its cached snapshot; an explicit analysis visits known engine/project/DDC/Vault paths under time/entry limits, not every drive. Partial measurements and skipped junctions must not be presented as exact totals.

## Read the figures correctly

Engine/project footprints already include generated subfolders. Do not add DDC or Safe Clean category totals to them as if they were independent usage. Quotas are placement limits, not reserved physical space. The first enabled ordered library with quota and free space is chosen for a new package. Cache, temporary download, final package, decompressed content and rollback data can coexist.

Increasing compression can reduce the final package but uses CPU/time and does not eliminate staging needs. Changing a folder setting does not move existing data. Keep manifests/sidecars beside their payloads. Do not move active transfers or journals manually.

## Safe Clean

:::flow
Analyze known locations and inspect partial flags
Prepare a plan for an allowed generated folder
Review exact path, bytes and permanent-deletion warning
Confirm; revalidation occurs before removal
Reanalyze after completion or cancellation
:::

Safe Clean permanently deletes allowed generated output; it is not the Recycle Bin. Supported project targets include Intermediate, Binaries, DerivedDataCache, Saved/Logs, Saved/Cooked and Saved/StagedBuilds, plus the engine's Engine/DerivedDataCache. Regeneration can take time and require a working toolchain. Close users of the affected files first. A canceled clean may be partial.

Whole engine/project roots, arbitrary folders, shared/global DDC/Zen, Vault Cache and Git/LFS object stores are not general Safe Clean targets. Inventory is not deletion permission. Incomplete or changed/reparse-point plans must be refused. Keep Source, Content, Config and original downloads out of ad-hoc cleanup.

A .cyenginehub-update folder with a recovery journal may contain essential undo data; see [engine updates](help:engine-updates) before touching it. Use [downloads](help:downloads) to control parallel temporary-space demand.
