---
id: library
title: Asset library and local packages
description: Browse assets, inspect exact variants and understand local packages.
section: Assets and libraries
order: 8
revision: 2
---
# Asset library and local packages {#asset-library-and-local-packages}

Library separates sources: Fab, historical Megascans, local packages and marketplace connectors. Search and collections organize loaded catalog data; they do not acquire products you do not own. A marketplace can be enabled while its account remains disconnected.

## Find and inspect an asset {#find-and-inspect-an-asset}

1. Choose the source and refresh its catalog when online data is needed. Cached data can remain visible during refresh or a network failure.
2. Filter/search, switch cards or rows, and open a product sheet.
3. Read the description, available engine/platform variants and local downloads. Click safe links in descriptions; HTML is normalized rather than executed as a web page.
4. Use the gallery's thumbnails/arrows and enlarged view. Some catalog fallbacks contain only one cover; Retry gallery requests richer metadata but cannot invent unavailable images.
5. Choose Download, Install or a specific local derivative deliberately.

Collections and pack groups are local organization; they do not change provider ownership. A group can associate several packs and installation choices, but each pack still has its own content type, license and compatibility. Hiding a product does not delete downloaded files.

## What a local package contains {#what-a-local-package-contains}

A package can be a directory or compressed payload with a JSON sidecar describing source, variant, file inventory, engine version when known, compression and derivation. Keep payload and sidecar together. Native Unreal content, plugins, exchange formats and unknown formats are different kinds; a ZIP extension alone does not establish installability.

The ordered storage libraries select a suitable enabled location subject to quota/space. Changing a setting does not migrate old downloads automatically. A missing local path must be repaired/reimported; the hub must not pretend that a similarly named different-platform package is the same variant.

Read [Fab](doc:fab), [connectors](doc:connectors), [installation](doc:install), [derivatives](doc:derivatives) or [storage](doc:storage) for the corresponding workflow.
