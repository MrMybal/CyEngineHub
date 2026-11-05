# Git branches and source engines

Git support is observation-first. Enabling Git in Connections only exposes the integration; it does not authenticate every remote. Credentials remain managed by Git and its credential manager, not by a universal CyEngineHub login.

## Inspect and check updates

1. Register the project or source-engine location and open its Git inspection.
2. Run an analysis to read the current branch, working-tree counters and configured remotes. Shared repositories are inspected once per snapshot.
3. Select the intended remote and explicitly fetch to update remote-tracking references. Fetch may contact the network and require credentials.
4. Inspect the result before deciding how to merge/build in your normal Git workflow. A remote comparison needs fresh refs and a meaningful upstream; an unverified result is not “up to date”.

The implemented fetch uses the validated remote name. It does not pull, checkout, merge, rebase or reset your working tree. CyEngineHub does not silently discard modifications to make a source update succeed. After a source update performed with your tools, regenerate/build the engine with its required toolchain before assuming the binaries changed.

## Large repositories and privacy

Snapshots use bounded/read-only status commands with terminal prompts disabled. Aggregate change counts are stored rather than huge per-file inventories. Remote display URLs remove user information, query strings and fragments before caching. Do not put credentials into remote URLs or screenshots.

LFS measurement is bounded and a value prefixed by ≥ is partial. There is no background polling or automatic LFS/object pruning. Git data shown in [Storage](help:storage) is not permission to delete object stores.

If authentication fails, verify the same remote with your Git client and Windows credential manager. Do not paste tokens into logs or support reports. See [engines](help:engines) and [troubleshooting](help:troubleshooting).
