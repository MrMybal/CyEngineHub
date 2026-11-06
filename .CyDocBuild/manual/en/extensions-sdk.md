---
id: extensions-sdk
title: Hub extensions, scripts and conversion SDK
description: Install hub extensions and implement preparation or conversion steps.
section: Plugins and automation
parent: plugins
order: 18
revision: 2
---
# Hub extensions, scripts and conversion SDK {#hub-extensions-scripts-and-conversion-sdk}

Three extension mechanisms have different contracts. A hub extension describes a hub feature; an installation/preparation script operates on a selected pack; an Unreal .uplugin is loaded by Unreal. Installing one does not grant the others' capabilities.

## Declarative hub packages {#declarative-hub-packages}

An extracted extension directory contains extension.json validated against the repository schema. Open Extensions → Install package, choose the directory, inspect the exact target/replacement plan and confirm. The hub fingerprints files, stages and reparses the package, retains a replaced version and promotes the new one. Do not include credentials, cookies or local user paths.

Marketplace manifests use a unique ID, Marketplace/WebLink declarations and an authorized HTTPS inventory URL. Optional common package-storage settings add local-library behavior. A manifest does not implement a provider API by itself. Arbitrary user-extension executables are not automatically trusted or enabled by installing a descriptor.

## Preparation/installation scripts {#preparation-installation-scripts}

Use the script folder exposed by the installation window; add a trusted manifest and executable, then refresh scripts. Declared content/target kinds and options determine where a step can run. A preparation step must output a contained variant of the declared type for the next step; it must not silently install into a user project.

External scripts run with your Windows permissions, not in a security sandbox. Review code and executable paths. Downloading a pack does not authorize embedded code execution. The host validates declared result paths, not the entire behavior of arbitrary code.

## Source-converter SDK {#source-converter-sdk}

IAssetSourceConverter exposes a descriptor, supported extensions/options and ConvertAsync. Register implementations with AssetSourceConverterRegistry.Register. The request gives a source folder, isolated workspace, options and optional target engine; the result provides output, report and content kind. The host owns packaging/cleanup. This C# SDK is not an automatic untrusted-DLL loader.

IFabPreparationScript integrates a converter into a chain using OutputContentKind and Outputs["variant"]. Follow the [converter contract](doc:api-source-converter#signature) and [installation-script contract](doc:api-installation-script#parameters) for public signatures, parameters, results, imports and short C# examples. This manual does not expose implementation source listings. See [conversion](doc:conversion) and [AI/MCP](doc:ai-mcp).
