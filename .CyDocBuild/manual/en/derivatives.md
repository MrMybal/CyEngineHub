---
id: derivatives
title: Derived packages and conversion workspaces
description: Keep a prepared revision and reuse it without changing the supplier download.
section: Assets and libraries
parent: conversion
order: 15
revision: 2
---
# Derived packages and conversion workspaces {#derived-packages-and-conversion-workspaces}

A derivative is a distinct local version associated with its source product or artifact. It must not replace the supplier download. Use it to retain converted, corrected or optimized content and avoid repeating the same preparation for each project.

## Two creation routes {#two-creation-routes}

For supported local source files, Convert into local derivative runs the selected converter and publishes its validated output. For native Unreal packages, Create Derived opens an editing workspace: choose the local source, target engine, final content type, name and notes. The hub prepares a temporary project while preserving the package's Content layout. Open it in the selected editor and make the required edits.

After saving/closing the relevant editor work, Package Data creates a new stored revision. Reopen/verify it before installing into important projects. A derivative can itself be the source of another editing workspace. Version-conversion scripts can also offer saving the prepared result as a derivative; enable that option deliberately.

```diagram
{"type":"flow","title":"Workflow steps","nodes":[{"id":"step-0","label":"Original library artifact remains unchanged","x":20,"y":20,"width":488},{"id":"step-1","label":"Isolated conversion or editing workspace","x":20,"y":130,"width":488},{"id":"step-2","label":"Target-engine validation and packaging","x":20,"y":240,"width":488},{"id":"step-3","label":"New local derivative with source provenance","x":20,"y":350,"width":488},{"id":"step-4","label":"Reuse this exact local revision in project installations","x":20,"y":460,"width":488}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

## Identity and cleanup {#identity-and-cleanup}

The sidecar records source product/artifact, derivation name/notes, final type, engine version where known, date, sizes and compression. Keep the sidecar with its payload. A derivative's engine version must reflect saved content, not a renamed label.

An AssetPack packages Content rather than generated engine/project caches. Temporary workspaces may be explicitly deleted after publication; their already stored derivatives remain. Close Unreal before cleanup. Only hub-owned temporary roots are eligible; do not manually delete an ambiguous folder containing your only edits.

The hub's transactional checks protect its supported workflow, not arbitrary tools you launch inside an editor. Review warnings and make an independent backup of valuable edits. See [conversion](doc:conversion), [installation](doc:install) and [storage](doc:storage).
