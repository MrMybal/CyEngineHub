---
id: welcome
title: Welcome to CyEngineHub
description: Set up your workspace and learn how to use the manual.
section: Getting started
order: 0
revision: 2
---
# Welcome to CyEngineHub {#welcome-to-cyenginehub}

CyEngineHub is a local-first Windows workspace for engines, projects and asset libraries. This manual describes the delivered Alpha features, not every feature offered by the upstream engines. It is bundled with the application: reading, searching, diagrams and illustrations work without an account or network connection.

## First session {#first-session}

1. Open Settings (Paramètres). Configure engine providers and the locations you actually use. Avoid pointing discovery at an entire drive.
2. Refresh Engines and Projects. Inspect detected versions, paths and associations before launching anything.
3. Connect Epic/Fab only if you need your owned online library. Other marketplace plugins have their own web sessions.
4. Configure each marketplace's download location, ordered libraries, quotas and compression before large transfers.
5. Try a small, trusted pack in a disposable project before installing or converting important content.

```diagram
{"type":"flow","title":"Workflow steps","nodes":[{"id":"step-0","label":"Configure known engine and project locations","x":20,"y":20,"width":512},{"id":"step-1","label":"Review detected installations and associations","x":20,"y":130,"width":512},{"id":"step-2","label":"Choose an exact pack variant or a local source","x":20,"y":240,"width":512},{"id":"step-3","label":"Review destination, script and options","x":20,"y":350,"width":512},{"id":"step-4","label":"Confirm, monitor the task, then verify in the target editor","x":20,"y":460,"width":512}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

## How this manual works {#how-this-manual-works}

This manual uses the bundled CyDoc reader in its own window, opened directly from a local file:// URL. Choose English or Français in its toolbar. English is the default; help language is remembered independently of the partial application-interface localization. Ctrl+K opens local search. Back and Forward navigate articles; Return to app closes the help window without changing the application screen. F1 opens help for the current screen. Escape dismisses image enlargement, search or menus; it does not close the entire manual. If WebView2 is unavailable, Open in browser opens the same offline files in your installed browser. No server, CDN or account is needed.

Use the image's expand control to enlarge it inside the window; use the wheel to zoom, drag to pan, and Reset view to reset. The minimal image toolbar also exposes annotation controls. Diagrams are accessible static SVGs, not interactive node editors. Screenshots use fictional data and show the application's French labels; each caption explains the view in the selected manual language. The reader remembers article positions while navigating and reopening. Settings, annotations and bookmarks remain in the local reader profile.

Search from the field at the top or press Ctrl+K. Expand a sidebar topic to see its subpages. Hover or focus a page title to read its complete title and description. The toolbar offers six themes; changing a theme does not change the application's appearance. My space contains your private bookmarks and notes.

```gallery
{"id":"workspace-gallery","title":"Choose the workspace for your task","controls":"minimal","items":[{"type":"image","src":"assets/screenshots/engines.png","alt":"Engine catalog: distinguish installations by their local label and path.","title":"Engine installations","description":"Use the engine catalog to choose the exact official or source installation."},{"type":"image","src":"assets/screenshots/project-creation.png","alt":"Project creation: choose the engine, project type and parent folder.","title":"Create a project","description":"Check the project type and destination before confirming creation."},{"type":"image","src":"assets/screenshots/source-conversion.png","alt":"Source conversion: select a converter and inspect target-engine options.","title":"Convert source assets","description":"Prepare a derivative without changing the supplier's original files."}]}
```

## Before making changes {#before-making-changes}

Keep independent backups or source control. A successful file operation does not prove that every shader, Blueprint or plugin behaves identically in a different engine. Review warnings and reports. An enabled connector is not the same as an authenticated account. A local development build is not necessarily published on GitHub.

Continue with [navigation](doc:navigation), [project creation](doc:projects), or [installing a pack](doc:install). If a step fails, use [troubleshooting](doc:troubleshooting).
