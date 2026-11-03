"""Host-owned converter. Run with --factory-startup --disable-autoexec, never from an asset's scripts."""
import json
import os
from pathlib import Path
import re
import sys
import bpy

config = json.loads(Path(sys.argv[sys.argv.index("--") + 1]).read_text(encoding="utf-8"))
source = Path(config["source"]).resolve()
destination = Path(config["destination"]).resolve()
report_file = Path(config["report"])
report = {"success": False, "converter": "blender.interchange", "blenderVersion": bpy.app.version_string,
          "format": config["format"], "inputs": [], "outputs": [], "warnings": [], "statistics": []}
supported = {".blend", ".fbx", ".obj", ".gltf", ".glb", ".usd", ".usda", ".usdc", ".usdz"}


def contained(path, root):
    try:
        path.resolve().relative_to(root)
        return True
    except ValueError:
        return False


def safe_name(value):
    return re.sub(r"[^A-Za-z0-9_-]", "_", value)[:100] or "Asset"


def warning(message):
    if message not in report["warnings"]:
        report["warnings"].append(message)
    if not config["allowLossy"]:
        raise RuntimeError(message + " · Autoriser explicitement les pertes pour poursuivre.")


def invoke(operator, **kwargs):
    accepted = {item.identifier for item in operator.get_rna_type().properties}
    result = operator(**{key: value for key, value in kwargs.items() if key in accepted})
    if "FINISHED" not in result:
        raise RuntimeError("Opérateur Blender non terminé : " + str(result))


def fix_dependencies():
    for library in bpy.data.libraries:
        path = Path(bpy.path.abspath(library.filepath)).resolve()
        if not contained(path, source) or not path.is_file():
            raise RuntimeError("Bibliothèque Blender liée hors du pack ou manquante : " + path.name)
    for image in bpy.data.images:
        if image.source in {"GENERATED", "VIEWER"} or image.name == "Render Result":
            continue
        if image.source in {"MOVIE", "SEQUENCE", "TILED"}:
            warning("Texture animée/UDIM non représentable automatiquement : " + image.name)
            continue
        if image.packed_file:
            continue
        path = Path(bpy.path.abspath(image.filepath, library=image.library)).resolve()
        if contained(path, source) and path.is_file():
            image.filepath = str(path)
            continue
        # Recover relocatable downloads only from the explicitly selected package.
        candidates = [candidate for candidate in source.rglob(path.name) if candidate.is_file() and contained(candidate, source)]
        if len(candidates) != 1:
            raise RuntimeError("Texture absente/ambiguë dans le pack : " + path.name)
        image.filepath = str(candidates[0])
    for material in bpy.data.materials:
        if not material.use_nodes:
            warning("Matériau sans graphe Principled : " + material.name)
            continue
        allowed = {"ShaderNodeOutputMaterial", "ShaderNodeBsdfPrincipled", "ShaderNodeTexImage", "ShaderNodeNormalMap",
                   "ShaderNodeTexCoord", "ShaderNodeMapping", "ShaderNodeSeparateColor", "ShaderNodeSeparateRGB",
                   "ShaderNodeRGB", "ShaderNodeValue", "NodeReroute"}
        unusual = sorted({node.bl_idname for node in material.node_tree.nodes if node.bl_idname not in allowed})
        if unusual:
            warning("Graphe matériau nécessitant bake/adaptation : " + material.name + " (" + ", ".join(unusual) + ")")
        if config["format"] == "FBX":
            warning("FBX ne conserve pas intégralement le graphe PBR : " + material.name)


try:
    files = []
    for root, dirs, names in os.walk(source, followlinks=False):
        if any((Path(root) / name).is_symlink() for name in dirs):
            raise RuntimeError("Liens de dossier interdits dans le pack.")
        for name in names:
            path = Path(root) / name
            if path.is_symlink():
                raise RuntimeError("Lien de fichier interdit dans le pack.")
            if path.suffix.lower() in supported:
                files.append(path)
            if len(files) > 1_000:
                raise RuntimeError("Plus de 1 000 fichiers de scène : fractionner le pack.")
    if not files:
        raise RuntimeError("Aucune source Blender/USD/FBX/OBJ/glTF à convertir.")
    mode = config.get("inputSelection", "all")
    if mode not in {"all", "blendOnly", "interchangeOnly"}:
        raise RuntimeError("Sélection de fichiers invalide.")
    selected = [path for path in files if mode == "all" or (path.suffix.lower() == ".blend") == (mode == "blendOnly")]
    report["explicitlyExcludedInputs"] = [str(path.relative_to(source)) for path in files if path not in selected]
    files = selected
    for index, path in enumerate(sorted(files)):
        print("CYENGINEHUB_CONVERT %d/%d %s" % (index + 1, len(files), path.name), flush=True)
        if path.suffix.lower() == ".blend":
            bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False, use_scripts=False)
        else:
            bpy.ops.wm.read_factory_settings(use_empty=True)
            extension = path.suffix.lower()
            if extension == ".fbx":
                invoke(bpy.ops.import_scene.fbx, filepath=str(path))
            elif extension == ".obj":
                if bpy.app.version >= (4, 0, 0):
                    invoke(bpy.ops.wm.obj_import, filepath=str(path))
                else:
                    invoke(bpy.ops.import_scene.obj, filepath=str(path))
            elif extension in {".gltf", ".glb"}:
                invoke(bpy.ops.import_scene.gltf, filepath=str(path))
            else:
                invoke(bpy.ops.wm.usd_import, filepath=str(path))
        fix_dependencies()
        report["inputs"].append(str(path.relative_to(source)))
        for scene_index, scene in enumerate(list(bpy.data.scenes)):
            bpy.context.window.scene = scene
            geometry = [obj for obj in scene.objects if obj.type in {"MESH", "CURVE", "SURFACE", "FONT", "META"}]
            if not geometry:
                continue
            unsupported = [obj.name for obj in scene.objects if obj.type in {"VOLUME", "POINTCLOUD", "GPENCIL", "GREASEPENCIL"}]
            if unsupported:
                warning("Objets non représentables : " + ", ".join(unsupported))
            if any(obj.constraints for obj in scene.objects):
                warning("Contraintes de scène : vérifier le bake de l’animation exportée.")
            if any(modifier.type == "NODES" for obj in geometry for modifier in obj.modifiers):
                warning("Geometry Nodes : réalisation/bake manuel à vérifier avant export.")
            for obj in geometry:
                if obj.type != "MESH":
                    warning("Objet non-mesh à convertir manuellement : " + obj.name)
            base = "%04d_%s_%03d_%s" % (index, safe_name(path.stem), scene_index, safe_name(scene.name))
            folder = destination / base
            folder.mkdir(parents=True, exist_ok=False)
            output = folder / (base + (".glb" if config["format"] == "GLB" else ".fbx"))
            if config["format"] == "GLB":
                invoke(bpy.ops.export_scene.gltf, filepath=str(output), export_format="GLB", use_active_scene=True,
                       export_animations=config["animations"], export_skins=True, export_morph=True,
                       export_materials="EXPORT", export_yup=True, export_cameras=False, export_lights=False)
            else:
                invoke(bpy.ops.export_scene.fbx, filepath=str(output), object_types={"MESH", "ARMATURE", "EMPTY"},
                       bake_anim=config["animations"], add_leaf_bones=False, path_mode="COPY", embed_textures=True,
                       axis_forward="-Z", axis_up="Y")
            if not output.is_file() or output.stat().st_size == 0:
                raise RuntimeError("Export vide : " + output.name)
            report["outputs"].append(str(output.relative_to(destination)).replace("\\", "/"))
            report["statistics"].append({"output": output.name, "meshes": sum(obj.type == "MESH" for obj in scene.objects),
                                         "vertices": sum(len(obj.data.vertices) for obj in scene.objects if obj.type == "MESH"),
                                         "materials": len({slot.material.name for obj in scene.objects if obj.type == "MESH" for slot in obj.material_slots if slot.material}),
                                         "uvMeshes": sum(bool(obj.data.uv_layers) for obj in scene.objects if obj.type == "MESH"),
                                         "armatures": sum(obj.type == "ARMATURE" for obj in scene.objects),
                                         "actions": len(bpy.data.actions)})
    if not report["outputs"]:
        raise RuntimeError("Aucune géométrie exportable dans les scènes du pack.")
    report["validation"] = []
    for relative, expected in zip(report["outputs"], report["statistics"]):
        bpy.ops.wm.read_factory_settings(use_empty=True)
        path = destination / relative
        if config["format"] == "GLB":
            invoke(bpy.ops.import_scene.gltf, filepath=str(path))
        else:
            invoke(bpy.ops.import_scene.fbx, filepath=str(path))
        meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
        if not meshes or not any(obj.data.vertices for obj in meshes):
            raise RuntimeError("Réouverture sans géométrie : " + path.name)
        if expected["materials"] and not any(obj.material_slots for obj in meshes):
            raise RuntimeError("Matériaux perdus lors de l’export : " + path.name)
        if expected["uvMeshes"] and not any(obj.data.uv_layers for obj in meshes):
            raise RuntimeError("UV perdus lors de l’export : " + path.name)
        if expected["armatures"] and not any(obj.type == "ARMATURE" for obj in bpy.context.scene.objects):
            raise RuntimeError("Armature perdue lors de l’export : " + path.name)
        report["validation"].append({"output": relative, "reopened": True, "meshes": len(meshes),
                                     "vertices": sum(len(obj.data.vertices) for obj in meshes),
                                     "materials": len(bpy.data.materials), "images": len(bpy.data.images), "actions": len(bpy.data.actions)})
    report["success"] = True
except Exception as error:
    report["error"] = str(error)
    raise
finally:
    report_file.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
