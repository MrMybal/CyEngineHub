# CyEngineHub extensions

CyEngineHub extensions are not Unreal `.uplugin` packages. They add hub-level
tools, asset processors or engine integrations through an extracted directory
containing a validated `extension.json` file.

## Current execution policy

The catalog accepts built-in and user extensions. User packages can be installed
transactionally and declarative HTTPS integrations can be opened. A user package
cannot run a local executable yet, even if its manifest requests one. Local
execution is allow-listed to the exact built-in `cyenginehub.asset-bridge`
identity and `CyEngineHub.AssetBridge.Tool.exe` entry point.

This is intentional: an external executable runs with the current Windows
account's permissions and is not a sandbox. A future release may add signed
publishers and a per-extension trust grant; a manifest alone will never imply
trust.

## Manifest example

The versioned [JSON Schema](../extensions/extension.schema.json) is the public
contract for `extension.json`. A complete declarative package is available in
[examples/extensions/studio.example.asset-guide](../examples/extensions/studio.example.asset-guide).
It can be copied, edited and selected directly from **Extensions Hub → Installer
un package**.

IDs use lowercase letters, digits, dots and hyphens. Versions use semantic
`major.minor.patch` syntax. Websites must be absolute HTTPS URLs without embedded
credentials. Entry points, when declared, must be contained `.exe` paths and may
use only `{Input}`, `{Output}`, `{SourceEngine}` and `{TargetEngine}` placeholders.

The optional `$schema` property enables editor completion. Runtime validation is
still authoritative, and schema validity never grants permission to execute a
binary.

## Installation safety

Before confirmation CyEngineHub:

- rejects volume roots and reparse points;
- parses the manifest and checks the contained entry point;
- inventories at most 2,000 files and 512 MiB;
- records the size, timestamp and SHA-256 of every file;
- shows the exact target and whether an existing version will be replaced.

After confirmation the source is fingerprinted again, copied into staging and
parsed a second time. An existing version is moved into `.backups` before the
staging directory is atomically promoted.

## Built-in Asset Bridge

Asset Bridge transfers these exchange formats without overwriting an existing
destination:

- meshes/scenes: FBX, OBJ, glTF and GLB;
- textures: PNG, JPEG, TGA, TIFF, EXR and HDR;
- audio: WAV and OGG.

Before presenting the confirmation, CyEngineHub performs a bounded inspection of
the source and reports transferable bytes/files, engine-native files and
unsupported files. A plan is refused when no portable asset is present, when the
direction uses the same engine, or when either side is not Unreal/Unity/Godot/CryEngine. The
inspection is capped at 50,000 entries, 10,000 transferable files and 20 GiB.
Reparse points are never followed.

For Unreal → Unity, select either the Unity project root or an existing/future
folder beneath its `Assets` directory. Selecting the root automatically creates a
unique `Assets/CyEngineHubImports/<source>-<plan>` destination. For Unity → Unreal,
select the `.uproject`, its project root or a path already beneath the bridge
staging area. A project root resolves to
`Saved/CyEngineHubAssetBridge/<source>-<plan>`. Import into Unreal Content Browser
from that staging area afterward.

For a Godot target, select `project.godot`, the project root or an existing/future
path beneath `CyEngineHubImports`. Selecting the project resolves to
`CyEngineHubImports/<source>-<plan>` inside the project. Godot documents that
portable source assets placed in the project folder are imported automatically;
the generated `.godot/imported` cache remains owned by the editor and is never
written by the bridge.

For a CryEngine target, select its unique `.cryproject`, project root or a path
already below `CyEngineHubImports` in the assets directory declared by
`content/assets`. The descriptor is bounded to 1 MiB and the resolved assets path
must remain inside the project without crossing a reparse point.

The input is inspected again and the project marker/output are resolved again
after confirmation. A changed selection or changed target project invalidates the
plan. The bridge executable then refuses every destination collision.

It deliberately ignores Unreal `.uasset`/`.umap`, Unity native assets/metadata,
Godot scenes/resources/scripts, CryEngine-native resources and source code. Epic documents that assets saved
by a newer Unreal version are not readable by an older one, and Unreal's FBX
pipeline itself is the supported interoperability surface for meshes. Raw
Blueprint graphs, Niagara systems, native materials and engine-specific metadata
require a specialized provider or editor plugin.

## Asset Downgrader integration

The built-in Asset Downgrader entry is a link to the third-party Fab product; the
commercial plugin itself is not redistributed by CyEngineHub. Its Fab page calls
the workflow experimental, requires backups and notes that some targets/assets
need a custom Unreal build. CyEngineHub therefore opens the official listing and
continues to manage the installed `.uplugin` through the existing Unreal Plugins
page instead of patching `.uasset` files itself.

References:

- [Epic: versioning of Unreal assets and packages](https://dev.epicgames.com/documentation/unreal-engine/versioning-of-assets-and-packages-in-unreal-engine)
- [Epic: Unreal FBX content pipeline](https://dev.epicgames.com/documentation/unreal-engine/fbx-content-pipeline-in-unreal-engine)
- [Godot: asset import process](https://docs.godotengine.org/en/stable/tutorials/assets_pipeline/import_process.html)
- [Fab: Asset Downgrader](https://www.fab.com/listings/86a0ce48-ff38-493c-906d-8d2297067ee6)
