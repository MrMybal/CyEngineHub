---
id: navigation
title: Navigation and task states
description: Find catalogs, return to detail sheets and understand task states.
section: Getting started
parent: welcome
order: 1
revision: 2
---
# Navigation and task states {#navigation-and-task-states}

The left sidebar separates Engines, Projects, Library, Extensions and Tools from activity pages: Storage, Actions, Downloads, Log and Settings. Help / Documentation and F1 open this manual without changing the selected project or product.

## Catalogs and detail sheets {#catalogs-and-detail-sheets}

Use search and filters to narrow a catalog. Card, compact/image and row presentations vary by catalog; changing presentation does not move files. Favorites are local organization, not vendor bookmarks. Opening a sheet shows actions for that exact installation, project or asset. Engine aliases are labels; paths and associations determine the real target.

Use the sheet's return button to go back to its catalog. Returning from a Fab product restores asset browsing rather than jumping to an unrelated application section. Project subpages retain a return route to the project. The help reader has its own article history; closing it does not navigate the application's history.

## Actions versus Downloads {#actions-versus-downloads}

Actions tracks builds, installs, analyses and conversions. Downloads tracks supported library transfers. Operations continue while you browse other pages. Open the available project/product/engine action on a row, or double-click a linked action where supported, to return to its target. A task with multiple targets can expose multiple links.

- Waiting: accepted, but a scheduler budget, shared destination or source lock prevents starting yet.
- Running: work is active; read the current phase, not only the percentage.
- Completed: the operation returned success and the indicator uses its completion state.
- Failed: inspect the reason and logs; do not assume nothing happened before the failure.
- Canceled: cancellation was requested and processed; already committed earlier steps may remain.

100% for a transfer phase does not mean that extraction, compression, target validation or installation has finished. Trust the terminal task state. Some external tools cannot report a precise percentage and use an indeterminate indicator.

Connection badges open the matching connection settings. Active/enabled, authenticated and verified Git access are distinct states. See [downloads and scheduling](doc:downloads), [connections](doc:settings) and [troubleshooting](doc:troubleshooting).
