import os
import json
import unreal

config_file = os.environ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"]
with open(config_file, encoding="utf-8") as source:
    config = json.load(source)
handlers = {"native-texture-v1": "CyHubNativeTextureLibrary", "native-material-v1": "CyHubNativeMaterialLibrary", "native-mesh-v1": "CyHubNativeMeshLibrary", "native-world-v1": "CyHubNativeWorldLibrary"}
index_file = os.path.join(config["exchangeRoot"], "native-adapters.json")
if config["operation"] == "export":
    with open(config["planPath"], encoding="utf-8") as source:
        plan = json.load(source)
    adapters = sorted(set(asset["adapter"] for asset in plan["assets"]))
    with open(index_file, "w", encoding="utf-8") as target:
        json.dump(dict(schemaVersion=1, adapters=adapters), target)
else:
    if os.path.isfile(index_file):
        with open(index_file, encoding="utf-8") as source:
            index = json.load(source)
        if index.get("schemaVersion") != 1 or not isinstance(index.get("adapters"), list):
            raise RuntimeError("Index des adaptateurs natifs invalide")
        adapters = index["adapters"]
    else:
        adapters = ["native-texture-v1"]  # Older texture-only variants.

# Textures are created before graphs so texture references already resolve on target.
if not adapters or len(adapters) > len(handlers) or any(not isinstance(a, str) for a in adapters) or len(set(adapters)) != len(adapters):
    raise RuntimeError("Liste des adaptateurs natifs vide ou invalide")
adapters.sort(key=lambda adapter: {"native-texture-v1": 0, "native-material-v1": 1, "native-mesh-v1": 2, "native-world-v1": 3}.get(adapter, 99))
combined = dict(exported=[], imported=[], unsupported=[], errors=[], adaptations=[])
for adapter in adapters:
    if adapter not in handlers:
        combined["errors"].append("Adaptateur natif inconnu : " + adapter)
        break
    part = dict(config)
    part["reportPath"] = config["reportPath"] + "." + adapter + ".json"
    if config["operation"] == "export":
        filtered = dict(plan)
        filtered["assets"] = [asset for asset in plan["assets"] if asset["adapter"] == adapter]
        part["planPath"] = os.path.join(config["exchangeRoot"], adapter + "-plan.json")
        with open(part["planPath"], "w", encoding="utf-8") as target:
            json.dump(filtered, target)
    part_file = config_file + "." + adapter + ".json"
    with open(part_file, "w", encoding="utf-8") as target:
        json.dump(part, target)
    try:
        succeeded = getattr(unreal, handlers[adapter]).run(part_file)
    except Exception as error:
        combined["errors"].append("Échec de l’adaptateur " + adapter + " : " + str(error))
        break
    if os.path.isfile(part["reportPath"]):
        with open(part["reportPath"], encoding="utf-8") as source:
            report = json.load(source)
        for field in combined:
            combined[field].extend(report.get(field, []))
    else:
        combined["errors"].append("Rapport natif absent : " + adapter)
    if not succeeded:
        if not combined["errors"]:
            combined["errors"].append("Adaptateur natif refusé : " + adapter)
        break
with open(config["reportPath"], "w", encoding="utf-8") as target:
    json.dump(combined, target, indent=2)
if combined["errors"]:
    raise RuntimeError("Le bridge natif a refusé l'opération. Consulter le rapport et le log.")
