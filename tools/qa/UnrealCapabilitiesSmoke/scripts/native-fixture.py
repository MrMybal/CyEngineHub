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

if os.environ.get('CYHUB_NATIVE_GRAPH') == 'true':
    tools = unreal.AssetToolsHelpers.get_asset_tools()
    material = tools.create_asset('M_Native', '/Game/NativeQA', unreal.Material, unreal.MaterialFactoryNew())
    material.set_editor_property('two_sided', True)
    edit = unreal.MaterialEditingLibrary
    sample = edit.create_material_expression(material, unreal.MaterialExpressionTextureSampleParameter2D, -600, 0)
    sample.set_editor_property('parameter_name', 'Albedo')
    sample.set_editor_property('texture', texture)
    sample.set_editor_property('sampler_type', unreal.MaterialSamplerType.SAMPLERTYPE_MASKS)
    strength = edit.create_material_expression(material, unreal.MaterialExpressionScalarParameter, -600, 250)
    strength.set_editor_property('parameter_name', 'Strength')
    strength.set_editor_property('default_value', 0.6)
    multiply = edit.create_material_expression(material, unreal.MaterialExpressionMultiply, -300, 0)
    assert edit.connect_material_expressions(sample, 'RGB', multiply, 'A')
    assert edit.connect_material_expressions(strength, '', multiply, 'B')
    assert edit.connect_material_property(multiply, '', unreal.MaterialProperty.MP_BASE_COLOR)
    assert edit.connect_material_property(strength, '', unreal.MaterialProperty.MP_ROUGHNESS)
    edit.recompile_material(material)
    assert library.save_loaded_asset(material, only_if_is_dirty=False)
    instance = tools.create_asset('MI_Native', '/Game/NativeQA', unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
    edit.set_material_instance_parent(instance, material)
    edit.set_material_instance_scalar_parameter_value(instance, 'Strength', 0.35)
    assert library.save_loaded_asset(instance, only_if_is_dirty=False)
    print('NATIVE_MATERIAL_GRAPH_FIXTURE_READY')
