import os
import unreal

if not unreal.CyHubNativeTextureLibrary.run(os.environ["CYENGINEHUB_VERSION_BRIDGE_CONFIG"]):
    raise RuntimeError("Le bridge natif a refusé l'opération. Consulter le rapport et le log.")
