---
id: connectors
title: Marketplace plugins and local imports
description: Download entitled files and register complete kits from marketplace plugins.
section: Assets and libraries
parent: library
order: 11
revision: 2
---
# Marketplace plugins and local imports {#marketplace-plugins-and-local-imports}

Built-in connectors include Cosmos, KitBash3D/Cargo, itch.io, BlenderKit/Blendkit, ArtStation, Gumroad and CGTrader. Each adds a library source and its own Marketplace settings panel. BlenderKit and Blendkit designate the same service, not two independent integrations.

## Common workflow {#common-workflow}

1. Enable the connector, select a download folder and ordered libraries/quotas/compression.
2. Open its official inventory/site. Sign in and complete provider checks yourself in that connector's isolated WebView2 session.
3. Download an entitled file through the website. The hub directs supported transfers to the configured location and indexes completed files, not failed/partial transfers.
4. Open My downloaded assets or the last-download sheet. Alternatively add an existing ZIP or a complete local kit folder.
5. Inspect detected type and contents, edit local name/notes, then configure an installation script or create a derivative.

```diagram
{"type":"flow","title":"Workflow steps","nodes":[{"id":"step-0","label":"Official site or existing complete local kit","x":20,"y":20,"width":504},{"id":"step-1","label":"Completed download / explicit local registration","x":20,"y":130,"width":504},{"id":"step-2","label":"Native local sheet and common JSON/package storage","x":20,"y":240,"width":504},{"id":"step-3","label":"Inspect actual content and select a converter if necessary","x":20,"y":350,"width":504},{"id":"step-4","label":"Review a target and confirm installation","x":20,"y":460,"width":504}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

## Service-specific limits {#service-specific-limits}

Cargo opens My Assets for purchased complete kits; it does not replace the official Cargo DCC/add-on transport. Blendkit downloads requiring its official add-on remain in that client; add the resulting complete folder. Games/tools from itch.io and documents/executables from any store remain files, not Unreal assets. Proprietary .max/.c4d/Maya sources need an appropriate licensed exporter or converter.

These connectors provide official web access and local catalogs, not a blanket implementation of every store's OAuth/API inventory synchronization. They do not scrape ownership, private cookies or signed URLs from cards. Website login does not automatically authenticate Fab or another connector.

Add one complete kit, not an entire mixed library. Keep textures and dependencies together. Reassemble multipart archives externally; RAR/7z extraction is not provided by this connector flow. Browser transfers do not use Fab's connections-per-download setting. Changing a destination affects future transfers, not originals already on disk. See [conversion](doc:conversion), [downloads](doc:downloads) and [extensions](doc:extensions-sdk).
