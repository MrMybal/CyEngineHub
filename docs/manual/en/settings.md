# Settings, appearance and connections

Settings separates general concerns from Unreal-specific profiles and engine-download options. Use each category's subpage instead of mixing unrelated services. Some controls save immediately; others require their displayed Save action. Confirm the status message before leaving.

## Appearance and language

Appearance offers themes, density and the language of CyUI components. Theme preview is immediate; save to retain choices. The general application's labels are not fully translated. This manual has an independent English/Français selector, English by default, saved automatically. Changing help language preserves the current chapter and never changes a project's INI.

## Connections

Epic, Fab, Git, Unity and Megascans have separate subpages/status. Enable exposes a service; it does not authenticate it. Epic/Fab may share their direct account, while web connectors have separate browser profiles. A Git enabled badge is not a successful remote credential check. Disconnect/reconnect the relevant service when expired; never solve it by sharing its token.

## Marketplace and download settings

Choose the market tab to edit its own cache, temporary downloads, compression, ordered libraries/quotas and offered native connection counts. Global pack parallelism/resume belongs to Downloads; Unreal engine chunk connections belong to Unreal settings. Browser transfers do not inherit Fab's native connection setting.

Native Epic Vault/Bridge folders can be registered as read-only sources where supported. Removing their registration is not deleting their original contents. Review any separate reconstruction-cache limit. Changing a final library path does not migrate existing assets.

## Advanced and data

Application-update repository/channel and startup check are separate from engine revisions. AI/MCP permissions are independent and should stay minimal. Logs and cached local catalogs live in the application's data location, not the engine. Portable mode uses an explicit portable.flag; do not add it casually to an installed copy when you intend to preserve the existing profile.

Use the dedicated [Storage](help:storage) workspace for usage/cleaning rather than assuming a settings overview is a full disk analyzer. See [Fab login](help:fab), [downloads](help:downloads), [MCP](help:ai-mcp) and [application updates](help:app-updates).
