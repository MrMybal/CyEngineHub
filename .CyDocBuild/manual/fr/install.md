---
id: install
title: Installer un pack dans des projets
description: Choisissez les cibles, variantes, scripts et options de conflit avant installation.
section: Assets et bibliothèques
parent: library
order: 13
sourceRevision: 2
---
# Installer un pack dans des projets {#install-a-pack-into-projects}

Installer copie/importe dans une cible. Télécharger stocke un package de bibliothèque. Convertir prépare éventuellement une autre représentation avant installation. Choisissez l’action adaptée au lieu de les confondre.

## Contrôler la fenêtre d’installation {#review-the-installation-window}

1. Ouvrez la fiche produit/locale et choisissez Installer/configurer l’installation.
2. Sélectionnez les projets réels. Recherche et filtre compatibles uniquement déterminent les cibles visibles ; désactivez le filtre pour examiner les autres.
3. Vérifiez toutes les variantes proposées, versions moteur et plateformes. Un avertissement de compatibilité éclaire le choix ; ce n’est pas une absence de téléchargement.
4. Choisissez méthode/chaîne par cible et ouvrez Configurer. Vérifiez moteurs source/cible, sous-dossier de destination et politique de conflits.
5. Préférez le package local exact utilisable. Relisez transfert réseau éventuel et option de dérivé avant Installer dans les cibles.

## Scripts et forçage explicite {#scripts-and-explicit-force}

Import de contenu natif, installation de plugin, import RAW et conversion de version sont des méthodes différentes. Une chaîne peut préparer les données avant l’installation finale ; ordre et types de sortie déclarés comptent. Actualisez les scripts après ajout d’un script local de confiance.

L’option explicite de forçage/bypass permet de tenter la méthode choisie malgré des avertissements de compatibilité ; elle ne rend pas une sérialisation non prise en charge fonctionnelle, ne donne pas de droits fournisseur et ne fournit pas les outils/fichiers absents. Le succès n’est pas garanti. Gardez une sauvegarde indépendante et contrôlez toujours la cible affichée.

Choisissez les conflits volontairement. Sauvegardes et reçus ne couvrent que les parcours qui les prennent en charge ; un script externe n’est pas nécessairement transactionnel. Les imports dans l’éditeur ouvert sont plus stricts et n’écrasent pas les assets existants. Fermez éditeurs/builds conflictuels lorsqu’un accès exclusif est requis.

## Vérifier le résultat {#verify-the-result}

Suivez Actions jusqu’à préparation, import et validation. Ouvrez le projet lié, rechargez le contenu dans le moteur cible et examinez dépendances, matériaux et Blueprints. Un transfert à 100 % n’est pas la fin de l’installation. Conservez les rapports des conversions en échec ou approximatives. Voir [conversion](doc:conversion), [plugins](doc:plugins) et [dérivés](doc:derivatives).
