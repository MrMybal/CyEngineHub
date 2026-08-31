# Git safety and performance

The first CyEngineHub Git milestone is intentionally observation-first.

## Commands used for snapshots

- `git rev-parse` resolves repository and common Git directories;
- `git status --porcelain=v2 --branch` supplies branch and worktree counters;
- `git remote -v` supplies fetch remotes;
- `git lfs track` lists configured LFS patterns.

Read commands run with optional Git locks and terminal prompts disabled. Status
output is parsed as it arrives. CyEngineHub stores aggregate counts, not the potentially
huge list of changed file paths.

## Remote safety

Remote URLs are display metadata only; commands address a remote by its validated
name. Before a snapshot is cached, URL user information, query strings and
fragments are removed so embedded credentials or tokens are not written to the
CyEngineHub database.

The only network mutation exposed in this milestone is:

    git fetch --prune --progress <remote>

Fetch updates remote-tracking references. It does not merge, checkout or rewrite
the working tree. Pull, checkout, merge, rebase, reset and prune are deliberately
not exposed yet.

## Large repositories and LFS

There is no background polling. A snapshot starts only when the user requests an
analysis. Repositories shared by several projects or engines are inspected once.

Physical LFS storage enumeration stops after 250,000 objects or five seconds per
repository. The UI prefixes object count and size with `≥` when a result is
partial. No LFS prune or object deletion is implemented.
