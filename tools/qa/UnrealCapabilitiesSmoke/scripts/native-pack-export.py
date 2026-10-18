import unreal, json, os
root = os.environ['CYHUB_PACK_INVENTORY']
registry = unreal.AssetRegistryHelpers.get_asset_registry()
registry.scan_paths_synchronous(['/Game'], True)
handlers = {'native-texture-v1': unreal.CyHubNativeTextureLibrary, 'native-material-v1': unreal.CyHubNativeMaterialLibrary, 'native-mesh-v1': unreal.CyHubNativeMeshLibrary, 'native-world-v1': unreal.CyHubNativeWorldLibrary}
types = {'Texture2D': 'native-texture-v1', 'TextureCube': 'native-texture-v1', 'Material': 'native-material-v1', 'MaterialFunction': 'native-material-v1', 'MaterialInstanceConstant': 'native-material-v1', 'StaticMesh': 'native-mesh-v1', 'World': 'native-world-v1', 'BlueprintGeneratedClass': 'native-world-v1'}
for adapter, handler in handlers.items():
    if os.environ.get('CYHUB_ADAPTER_FILTER') and adapter not in os.environ['CYHUB_ADAPTER_FILTER'].split(','):
        continue
    assets = [dict(assetPath=str(d.package_name)+'.'+str(d.asset_name), assetType=str(d.asset_class_path.asset_name), adapter=adapter) for d in registry.get_assets_by_path('/Game', recursive=True) if types.get(str(d.asset_class_path.asset_name)) == adapter]
    plan_file = os.path.join(root, adapter+'-plan.json')
    with open(plan_file, 'w') as target: json.dump(dict(canConvert=True,mode='native',assets=assets),target)
    config = dict(exchangeRoot=root,operation='export',planPath=plan_file,reportPath=os.path.join(root,adapter+'-report.json'))
    config_file = os.path.join(root,adapter+'-config.json')
    with open(config_file, 'w') as target: json.dump(config,target)
    result = handler.run(config_file)
    unreal.log('TRANSPORT_DIAGNOSTIC '+adapter+' '+str(result)+' ('+str(len(assets))+' assets; not full-pack publication)')
