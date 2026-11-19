# Unreal Version Bridge repair through CyMCPSDK

Enable optional AI assistance in the conversion form. If conversion is blocked, connect the generated `mcp.json` in your MCP client. It selects the packaged Version Bridge executable and binds it to that repair copy. For an existing copy, choose **Reprendre une tâche IA** in the hub to refresh its configuration, or pass `--workspace <repair-folder>` to the packaged executable from the new configuration.

| Tool | Effect |
| --- | --- |
| `op_describe` | Read the exact engine versions, inventory, diagnostic and saved execution consent. |
| `op_list_files` | List filenames and sizes in the repair copy, with an offset and a limit up to 200. |
| `op_read_file` | Read bounded text diagnostics, plans, scripts or plugin sources. |
| `op_write_file` | Edit an allowed repair script or source, configuration, shader or descriptor for the target plugin. |
| `op_run_source` / `op_run_target` | Execute the corresponding repair script with the source or target editor in its dedicated copy. |
| `op_build_source_plugin` / `op_build_plugin` | Compile plugin source with the selected engine. |
| `op_validate` | Compile, save, validate and reopen the expected packages in the target engine. |

Pass business arguments inside `parameters`. The optional `queueTimeout` and `executionTimeout` are in seconds. Each submission returns `jobId` and `state`. Use `cy_get_job` to read outputs from a completed job, `cy_cancel_job` to request cancellation and `cy_list_jobs` to find recent or historical results. `cy_finish_task` clears temporary job reports after active jobs stop. The repair workspace and its results can be inspected after reconnecting. A previously interrupted job is reported as failed and can be submitted again after inspection.

Writes, script execution, compilation and validation require the saved execution consent. Python, C++ and Build.cs run under the user's OS account. Use version-appropriate editor APIs for asset conversion. Final publishing and installation require resuming the task with the same pack and engines in CyEngineHub, which revalidates the outputs independently.
