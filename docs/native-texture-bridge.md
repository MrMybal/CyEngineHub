# Premier adaptateur natif Texture2D

## Fonctionnement

Le hub copie le pack dans un projet temporaire UE 5.6, inspecte les types/dépendances puis charge le plugin C++ CyHubNativeBridge compilé pour ce moteur. Celui-ci lit directement `FTextureSource` et les propriétés prises en charge. L'échange contient du JSON et les octets source `.bin`, sans TGA/PNG/FBX et sans `AssetImportTask`.

Le même plugin, compilé avec UE 5.5, crée un `UTexture2D`, initialise `FTextureSource`, applique les propriétés et appelle `UPackage::SavePackage`. Le hub démarre ensuite **un nouveau processus 5.5** qui recharge chaque texture et compare format, dimensions, pixels byte à byte et propriétés. Cette réouverture est obligatoire même si l'ancienne option `validateOutput` est désactivée. Une erreur ou un nombre d'assets différent bloque toute publication/installation.

Les plugins sont copiés uniquement dans les deux projets de conversion, jamais dans les moteurs installés ou dans le projet utilisateur final. Le transfert conserve les chemins `/Game` des textures.

## Périmètre v1

- Classe exacte Texture2D, couple majeur/mineur 5.6 → 5.5, Windows x64.
- Données source BGRA8 ou G8, un bloc, une couche, une slice et un mip **source**. Les mipmaps GPU sont régénérées.
- Pixels source limités à 256 Mio par texture. Pas d'UDIM, de texture virtuelle, de HDR, de tableau/cubemap ou de dépendances texture externes.
- Liste de propriétés contrôlée dans `CyHubNativeTextureLibrary.cpp` : sRGB, compression, mipmaps, LOD, filtrage, adressage, réglages de couleur/alpha, padding, normalisation des normales, etc.
- Une propriété éditable non prise en charge avec valeur non par défaut provoque un refus. Les sous-classes et les types non implémentés restent bloqués.
- Le profil Texture2D révision 2 déclare `native-texture-v1`. Une modification JSON ne peut pas autoriser ce code sur une autre famille ou un autre couple de versions.

**Adaptations explicites** : les données GPU et la version du SDK Oodle sont régénérées par le moteur cible ; l'historique `AssetImportData` et l'état de compression différée ne sont pas transférés. Cette politique figure dans les rapports C++. La validation prouve la conservation des pixels source et des propriétés contrôlées, pas une identité bit à bit des données GPU ou une équivalence exhaustive de rendu entre moteurs.

## Compilation et distribution

Les moteurs doivent être déjà installés, ainsi que les outils C++ compatibles pour compiler les plugins. Aucun téléchargement de moteur n'est effectué.

```powershell
./tools/build-native-texture-adapters.ps1 -EngineRoots @($SourceEngine, $TargetEngine)
dotnet build src/CyEngineHub.Desktop -c Release
```

La compilation est séquentielle, car les versions Unreal partagent un cache de règles. UAT remplace exclusivement les packages dédiés `artifacts/native-texture-adapters/UE_5.6` et `UE_5.5`. Le script valide ces chemins et refuse les jonctions. Les DLL, fichiers `.modules` et descripteurs sont copiés au build dans `Extensions/cyenginehub.unreal-version-bridge/native-binaries/UE_5.x`.

Les binaires doivent correspondre aux moteurs : le nom majeur/mineur du dossier ne dispense pas du contrôle BuildId effectué par Unreal. Une autre distribution ou révision du moteur peut nécessiter de les recompiler. Si les binaires sont absents, le hub refuse l'opération avec un diagnostic ; aucune compilation ou modification du moteur n'est lancée implicitement au téléchargement.

## Test réel reproductible

```powershell
dotnet run --project tools/qa/UnrealCapabilitiesSmoke -c Release -- --native-textures $SourceEngine $TargetEngine
```

Le scénario crée ses propres fixtures (image RGBA à pixels variés, normal map, texture G8), passe par le vrai pipeline du hub, recharge le résultat installé, compare les propriétés et pixels, contrôle l'intégrité de tous les fichiers source et altère volontairement un payload **de test** pour vérifier le rejet d'une divergence. L'utilisation de TGA est limitée à la fabrication de la fixture, pas au bridge natif testé.

## Résultats vérifiés le 11 septembre 2026

- Test réel avec UE 5.6.1 et UE 5.5.4 réussi pour les trois textures, y compris réouverture après installation et rejet du pixel altéré.
- Sources originales inchangées. Données du scénario : 6 215 639 octets (hors moteurs, compilation et binaires des adaptateurs).
- Rapport : `artifacts/unreal-capabilities-0bee6b51570340488e43ba4e3f1e2f2f/native-summary.json`.
- Suite .NET complète : 418 tests réussis, aucun échec ; rapports dans `artifacts/test-results-native-final`.
- Build Windows Release : aucun avertissement, aucune erreur ; binaire et adaptateurs dans `artifacts/unreal-native-build`.

Ces résultats ne valident pas encore le transfert natif des StaticMesh, matériaux ou Blueprints, ni les variantes Texture2D hors du périmètre décrit ci-dessus.
