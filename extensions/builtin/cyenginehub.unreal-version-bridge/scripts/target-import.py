import json
import os
import unreal

with open(os.environ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"], "r", encoding="utf-8") as handle:
    config = json.load(handle)

tasks = []
imported = []
errors = []
for root, _, files in os.walk(config["exchangeRoot"]):
    for filename in files:
        if os.path.splitext(filename)[1].lower() in {'.bin', '.mtl'}:
            continue  # Companion data is consumed by the glTF/OBJ importer, not a standalone asset.
        source = os.path.join(root, filename)
        relative_dir = os.path.relpath(root, config["exchangeRoot"]).replace("\\", "/")
        destination = "/Game" if relative_dir == "." else "/Game/" + relative_dir
        task = unreal.AssetImportTask()
        task.filename = source
        task.destination_path = destination
        task.automated = True
        task.save = True
        task.replace_existing = False
        tasks.append(task)

if tasks:
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks(tasks)

for task in tasks:
    paths = list(task.imported_object_paths)
    if paths:
        imported.extend(paths)
    else:
        errors.append(task.filename + " : import sans résultat")

if config["validateOutput"]:
    for asset_path in imported:
        try:
            asset = unreal.EditorAssetLibrary.load_asset(asset_path)
            if asset:
                unreal.EditorAssetLibrary.save_loaded_asset(asset, only_if_is_dirty=False)
            else:
                errors.append(asset_path + " : validation impossible")
        except Exception as exception:
            errors.append(asset_path + " : " + str(exception))

unreal.EditorAssetLibrary.save_directory("/Game", only_if_is_dirty=False, recursive=True)
with open(config["reportPath"], "w", encoding="utf-8") as handle:
    json.dump({"imported": imported, "errors": errors}, handle, indent=2)

print("CYENGINEHUB_VERSION_BRIDGE imported=%d errors=%d" % (len(imported), len(errors)))
