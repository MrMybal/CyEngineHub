---
id: glossary
title: Glossary, privacy and licenses
description: Understand package terminology, privacy boundaries and component licenses.
section: Reference
order: 24
revision: 2
---
# Glossary, privacy and licenses {#glossary-privacy-and-licenses}

## Terms used throughout the hub {#terms-used-throughout-the-hub}

- Engine installation: a specific directory/build, not merely “Unreal 5.5”. Alias: its local display name. Association: the identifier used to connect a project to an installation.
- Project: an engine-native workspace/descriptor. Profile: reusable new-project defaults, not a live editor for every existing project.
- Asset/product: a catalog identity. Artifact/variant: a precise platform/version distribution. Local package: downloaded/stored payload plus metadata.
- RAW/exchange: source data such as FBX/GLB/images, not already serialized UAssets. Native content: engine-specific files such as .uasset/.umap. Plugin: code/content described for a host engine.
- Derivative: a separate prepared version with source provenance. Staging/workspace: temporary processing, not necessarily disposable before a failed transaction is recovered.
- Manifest/sidecar: machine-readable file/type/version metadata. Chunk: a distribution block. Delta: changed ranges plus undo data for an engine patch.
- Cache: reusable temporary data. Quota: placement allowance. Footprint: measured disk contents, possibly partial. Download bytes, extra disk space and final size are different quantities.
- Build: compile a target. Generate project files: produce an IDE workspace. Cook: prepare content for a platform. Package/archive: assemble a distributable output.
- Enabled: integration exposed. Connected: account/session available. Verified: a particular check succeeded. Queued/running/completed: different task states.
- Alpha/experimental: explicit limited validation, not universal compatibility. Force: attempt despite a preflight warning, not guaranteed correctness.

## Local data and privacy {#local-data-and-privacy}

Catalogs, preferences, receipts, queues and logs are local application data. Browser profiles and Windows secure sessions remain sensitive. AI clients may forward the metadata they read to their provider. Keep backups and redact diagnostics before sharing. The help reader makes no network request unless you click an external link.

## Project licensing {#project-licensing}

The hub's original code, scripts and documentation use GPL-3.0-only, with separately licensed components and third-party notices. The original Unreal Editor client and native conversion adapter code have separate MIT notices. Engine code, marketplace assets and user projects retain their own rights; these licenses do not grant ownership of supplier assets.

Consult the bundled LICENSE, LICENSING.md, THIRD_PARTY_NOTICES.md, LICENCES and Licenses folders for the actual notices and scope. A distribution must preserve the applicable licenses and provide corresponding source as required; the manual is not a substitute for those texts or a universal legal compatibility assessment.

Return to [getting started](doc:welcome) or [troubleshooting](doc:troubleshooting).
