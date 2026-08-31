import json
import os
import unreal

with open(os.environ["CYENGINEHUB_RELOCATED_IMPORT_CONFIG"], "r", encoding="utf-8") as handle:
    config = json.load(handle)

destination = config["destinationPath"].rstrip("/")
preserve_roots = bool(config["preserveRootFolders"])
errors = []

original_assets = list(
    unreal.EditorAssetLibrary.list_assets("/Game", recursive=True, include_folder=False)
)

# Le premier chargement se fait impérativement aux chemins d’origine du pack.
for asset_path in original_assets:
    try:
        if not unreal.EditorAssetLibrary.load_asset(asset_path):
            errors.append(asset_path + " : chargement impossible au chemin d’origine")
    except Exception as exception:
        errors.append(asset_path + " : erreur au chemin d’origine : " + str(exception))

packages = [asset_path.split(".")[0] for asset_path in original_assets]
relative_packages = [
    package[len("/Game/") :]
    for package in packages
    if package.startswith("/Game/")
]
directory_roots = sorted(
    {relative.split("/", 1)[0] for relative in relative_packages if "/" in relative}
)
direct_packages = sorted(
    {package for package, relative in zip(packages, relative_packages) if "/" not in relative}
)

for root in directory_roots:
    source = "/Game/" + root
    target = destination + "/" + root if preserve_roots or len(directory_roots) > 1 else destination
    if target == source or target.startswith(source + "/"):
        errors.append("Destination invalide pour " + source + " : " + target)
        continue
    try:
        if not unreal.EditorAssetLibrary.rename_directory(source, target):
            errors.append("Déplacement refusé : " + source + " vers " + target)
    except Exception as exception:
        errors.append("Déplacement impossible : " + source + " : " + str(exception))

if direct_packages:
    rename_data = []
    for package in direct_packages:
        try:
            asset = unreal.EditorAssetLibrary.load_asset(package)
            if not asset:
                errors.append(package + " : asset racine introuvable")
                continue
            name = package.rsplit("/", 1)[-1]
            rename_data.append(unreal.AssetRenameData(asset, destination, name))
        except Exception as exception:
            errors.append(package + " : préparation du déplacement impossible : " + str(exception))
    if rename_data:
        try:
            if not unreal.AssetToolsHelpers.get_asset_tools().rename_assets(rename_data):
                errors.append("Le déplacement des assets placés directement sous /Game a échoué")
        except Exception as exception:
            errors.append("Déplacement des assets racine impossible : " + str(exception))

try:
    redirectors = []
    for asset_path in unreal.EditorAssetLibrary.list_assets("/Game", recursive=True, include_folder=False):
        asset = unreal.EditorAssetLibrary.load_asset(asset_path)
        if asset and asset.get_class().get_name() == "ObjectRedirector":
            redirectors.append(asset)
    if redirectors:
        unreal.AssetToolsHelpers.get_asset_tools().fixup_referencers(redirectors)
except Exception as exception:
    errors.append("Correction des redirectors impossible : " + str(exception))

moved_assets = list(
    unreal.EditorAssetLibrary.list_assets(destination, recursive=True, include_folder=False)
)
for asset_path in moved_assets:
    try:
        asset = unreal.EditorAssetLibrary.load_asset(asset_path)
        if not asset:
            errors.append(asset_path + " : chargement impossible après déplacement")
            continue
        unreal.EditorAssetLibrary.save_loaded_asset(asset, only_if_is_dirty=False)
    except Exception as exception:
        errors.append(asset_path + " : validation impossible : " + str(exception))

try:
    remaining = []
    for asset_path in unreal.EditorAssetLibrary.list_assets("/Game", recursive=True, include_folder=False):
        package = asset_path.split(".")[0]
        if package == destination or package.startswith(destination + "/"):
            continue
        asset = unreal.EditorAssetLibrary.load_asset(asset_path)
        if not asset or asset.get_class().get_name() != "ObjectRedirector":
            remaining.append(asset_path)
    if remaining:
        errors.append("Assets non déplacés : " + ", ".join(remaining[:10]))
except Exception as exception:
    errors.append("Vérification des chemins résiduels impossible : " + str(exception))

try:
    unreal.EditorAssetLibrary.save_directory(destination, only_if_is_dirty=False, recursive=True)
except Exception as exception:
    errors.append("Sauvegarde finale impossible : " + str(exception))

with open(config["reportPath"], "w", encoding="utf-8") as handle:
    json.dump(
        {
            "originalAssetCount": len(original_assets),
            "movedAssetCount": len(moved_assets),
            "movedAssets": moved_assets,
            "errors": errors,
        },
        handle,
        indent=2,
    )

print("CYENGINEHUB_RELOCATED original=%d moved=%d errors=%d" % (
    len(original_assets),
    len(moved_assets),
    len(errors),
))
