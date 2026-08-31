# Roadmap

## Milestone 0 — foundation (implemented)

- desktop shell and navigation;
- local database, settings and logs;
- task scheduler and shared process runner;
- neutral provider registry and dedicated Unreal, Unity, Godot and CryEngine
  module boundaries;
- validated CyEngineHub extension manifests, transactional local installation
  and first-party-only execution policy, with a JSON Schema and example package;
- project-aware Unreal ↔ Unity ↔ Godot ↔ CryEngine Asset Bridge staging with bounded preflight
  inspection and post-confirmation revalidation;
- active Unity/Godot/CryEngine local providers with bounded installation/project discovery,
  version compatibility checks, editor/project launch and provider-neutral native
  package inventory;
- confirmed transactional local-package installation for Unity embedded packages
  and Godot editor addons, including persistent receipts, guarded rollback and
  reversible removal/restoration for safe project-owned packages;
- Unreal engine and project discovery;
- engine/project launch;
- automated non-UI tests.

## Milestone 1 — Unreal developer workflows

1. Source engine actions: Setup, GenerateProjectFiles, Development Editor UBT,
   register and confirmed unregister are implemented. Clone/update and dedicated
   Clean/Rebuild buttons remain; Clean/Rebuild are already available to presets.
2. Project Editor builds and UAT BuildCookRun packaging are implemented, with
   live/persistent logs, cancellation, six built-in presets and locally persisted
   custom presets.
3. Manual cached Git snapshots are implemented for engines, projects and plugins: branch,
   commit, worktree counts, upstream divergence, remotes, bounded LFS analysis
   and fetch. Targeted event invalidation remains.
4. Local plugin libraries, `.uplugin` parsing and compatibility, transactional
   engine/project installation and guarded Win64 `BuildPlugin` are implemented.
   Git/archive/Fab acquisition and multi-target BuildPlugin profiles remain.
5. Manual cached toolchain inventory is implemented for Visual Studio, MSVC,
   Windows SDK, .NET, CMake/Ninja/Clang, Linux cross-toolchains and Android
   SDK/NDK/JDK/Gradle/ADB. Per-engine requirements, environment-variable scopes
   and read-only Quest discovery are implemented. Guided installation, variable
   editing and device actions remain.
6. Manual cached storage analysis is implemented for engine/project footprints,
   generated folders, DDC, Zen data, Vault Cache, Git LFS and volumes. Bounded
   measurement, low-space warnings and confirmed Safe Clean for exact generated
   paths are implemented. Vault/global cache cleanup and LFS prune dry-runs
   remain.

Exit criteria: the application is useful for daily local Unreal development even
with every Epic/Fab network feature disabled.

## Milestone 2 — multi-engine provider activation

- Unity: Hub/custom Editor discovery, project-version parsing and exact-version
  launch, UPM manifest/lock/embedded inventory, guarded Fab import and confirmed
  local embedded-package installation plus reversible standalone-embedded removal
  are implemented; registry/Git acquisition and manifest-based update/removal remain;
- Godot: portable/PATH editor discovery, `project.godot` parsing, compatible
  launch, bounded addon/GDExtension inventory and guarded portable Fab import are
  implemented; confirmed local editor-addon installation and reversible removal
  of disabled addons are implemented while remote acquisition and guarded export
  presets remain;
- CryEngine: configured/Launcher/official-registry installation discovery,
  `.cryengine`/`.cryproject` parsing, required-engine matching and Sandbox/project
  launch plus declared-plugin inventory are implemented; plugin installation and
  build entry points remain;
- generic compact project and installation view models are implemented for active
  local providers; selected-provider detail pages remain;
- add asset compatibility routing so Fab content is offered only to valid target
  engines and project versions;
- extend Asset Bridge with provider-owned import/export adapters while keeping
  raw engine formats outside the generic copier;
- define a signed publisher/trust model before enabling executable extensions
  supplied by users;
- activate each capability independently only after provider-specific tests.

Exit criteria: at least Unity and Godot can be discovered, can launch a project
and can receive one validated local asset/package format without Unreal code in
their execution path.

## Milestone 3 — advanced local workflows

- fork/upstream comparison;
- build queue resource policies and queryable persistent histories (plain
  operation logs already exist);
- deeper packaging diagnostics (initial compiler, Android, Git LFS and plugin
  hints already exist);
- guarded Pull/checkout/merge/rebase workflows and Git maintenance;
- advanced multi-target clean, retained cleanup history and recovery policies
  (per-target Safe Clean with a mandatory estimate already exists);
- Android/Quest install, launch and logcat;
- CLI over the same modules.

## Milestone 4 — Epic/Fab boundary and research

- implemented: inspect only allow-listed local EGL manifests, Vault Cache and
  Fab plugin package locations;
- implemented: official browser handoff for Epic sign-in/account and Fab My
  Library, without importing browser/EGL credentials;
- implemented: classify capabilities as local, browser-only, not exposed or
  configuration-required behind replaceable contracts;
- implemented: selected-project handoff to Fab in Unreal Editor and guarded local
  import of already extracted Unreal content packs, including persistent history
  and confirmed fingerprint-validated rollback;
- implemented: cached local library of explicitly configured extracted Unreal,
  Unity, Godot and CryEngine packs, with bounded manual discovery, engine classification and
  guarded-import routing;
- implemented: confirmed GitHub clone for official/authorized Unreal source
  repositories, followed by the existing explicit Setup/build workflow;
- remaining: identify a supported entitlement/catalog/download surface and
  validate its terms before building an isolated prototype.

## Milestone 5 — direct Epic downloads

Only after the research gate and a supported integration surface:

- resumable chunk download;
- integrity verification;
- component selection;
- delta patching;
- bandwidth limits and scheduling;
- complete isolation in CyEngineHub.Epic.
