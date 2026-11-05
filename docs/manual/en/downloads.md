# Downloads, resume and concurrency

Two settings govern different resources: the global number of simultaneous library downloads, and native Fab/Megascans connections per download. More connections do not necessarily improve speed; server limits, compression, CPU and disk can dominate.

## Configure transfers

Use Settings → Downloads for global parallelism and startup resume. Use each Marketplace panel for its native connections (1–32 where offered), storage and compression. Saving a lower limit does not interrupt transfers already active; it restricts subsequent starts. Browser connectors use their own website transfer behavior, not Fab's chunk-connection control.

Native Fab/Megascans library requests are journaled with exact asset/variant and the storage/compression choices captured when queued. Interrupted requests can resume; failed requests need Resume/retry rather than an endless automatic retry loop. Explicitly canceled requests are not silently restarted. Corrupt queue data must be reported, not discarded as an empty queue.

## Why a task may wait

Independent installations can overlap under scheduler I/O budgets. Tasks writing the same project or overlapping destination must serialize. Shared asset staging, library quota placement and disk contention can also prevent a start. Different project names alone do not prove independent I/O. Waiting is not a download failure; inspect its links and phase before queuing duplicates.

## Local reuse and restart boundaries

Choose the matching local artifact when available. A Windows variant cannot be substituted by an unrelated Mac download merely because the product name matches. Fab can reuse complete verified chunks; incomplete chunks are fetched again. Resume is not a promise of byte-perfect HTTP continuation. Interrupted extraction/compression may need repeating.

The persistent library-download journal does not restore every project installation, engine acquisition or browser transfer after an application restart. Inspect Actions and local output before retrying those workflows. Do not close a connector window with active transfers unless you finish/cancel them; quitting the whole hub stops its browsers.

An imported queue backup contains private metadata. Import without starting, review matched identities, then resume explicitly. Never share its credentials or signed URLs. See [storage](help:storage) and [troubleshooting](help:troubleshooting).
