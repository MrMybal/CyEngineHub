---
id: engines
title: Engines and associations
description: Identify engine installations, rename labels and manage associations.
section: Engines and projects
order: 2
revision: 2
---
# Engines and associations {#engines-and-associations}

The engine catalog distinguishes official installations, source builds and other providers. Refresh reads configured and conventional locations; discovery is not an engine installation. Check the path and exact version in a sheet before applying changes.

```image
{"id":"engines","src":"assets/screenshots/engines.png","alt":"Engine catalog with fictional official and source installations.","controls":"minimal"}
```

## Managing an Unreal installation {#managing-an-unreal-installation}

1. Open Engines and select the installation. Use its path or folder action to distinguish otherwise similar versions.
2. Rename the local display label when you maintain several builds. The alias should also appear in project-creation engine choices; it does not rename the folder or change Unreal's version.
3. Inspect the association identifier. Editing the association updates Unreal's registration for this installation, not every project's descriptor automatically. Check affected projects explicitly.
4. Launch opens the chosen editor; engine-home launch does not silently select a recent project. Close it before modifying its binaries or editor plugins.
5. Inspect installation components, plugins, toolchains and size from the corresponding subpage. A partial size is a lower bound, not exact accounting.

Unregistering removes registration/catalog information; it is not the same as uninstalling files. Read the separate deletion plan before any engine removal. Do not alter an installation being used by an editor or compiler.

## Source and alternate providers {#source-and-alternate-providers}

Source engines expose branch/source metadata and Git inspection where a repository exists. Fetch is not a build or pull. Use the [Git chapter](doc:git) before updating source code.

Unity discovery recognizes its project version and Assets folder and requires the matching editor. Godot matches the declared major/minor where available. CRYENGINE reads its project/engine descriptors and declared engine identity. Their supported launch, inventory and package operations are not identical to Unreal's; do not infer a shared installer or universal conversion from a provider appearing in the catalog.

For official patch revisions, see [engine updates](doc:engine-updates). For missing compilers or SDKs, see [builds and toolchains](doc:builds).
