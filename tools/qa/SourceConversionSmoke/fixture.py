import bpy
import os
from pathlib import Path
import sys

root = Path(sys.argv[sys.argv.index("--") + 1])
root.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_cube_add()
cube = bpy.context.object
cube.name = "TexturedCube"
image = bpy.data.images.new("PackedChecker", width=4, height=4, alpha=True)
image.pixels = [channel for index in range(16) for channel in ((1, 0.1, 0.2, 1) if index % 2 else (0.1, 0.9, 0.2, 1))]
image.pack()
material = bpy.data.materials.new("FixturePBR")
material.use_nodes = True
bsdf = material.node_tree.nodes.get("Principled BSDF")
bsdf.inputs["Roughness"].default_value = 0.35
bsdf.inputs["Metallic"].default_value = 0.2
texture = material.node_tree.nodes.new("ShaderNodeTexImage")
texture.image = image
material.node_tree.links.new(texture.outputs["Color"], bsdf.inputs["Base Color"])
cube.data.materials.append(material)
cube.shape_key_add(name="Basis")
key = cube.shape_key_add(name="Morph")
key.data[0].co.z += 0.3
cube.location.x = 0
cube.keyframe_insert(data_path="location", frame=1)
cube.location.x = 2
cube.keyframe_insert(data_path="location", frame=12)
cube.location.x = 0
bpy.context.scene.frame_end = 12
# Autoload text is a regression guard: opening the asset must never execute it.
text = bpy.data.texts.new("autoload.py")
text.write("from pathlib import Path\nPath(" + repr(str(root / "AUTOEXEC_RAN")) + ").write_text('unsafe')\n")
text.use_module = True
bpy.ops.wm.save_as_mainfile(filepath=str(root / "Fixture.blend"))
print("CYHUB_FIXTURE_SAVED", flush=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.mesh.primitive_monkey_add()
usd = root.parent / "UsdSource"
usd.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.usd_export(filepath=str(usd / "Suzanne.usdc"))
