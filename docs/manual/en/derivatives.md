# Derived packages and conversion workspaces

A derivative is a distinct local version associated with its source product or artifact. It must not replace the supplier download. Use it to retain converted, corrected or optimized content and avoid repeating the same preparation for each project.

## Two creation routes

For supported local source files, Convert into local derivative runs the selected converter and publishes its validated output. For native Unreal packages, Create Derived opens an editing workspace: choose the local source, target engine, final content type, name and notes. The hub prepares a temporary project while preserving the package's Content layout. Open it in the selected editor and make the required edits.

After saving/closing the relevant editor work, Package Data creates a new stored revision. Reopen/verify it before installing into important projects. A derivative can itself be the source of another editing workspace. Version-conversion scripts can also offer saving the prepared result as a derivative; enable that option deliberately.

:::flow
Original library artifact remains unchanged
Isolated conversion or editing workspace
Target-engine validation and packaging
New local derivative with source provenance
Reuse this exact local revision in project installations
:::

## Identity and cleanup

The sidecar records source product/artifact, derivation name/notes, final type, engine version where known, date, sizes and compression. Keep the sidecar with its payload. A derivative's engine version must reflect saved content, not a renamed label.

An AssetPack packages Content rather than generated engine/project caches. Temporary workspaces may be explicitly deleted after publication; their already stored derivatives remain. Close Unreal before cleanup. Only hub-owned temporary roots are eligible; do not manually delete an ambiguous folder containing your only edits.

The hub's transactional checks protect its supported workflow, not arbitrary tools you launch inside an editor. Review warnings and make an independent backup of valuable edits. See [conversion](help:conversion), [installation](help:install) and [storage](help:storage).
