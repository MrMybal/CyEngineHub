# Tests inter-moteurs — 11 septembre 2026

## Résultat vérifié

- **384 tests réussis, 0 échec** : 22 nouveaux cas depuis la passe précédente.
- Build Windows Release : **0 erreur, 0 avertissement**, mis à jour dans `artifacts/unreal-validated-build`.
- Conversions réelles **Unreal 5.6.1 → 5.5.4** et **5.5.4 → 5.6.1** : Texture2D, StaticMesh et SoundWave rechargés dans le moteur cible. Le mesh possède une géométrie non vide et le son une durée positive.
- Aller-retour réel par les exécutables du pont : **Unreal → dossier de projet Unity → dossier de projet Godot → dossier de projet CryEngine → Unreal**. Les trois fichiers exportés FBX/TGA/WAV sont identiques à chaque étape (SHA-256), puis réimportés et rechargés dans Unreal 5.5.4.

## Ce que les tests inter-moteurs valident exactement

Asset Bridge transporte des formats d’échange ; il ne traduit pas les formats natifs ni les scripts. Les 12 directions entre les quatre familles sont testées avec le planificateur et le code réel de l’outil (exécuté en processus xUnit). Le test d’aller-retour exécute en plus l’outil comme vrai processus externe.

Les projets Unity/Godot/CryEngine sont des fixtures de structure : **leurs éditeurs ne sont pas lancés**. Le rendu, les matériaux, les scènes, les plugins d’import tiers et la compatibilité effective de chaque format dans chaque version de ces moteurs restent à tester dans leurs éditeurs. L’aller-retour ne constitue pas une conversion de scènes Unity ou Godot en scènes Unreal.

## Nouveaux cas automatisés

| Cas | Nombre | Vérification |
|---|---:|---|
| Directions inter-moteurs | 12 | Plan autorisé, destination propre au moteur, copie du modèle et de ses fichiers annexes, identité des octets, exclusion du code et des .uasset |
| Plan expiré / source modifiée / annulation | 3 | Refus sans publication |
| Destination déjà occupée | 1 | Aucun écrasement, source conservée |
| Modèle seul avec dépendances externes | 2 | Refus explicite glTF/OBJ, demande de sélectionner le dossier complet |
| Installation + rollback | 4 | Conservation des .bin et .mtl puis retrait des fichiers installés, source inchangée, dans les quatre types de projet |

Le test d’installation Unreal porte sur un pack natif contenant aussi des fichiers annexes. Son installateur par copie refuse normalement un pack exclusivement RAW : celui-ci nécessite l’import par l’éditeur, testé séparément dans l’aller-retour.

Une assertion de régression existante vérifie également que les deux commandlets Version Bridge activent l’audio et n’emploient plus `-NoSound`.

## Défauts corrigés

1. **glTF/OBJ incomplets** : `.bin` et `.mtl` étaient ignorés par le pont et les installateurs. Ils sont maintenant conservés dans la même arborescence. Le script d’import Unreal ne tente pas de les importer comme des assets autonomes ; ils restent disponibles pour l’importeur du modèle.
2. **Sélection d’un modèle seul** : un glTF avec URI externe ou un OBJ avec `mtllib` pouvait être transféré sans ses dépendances. L’outil et le planificateur demandent maintenant le dossier du pack. Cette mesure ne reconstruit pas automatiquement toutes les dépendances externes à ce dossier.
3. **Conversion de sons Unreal** : `-NoSound` empêchait l’initialisation des décodeurs en commandlet ; le test a reproduit `Decoder for AudioFormat 'BINKA' not found` dans le moteur cible. Le Version Bridge emploie désormais `-AllowCommandletAudio`. Aucun son n’est joué par le test.

Les restrictions sur les scripts/exécutables restent en place. Le transfert des fichiers `.bin`/`.mtl` est une copie de données, pas une exécution.

## Isolation et limites

- Aucun moteur téléchargé ; seuls les moteurs Unreal déjà installés ont été exécutés.
- Uniquement de nouveaux projets et catalogues QA. Texture et silence générés ; mesh du cube standard du moteur dupliqué dans le projet QA. Aucun asset ou projet utilisateur utilisé.
- DDC, fichiers temporaires et résultats de conversion confinés dans la racine de chaque test ; plafond de **8 Gio par exécution**, délai de quatre minutes par commandlet.
- Ensemble des huit dossiers `unreal-capabilities-*` des deux passes : **moins de 40 Mo**. Outils QA et build dédié restent autour de **2,1 Go**, loin des 30 Go demandés. Pas de nouvelle copie complète du build.
- Pas de compilation C++, de conversion universelle de Blueprints, de mondes, d’animations squelettiques ou de matériaux complexes validée ici.
- Aucun commandlet de test ne reste actif à la fin. Rapports et projets conservés pour diagnostic.

## Preuves

- Suite complète : `artifacts/test-results-inter-engine-final/*.trx`, **384/384**.
- Régression initiale des dépendances : `artifacts/inter-engine-initial/*.trx`, 12 échecs avant correction.
- 5.6 → 5.5 : `artifacts/unreal-capabilities-c06fa3265ea442ef9aa9b16e65d9a476/summary.json`.
- 5.5 → 5.6 : `artifacts/unreal-capabilities-773fa0c9a80e492aaf5d757eec6d821f/summary.json`.
- Aller-retour externe et rechargement : `artifacts/unreal-capabilities-cbc7956177dc43f4b1c7fd48c04ed3ed/summary.json`.
- Reproduction de l’échec audio avant correction : `artifacts/unreal-capabilities-f7695902460e42b09cae015c19743837/ConversionWork/.staging`.

Pour rejouer :

```powershell
dotnet test CyEngineHub.sln -c Release --logger trx --results-directory artifacts/test-results
dotnet run --project tools/qa/UnrealCapabilitiesSmoke -c Release -- --live $SourceEngine $TargetEngine
```

La commande réelle crée toujours une nouvelle racine QA. Inverser les deux chemins pour tester la montée de version. Elle inclut maintenant l’aller-retour par le pont externe.
