# Test réel de pack complexe : Unreal 5.6 vers 5.5

## Verdict

**Non validé : les deux profils échouent sur ce pack.** Les erreurs bloquent l'installation. Aucun asset partiel installé dans les cibles et SHA-256 du contenu source inchangés.

Moteurs locaux : UE 5.6.1 vers UE 5.5.4. Aucun moteur téléchargé ni projet utilisateur employé. Les processus Unreal du scénario sont terminés.

## Reproduction

```powershell
dotnet run --project tools/qa/UnrealCapabilitiesSmoke -c Release -- --complex $SourceEngine $TargetEngine
```

Mode de caractérisation : le code de sortie 0 signifie que les mesures sont terminées, **pas que la conversion réussit**. Lire `outcomes[].operationSucceeded` dans `complex-summary.json`.

La fixture crée neuf assets, puis les recharge dans un second processus 5.6 avant le vrai script de conversion/installation du hub : texture, matériau paramétré, instance de matériau, StaticMesh utilisant cette instance, Blueprint StaticMeshActor référençant le mesh, map contenant ce Blueprint, SkeletalMesh, Skeleton et AnimSequence copiés des ressources de tutoriel moteur.

Limite : mesh squelettique et animation gardent leur squelette commun de tutoriel `/Engine`. Le Skeleton dupliqué dans `/Game` est un asset supplémentaire, pas un rig autonome validé. Le contrôle porte sur chargement, classes et certaines références, pas une équivalence exhaustive des graphes, du rendu ou de l'animation.

## Résultats

| Étape | Résultat |
| --- | --- |
| Création et réouverture source 5.6 | 9 assets chargés, aucune erreur du contrôle |
| Profil safe | Import texture en erreur, installation bloquée |
| Profil extended | Arrêt UE 5.6 code 3, installation bloquée |
| Réouverture des deux cibles 5.5 | Aucun asset du pack installé |
| Intégrité source | SHA-256 inchangés après chaque conversion |

### Import safe

Deux exports : `SM_Complex.fbx` et `T_Complex.tga`. Sept assets signalés non supportés. L'import FBX crée déjà `T_Complex`, `MI_Complex` et `SM_Complex`. L'import séparé de la texture ne retourne ensuite aucun chemin : le script déclare « import sans résultat » et le service refuse l'installation.

Une collision entre la texture importée avec le FBX et la tâche indépendante est la cause probable, cohérente avec `replace_existing = False`. Il faut valider import/déduplication et références, pas simplement ignorer l'erreur. Un matériau créé par FBX ne prouve pas la conservation du graphe original.

### Export extended

Arrêt dans `Exporter.RunAssetExportTask` lors de l'export FBX :

```text
Assertion failed: MeshObject
Engine/Private/Components/SkinnedMeshComponent.cpp, line 4635
```

La pile implique MeshMergeUtilities et UnrealEd. Le scénario utilise `-NullRHI` : une dépendance au contexte de rendu est une piste, pas une cause démontrée. Le log n'identifie pas avec certitude la tâche d'asset précise. Ni export animation, ni import squelette 5.5 ne sont validés.

### Limites structurelles

Safe exporte StaticMesh, Texture2D, SoundWave ; extended ajoute SkeletalMesh, AnimSequence, DataTable. Aucun des deux ne reconstruit Blueprint, World, Material, MaterialInstanceConstant ou Skeleton en assets natifs équivalents. Corriger les erreurs ci-dessus ne suffira donc pas à convertir fidèlement un pack complexe complet.

Priorités : import déterministe des dépendances ; export squelettique isolé avec contexte compatible ; détection explicite des pertes avant publication ; tests dédiés de reconstruction et références.

## Preuves et stockage

Racine : `artifacts/unreal-capabilities-a4cffb32de624c4ab00953182436ae32/`.

- `source-reopen.json`, `target-safe.json`, `target-extended.json`, `complex-summary.json`.
- `Work_safe/.staging/d98b3778932942d0a31cf18f591633c0/` : source-report.json, target-report.json, target-editor.log.
- `Work_extended/.staging/b49f77b80d6a417daae439d6644d69e2/source-editor.log`.

Pic échantillonné de cette racine : 11 397 131 octets (~10,9 Mio). Les 14 racines `artifacts/unreal-capabilities-*`, essais précédents compris, totalisent moins de 65 Mo au contrôle final. Ces chiffres excluent moteurs préinstallés et binaires de build. Runner limité à 8 Gio par scénario, quatre minutes par commande.

Aucune correction de production dans cette passe : scénario QA et documentation des échecs observés.
