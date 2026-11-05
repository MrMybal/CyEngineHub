# Project profiles, features and INI

A profile prepares a new project's plugins and configuration; saving it does not create a project or rewrite existing projects. Open Projects → Creation profiles, or the Unreal profile settings. Select a reference engine to inspect its plugins. Built-in profiles are preserved: customize a copy. Personal profiles can be edited, duplicated and deleted. Import from an existing project when appropriate.

## Configuration precedence

:::flow
Engine defaults and inherited enabled plugins
Selected creation profile
Checked feature packs, in displayed order
Manual plugin overrides
Manually edited INI file replaces that generated file
:::

The last feature pack wins for the same scalar setting or plugin; arrows reorder packs. Changing the profile resets manual overrides/INI edits but retains selected packs. Reset overrides restores generated choices. Reset an INI file restores its generated contents, or removes a manually added file.

The INI tab previews the Default*.ini files written into the new project's Config folder, not every setting inherited from Unreal. Scalar values use layer precedence. Unreal array operators +, -, . and ! retain their order. Do not include paths outside Config.

## Minimal and specialized profiles

Lite/UE4-style profiles remain UE5 projects; a UE4-style rendering configuration does not change the engine version or downgrade assets. Minimal profiles can disable engine plugins by default and explicitly enable essential dependencies. Required absent or explicitly disabled dependencies must be resolved before creation. Installed engine plugins are not deleted by selecting a profile.

Feature packs provide reusable combinations for areas such as VR/OpenXR, Niagara, PCG, gameplay abilities, UI, animation, audio and cinematics. Their presence is not a guarantee of hardware/runtime support. Review the final plugin list and INI preview with the selected engine.

To use a saved profile, return to [project creation](help:projects), select it and inspect the final composition before confirming.
