"""Real official MCP client -> shipped Core wheel -> packaged .NET worker.
Optional --unreal exercises real source/target editors and a small C++ plugin.
These fixtures validate the repair workflow, not arbitrary marketplace conversions.
"""
import argparse
import asyncio
import json
import os
from pathlib import Path
import sys
import time
from importlib.metadata import version, metadata
import psutil
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


def fixture(root, *, execute=True, real=False, plugin=False, source_engine=None, target_engine=None):
    root.mkdir(parents=True)
    for folder in ("SourceProject/Content", "TargetProject/Content", "Scripts", "Exchange"):
        (root / folder).mkdir(parents=True)
    if real and (source_engine is None or target_engine is None):
        raise ValueError("Real editor tests require explicit --source-engine and --target-engine paths.")
    source_engine = source_engine or root / "FixtureSourceEngine"
    target_engine = target_engine or root / "FixtureTargetEngine"
    def engine_version(engine, fallback):
        if not real:
            return fallback
        data = json.loads((engine / "Engine/Build/Build.version").read_text("utf-8-sig"))
        return ".".join(str(data[key]) for key in ("MajorVersion", "MinorVersion", "PatchVersion"))
    task = dict(schemaVersion=1, assetId="mcp-smoke", artifactId="fixture", sourceEngineRoot=str(source_engine),
                targetEngineRoot=str(target_engine), sourceVersion=engine_version(source_engine, "5.7.0"),
                targetVersion=engine_version(target_engine, "5.5.0"), allowCodeExecution=execute,
                sourceEditor=str(source_engine / "Engine/Binaries/Win64/UnrealEditor-Cmd.exe") if real else sys.executable,
                targetEditor=str(target_engine / "Engine/Binaries/Win64/UnrealEditor-Cmd.exe") if real else sys.executable,
                expectedPackages=[] if plugin else ["/Game/Example"], plugins=["CyRepairFixture"] if plugin else [],
                requiredModules=["CyRepairFixture"] if plugin else [])
    (root / "agent-task.json").write_text(json.dumps(task), encoding="utf-8")
    (root / "agent-diagnostic.txt").write_text("Fixture native type intentionally requires an adapter. No real user pack is modified.", encoding="utf-8")
    for side in ("Source", "Target"):
        path = root / f"{side}Project/CyVersionBridge{side}.uproject"
        if real:
            entries = [dict(Name="PythonScriptPlugin", Enabled=True), dict(Name="EditorScriptingUtilities", Enabled=True)]
            if plugin:
                entries.append(dict(Name="CyRepairFixture", Enabled=True))
            path.write_text(json.dumps(dict(FileVersion=3, Plugins=entries)), encoding="utf-8")
        else:
            # Executed by Python in place of the editor; actual Unreal covered separately.
            path.write_text("import sys\nfrom pathlib import Path\nscript=next(a[8:] for a in sys.argv if a.startswith('-script='))\nexec(compile(Path(script).read_text('utf-8'),script,'exec'))\n", encoding="utf-8")
    return root


async def wait_job(session, reply, *, timeout=30, expected="Completed"):
    assert not reply.isError, reply
    job = json.loads(reply.content[0].text)
    deadline = time.monotonic() + timeout
    while job["state"] not in ("Completed", "Failed", "Cancelled"):
        assert time.monotonic() < deadline, job
        await asyncio.sleep(.1)
        response = await session.call_tool("cy_get_job", {"jobId": job["jobId"]})
        assert not response.isError, response
        job = json.loads(response.content[0].text)
    assert job["state"] == expected, job
    return job


async def invoke(session, operation, parameters=None, **options):
    reply = await session.call_tool("op_" + operation, {"parameters": parameters or {}, "executionTimeout": options.get("timeout", 30)})
    return await wait_job(session, reply, **options)


def connection(app, workspace):
    return StdioServerParameters(command=sys.executable,
        args=[str(app / "Extensions/cyenginehub.unreal-version-bridge/mcp/server.py"), "--data-dir", str(workspace / "McpRuntime"), "mcp"],
        env={**os.environ, "CYHUB_BRIDGE_WORKSPACE": str(workspace)})


async def main(app, root, real, source_engine=None, target_engine=None):
    assert version("cytools-core") == "0.10.1"
    assert metadata("cytools-core")["License-Expression"] == "MIT"
    assert version("mcp") == "1.30.0"
    workspace = fixture(root / "protocol")
    async with stdio_client(connection(app, workspace)) as streams:
        async with ClientSession(*streams) as session:
            await session.initialize()
            names = {tool.name for tool in (await session.list_tools()).tools}
            assert {"op_describe", "op_read_file", "op_write_file", "op_run_source", "op_run_target", "op_build_plugin", "op_build_source_plugin", "op_validate", "cy_get_job", "cy_cancel_job"} <= names
            job = await invoke(session, "describe")
            assert job["outputs"]["task"]["expectedPackages"] == ["/Game/Example"]
            assert job["outputs"]["canInstall"] is False
            await invoke(session, "write_file", dict(path="../escape.py", text="bad"), expected="Failed")
            await invoke(session, "write_file", dict(path="agent-task.json", text="{}"), expected="Failed")
            await invoke(session, "write_file", dict(path="Scripts/source.py", text="import os\nfrom pathlib import Path\nPath(os.environ['CYHUB_BRIDGE_EXCHANGE'],'proof.txt').write_text('MCP real worker',encoding='utf-8')\n"))
            ran = await invoke(session, "run_source")
            assert ran["outputs"]["installed"] is False and ran["outputs"]["validated"] is False
            assert (workspace / "Exchange/proof.txt").read_text("utf-8") == "MCP real worker"
            run_workspace = Path(ran["workspace"])
            assert run_workspace.is_dir()
            await session.call_tool("cy_finish_task", {})
            assert not run_workspace.exists()
            assert (workspace / "Scripts/source.py").is_file() and (workspace / "Exchange/proof.txt").is_file()
            print("PASS MCP discovery, actual worker, bounded writes, persistent repair vs temporary job cleanup", flush=True)
    readonly = fixture(root / "readonly", execute=False)
    async with stdio_client(connection(app, readonly)) as streams:
        async with ClientSession(*streams) as session:
            await session.initialize()
            await invoke(session, "describe")
            await invoke(session, "write_file", dict(path="Scripts/source.py", text="pass"), expected="Failed")
            await invoke(session, "run_target", expected="Failed")
            print("PASS independently enforced read-only consent", flush=True)
    cancellation = fixture(root / "cancellation")
    async with stdio_client(connection(app, cancellation)) as streams:
        async with ClientSession(*streams) as session:
            await session.initialize()
            script = "import os,json,subprocess,sys,time\nfrom pathlib import Path\nchild=subprocess.Popen([sys.executable,'-c','import time;time.sleep(300)'])\nPath(os.environ['CYHUB_BRIDGE_EXCHANGE'],'pids.json').write_text(json.dumps([os.getpid(),child.pid]))\ntime.sleep(300)\n"
            await invoke(session, "write_file", dict(path="Scripts/source.py", text=script))
            reply = await session.call_tool("op_run_source", {"parameters": {}, "executionTimeout": 60})
            job = json.loads(reply.content[0].text)
            proof = cancellation / "Exchange/pids.json"
            deadline = time.monotonic() + 20
            while not proof.exists():
                assert time.monotonic() < deadline
                await asyncio.sleep(.1)
            pids = json.loads(proof.read_text())
            await session.call_tool("cy_cancel_job", {"jobId": job["jobId"]})
            await wait_job(session, reply, expected="Cancelled")
            assert all(not psutil.pid_exists(pid) for pid in pids), pids
            # Lock released only after the editor and its descendant stop.
            await invoke(session, "describe")
            print("PASS cancellation stops the owned worker/editor/descendant before releasing the task", flush=True)
    if not real:
        return
    actual = fixture(root / "unreal-blueprint", real=True, source_engine=source_engine, target_engine=target_engine)
    async with stdio_client(connection(app, actual)) as streams:
        async with ClientSession(*streams) as session:
            await session.initialize()
            source = """import unreal,os,json
from pathlib import Path
factory=unreal.BlueprintFactory()
factory.set_editor_property('parent_class',unreal.Actor)
asset=unreal.AssetToolsHelpers.get_asset_tools().create_asset('Example','/Game',unreal.Blueprint,factory)
if not asset: raise RuntimeError('Source fixture creation failed')
unreal.EditorAssetLibrary.save_loaded_asset(asset)
Path(os.environ['CYHUB_BRIDGE_EXCHANGE'],'blueprint.json').write_text(json.dumps({'name':'Example','parent':'/Script/Engine.Actor','fixture':'empty actor blueprint'}))
"""
            target = """import unreal,os,json
from pathlib import Path
data=json.loads(Path(os.environ['CYHUB_BRIDGE_EXCHANGE'],'blueprint.json').read_text())
factory=unreal.BlueprintFactory()
factory.set_editor_property('parent_class',unreal.load_class(None,data['parent']))
asset=unreal.AssetToolsHelpers.get_asset_tools().create_asset(data['name'],'/Game',unreal.Blueprint,factory)
if not asset: raise RuntimeError('Target reconstruction failed')
unreal.BlueprintEditorLibrary.compile_blueprint(asset)
unreal.EditorAssetLibrary.save_loaded_asset(asset)
"""
            await invoke(session, "write_file", dict(path="Scripts/source.py", text=source))
            await invoke(session, "write_file", dict(path="Scripts/target.py", text=target))
            await invoke(session, "run_source", timeout=600)
            await invoke(session, "run_target", timeout=600)
            await invoke(session, "validate", timeout=600)
            assert (actual / "TargetProject/Content/Example.uasset").is_file()
            print("PASS real UE 5.7 -> 5.5 empty Blueprint repair, dependency/compile/save and fresh reopen", flush=True)
    plugin = fixture(root / "unreal-plugin", real=True, plugin=True, source_engine=source_engine, target_engine=target_engine)
    for side in ("Source", "Target"):
        directory = plugin / f"{side}Project/Plugins/CyRepairFixture"
        source = directory / "Source/CyRepairFixture"
        source.mkdir(parents=True)
        (directory / "CyRepairFixture.uplugin").write_text(json.dumps(dict(FileVersion=3, Version=1, VersionName="1.0", FriendlyName="CyRepairFixture", CanContainContent=False,
            Modules=[dict(Name="CyRepairFixture", Type="Runtime", LoadingPhase="Default")])), encoding="utf-8")
        (source / "CyRepairFixture.Build.cs").write_text('using UnrealBuildTool; public class CyRepairFixture : ModuleRules { public CyRepairFixture(ReadOnlyTargetRules Target) : base(Target) { PCHUsage=PCHUsageMode.UseExplicitOrSharedPCHs; PublicDependencyModuleNames.Add("Core"); } }', encoding="utf-8")
        (source / "CyRepairFixture.cpp").write_text('#include "Modules/ModuleManager.h"\nIMPLEMENT_MODULE(FDefaultModuleImpl, CyRepairFixture)\n', encoding="utf-8")
    async with stdio_client(connection(app, plugin)) as streams:
        async with ClientSession(*streams) as session:
            await session.initialize()
            await invoke(session, "build_plugin", timeout=1200)
            await invoke(session, "validate", timeout=1200)
            assert list((plugin / "TargetProject/Plugins/CyRepairFixture/Binaries/Win64").glob("*.dll"))
            print("PASS real UE 5.5 C++ plugin compilation, editor loading and fresh reopen", flush=True)


async def recheck_real(app, root):
    for folder in ("unreal-blueprint", "unreal-plugin"):
        async with stdio_client(connection(app, root / folder)) as streams:
            async with ClientSession(*streams) as session:
                await session.initialize()
                await invoke(session, "validate", timeout=1200)
                print("PASS real target validation and fresh reopen: " + folder, flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--app", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--unreal", action="store_true")
    parser.add_argument("--source-engine", type=Path)
    parser.add_argument("--target-engine", type=Path)
    parser.add_argument("--recheck-real", action="store_true")
    args = parser.parse_args()
    asyncio.run(recheck_real(args.app.resolve(), args.output.resolve()) if args.recheck_real else
        main(args.app.resolve(), args.output.resolve(), args.unreal, args.source_engine, args.target_engine))
