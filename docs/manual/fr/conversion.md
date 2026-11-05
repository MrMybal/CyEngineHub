# Conversion de sources et versions Unreal

La conversion dépend du contenu réel, pas du nom du market. Un téléchargement peut contenir assets Unreal, plugin, scènes Blender, formats d’échange ou sources propriétaires non prises en charge. Conservez originaux, textures et dépendances.

![Options de conversion de sources et création facultative d’UAssets avec le moteur cible.](image:source-conversion)

## Convertir en dérivé local

Depuis une fiche locale, choisissez Convertir en dérivé local. Sélectionnez Blender → GLB/FBX ou Sources RAW → package UAsset, ses options et le profil de stockage. Blender s’installe séparément ; un moteur Unreal cible est requis pour les UAssets. Le chaînage facultatif exporte puis importe dans un projet temporaire du moteur cible. Aucun projet utilisateur n’est installé sans confirmation distincte.

:::flow
Examiner sources de confiance et dépendances
Blender / convertisseur adapté produit GLB ou FBX
L’éditeur Unreal cible importe les données RAW si demandé
Sauvegarder, recharger et examiner les rapports de validation
Publier un dérivé local distinct ; conserver l’original
:::

GLB privilégie PBR/textures embarquées ; FBX ne représente qu’un sous-ensemble des matériaux complexes. Dépendances externes absentes, shaders procéduraux, UDIM/vidéo, Geometry Nodes, rigs ou objets non pris en charge peuvent exiger un accord d’approximation ou provoquer un échec. Cet accord n’est pas un bake universel. Autoexec Blender est désactivé, mais Blender/codecs natifs et convertisseurs externes ne sont pas des bacs à sable.

## Conversion native entre versions Unreal

Choisissez Version Bridge dans la chaîne si nécessaire. Les adaptateurs compilés lisent les données prises en charge et le moteur cible sauvegarde ses propres packages ; changer l’en-tête ne réalise pas un downgrade. Les adaptateurs doivent correspondre aux builds installés.

Le parcours natif 5.7 → 5.5/5.3 a qualifié un pack complet avec textures, matériaux/fonctions, meshes statiques/LOD et deux niveaux. Cela ne qualifie pas tous les Blueprints gameplay, animations squelettiques, World Partition, mondes en streaming ou plugins. Classes/propriétés non représentables restent signalées. Le code d’un plugin peut nécessiter adaptation d’API et recompilation ; un plugin binaire seul ne se retargete pas magiquement.

L’assistance agent exige le [consentement MCP/exécution de code](help:ai-mcp) distinct, puis une validation réelle dans la cible. Voir [dérivés](help:derivatives) et [extensions/SDK](help:extensions-sdk).
