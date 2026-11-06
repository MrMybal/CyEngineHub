---
id: conversion
title: Source conversion and Unreal version bridges
description: Convert source formats or Unreal versions while preserving originals.
section: Assets and libraries
parent: install
order: 14
revision: 2
---
# Source conversion and Unreal version bridges {#source-conversion-and-unreal-version-bridges}

Conversion is chosen by actual content, not the marketplace name. A store download may contain native Unreal assets, a plugin, Blender scenes, exchange files or unsupported proprietary sources. Keep originals and all referenced textures/dependencies.

```image
{"id":"source-conversion","src":"assets/screenshots/source-conversion.png","alt":"Source-conversion options, including optional target-engine UAsset creation.","controls":"minimal"}
```

## Convert sources into a local derivative {#convert-sources-into-a-local-derivative}

From a local sheet, choose Convert into local derivative. Select Blender → GLB/FBX or RAW sources → UAsset package, options and destination profile. Blender must be installed separately; a target Unreal installation is required for UAssets. Optional chaining first exports exchange files and then imports them in a temporary target-engine project. It does not install into a user project until a separate installation is confirmed.

```diagram
{"type":"flow","title":"Workflow steps","nodes":[{"id":"step-0","label":"Inspect trusted source files and dependencies","x":20,"y":20,"width":504},{"id":"step-1","label":"Blender / suitable source converter produces GLB or FBX","x":20,"y":130,"width":504},{"id":"step-2","label":"Optional target Unreal editor imports RAW data","x":20,"y":240,"width":504},{"id":"step-3","label":"Save and reopen target assets; inspect validation reports","x":20,"y":350,"width":504},{"id":"step-4","label":"Publish a distinct local derivative; preserve the original","x":20,"y":460,"width":504}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

GLB favors PBR/embedded textures; FBX represents only part of complex materials. Missing external dependencies, procedural shaders, UDIM/video, Geometry Nodes, rigs and unsupported objects may require explicit approximation consent or cause failure. Consent is not a universal bake. Blender autoexec is disabled, but Blender/native codecs and external converters are not security sandboxes.

## Native Unreal version conversion {#native-unreal-version-conversion}

Select Version Bridge in the installation chain when appropriate. Compiled source/target adapters read supported data and the target engine saves its own packages; changing a version header alone is not a downgrade. Adapters must match installed engine builds.

The 5.7 → 5.5/5.3 native bridge handles supported textures, materials/functions, static meshes/LODs and simple levels. This is not universal support for gameplay Blueprints, skeletal animation, World Partition, streaming worlds or every plugin. Inspect the per-asset conversion report and validate the result in the target editor. Unsupported non-default properties/classes remain explicit. Source plugin code may require API changes and recompilation; binary-only plugins cannot be retargeted by changing a version label.

Use optional agent repair only with the separate [MCP/code-execution consent](doc:ai-mcp), and still validate in the target engine. See [derivatives](doc:derivatives) and [extensions/SDK](doc:extensions-sdk).
