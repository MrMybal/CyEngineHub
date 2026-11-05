# Create and open projects

Use Projects → New project. The Unreal creator has Project & profile, Features, Plugins, INI configuration, Collections and Post-creation tabs. Creating a project is separate from creating a reusable profile.

![Actual project-creation form using fictional project and engine locations.](image:project-creation)

## Required choices

1. Select the engine family and Unreal project type: C++ or Blueprint only. A Blueprint-only project does not require the same native starter module/build as C++.
2. Select the actual engine installation. Custom names, version and path tooltip distinguish duplicate versions. Inspect the association instead of treating its identifier as a project name.
3. Enter a technical project name and an existing parent folder. You can type the path or use Browse to open the Windows folder picker. The project goes in its own subfolder; an existing project must not be overwritten.
4. Choose a profile, feature packs and plugins. Review INI files and dependencies before creation.
5. Configure collections and post-creation actions only when required, then confirm.

:::flow
Validate name, parent folder and exact engine
Write project descriptor, source/configuration and requested plugin template
Generate project files for C++
Queue initial editor build when required
Open the new project sheet; follow setup/build progress
:::

## After creation

The project sheet opens directly. C++ setup generates project files before the initial editor build; look for the generated solution/project output and inspect Actions if generation fails. A directory containing a .uproject is not proof that the native setup completed. Keep the selected engine's compiler and Windows SDK available.

Post-creation plugin generation offers the engine's available templates, such as Blank, Content Only, Blueprint Library or editor templates. Availability depends on that engine's Plugin Browser templates; do not assume every template exists in every version. Generated C++ plugins need compilation; Content Only does not create a native module.

Opening an existing project uses its detected association. Fix a missing engine association deliberately rather than silently opening with a random version. See [profiles](help:profiles), [builds](help:builds), [plugins](help:plugins) and [troubleshooting](help:troubleshooting).
