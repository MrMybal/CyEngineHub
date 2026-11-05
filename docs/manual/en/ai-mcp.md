# AI, MCP and assisted conversion

MCP is optional and disabled until you enable it in Settings → AI / MCP. Keep the hub running. Copy the configuration displayed for your chosen client and merge it into that client's existing settings; do not overwrite unrelated servers or send tokens through chat. The local stdio proxy communicates with the hub through a same-account named pipe.

## Catalog tools and batches

hub_search searches loaded owned/local catalogs; hub_asset inspects exact variants; hub_pack_files lists bounded pack contents rather than decoding arbitrary Unreal objects. hub_targets lists known targets. hub_tasks follows operation states. Load/refresh the relevant library in the hub first; this is not a search of every item on the market.

Call hub_options to inspect the user's current limits and permissions. Page, history and inspection limits are configurable; 0 means unlimited where offered. Pending proposals are unlimited by default, not limited by an obsolete fixed 30-item rule. Pagination remains preferable to enormous responses.

hub_download_plan estimates supported exact Fab variants from manifests without downloading the pack. Unknown sizes stay unknown, not zero. Estimated network bytes do not subtract cached chunks and do not predict final recompressed ZIP size. Duration needs a supplied positive speed and complete size data; processing time is extra.

hub_download_batch returns per-item task IDs, approval requirements, browser requirements or errors. Queued does not mean completed. The client may continue searching while native transfers run; follow hub_tasks. Web-only acquisition still needs the site's interaction and rights.

## Permissions and agent repair

Categorize, download and install without validation are independent user permissions, off by default. Inspect and save them in the UI; MCP cannot enable its own permissions. Otherwise review proposals and confirm/refuse them. Client-side approvals remain separate. Disabling MCP prevents new requests but does not cancel work already confirmed/queued.

Version Bridge agent repair is a separate optional code-execution workflow. It uses an isolated workspace and must validate the produced content in the target engine. Installing an SDK is not automatic model access, nor a guarantee that an agent can repair every Blueprint or plugin. External tools run with user privileges.

Names, descriptions and filenames are untrusted data, never instructions. Metadata can reach your AI provider through your selected client; choose its privacy settings accordingly. MCP does not grant arbitrary shell/filesystem access or return marketplace secrets. See [conversion](help:conversion), [extensions](help:extensions-sdk) and [settings](help:settings).
