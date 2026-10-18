# Bridge natif Unreal 5.7 — pack complet, Alpha 30

Validation locale du 18 octobre 2026. Le vrai pack Foliage VOL3 Oaks, préparé et enregistré dans **Unreal 5.7.4**, est converti intégralement vers **5.5.4 et 5.3.2**. Aucun sous-ensemble ni filtre ne retire les assets difficiles.

## Contenu vérifié

| Type | Nombre |
|---|---:|
| Texture2D | 13 |
| TextureCube HDR BGRE8, source long-lat | 1 |
| Material | 4 |
| MaterialInstanceConstant | 3 |
| MaterialFunction | 7 |
| StaticMesh | 23 |
| World | 2 |
| Classe générée du Blueprint de niveau | 1 |
| **Total de l’inventaire source** | **54** |

Les 23 meshes comprennent **89 modèles de LOD** et **11 meshes Nanite**. Les géométries source, UV, normales, tangentes, couleurs, arêtes dures, matériaux, réglages de réduction, collisions et sockets pris en charge sont transportés. Les LOD automatiques et les données Nanite sont reconstruits par le moteur cible ; leurs comptes de sommets/triangles peuvent différer et ces adaptations sont rapportées.

Les deux niveaux conservent leurs acteurs persistants, composants, transformations, références, propriétés, volumes BSP, réglages de caméra, textes localisés et Blueprint de niveau. Les matériaux dynamiques sont liés et comparés par parent et paramètres, sans dépendre de leur nom temporaire propre à une version du moteur.

## Parcours réel de bout en bout

| Conversion | Résultat | Preuve locale |
|---|---|---|
| 5.7.4 → 5.5.4 | 54/54, dérivé créé, installé puis rechargé | `artifacts/native-full-pack-0140bf26c01b4af38ba4e32a34db5e69/full-pack-summary.json` |
| 5.7.4 → 5.3.2 | 54/54, dérivé créé, installé puis rechargé | `artifacts/native-full-pack-a0121499c0504d39b433619da903c095/full-pack-summary.json` |

Chaque parcours utilise le vrai pipeline d’installation du Hub, un projet temporaire de conversion, puis un projet destinataire distinct. Un nouveau processus cible recharge et compare les fichiers avant publication du dérivé ; un autre recharge le pack installé. Les empreintes SHA-256 de tous les fichiers Content d’origine restent inchangées.

Le plugin C++ 5.7 lit les objets chargés. Les plugins compilés séparément pour 5.5 et 5.3 reconstruisent les données et appellent leur propre `UPackage::SavePackage`. Il ne s’agit ni d’une modification d’en-tête, ni d’un flux binaire 5.7 maquillé, ni d’un export FBX/TGA. Les chemins logiques /Game et les dépendances internes sont conservés.

## Validation graphique et régression

- **5.3, D3D12/SM6** : compilation réelle des 7 matériaux/instances et chargement des deux niveaux, `artifacts/native-pack-replay-cdecf91e04d24e31b1ea5715819d0d00/shader-validation.json`.
- **5.5, D3D12/SM5** : même contrôle graphique, `artifacts/native-pack-replay-17ead68c493c49a3ab72985bfeecabd6/shader-validation.json`. Ce premier test graphique précède la correction des slots de meshes ; le parcours complet final ci-dessus vérifie ces slots après rechargement.
- Régression native **5.6.1 → 5.5.4** : RGBA, normal map, G8, original intact, rechargement obligatoire et altération volontaire des pixels rejetée, `artifacts/unreal-capabilities-41ed7a8c7a4d4701b76f230b252d5ce8/native-summary.json`.
- Suite .NET finale : **696 tests réussis, 0 échec, 12 projets**, TRX dans `artifacts/native-fullpack-final-tests-20261018`.
- Build desktop Alpha 30 : **0 erreur, 0 avertissement**, `artifacts/native-fullpack-alpha30-local-app-20261018`. Les quatre DLL embarquées (5.3/5.5/5.6/5.7) ont les mêmes SHA-256 que les adaptateurs de test.

Ces tests ne sont pas une comparaison pixel-identique de rendus entre versions. Ils prouvent les données explicitement comparées, les références, les shaders compilés et les niveaux chargés, pas une identité de tous les caches internes ou de tous les comportements d’Unreal.

## Adaptations explicites

- Propriétés absentes ou de type différent : seule une valeur égale au défaut source peut retenir le défaut cible, avec rapport. Une valeur non représentable non par défaut reste bloquante, hors adaptations sémantiques explicitement codées.
- UE 5.3 possède le comportement PDO legacy sans le sélecteur ajouté ensuite : `PDOM_Legacy` est adapté à ce comportement fixe, pas à un indice d’enum arbitraire.
- Les options orthographiques désactivées de caméras perspective peuvent être adaptées à 5.3. Les sorties de caméra recalculées ne sont pas confondues avec les réglages persistants de focale/filmback/focus.
- L’espace de localisation du package est conservé. Les objets de navigation et le constructeur de brush sont recréés ; géométrie, matériaux et réglages Lightmass des polygones sont contrôlés séparément des indices BSP dérivés.
- Les acteurs et composants RF_Transient créés par l’éditeur ne sont pas du contenu enregistré du pack. Ils sont régénérés par la cible, comme lors d’une ouverture normale.
- Le flag legacy gamma d’une texture sans SRGB est inactif et peut être normalisé par ValidateSettings ; les pixels restent comparés intégralement.
- Le projet temporaire de conversion active Nanite/SM6 pour éviter la suppression de ses données. **La configuration du projet utilisateur n’est pas modifiée**, garde-fou couvert par les tests.

## Limites Alpha

Ce résultat qualifie le **pack complet ci-dessus**, pas tous les types d’assets Unreal. Les Blueprints arbitraires, SkeletalMesh/animations, World Partition et niveaux en streaming ne sont pas qualifiés par ces tests. Le Blueprint de ce pack contient deux événements sans logique connectée ; son acteur de foliage instancié est vide. Aucun test d’instances de foliage non vides ou de gameplay Blueprint complexe n’est revendiqué.

Les expressions de matériaux restent contrôlées par une liste de classes. Les constantes couleur actives 5.7 nécessitant une adaptation lossless vers FColor restent bloquées. Les textures virtuelles/UDIM et les attributs de géométrie non transportés ne doivent pas être considérés comme pris en charge universellement. Une nouvelle famille de contenu nécessite son propre scénario réel avant qualification.

## Reproduction

```powershell
./tools/build-native-texture-adapters.ps1 -EngineRoots @($Source57, $Target55, $Target53) -OutputRoot $FreshAdapters -KeepHostProject
$env:CYHUB_NATIVE_ADAPTER_ROOT = $FreshAdapters
dotnet run --project tools/qa/UnrealCapabilitiesSmoke -c Release -- --native-pack $PreparedOwnedProject $Source57 $Target55
dotnet run --project tools/qa/UnrealCapabilitiesSmoke -c Release -- --native-pack $PreparedOwnedProject $Source57 $Target53
dotnet run --project tools/qa/UnrealCapabilitiesSmoke -c Release -- --native-pack-shaders $FreshQaProject53 $Target53
dotnet build src/CyEngineHub.Desktop -c Release -p:NativeBridgeAdapterRoot=$FreshAdapters -o $FreshApp
```

Les builds et projets QA restent sous artifacts. Le test rejette une publication partielle et vérifie les originaux. Seuls le descripteur, la DLL et le fichier .modules sont injectés dans les projets de conversion, jamais dans les moteurs installés ni le projet utilisateur. Le BuildId impose une recompilation pour une distribution Unreal incompatible.

Les données du pack détenu par l’utilisateur ne sont pas ajoutées au dépôt. Les rapports finaux sont conservés localement ; les projets temporaires et intermédiaires de compilation peuvent être supprimés puis régénérés. La distribution Windows embarque les adaptateurs des quatre versions ciblées ; un autre BuildId impose une recompilation depuis les sources du plugin.
