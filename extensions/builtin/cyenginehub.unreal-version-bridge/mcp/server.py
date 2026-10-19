"""Optional extension companion; the official CyToolsCore owns MCP, jobs and cancellation."""
import os
import sys
from importlib.metadata import version
from pathlib import Path
from cytools_core import Runtime, CyToolError, load_manifest
from cytools_core.transports.cli import run_cli
from cytools_core.workers import ProcessWorker


def create_runtime(data_dir, **options):
    if version("cytools-core") != "0.10.1" or version("mcp") != "1.30.0":
        raise CyToolError("MissingDependency", "Use Setup.ps1: this integration requires the shipped MIT CyToolsCore 0.10.1 and MCP 1.30.0.")
    root = Path(__file__).resolve().parent
    workspace = os.environ.get("CYHUB_BRIDGE_WORKSPACE")
    if not workspace or not (Path(workspace) / "agent-task.json").is_file():
        raise CyToolError("MissingWorkspace", "Use the mcp.json prepared by the hub for a specific repair task.")
    worker_path = root / "tools" / "CyEngineHub.VersionBridge.Tool.exe"
    if not worker_path.is_file():
        raise CyToolError("MissingRuntime", "The extension's VersionBridge worker is absent; rebuild or repair the hub.")
    runtime = Runtime(load_manifest(root / "CyTool.json"), data_dir, **options)
    runtime.configure_inputs(Path(data_dir) / "inputs", fields={})
    worker = ProcessWorker([str(worker_path)], idle_timeout=7200)
    runtime.register_worker("unreal-repair", worker)

    def make_handler(operation):
        def handler(context, parameters):
            reply = worker.run(context, {"operation": operation, "arguments": parameters})
            if not reply.get("ok"):
                raise CyToolError("RepairFailed", reply.get("error", "Unreal repair failed"), recoverable=True)
            return reply["result"]
        return handler

    for operation in runtime.descriptor["operations"]:
        runtime.register(operation["id"], make_handler(operation["id"]))
    return runtime


if __name__ == "__main__":
    raise SystemExit(run_cli(create_runtime, sys.argv[1:]))
