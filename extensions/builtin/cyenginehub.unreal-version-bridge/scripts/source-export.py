import json
import os
import re
import unreal

with open(os.environ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"], "r", encoding="utf-8") as handle:
    config = json.load(handle)

exported = []
unsupported = []
errors = []
safe_types = {"StaticMesh": ".fbx", "Texture2D": ".tga", "SoundWave": ".wav"}
supported = safe_types
with open(config["planPath"], encoding="utf-8") as handle:
    plan = json.load(handle)
if not plan.get("canConvert") or plan.get("mode") != "portable":
    raise RuntimeError("Le plan ne permet pas cet export portable.")
decisions = {item["assetPath"]: item for item in plan["assets"]}

for asset_path in unreal.EditorAssetLibrary.list_assets("/Game", recursive=True, include_folder=False):
    try:
        asset = unreal.EditorAssetLibrary.load_asset(asset_path)
        if not asset:
            errors.append(asset_path + " : chargement impossible")
            continue
        class_name = asset.get_class().get_name()
        decision = decisions.get(asset_path)
        if not decision or decision["status"] != "lossy-confirmed" or decision["assetType"] != class_name:
            errors.append(asset_path + " : asset absent ou différent du plan validé")
            continue
        extension = supported.get(class_name)
        if not extension:
            unsupported.append(asset_path + " [" + class_name + "]")
            continue
        relative = asset_path.replace("/Game/", "", 1).split(".")[0]
        relative = re.sub(r"[^A-Za-z0-9_./-]", "_", relative)
        filename = os.path.join(config["exchangeRoot"], relative + extension)
        os.makedirs(os.path.dirname(filename), exist_ok=True)
        task = unreal.AssetExportTask()
        task.object = asset
        task.filename = filename
        task.automated = True
        task.prompt = False
        task.replace_identical = True
        task.write_empty_files = False
        result = unreal.Exporter.run_asset_export_task(task)
        if result and os.path.isfile(filename):
            exported.append({"asset": asset_path, "class": class_name, "file": filename})
        else:
            errors.append(asset_path + " : aucun exporteur compatible")
    except Exception as exception:
        errors.append(asset_path + " : " + str(exception))

seen_exports = {entry["asset"] for entry in exported}
for expected in decisions:
    if expected not in seen_exports:
        errors.append(expected + " : export attendu manquant")
with open(config["reportPath"], "w", encoding="utf-8") as handle:
    json.dump({"exported": exported, "unsupported": unsupported, "errors": errors}, handle, indent=2)

print("CYENGINEHUB_VERSION_BRIDGE exported=%d unsupported=%d errors=%d" %
      (len(exported), len(unsupported), len(errors)))
