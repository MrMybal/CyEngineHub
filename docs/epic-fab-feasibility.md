# Epic and Fab feasibility

Status reviewed on 2026-08-26. This matrix is intentionally conservative:
CyEngineHub does not use undocumented Epic Games Launcher OAuth endpoints, reuse EGL
credentials, or claim that an external browser session is an in-app connection.

| Capability | Status in CyEngineHub | Official surface | Without EGL | Risk |
|---|---|---|---:|---|
| Open Epic sign-in/account | Implemented | Official Epic web pages | Yes | Low |
| Verify the signed-in Epic identity | Not available | Requires an application-specific supported authentication integration | Unknown | High |
| EOS/Epic Account Services login | Isolated future contract | Registered EOS Product, Client and Application | Yes | Medium |
| Detect installed Epic artifacts | Implemented | Local `.item` manifests and `LauncherInstalled.dat` | Yes | Low |
| Detect and launch EGL | Implemented, optional | Local executable/Windows installation state | No | Low |
| Inventory Vault Cache/Fab plugin packages | Implemented as local folder counts | Local filesystem only | Yes | Low |
| Open Fab My Library | Implemented | Official Fab web page | Yes | Low |
| List the global owned Fab library inside CyEngineHub | Not exposed | No stable public third-party desktop API confirmed | Unknown | High |
| Add Fab UE content to a target project | Implemented | Fab in Editor/EGL, or confirmed local pack import | Yes after acquisition | Medium |
| Add extracted Fab/Unity assets to a Unity project | Implemented with strict data-only allow-list | Confirmed local pack import after supported acquisition/export | Yes after acquisition | Medium |
| Add portable Fab assets to a Godot project | Implemented with strict portable-data allow-list | Confirmed local pack import after supported acquisition/export | Yes after acquisition | Medium |
| Add portable Fab assets to a CryEngine project | Implemented with strict portable-data allow-list | Confirmed local pack import after supported acquisition/export | Yes after acquisition | Medium |
| Download a Fab product directly inside CyEngineHub | Not exposed | Fab website, EGL and supported editor integrations | Unknown | High |
| Download UE source from GitHub | Implemented | EpicGames/UnrealEngine or an authorized GitHub fork | Yes | Low |
| Download or patch UE binaries directly | Research gate | Official documented route remains EGL | No | High |

Epic Account Services supports account authentication for a product registered
in the Epic Developer Portal; the setup requires product, client and application
configuration. This is not treated as access to the user’s global Fab library.
See Epic’s [Online Subsystem EOS setup](https://dev.epicgames.com/documentation/en-us/unreal-engine/online-subsystem-eos-plugin-in-unreal-engine)
and [Lyra EOS configuration](https://dev.epicgames.com/documentation/en-us/unreal-engine/using-lyra-with-epic-online-services-in-unreal-engine).

Fab documents My Library and acquisition/export through its supported website,
Epic Games Launcher and editor integrations. CyEngineHub therefore opens
[Fab My Library](https://www.fab.com/library) externally and does not emulate an
undocumented launcher client. See Fab’s official
[purchasing and downloading guide](https://dev.epicgames.com/documentation/fab/purchasing-and-downloading-assets-in-fab?lang=en-US)
and [exporting from Fab in Launcher](https://dev.epicgames.com/documentation/en-us/fab/exporting-assets-from-fab-in-launcher).

## Implemented boundary

- official HTTPS handoff for Epic sign-in, account management, Developer Portal,
  Fab and Fab My Library;
- optional EGL executable detection and launch;
- confirmed, cancelable Git clone of `EpicGames/UnrealEngine` or a GitHub fork
  named `UnrealEngine`, with no embedded credentials and no automatic `Setup`;
- bounded, read-only parsing of allow-listed installation metadata;
- local counts for Vault Cache and Fab plugin package directories;
- selected-project launch for the official in-Editor Fab window;
- confirmed local Unreal-content import preserving the source `Content`
  hierarchy, with staging, collision backups, engine-version guard, persistent
  receipts and confirmed rollback that blocks post-import modifications;
- confirmed local Unity asset import into an isolated `Assets/CyEngineHubImports`
  folder, preserving `.meta` GUIDs while rejecting code, shaders and binaries;
- confirmed local Godot asset import into an isolated `CyEngineHubImports`
  folder, rejecting scripts, shaders, native extensions and binaries;
- confirmed local CryEngine asset import into the declared assets directory under
  `CyEngineHubImports`, rejecting code, scripts, shaders and binaries;
- metadata-only local library for already extracted Unreal, Unity, Godot and CryEngine packs under
  explicit configured roots, with engine classification and guarded importer
  routing; it is not an entitlement or remote-library surface;
- SQLite-backed snapshot cache containing inventory data only;
- replaceable `IEpicAuthenticationService`, `IEpicLocalCatalogService` and
  `IFabBackend` contracts.

## Research gate for direct downloads

Before any direct download code enters the product:

1. identify a supported authentication and authorization surface for this type
   of desktop tool;
2. document entitlement, catalog, manifest and CDN provenance;
3. verify the applicable Epic/Fab terms for distributing the integration;
4. prove that the flow does not depend on EGL client secrets, cookies or private
   endpoints;
5. prototype in an isolated backend with revocable local credentials;
6. add resumability, integrity checks, rate limits and auditable consent;
7. integrate only if the supported contract provides the required capabilities.

No password storage, credential reuse, entitlement bypass, DRM bypass or
unauthorized content sharing is acceptable.

## Why the local Fab export socket is not used

The Fab export plugin currently installed on the test machine contains a
localhost TCP implementation used by Fab in Launcher. It is not an official,
versioned public contract and accepts a private JSON payload. CyEngineHub deliberately
does not connect to that port. The official documentation describes export as a
Launcher/plugin workflow, so automation stays behind the replaceable backend
until Epic publishes a supported contract.
