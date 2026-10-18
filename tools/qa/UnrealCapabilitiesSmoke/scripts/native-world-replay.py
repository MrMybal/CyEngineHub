"""World-only diagnostic against an already reconstructed disposable fixture, never publication QA."""
import json, os, unreal
config_file = os.environ['CYENGINEHUB_VERSION_BRIDGE_CONFIG']
with open(config_file, encoding='utf-8') as source:
    config = json.load(source)
config['reportPath'] = os.path.join(os.path.dirname(config_file), 'world-diagnostic-report.json')
part = config_file + '.world-diagnostic.json'
with open(part, 'w', encoding='utf-8') as output:
    json.dump(config, output)
if not unreal.CyHubNativeWorldLibrary.run(part):
    raise RuntimeError('World-only reconstruction refused; inspect diagnostic report')
unreal.log('WORLD_ONLY_DIAGNOSTIC_OK (not a published full-pack validation)')
