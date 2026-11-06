---
id: editor-plugin
title: Use CyEngineHub inside Unreal Editor
description: Connect Unreal Editor to the hub and install into its open project.
section: Plugins and automation
parent: plugins
order: 17
revision: 2
---
# Use CyEngineHub inside Unreal Editor {#use-cyenginehub-inside-unreal-editor}

The editor-only CyEngineHub plugin provides owned-library search and installation into the open project while the desktop hub remains running. It uses native Slate and a same-Windows-account named pipe, not a marketplace password in Unreal or an HTTP/shell service. It is independent of AI/MCP.

## Install and connect {#install-and-connect}

1. Close editors using the engine. In its CyEngineHub sheet, choose Install editor plugin and confirm the exact engine path.
2. The hub builds against that engine using Visual Studio C++/Windows SDK and installs into Engine/Plugins/Marketplace/CyEngineHub. No whole-engine copy is created.
3. The descriptor enables an Editor module by default. A project that explicitly disables the plugin/default engine plugins still needs to enable it. It is not a runtime module packaged into the game.
4. Register the project's .uproject in the hub with the correct engine association and enable the local editor connection.
5. In Unreal, open Window → CyEngineHub · Alpha and Connect/Refresh. Keep the desktop hub open.

Precompiled plugin ZIPs require an exact matching engine build/module ID. If Unreal reports a mismatch, compile from the original plugin source for your installation rather than editing the descriptor version to conceal it.

## Install from the editor {#install-from-the-editor}

Search the owned library, inspect the selected variant and confirm the displayed open project. Direct editor import supports the compatible native content/plugin scope and uses strict additive conflict handling: existing assets/plugins are not replaced. For conversion or advanced chains, use Options in the Hub to review the normal desktop installation window without automatically starting work.

Task progress is scoped to the project. Closing the tab does not cancel already confirmed hub tasks. If the pipe disconnects after confirmation, inspect Actions before retrying. Successful content import refreshes the asset registry; restart/rebuild may still be needed for native plugin modules.

See [installation](doc:install), [plugins](doc:plugins) and [troubleshooting](doc:troubleshooting).
