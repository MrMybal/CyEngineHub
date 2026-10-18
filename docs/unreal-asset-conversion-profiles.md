# Profils de conversion Unreal par type d'asset

## Disponible dans le hub

Dans **Extensions Hub → Unreal Version Bridge** : catalogue JSON facultatif, aperçu des profils et bouton **Enregistrer / recharger les profils**. Copier le catalogue intégré avant de l'éditer pour éviter qu'une mise à jour du logiciel ne remplace ses modifications.

Dans **Configurer** du script **Unreal · Convertir puis installer** :

- `conversionMode` : `native` (par défaut) ou `portable` ;
- `assetProfileFile` : catalogue JSON remplaçant le réglage global, vide pour hériter ;
- `analyzeOnly` : produit un diagnostic sans exporter, publier ou installer ;
- `allowLossyConversion` : consentement explicite aux pertes du chemin portable, désactivé par défaut.

Le mode natif dispose d'adaptateurs C++ expérimentaux **Texture2D 5.6 → 5.5** et **Texture2D, Material, MaterialInstanceConstant 5.7 → 5.5/5.3**. Il transfère les pixels source BGRA8/G8 et des graphes contrôlés, sans export/import de fichier image. Les classes et nœuds non adaptés restent bloquants. Voir [le compte rendu 5.7](native-57-bridge-validation.md). Le chemin portable reste explicitement séparé.

## Catalogue v1

Fichier intégré : `extensions/builtin/cyenginehub.unreal-version-bridge/profiles/ue-5.6-to-5.5.json`.

Pour le natif 5.7 → 5.5/5.3, le service choisit automatiquement `ue-5.7-native.json` (six profils). Le champ personnalisé, s'il est renseigné, remplace cette sélection. L'aperçu dans Extensions affiche les deux catalogues intégrés.

Onze types : StaticMesh, Texture2D, SoundWave, SkeletalMesh, Skeleton, AnimSequence, Material, MaterialInstanceConstant, Blueprint, World, DataTable. Une règle correspond exactement à un type et un couple majeur/mineur. Les patches ne sont pas discriminés par ce schéma ; les essais réels actuels utilisent 5.6.1 et 5.5.4 et ne valident pas toutes les révisions.

Chaque entrée contient : `id`, `revision`, `assetType`, `sourceVersion`, `targetVersion`, `nativeAdapter`, `portableAdapter`, `checks`, `losses`, `notes`.

- `checks` documente les contrôles à prévoir pour la fidélité ; cette liste n'exécute pas automatiquement des validations.
- `notes` explique les adaptations manquantes ; `losses` documente les pertes envisagées.
- Les adaptateurs exécutables sont autorisés par le code, pas par une déclaration JSON. Écrire un identifiant natif dans le fichier n'active aucune conversion native.
- Schéma, révisions, champs obligatoires et doublons sont validés. Fichier limité à 1 Mio, 256 profils. Aucun wildcard de version et aucune commande exécutable fournie par le JSON.

## Analyse et garde-fous exécutés

Une copie isolée est ouverte avec le moteur source. `source-inspect.py` inventorie classes, dépendances de packages directes (hard/soft) et indicateurs Nanite, LOD multiples, virtual texturing. Toute erreur d'inspection bloque l'asset. Ce n'est pas encore une analyse exhaustive des structures binaires ni des dépendances transitives.

Le plan distingue `unsupported`, `blocked`, `loss-confirmation-required`, `lossy-confirmed` et `adaptation-required`. Ce dernier indique un candidat natif, pas une conversion déjà validée : le contrôle C++ peut encore le refuser. **Un seul asset bloquant arrête le pack entier avant export.** Les graphes natifs 5.7 autorisent les dépendances `/Game` présentes dans le même inventaire ; une dépendance non convertible bloque toujours l'ensemble. Les dépendances de plugins non adaptées, Nanite, LOD multiples et virtual texturing restent bloquants même avec accord sur les pertes.

Les adaptateurs portables autorisés sont limités à StaticMesh/FBX, Texture2D/TGA et SoundWave/WAV. Ils restent expérimentaux : les dépendances moteur, propriétés, rendu et données dérivées ne sont pas intégralement comparés. Le consentement ne garantit pas la fidélité. Les anciens préréglages `safe`/`extended` ne contournent pas les règles par type ; l'export squelettique qui plantait reste bloqué.

Les sources natives `.umap` seules sont aussi inspectées. Un export doit correspondre au plan et chaque asset attendu doit être exporté. Un rapport contenant des types non convertibles empêche toute publication partielle. Les fichiers portables seuls exigent aussi le mode portable et l'accord sur les pertes ; ils n'ont pas d'inventaire natif préalable.

## Traçabilité

La zone temporaire contient `asset-inventory.json`, `asset-conversion-plan.json`, `profiles-snapshot.json` et les logs. L'analyse seule et les échecs conservent ces diagnostics. Leur chemin est retourné dans les résultats de la tâche / le message d'erreur.

Une variante publiée conserve le plan et le catalogue exact utilisés, en plus du rapport source/cible existant. Les champs de contrôle du catalogue ne doivent jamais être assimilés à une validation exhaustive déjà effectuée.

## Vérifications du 11 septembre 2026

- Suite .NET : **414 tests réussis**, dont 30 nouveaux cas ; résultats sous `artifacts/test-results-asset-profiles-full/`.
- Build Windows Release : zéro avertissement, zéro erreur, `artifacts/unreal-profile-build/CyEngineHub.Desktop.exe`.
- Test réel complexe 5.6.1 → 5.5.4 : neuf assets inspectés sans erreur, dépendances retrouvées, analyse native produite, modes safe/extended bloqués **avant export**, aucune publication/installation partielle, contenu source SHA-256 inchangé et cibles vides vérifiées après réouverture 5.5.
- Preuves : `artifacts/unreal-capabilities-dc0c9e5ed3c74e6a84f3a2e21f045ed5/complex-summary.json`. Pic échantillonné de ce scénario : 10 587 137 octets (~10,1 Mio), aucun projet utilisateur utilisé.
- Chemin portable réellement retesté après ajout des profils : StaticMesh simple, Texture2D et SoundWave convertis de 5.6 vers 5.5 et rechargés ; installation standard, relocalisation et installation de plugin également vérifiées. Scénario : `artifacts/unreal-capabilities-213487d2b23c43c08ebcebb13786f470/summary.json`. L'aller-retour des fichiers par Unity/Godot/CryEngine vérifie les transferts et empreintes, pas leur import dans les éditeurs de ces moteurs.

Ces premiers tests valident l'analyse et les garde-fous, **pas le downgrade natif d'un pack complexe**. Les validations ultérieures de l'adaptateur Texture2D sont documentées séparément. Les adaptateurs StaticMesh et autres familles restent à implémenter.
