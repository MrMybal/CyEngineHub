# Unreal Editor plugin Alpha validation — 2026-10-25

The final source was compiled against these installed Windows engines:

| Engine | Native compilation | Editor / desktop bridge integration |
| --- | --- | --- |
| 4.27.2 | Passed | Passed |
| 5.2.1 | Passed | Compilation only |
| 5.3.2 | Passed | Compilation only |
| 5.4.4 | Passed | Compilation only |
| 5.5.4 | Passed | Compilation only |
| 5.6.1 | Passed | Compilation only |
| 5.7.4 | Passed | Compilation only |
| 5.8.3 | Passed | Passed |

**5.0 / 5.1 are not installed and remain unverified.** Source compatibility is
intended, not a claim of a tested binary for these versions.

## Automated checks

- **857 .NET regression tests passed**, including editor protocol validation,
  version/platform matching, explicit confirmation, unknown project rejection,
  same-user pipe round trips, plugin staging, failed-build cleanup and concurrent
  conflict preservation.
- The desktop QA fixture exercises the engine-sheet install confirmation/cancel,
  enabled editor bridge with AI/MCP disabled, owned catalog, queued real import,
  task status and replay suppression.
- In UE 4.27.2 and 5.8.3, `CyEngineHub.Editor.PanelAndBridge` constructs the native
  Slate panel and uses the C++ pipe client against the real desktop backend.
  It validates matching C++/.NET account endpoint derivation, search, refusal of
  unconfirmed installation, confirmed task creation, replay returning the same
  task, and actual task completion.
- The disposable project receives an engine-supplied `DefaultDiffuse.uasset`,
  copied through a synthetic owned Fab download, with byte-for-byte verification.
  No marketplace transfer, purchase or real user-project mutation occurs.
  This is bridge/import validation, not a new claim about conversion coverage.
- UE 4.27's generic offscreen window cannot save its desktop placement at shutdown.
  The automation-only fixture disables layout persistence before exiting; both
  final editor processes exit successfully. Normal plugin use is unchanged.
- Release privacy inspection passes for all 80 native-package files, including
  DLL strings. Intermediate projects/PDBs are excluded from these packages.
- The eight ZIP archives also pass privacy inspection; Gitleaks finds no leak
  in the changed/new source files. Alpha.34 distributes the editor plugin source
  with the desktop app and the eight precompiled ZIPs as separate release assets.

Native compile success alone is not a full visual or behavioral validation of
each intermediate engine. Product galleries, advanced conversion, RAW processors,
pack-group editing and provider-specific web downloads remain desktop workflows;
see [the current scope](unreal-editor-plugin.md).
