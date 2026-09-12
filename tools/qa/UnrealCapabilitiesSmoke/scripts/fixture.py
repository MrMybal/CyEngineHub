import json, os, unreal

mode = os.environ['CYHUB_QA_MODE']
report = os.environ['CYHUB_QA_REPORT']
errors = []
try:
    if mode == 'create':
        tools = unreal.AssetToolsHelpers.get_asset_tools()
        parent = tools.create_asset('M_Parent', '/Game/OriginalPack', unreal.Material, unreal.MaterialFactoryNew())
        child = tools.create_asset('MI_Child', '/Game/OriginalPack', unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())
        unreal.MaterialEditingLibrary.set_material_instance_parent(child, parent)
        for asset in [parent, child]:
            assert unreal.EditorAssetLibrary.save_loaded_asset(asset, only_if_is_dirty=False)
        task = unreal.AssetImportTask()
        task.filename = os.environ['CYHUB_QA_TGA']
        task.destination_path = '/Game/Textures'
        task.automated = True
        task.save = True
        tools.import_asset_tasks([task])
        assert len(task.imported_object_paths) == 1, 'Texture import failed'
        mesh = unreal.EditorAssetLibrary.duplicate_asset('/Engine/BasicShapes/Cube', '/Game/Models/SM_QA')
        assert mesh and unreal.EditorAssetLibrary.save_loaded_asset(mesh, only_if_is_dirty=False), 'Mesh fixture creation failed'
        audio = unreal.AssetImportTask()
        audio.filename = os.path.join(os.path.dirname(os.environ['CYHUB_QA_TGA']), 'S_QA.wav')
        audio.destination_path = '/Game/Audio'
        audio.automated = True
        audio.save = True
        tools.import_asset_tasks([audio])
        assert len(audio.imported_object_paths) == 1, 'Audio import failed'
    else:
        base = '/Game/QA/OriginalPack' if mode == 'relocated' else '/Game/OriginalPack'
        if mode in ['normal', 'relocated']:
            parent = unreal.EditorAssetLibrary.load_asset(base + '/M_Parent')
            child = unreal.EditorAssetLibrary.load_asset(base + '/MI_Child')
            assert parent and child, 'Asset missing after install'
            assert child.get_editor_property('parent').get_path_name() == parent.get_path_name(), 'Material parent reference not relocated'
            if mode == 'relocated':
                assert not unreal.EditorAssetLibrary.does_asset_exist('/Game/OriginalPack/MI_Child'), 'Old package path still present'
        elif mode in ['converted', 'roundtrip']:
            texture = unreal.EditorAssetLibrary.load_asset('/Game/Textures/T_QA')
            assert texture and texture.get_class().get_name() == 'Texture2D', 'Converted texture not loadable by target engine'
            mesh = unreal.EditorAssetLibrary.load_asset('/Game/Models/SM_QA')
            assert mesh and mesh.get_class().get_name() == 'StaticMesh', 'Converted mesh not loadable'
            assert mesh.get_num_lods() >= 1 and mesh.get_bounds().sphere_radius > 0, 'Converted mesh has no geometry'
            sound = unreal.EditorAssetLibrary.load_asset('/Game/Audio/S_QA')
            assert sound and sound.get_class().get_name() == 'SoundWave', 'Converted sound not loadable'
            assert sound.get_editor_property('duration') > 0, 'Converted sound has no duration'
        elif mode == 'plugin':
            unreal.AssetRegistryHelpers.get_asset_registry().scan_paths_synchronous(['/QaPlugin'], True)
            asset = unreal.EditorAssetLibrary.load_asset('/QaPlugin/M_Parent')
            assert asset and asset.get_class().get_name() == 'Material', 'Installed content plugin not mounted'
except Exception as error:
    errors.append(str(error))
with open(report, 'w', encoding='utf-8') as handle:
    json.dump({'mode': mode, 'errors': errors}, handle, indent=2)
print('CYHUB_QA ' + mode + ' errors=' + str(len(errors)))
