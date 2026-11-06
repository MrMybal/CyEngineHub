---
id: plugins
title: Engine and project plugins
description: Inspect, install and rebuild engine or project plugins.
section: Plugins and automation
order: 16
revision: 2
---
# Engine and project plugins {#engine-and-project-plugins}

Unreal .uplugin packages are not CyEngineHub extensions. Open the plugin inventory, select the intended target and analyze known local libraries. Scans are bounded, skip generated/cache directories and do not traverse junctions. A broken descriptor is a diagnostic entry, not proof that the whole library is unusable.

## Install or build an Unreal plugin {#install-or-build-an-unreal-plugin}

1. Select the plugin source and engine/project target; inspect path, descriptor version and supported platforms.
2. Prepare installation to review the plan without changing the target.
3. Confirm only after checking compatibility and any replacement. Supported installation stages files, rechecks the descriptor, retains a replaced version as backup and promotes the new directory.
4. Compile source plugins for the actual engine. Generated Binaries are omitted when rebuilding from source; copying another engine's DLL is not recompilation.
5. Open the target editor and verify module loading and functionality.

A source plugin from another major/minor may be attempted with a rebuild warning; API adaptation may still be required. A binary-only plugin from another branch is not safely interchangeable. Explicit platform restrictions also matter. Missing version metadata is unknown, not proof of compatibility.

BuildPlugin uses a unique empty/nonexistent output location because Unreal's packaging tool can clear it. Never select the engine or plugin source as output. Keep backups until the replacement is validated; they are not automatically purged.

## Other providers {#other-providers}

Unity local embedded-package installation validates package.json and places content beneath Packages without taking over the package-manager manifest. Godot local editor addons validate plugin.cfg and are not automatically activated in project.godot. Their retained receipts support guarded rollback where provided. CRYENGINE currently exposes declared-plugin inventory, not a general plugin installer.

For a plugin template at project creation, see [projects](doc:projects). For the CyEngineHub Unreal client, see [editor plugin](doc:editor-plugin). For hub-level tools, see [extensions/SDK](doc:extensions-sdk).
