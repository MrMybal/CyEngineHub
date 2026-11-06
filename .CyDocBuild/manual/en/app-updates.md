---
id: app-updates
title: Update CyEngineHub itself
description: Check published releases and decide when to install a hub update.
section: Settings and maintenance
parent: settings
order: 22
revision: 2
---
# Update CyEngineHub itself {#update-cyenginehub-itself}

The app updater checks published GitHub releases, not uncommitted code, tags alone or local development builds. The default repository is MrMybal/CyEngineHub. Configure the repository, startup check and prerelease channel under Settings → Advanced → Updates.

## Check and decide {#check-and-decide}

A startup check begins after update preferences load, independently of catalog/account initialization, and times out rather than blocking the interface. The sidebar shows checking, current, available or retry after an error. Offline/error does not mean “up to date”. Manual checking remains available when startup checking is disabled.

An available version opens a proposal with notes and actions. Later/Escape dismisses it without downloading; the sidebar reopens it. A normal installed build does not silently download or launch an installer. A local build newer than published releases is not offered a downgrade.

```diagram
{"type":"flow","title":"Workflow steps","nodes":[{"id":"step-0","label":"Find a newer published release allowed by the selected channel","x":20,"y":20,"width":544},{"id":"step-1","label":"Review notes and choose Download and install","x":20,"y":130,"width":544},{"id":"step-2","label":"Download a Windows installer and verify SHA-256","x":20,"y":240,"width":544},{"id":"step-3","label":"Stop the hub cleanly; the updater waits for its process to exit","x":20,"y":350,"width":544},{"id":"step-4","label":"Run the verified installer; reopen and check the version","x":20,"y":460,"width":544}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

## Missing proposal or unavailable installation {#missing-proposal-or-unavailable-installation}

Check repository, channel and saved startup preference. Alpha/beta releases require the appropriate channel unless a release was explicitly published as the project's current non-prerelease. The Latest badge alone is not a quality/stability certification.

Automatic installation needs a compatible Windows installer and a valid GitHub digest or checksum asset. Without them, the release/notes can remain visible while automatic installation is unavailable. A SHA mismatch rejects the downloaded installer. Do not bypass that check with a renamed file.

Finish/cancel important tasks and keep backups before updating. The launcher refuses to run the installer if the old hub does not exit in time. Portable users should prefer the ZIP when retaining their portable location/profile; the Windows installer follows the ordinary installed-app route. Local binaries are not published automatically by building them. See [engine patches](doc:engine-updates) for Unreal revisions and [troubleshooting](doc:troubleshooting) for failures.
