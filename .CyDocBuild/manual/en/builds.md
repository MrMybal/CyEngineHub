---
id: builds
title: Builds, packaging and toolchains
description: Generate IDE files, build targets and prepare packaging presets.
section: Engines and projects
parent: projects
order: 7
revision: 2
---
# Builds, packaging and toolchains {#builds-packaging-and-toolchains}

A build compiles a target; generating IDE files creates a solution/project workspace. Neither is the same as cooking/package distribution. Unreal's tools and supported compiler/SDK must be installed separately.

## Build a project {#build-a-project}

1. Open the project sheet and check its engine association.
2. Open Builds & Packaging. Select the intended preset or create one with target, platform, configuration and operation.
3. For a native editor build, use the project's Editor target, the required platform and Development/DebugGame as appropriate. Shipping normally targets the game, not the editor.
4. Execute and follow the sheet's progress or Actions. Inspect the first meaningful error in the retained output, not only the final exit code.
5. Use Return to project to leave the subpage. Double-click a linked action or use its project link to reopen the sheet while work continues.

Packaging presets expose cooking, staging, packaging, archiving, Pak/IoStore, compression and prerequisites where supported. Review archive paths and target settings; selecting a flag is not a guarantee that every engine/platform combination accepts it. Cross-compilation needs the matching external toolchain.

## Tools and prerequisites {#tools-and-prerequisites}

Run the explicit toolchain analysis to inventory Visual Studio/MSVC, .NET and platform SDK requirements. Detection reads known paths, registry/configuration and bounded version commands. It is not an SDK installer and does not rewrite environment variables. Per-engine recommended/banned compiler ranges matter more than simply choosing the newest MSVC.

Process/User/Machine environment values can differ; restart the affected application after deliberately changing your environment with its normal installer/tools. Android/Quest discovery may start ADB's normal local server, but does not install or launch apps on devices.

## Missing solution or initial build {#missing-solution-or-initial-build}

Check whether creation was Blueprint-only, whether C++ project-file generation finished, and which engine/toolchain generated it. A build alone is not the solution generator. If setup fails, fix the generation error before retrying the editor build. Do not delete Source or Config to repair generated files. See [project creation](doc:projects), [storage](doc:storage) and [troubleshooting](doc:troubleshooting).
