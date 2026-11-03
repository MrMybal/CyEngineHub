import json
import os
import unreal

registry = unreal.AssetRegistryHelpers.get_asset_registry()
registry.scan_paths_synchronous(["/Game"], True)
assets = []
for data in registry.get_assets_by_path("/Game", recursive=True):
    path = str(data.package_name) + "." + str(data.asset_name)
    asset = unreal.EditorAssetLibrary.load_asset(path)
    if not asset:
        raise RuntimeError("Fresh process cannot reload " + path)
    record = {"path": path, "class": asset.get_class().get_name()}
    if isinstance(asset, unreal.MaterialInstanceConstant):
        record["textures"] = [str(item.parameter_value.get_path_name()) for item in asset.get_editor_property("texture_parameter_values") if item.parameter_value]
        record["scalars"] = {str(item.parameter_info.name): item.parameter_value for item in asset.get_editor_property("scalar_parameter_values")}
    assets.append(record)
classes = [item["class"] for item in assets]
if not any(name in classes for name in ["StaticMesh", "SkeletalMesh"]) or "Texture2D" not in classes or "AnimSequence" not in classes:
    raise RuntimeError("Geometry/texture/animation lost: " + str(classes))
materials = [item for item in assets if item["class"] in ["Material", "MaterialInstanceConstant"]]
if not materials or not any(item.get("textures") for item in materials):
    raise RuntimeError("PBR material no longer references the imported texture")
with open(os.environ["CYHUB_VERIFY_REPORT"], "w", encoding="utf-8") as handle:
    json.dump({"success": True, "assets": assets}, handle, indent=2)
