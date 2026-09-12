import os, json, unreal

errors, assets = [], []
lib = unreal.EditorAssetLibrary
tools = unreal.AssetToolsHelpers.get_asset_tools()
root = '/Game/Complex'

def path(asset):
    return asset.get_path_name() if asset else None

try:
    if os.environ['CYHUB_COMPLEX_MODE'] == 'create':
        unreal.AssetRegistryHelpers.get_asset_registry().scan_paths_synchronous(['/Engine/Tutorial/SubEditors/TutorialAssets/Character'], True)
        task = unreal.AssetImportTask()
        task.filename = os.environ['CYHUB_COMPLEX_TEXTURE']
        task.destination_path = root
        task.automated, task.save = True, True
        tools.import_asset_tasks([task])
        texture = lib.load_asset(root + '/T_Complex')
        assert texture, 'Missing source texture'
        material = tools.create_asset('M_Complex', root, unreal.Material, unreal.MaterialFactoryNew())
        sample = unreal.MaterialEditingLibrary.create_material_expression(material, unreal.MaterialExpressionTextureSampleParameter2D, -300, 0)
        sample.set_editor_property('parameter_name', 'Albedo')
        sample.set_editor_property('texture', texture)
        assert unreal.MaterialEditingLibrary.connect_material_property(sample, 'RGB', unreal.MaterialProperty.MP_BASE_COLOR)
        unreal.MaterialEditingLibrary.recompile_material(material)
        instance = tools.create_asset('MI_Complex', root, unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
        unreal.MaterialEditingLibrary.set_material_instance_parent(instance, material)
        unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(instance, 'Albedo', texture)
        mesh = lib.duplicate_asset('/Engine/BasicShapes/Cube', root + '/SM_Complex')
        mesh.set_material(0, instance)
        factory = unreal.BlueprintFactory()
        factory.set_editor_property('parent_class', unreal.StaticMeshActor)
        blueprint = tools.create_asset('BP_Complex', root, unreal.Blueprint, factory)
        unreal.BlueprintEditorLibrary.compile_blueprint(blueprint)
        default = unreal.get_default_object(blueprint.generated_class())
        default.static_mesh_component.set_static_mesh(mesh)
        unreal.BlueprintEditorLibrary.compile_blueprint(blueprint)
        skeleton = lib.duplicate_asset('/Engine/Tutorial/SubEditors/TutorialAssets/Character/TutorialTPP_Skeleton', root + '/SK_Skeleton')
        skeletal = lib.duplicate_asset('/Engine/Tutorial/SubEditors/TutorialAssets/Character/TutorialTPP', root + '/SK_Complex')
        animation = lib.duplicate_asset('/Engine/Tutorial/SubEditors/TutorialAssets/Character/Tutorial_Walk_Fwd', root + '/A_Complex')
        assert skeleton and skeletal and animation, 'Engine tutorial fixture unavailable'
        # Keep the original, valid shared engine skeleton relationship. No retargeting of source assets.
        assert skeletal.get_editor_property('skeleton') == animation.get_editor_property('skeleton'), 'Source mesh/animation skeleton mismatch'
        for asset in [material, instance, mesh, blueprint, skeleton, skeletal, animation]:
            assert lib.save_loaded_asset(asset, only_if_is_dirty=False), 'Source save failed: ' + path(asset)
        world = unreal.EditorLoadingAndSavingUtils.new_blank_map(False)
        actor = unreal.EditorLevelLibrary.spawn_actor_from_class(blueprint.generated_class(), unreal.Vector(0, 0, 0))
        assert actor, 'Blueprint actor not spawned'
        actor.set_actor_label('QA_ComplexActor')
        assert unreal.EditorLoadingAndSavingUtils.save_map(world, root + '/L_Complex'), 'Map save failed'

    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    registry.scan_paths_synchronous(['/Game'], True)
    for asset_path in sorted(lib.list_assets(root, recursive=True, include_folder=False)):
        asset = lib.load_asset(asset_path)
        if not asset:
            errors.append('Cannot load ' + asset_path)
            continue
        kind = asset.get_class().get_name()
        entry = {'path': path(asset), 'class': kind}
        if kind == 'MaterialInstanceConstant':
            entry['parent'] = path(asset.get_editor_property('parent'))
            entry['albedo'] = path(unreal.MaterialEditingLibrary.get_material_instance_texture_parameter_value(asset, 'Albedo'))
        elif kind == 'StaticMesh':
            entry['materials'] = [path(item.material_interface) for item in asset.get_editor_property('static_materials')]
            entry['lods'] = asset.get_num_lods()
        elif kind == 'Blueprint':
            entry['default_mesh'] = path(unreal.get_default_object(asset.generated_class()).static_mesh_component.static_mesh)
        elif kind in ['SkeletalMesh', 'AnimSequence']:
            entry['skeleton'] = path(asset.get_editor_property('skeleton'))
            if kind == 'AnimSequence': entry['duration'] = asset.get_editor_property('sequence_length')
        assets.append(entry)
    if os.environ['CYHUB_COMPLEX_MODE'] == 'create':
        assert len(assets) >= 9, 'Incomplete complex source fixture'
except Exception as error:
    errors.append(str(error))
with open(os.environ['CYHUB_COMPLEX_REPORT'], 'w', encoding='utf-8') as handle:
    json.dump({'assets': assets, 'errors': errors}, handle, indent=2, sort_keys=True)
print('COMPLEX_QA assets=%d errors=%d' % (len(assets), len(errors)))
