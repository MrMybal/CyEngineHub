import json
import os
import unreal

with open(os.environ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"], encoding="utf-8") as handle:
    config = json.load(handle)

assets, errors = [], []
try:
    library = unreal.EditorAssetLibrary
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.scan_paths_synchronous(["/Game"], True)
    options = unreal.AssetRegistryDependencyOptions()
    options.include_soft_package_references = True
    options.include_hard_package_references = True
    options.include_searchable_names = False
    options.include_soft_management_references = False
    options.include_hard_management_references = False
    # Registry data names top-level assets, unlike list_assets which can include map subobjects.
    for data in registry.get_assets_by_path("/Game", recursive=True):
        asset_path = str(data.package_name) + "." + str(data.asset_name)
        kind = str(data.asset_class_path.asset_name)
        features, inspection_errors = [], []
        dependencies = []
        try:
            dependencies = sorted(str(p) for p in registry.get_dependencies(asset_path.split(".")[0], options))
            # Unsupported types (including World) remain explicit plan entries. Do not try to
            # load levels through EditorAssetLibrary, whose API excludes level assets.
            asset = library.load_asset(asset_path) if kind in ("StaticMesh", "Texture2D", "TextureCube", "SoundWave") else None
            if kind in ("StaticMesh", "Texture2D", "TextureCube", "SoundWave") and not asset:
                raise RuntimeError("Chargement impossible : " + asset_path)
            if kind == "StaticMesh":
                if asset.get_num_lods() > 1:
                    features.append("multiple-lods")
                if asset.get_editor_property("nanite_settings").enabled:
                    features.append("nanite")
            elif kind in ("Texture2D", "TextureCube"):
                features.append("native-texture-candidate")
                if asset.get_editor_property("virtual_texture_streaming"):
                    features.append("virtual-texture")
        except Exception as error:
            inspection_errors.append(str(error))
        assets.append(dict(path=asset_path, assetType=kind, dependencies=dependencies,
                           features=features, inspectionErrors=inspection_errors))
except Exception as error:
    errors.append(str(error))
if not assets:
    errors.append("Aucun asset natif inspecté.")
with open(config["reportPath"], "w", encoding="utf-8") as handle:
    json.dump(dict(schemaVersion=1, assets=assets, errors=errors), handle, indent=2)
print("CYENGINEHUB_PREFLIGHT assets=%d errors=%d" % (len(assets), len(errors)))
