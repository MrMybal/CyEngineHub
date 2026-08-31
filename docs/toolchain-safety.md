# Toolchain safety

CyEngineHub treats the developer environment as valuable shared state. The current
Toolchains milestone is an inventory and compatibility layer, not an installer.

## Explicit execution only

Startup only loads the last Toolchains snapshot from SQLite. Detection begins
when the user selects **Analyser les toolchains**. It does not poll, install a
watcher or recursively scan every drive.

The inventory reads known registry keys, environment variables, Unreal metadata
and standard installation roots. File reads are bounded. Unreal requirements are
read from `Windows_SDK.json`, `Android_SDK.json` and `Linux_SDK.json` when
available, with bounded source-file fallbacks for older engines.

## Processes that may run

An explicit refresh may run these local read-only commands when available:

- `vswhere` to enumerate Visual Studio instances;
- `dotnet --list-sdks` to enumerate .NET SDKs;
- `adb devices -l` to enumerate Android and Meta Quest devices.

Output capture is bounded and the shared process runner supports cancellation.
ADB enumeration can start the normal local ADB server, but CyEngineHub does not send
install, launch, stop, logcat, pull or shell commands in this milestone.

## State CyEngineHub does not change

The Toolchains page does not:

- run Visual Studio Installer, Android Studio installers, SetupAndroid or package
  managers;
- install, update or remove SDKs, compilers, JDKs, NDKs or cross-toolchains;
- write `ANDROID_HOME`, `ANDROID_SDK_ROOT`, `JAVA_HOME`, `NDKROOT`,
  `UE_SDKS_ROOT` or `LINUX_MULTIARCH_ROOT`;
- modify Process, User or Machine environment-variable scopes;
- mutate a connected Android or Quest device.

The three environment scopes are displayed independently because a value visible
to CyEngineHub's process can differ from the persisted user or machine configuration.

## Compatibility semantics

Compatibility is evaluated per Unreal Engine installation. The newest installed
MSVC is not automatically selected: CyEngineHub first honors Unreal's preferred
ranges and excludes versions that Unreal marks as banned. Required and installed
versions remain visible beside every result so warnings can be audited.

Future installers and device actions must be introduced as separate, explicit,
confirmable operations with a preview, cancellation and retained logs.
