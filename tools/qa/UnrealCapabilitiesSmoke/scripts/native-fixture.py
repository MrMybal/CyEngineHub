import os, unreal
library = unreal.EditorAssetLibrary
task = unreal.AssetImportTask()
task.filename = os.environ['CYHUB_NATIVE_IMAGE']
task.destination_path = '/Game/NativeQA'
task.automated, task.save = True, True
unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
texture = library.load_asset('/Game/NativeQA/T_Native')
assert texture
texture.set_editor_property('srgb', False)
texture.set_editor_property('filter', unreal.TextureFilter.TF_NEAREST)
texture.set_editor_property('address_x', unreal.TextureAddress.TA_CLAMP)
texture.set_editor_property('adjust_brightness', 0.75)
texture.set_editor_property('compression_settings', unreal.TextureCompressionSettings.TC_MASKS)
assert library.save_loaded_asset(texture, only_if_is_dirty=False)
normal = library.duplicate_asset('/Game/NativeQA/T_Native', '/Game/NativeQA/T_Normal')
normal.set_editor_property('compression_settings', unreal.TextureCompressionSettings.TC_NORMALMAP)
normal.set_editor_property('flip_green_channel', True)
assert library.save_loaded_asset(normal, only_if_is_dirty=False)
gray_task = unreal.AssetImportTask()
gray_task.filename = os.environ['CYHUB_NATIVE_GRAY']
gray_task.destination_path = '/Game/NativeQA'
gray_task.automated, gray_task.save = True, True
unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([gray_task])
gray = library.load_asset('/Game/NativeQA/T_Gray')
assert gray
gray.set_editor_property('srgb', False)
gray.set_editor_property('compression_settings', unreal.TextureCompressionSettings.TC_GRAYSCALE)
assert library.save_loaded_asset(gray, only_if_is_dirty=False)
print('NATIVE_TEXTURE_FIXTURE_READY')
