---
id: derivatives
title: Packages dérivés et ateliers de conversion
description: Conservez et réutilisez une révision préparée sans modifier le téléchargement original.
section: Assets et bibliothèques
parent: conversion
order: 15
sourceRevision: 2
---
# Packages dérivés et ateliers de conversion {#derived-packages-and-conversion-workspaces}

Un dérivé est une version locale distincte liée au produit/artefact source. Il ne remplace pas le téléchargement fournisseur. Il conserve une conversion, correction ou optimisation pour éviter de refaire la préparation pour chaque projet.

## Deux parcours de création {#two-creation-routes}

Pour les fichiers sources pris en charge, Convertir en dérivé local exécute le convertisseur puis publie sa sortie validée. Pour un package Unreal natif, Create Derived ouvre un atelier : choisissez source locale, moteur cible, type final, nom et notes. Le hub prépare un projet temporaire en préservant l’arborescence Content. Ouvrez-le dans l’éditeur choisi et effectuez vos corrections.

Après sauvegarde/fermeture des travaux éditeur concernés, Package Data crée une révision stockée. Rechargez/vérifiez-la avant une installation importante. Un dérivé peut servir de source à un autre atelier. Les scripts de conversion peuvent aussi proposer de conserver leur résultat comme dérivé ; activez cette option volontairement.

```diagram
{"type":"flow","title":"Étapes du processus","nodes":[{"id":"step-0","label":"L’artefact original reste intact dans la bibliothèque","x":20,"y":20,"width":544},{"id":"step-1","label":"Atelier de conversion ou d’édition isolé","x":20,"y":130,"width":544},{"id":"step-2","label":"Validation moteur cible puis packaging","x":20,"y":240,"width":544},{"id":"step-3","label":"Nouveau dérivé local avec provenance","x":20,"y":350,"width":544},{"id":"step-4","label":"Réutilisation de cette révision locale précise dans les projets","x":20,"y":460,"width":544}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

## Identité et nettoyage {#identity-and-cleanup}

Le sidecar conserve produit/artefact source, nom/notes du dérivé, type final, version moteur connue, date, tailles et compression. Gardez-le avec son contenu. La version doit refléter les données sauvegardées, pas une simple étiquette renommée.

Un AssetPack empaquette Content, pas les caches générés du moteur/projet. L’atelier temporaire peut être supprimé explicitement après publication ; les dérivés déjà stockés restent. Fermez Unreal avant nettoyage. Seules les racines temporaires du hub sont concernées ; ne supprimez pas un dossier ambigu contenant vos seules modifications.

Les contrôles transactionnels protègent le parcours pris en charge, pas tous les outils lancés dans l’éditeur. Lisez les avertissements et sauvegardez indépendamment les corrections importantes. Voir [conversion](doc:conversion), [installation](doc:install) et [stockage](doc:storage).
