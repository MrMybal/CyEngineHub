---
id: projects
title: Create and open projects
description: Choose Blueprint or C++, select a folder and follow initial setup.
section: Engines and projects
order: 5
revision: 2
---
# Create and open projects {#create-and-open-projects}

Use Projects → New project. The Unreal creator has Project & profile, Features, Plugins, INI configuration, Collections and Post-creation tabs. Creating a project is separate from creating a reusable profile.

```image
{"id":"project-creation","src":"assets/screenshots/project-creation.png","alt":"Actual project-creation form using fictional project and engine locations.","controls":"minimal"}
```

## Required choices {#required-choices}

1. Select the engine family and Unreal project type: C++ or Blueprint only. A Blueprint-only project does not require the same native starter module/build as C++.
2. Select the actual engine installation. Custom names, version and path tooltip distinguish duplicate versions. Inspect the association instead of treating its identifier as a project name.
3. Enter a technical project name and an existing parent folder. You can type the path or use Browse to open the Windows folder picker. The project goes in its own subfolder; an existing project must not be overwritten.
4. Choose a profile, feature packs and plugins. Review INI files and dependencies before creation.
5. Configure collections and post-creation actions only when required, then confirm.

```diagram
{"type":"flow","title":"Workflow steps","nodes":[{"id":"step-0","label":"Validate name, parent folder and exact engine","x":20,"y":20,"width":648},{"id":"step-1","label":"Write project descriptor, source/configuration and requested plugin template","x":20,"y":130,"width":648},{"id":"step-2","label":"Generate project files for C++","x":20,"y":240,"width":648},{"id":"step-3","label":"Queue initial editor build when required","x":20,"y":350,"width":648},{"id":"step-4","label":"Open the new project sheet; follow setup/build progress","x":20,"y":460,"width":648}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

## After creation {#after-creation}

The project sheet opens directly. C++ setup generates project files before the initial editor build; look for the generated solution/project output and inspect Actions if generation fails. A directory containing a .uproject is not proof that the native setup completed. Keep the selected engine's compiler and Windows SDK available.

Post-creation plugin generation offers the engine's available templates, such as Blank, Content Only, Blueprint Library or editor templates. Availability depends on that engine's Plugin Browser templates; do not assume every template exists in every version. Generated C++ plugins need compilation; Content Only does not create a native module.

Opening an existing project uses its detected association. Fix a missing engine association deliberately rather than silently opening with a random version. See [profiles](doc:profiles), [builds](doc:builds), [plugins](doc:plugins) and [troubleshooting](doc:troubleshooting).
