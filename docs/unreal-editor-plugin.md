# CyEngineHub inside Unreal Editor · Alpha

The native **CyEngineHub** editor-only plugin lives in
`extensions/unreal/CyEngineHub`. It uses Slate (not a browser), and a separate
same-Windows-account named pipe. No marketplace credentials leave the hub;
there is no HTTP listener, purchase API, shell endpoint or AI/MCP dependency.

## Installation and use

1. In CyEngineHub open the selected **engine sheet → CyEngineHub dans Unreal →
   Installer le plugin éditeur…**. Close editors using that engine first.
2. Confirm the engine path. The hub compiles the small plugin against that exact
   engine, then publishes it to `Engine/Plugins/Marketplace/CyEngineHub`.
   Visual Studio with the engine's C++ toolchain and Windows SDK is required.
   No engine sources are copied; temporary build projects are removed even on failure.
3. The descriptor sets `EnabledByDefault: true` and declares only an `Editor`
   module: it is not included in packaged games. A project that explicitly disables
   the plugin (or all default engine plugins) still needs to enable it.
4. Register the open `.uproject` in CyEngineHub, with its correct engine association.
   Open **Unreal → Window / Fenêtre → CyEngineHub · Alpha** and click
   **Connecter / Actualiser**. The hub must remain running.
5. Search owned pack names and collections, select an exact compatible variant,
   and confirm installation into the displayed open project. Search is paged in
   groups of 40; it does not load all product images into editor memory.

Precompiled QA ZIPs can also be used with their **exact matching engine build**:
close Unreal, extract the `CyEngineHub` folder into
`Engine/Plugins/Marketplace` (do not merge over an existing installation), then
enable the local editor connection in the desktop engine sheet. Otherwise use
the hub's source-build installer so the DLL/module ID matches the actual engine.

The hub uses the existing download/cache/compression/library and task scheduler.
The **Options dans le Hub…** button prepares the product's reviewed installation
screen without starting an installation, for advanced scripts and conversion.
The editor polls only tasks linked to its project; successful content imports
refresh `/Game` in the asset registry. Closing the tab does not cancel confirmed
hub tasks. If transport fails after confirmation, inspect hub tasks rather than
assuming the action was canceled. Request IDs suppress replay in the current
bridge session. Toggle the connection on/off in the engine sheet independently
of AI/MCP; the setting is remembered.

## Safety and current scope

- Direct editor installation supports compatible native Fab content packs and
  project plugins. An exact version/platform match is checked in both the session
  and the normal installation workflow. Opening the same project in another engine
  branch is rejected until its hub association is corrected.
- Live imports use the strict conflict policy: **no existing asset/plugin is
  replaced**, including saved script preferences that would normally replace with
  backup. The direct editor action uses an isolated additive script configuration;
  it neither inherits nor changes saved advanced script preferences. New native
  plugins require an editor restart; no DLL hot-loading occurs.
- Full projects cannot be injected into an existing project. Conversion,
  relocation, RAW processing and external scripts remain reviewed hub workflows,
  editor closed. Other marketplace entries can appear in searches but are not
  presented as directly installable without a compatible native variant.
- The Alpha does not yet embed all hub configuration screens, product galleries,
  or pack-group editing. Those remain in the desktop app.
- Existing engine plugin directories are never overwritten by the installer.
  No real engine or user project is changed by the QA compilation script.

## Build compatibility

Source compatibility is intended for Windows UE **4.27 and 5.0–5.8**, with separate
binaries for each engine build. An engine's `.modules` build ID must match; never
copy a 5.8 DLL into 5.5, or assume all patch builds use the same ID.

`tools/qa/build-editor-plugin.ps1 -EngineRoots <engine paths> -OutputRoot
<fresh repository artifacts directory>` builds isolated projects, exports only
plugin sources/descriptors/DLL/module manifest, and cleans generated intermediates.
Build success is not by itself a complete UI/import validation; consult the QA
results recorded for the current change. UE 5.0 and 5.1 are not installed on the
current development machine.
