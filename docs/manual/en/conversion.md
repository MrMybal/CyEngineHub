# Source conversion and Unreal version bridges

Conversion is chosen by actual content, not the marketplace name. A store download may contain native Unreal assets, a plugin, Blender scenes, exchange files or unsupported proprietary sources. Keep originals and all referenced textures/dependencies.

![Source-conversion options, including optional target-engine UAsset creation.](image:source-conversion)

## Convert sources into a local derivative

From a local sheet, choose Convert into local derivative. Select Blender → GLB/FBX or RAW sources → UAsset package, options and destination profile. Blender must be installed separately; a target Unreal installation is required for UAssets. Optional chaining first exports exchange files and then imports them in a temporary target-engine project. It does not install into a user project until a separate installation is confirmed.

:::flow
Inspect trusted source files and dependencies
Blender / suitable source converter produces GLB or FBX
Optional target Unreal editor imports RAW data
Save and reopen target assets; inspect validation reports
Publish a distinct local derivative; preserve the original
:::

GLB favors PBR/embedded textures; FBX represents only part of complex materials. Missing external dependencies, procedural shaders, UDIM/video, Geometry Nodes, rigs and unsupported objects may require explicit approximation consent or cause failure. Consent is not a universal bake. Blender autoexec is disabled, but Blender/native codecs and external converters are not security sandboxes.

## Native Unreal version conversion

Select Version Bridge in the installation chain when appropriate. Compiled source/target adapters read supported data and the target engine saves its own packages; changing a version header alone is not a downgrade. Adapters must match installed engine builds.

The tested 5.7 → 5.5/5.3 native path includes a complete qualified pack with textures, materials/functions, static meshes/LODs and two levels. This does not qualify arbitrary gameplay Blueprints, skeletal animation, World Partition, streaming worlds or every plugin. Unsupported non-default properties/classes must remain explicit. Source plugin code may require API changes and recompilation; binary-only plugins cannot be magically retargeted.

Use optional agent repair only with the separate [MCP/code-execution consent](help:ai-mcp), and still validate in the target engine. See [derivatives](help:derivatives) and [extensions/SDK](help:extensions-sdk).
