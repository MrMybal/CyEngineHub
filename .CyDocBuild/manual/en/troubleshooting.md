---
id: troubleshooting
title: Troubleshooting and useful diagnostics
description: Identify common failures and recover without losing original data.
section: Reference
order: 23
revision: 2
---
# Troubleshooting and useful diagnostics {#troubleshooting-and-useful-diagnostics}

Start with the exact failed task, engine/project path, selected artifact/platform and first meaningful error. Retry after correcting a cause, not by repeatedly queuing the same operation. Task phase and terminal state matter more than a lone percentage.

## Common symptoms {#common-symptoms}

- Missing engines/duplicate names: check configured roots, refresh, inspect path tooltips and local aliases. A display rename is not a registry association or directory rename.
- No C++ solution/build: verify C++ versus Blueprint-only, project-file generation, the selected engine, Visual Studio C++/SDK and the first setup error. Fix generation before compilation.
- Pack downloaded again: compare exact source/artifact, engine/platform and local path. An unavailable or different-platform local record cannot safely substitute another variant.
- Installation waiting: inspect overlapping target/source locks, disk budgets and active tasks. Different project names may still share a volume or staging source.
- Conversion fails: read its report for unsupported classes, missing dependencies, adapter build IDs or external-tool errors. Force does not implement missing conversion support.
- Web login/blank browser: check WebView2 runtime, provider availability and use the manual Epic login fallback where offered. Do not disable TLS or share cookies.
- One gallery image/description fallback: richer product metadata may be unavailable; retry explicitly. Catalog covers do not guarantee a complete remote gallery.
- Update requires space: distinguish download, temporary deltas/cache and final growth. Check the running hub version; an old installer does not gain a newer algorithm from edited sources.
- Editor plugin fails: check exact engine build, compiler, plugin activation, hub running/local connection and matching project association. Inspect Actions after a disconnect before retrying.

## Safe recovery {#safe-recovery}

Do not delete an engine-update journal, its delta files, a plugin backup or your only derivative workspace to “unstick” a task. Close file users, preserve reports and let the supported recovery/rollback path run. If a rollback itself fails, retain the journal and original backups for investigation. Never use a blanket disk clean on Source, Content, Config or purchased assets.

## Reporting a bug {#reporting-a-bug}

Record application version, operation, selected variant, target version, task phase and a minimal reproducible sequence. Use a disposable project/asset if possible. Redact usernames, proprietary paths, account IDs, credentials, signed URLs, queue inventories and private asset names from screenshots/logs. Do not publish a purchased pack merely to reproduce the issue.

Open the [project issue tracker](https://github.com/MrMybal/CyEngineHub/issues) only when you choose to use the network. This manual itself remains offline. See [storage](doc:storage), [downloads](doc:downloads), [conversion](doc:conversion) and [builds](doc:builds).
