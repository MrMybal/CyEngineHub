import unreal, os, json
registry = unreal.AssetRegistryHelpers.get_asset_registry()
registry.scan_paths_synchronous(['/Game'], True)
materials, maps = [], []
for data in registry.get_assets_by_path('/Game', recursive=True):
    path = str(data.package_name)+'.'+str(data.asset_name)
    kind = str(data.asset_class_path.asset_name)
    if kind in ('Material', 'MaterialInstanceConstant'):
        asset = unreal.load_asset(path)
        if kind == 'Material':
            unreal.MaterialEditingLibrary.recompile_material(asset)
        # GetStatistics submits missing shader jobs and waits for their completion in the actual RHI.
        stats = unreal.MaterialEditingLibrary.get_statistics(asset)
        row = dict(path=path, pixel_instructions=stats.num_pixel_shader_instructions,
                   vertex_instructions=stats.num_vertex_shader_instructions, samplers=stats.num_samplers)
        if row['pixel_instructions'] <= 0:
            raise RuntimeError('No successfully compiled pixel shader for '+path)
        materials.append(row)
        unreal.log('COMPILED_PACK_MATERIAL '+path+' '+str(row['pixel_instructions']))
for data in registry.get_assets_by_path('/Game', recursive=True):
    if str(data.asset_class_path.asset_name) == 'World':
        world = unreal.EditorLoadingAndSavingUtils.load_map(str(data.package_name))
        if not world:
            raise RuntimeError('Could not open target map '+str(data.package_name))
        actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors()
        maps.append(dict(path=str(data.package_name), actors=len(actors)))
if len(materials) < 7 or len(maps) < 2:
    raise RuntimeError('Full pack material/map coverage is missing')
with open(os.environ['CYHUB_SHADER_REPORT'], 'w', encoding='utf-8') as output:
    json.dump(dict(materials=materials, maps=maps, validation='Compiled through the real rendering RHI; not a pixel-identical cross-version image comparison'), output, indent=2)
