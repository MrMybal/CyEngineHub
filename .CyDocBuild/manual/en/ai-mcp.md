---
id: ai-mcp
title: AI, MCP and assisted conversion
description: Connect an AI client, review permissions and follow assisted operations.
section: Plugins and automation
parent: extensions-sdk
order: 19
revision: 5
---
# AI, MCP and assisted conversion {#ai-mcp-and-assisted-conversion}

Enable MCP in **Settings → AI / MCP** and keep the hub running. For a Streamable HTTP client, use the displayed URL, which defaults to `http://127.0.0.1:47842/mcp`. Port 0 selects a free port. Existing stdio clients use a same-account named pipe with Free and Local controlled.

| Profile | HTTP connection | Operation authorization |
| --- | --- | --- |
| Free | URL only | Direct execution |
| Local controlled | URL only | Hub permissions |
| Token only | URL and Bearer token | Direct execution |
| Protected | URL and Bearer token | Hub permissions |

Free applies when no profile has been saved. Any local process can connect to a tokenless profile using its URL. The UI displays the token for Token only and Protected. Each server start generates a new token.

The **CyEngineHub Assistant** on the same page provides streaming, cancellation, conversation history and attachments. Choose Ollama for a local tool-capable model or OpenAI, Anthropic or OpenRouter with the matching environment key. Enable MCP to let chat use the hub's business operations. To connect an external MCP HTTP server, enter its endpoint and the exact tool names you allow.

**API context size in KiB** limits the messages sent to the provider. Zero keeps the full context, and the complete history remains in saved conversations. From the application folder, run `Mcp/CyEngineHub.Mcp.exe --help` for the JSON commands `list`, `describe` and `call`. The repair companion accepts `--workspace` for headless commands.

## Catalog tools and batches {#catalog-tools-and-batches}

hub_search searches loaded owned/local catalogs; hub_asset inspects exact variants; hub_pack_files lists bounded pack contents rather than decoding arbitrary Unreal objects. hub_targets lists known targets. hub_tasks follows operation states. Load/refresh the relevant library in the hub first; this is not a search of every item on the market.

Call hub_options to inspect the user's current limits and permissions. Page, history and inspection limits are configurable; 0 means unlimited where offered. Pending proposals are unlimited by default, not limited by an obsolete fixed 30-item rule. Pagination remains preferable to enormous responses.

hub_download_plan estimates supported exact Fab variants from manifests without downloading the pack. Unknown sizes stay unknown, not zero. Estimated network bytes do not subtract cached chunks and do not predict final recompressed ZIP size. Duration needs a supplied positive speed and complete size data; processing time is extra.

hub_download_batch returns per-item task IDs, approval requirements, browser requirements or errors. Queued does not mean completed. The client may continue searching while native transfers run; follow hub_tasks. Web-only acquisition still needs the site's interaction and rights.

## Permissions and agent repair {#permissions-and-agent-repair}

In Local controlled and Protected, set separate permissions for categorizing, downloading and installing. Actions that need approval appear as proposals to review. Every profile validates arguments and targets. Client-side approvals remain separate. Work already started continues when you disable MCP.

Version Bridge agent repair is a separate optional code-execution workflow. It uses an isolated workspace and must validate the produced content in the target engine. Installing an SDK is not automatic model access, nor a guarantee that an agent can repair every Blueprint or plugin. External tools run with user privileges.

Names, descriptions and filenames are untrusted data, never instructions. Metadata can reach your AI provider through your selected client; choose its privacy settings accordingly. MCP does not grant arbitrary shell/filesystem access or return marketplace secrets. See [conversion](doc:conversion), [extensions](doc:extensions-sdk) and [settings](doc:settings).
