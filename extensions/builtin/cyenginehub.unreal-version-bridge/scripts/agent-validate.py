"""Host-owned validator. Never replaced by an agent-supplied successful report."""
import json
import os
import unreal

with open(os.environ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"], encoding="utf-8") as handle:
    config = json.load(handle)

loaded, errors = [], []
registry = unreal.AssetRegistryHelpers.get_asset_registry()
mounts = ["/Game"] + ["/" + name for name in config["pluginNames"]]
registry.scan_paths_synchronous(mounts, True)
options = unreal.AssetRegistryDependencyOptions()
options.include_soft_package_references = True
options.include_hard_package_references = True
options.include_searchable_names = False
options.include_soft_management_references = False
options.include_hard_management_references = False

for package in config["expectedPackages"]:
    try:
        data = registry.get_assets_by_package_name(package)
        if not data:
            raise RuntimeError("Package absent: " + package)
        for item in data:
            # AssetData.get_asset supports World unlike EditorAssetLibrary.load_asset.
            asset = item.get_asset()
            if not asset:
                raise RuntimeError("Load failed: " + package)
            if isinstance(asset, unreal.Blueprint):
                unreal.BlueprintEditorLibrary.compile_blueprint(asset)
                # UBlueprint.Status is protected from Python on supported editors.
                # The host ALSO rejects compiler errors in the actual editor log;
                # a stale generated class alone is never accepted as proof.
                if not asset.generated_class():
                    raise RuntimeError("Blueprint has no generated class after compilation: " + package)
        for dep in registry.get_dependencies(package, options):
            dependency = str(dep)
            if dependency.startswith(("/Script/", "/Memory/", "/Temp/")):
                continue
            if not registry.get_assets_by_package_name(dependency):
                raise RuntimeError("Unresolved dependency: " + package + " -> " + dependency)
        loaded.append(package)
    except Exception as error:
        errors.append(str(error))

if config["save"] and not errors:
    try:
        # Save maps and content packages, not only EditorAssetLibrary-compatible objects.
        if not unreal.EditorLoadingAndSavingUtils.save_dirty_packages(True, True):
            raise RuntimeError("Target package save failed")
    except Exception as error:
        errors.append(str(error))

with open(config["reportPath"], "w", encoding="utf-8") as handle:
    json.dump(dict(schemaVersion=1, loaded=loaded, errors=errors), handle, indent=2)
if errors:
    raise RuntimeError("Target validation failed: " + " | ".join(errors[:8]))
