import unreal

library = unreal.EditorAssetLibrary
edit = unreal.MaterialEditingLibrary
material = library.load_asset('/Game/NativeQA/M_Native')
instance = library.load_asset('/Game/NativeQA/MI_Native')
texture = library.load_asset('/Game/NativeQA/T_Native')
assert material and instance and texture
assert material.get_editor_property('two_sided')
assert instance.get_editor_property('parent').get_path_name() == material.get_path_name()
assert abs(edit.get_material_instance_scalar_parameter_value(instance, 'Strength') - 0.35) < 0.00001
assert edit.get_num_material_expressions(material) == 3
assert edit.get_material_default_texture_parameter_value(material, 'Albedo').get_path_name() == texture.get_path_name()
node = edit.get_material_property_input_node(material, unreal.MaterialProperty.MP_BASE_COLOR)
assert node and node.get_class().get_name() == 'MaterialExpressionMultiply'
print('NATIVE_GRAPH_INDEPENDENT_REFERENCE_CHECK_OK')
