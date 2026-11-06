---
id: conversion
title: Conversion de sources et versions Unreal
description: Convertissez les formats sources ou versions Unreal en conservant les originaux.
section: Assets et bibliothèques
parent: install
order: 14
sourceRevision: 2
---
# Conversion de sources et versions Unreal {#source-conversion-and-unreal-version-bridges}

La conversion dépend du contenu réel, pas du nom du market. Un téléchargement peut contenir assets Unreal, plugin, scènes Blender, formats d’échange ou sources propriétaires non prises en charge. Conservez originaux, textures et dépendances.

```image
{"id":"source-conversion","src":"assets/screenshots/source-conversion.png","alt":"Options de conversion de sources et création facultative d’UAssets avec le moteur cible.","controls":"minimal"}
```

## Convertir en dérivé local {#convert-sources-into-a-local-derivative}

Depuis une fiche locale, choisissez Convertir en dérivé local. Sélectionnez Blender → GLB/FBX ou Sources RAW → package UAsset, ses options et le profil de stockage. Blender s’installe séparément ; un moteur Unreal cible est requis pour les UAssets. Le chaînage facultatif exporte puis importe dans un projet temporaire du moteur cible. Aucun projet utilisateur n’est installé sans confirmation distincte.

```diagram
{"type":"flow","title":"Étapes du processus","nodes":[{"id":"step-0","label":"Examiner sources de confiance et dépendances","x":20,"y":20,"width":528},{"id":"step-1","label":"Blender / convertisseur adapté produit GLB ou FBX","x":20,"y":130,"width":528},{"id":"step-2","label":"L’éditeur Unreal cible importe les données RAW si demandé","x":20,"y":240,"width":528},{"id":"step-3","label":"Sauvegarder, recharger et examiner les rapports de validation","x":20,"y":350,"width":528},{"id":"step-4","label":"Publier un dérivé local distinct ; conserver l’original","x":20,"y":460,"width":528}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

GLB privilégie PBR/textures embarquées ; FBX ne représente qu’un sous-ensemble des matériaux complexes. Dépendances externes absentes, shaders procéduraux, UDIM/vidéo, Geometry Nodes, rigs ou objets non pris en charge peuvent exiger un accord d’approximation ou provoquer un échec. Cet accord n’est pas un bake universel. Autoexec Blender est désactivé, mais Blender/codecs natifs et convertisseurs externes ne sont pas des bacs à sable.

## Conversion native entre versions Unreal {#native-unreal-version-conversion}

Choisissez Version Bridge dans la chaîne si nécessaire. Les adaptateurs compilés lisent les données prises en charge et le moteur cible sauvegarde ses propres packages ; changer l’en-tête ne réalise pas un downgrade. Les adaptateurs doivent correspondre aux builds installés.

Le bridge natif 5.7 → 5.5/5.3 traite les textures, matériaux/fonctions, meshes statiques/LOD et niveaux simples pris en charge. Cela ne garantit pas tous les Blueprints de gameplay, animations squelettiques, World Partition, mondes en streaming ou plugins. Examinez le rapport de chaque asset et validez le résultat dans l’éditeur cible. Les classes/propriétés non représentables restent signalées. Le code d’un plugin peut nécessiter adaptation d’API et recompilation ; changer un libellé de version ne suffit pas pour un plugin binaire seul.

L’assistance agent exige le [consentement MCP/exécution de code](doc:ai-mcp) distinct, puis une validation réelle dans la cible. Voir [dérivés](doc:derivatives) et [extensions/SDK](doc:extensions-sdk).
