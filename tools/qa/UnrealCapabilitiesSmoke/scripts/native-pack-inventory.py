import unreal, json, os
registry = unreal.AssetRegistryHelpers.get_asset_registry()
registry.scan_paths_synchronous(['/Game'], True)
results = []
for data in registry.get_assets_by_path('/Game', recursive=True):
    path = str(data.package_name) + '.' + str(data.asset_name)
    kind = str(data.asset_class_path.asset_name)
    row = dict(path=path, kind=kind)
    try:
        if kind == 'World':
            world = unreal.EditorLoadingAndSavingUtils.load_map(str(data.package_name))
            actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors()
            classes = {}
            for actor in actors:
                name = actor.get_class().get_path_name()
                classes[name] = classes.get(name, 0) + 1
            row['actors'] = classes
            row['world_partition'] = str(world.get_editor_property('world_partition'))
        elif kind in ('Material', 'MaterialFunction', 'StaticMesh', 'TextureCube'):
            asset = unreal.load_asset(path)
            target = os.path.join(os.environ['CYHUB_PACK_INVENTORY'], str(data.asset_name) + '.copy')
            task = unreal.AssetExportTask()
            task.object = asset
            task.filename = target
            task.automated = True
            task.prompt = False
            task.replace_identical = True
            row['copy_export'] = unreal.Exporter.run_asset_export_task(task)
            if kind == 'StaticMesh':
                row['lods'] = asset.get_num_lods()
                row['nanite'] = asset.get_editor_property('nanite_settings').enabled
        results.append(row)
    except Exception as error:
        row['error'] = str(error)
        results.append(row)
with open(os.path.join(os.environ['CYHUB_PACK_INVENTORY'], 'source-diagnostics.json'), 'w', encoding='utf-8') as output:
    json.dump(results, output, indent=2)
