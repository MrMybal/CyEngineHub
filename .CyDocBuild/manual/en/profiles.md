---
id: profiles
title: Project profiles, features and INI
description: Compose new-project plugins, feature packs and INI settings.
section: Engines and projects
parent: projects
order: 6
revision: 2
---
# Project profiles, features and INI {#project-profiles-features-and-ini}

A profile prepares a new project's plugins and configuration; saving it does not create a project or rewrite existing projects. Open Projects → Creation profiles, or the Unreal profile settings. Select a reference engine to inspect its plugins. Built-in profiles are preserved: customize a copy. Personal profiles can be edited, duplicated and deleted. Import from an existing project when appropriate.

## Configuration precedence {#configuration-precedence}

```diagram
{"type":"flow","title":"Workflow steps","nodes":[{"id":"step-0","label":"Engine defaults and inherited enabled plugins","x":20,"y":20,"width":464},{"id":"step-1","label":"Selected creation profile","x":20,"y":130,"width":464},{"id":"step-2","label":"Checked feature packs, in displayed order","x":20,"y":240,"width":464},{"id":"step-3","label":"Manual plugin overrides","x":20,"y":350,"width":464},{"id":"step-4","label":"Manually edited INI file replaces that generated file","x":20,"y":460,"width":464}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

The last feature pack wins for the same scalar setting or plugin; arrows reorder packs. Changing the profile resets manual overrides/INI edits but retains selected packs. Reset overrides restores generated choices. Reset an INI file restores its generated contents, or removes a manually added file.

The INI tab previews the Default*.ini files written into the new project's Config folder, not every setting inherited from Unreal. Scalar values use layer precedence. Unreal array operators +, -, . and ! retain their order. Do not include paths outside Config.

## Minimal and specialized profiles {#minimal-and-specialized-profiles}

Lite/UE4-style profiles remain UE5 projects; a UE4-style rendering configuration does not change the engine version or downgrade assets. Minimal profiles can disable engine plugins by default and explicitly enable essential dependencies. Required absent or explicitly disabled dependencies must be resolved before creation. Installed engine plugins are not deleted by selecting a profile.

Feature packs provide reusable combinations for areas such as VR/OpenXR, Niagara, PCG, gameplay abilities, UI, animation, audio and cinematics. Their presence is not a guarantee of hardware/runtime support. Review the final plugin list and INI preview with the selected engine.

To use a saved profile, return to [project creation](doc:projects), select it and inspect the final composition before confirming.
