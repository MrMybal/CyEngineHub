---
id: settings
title: Paramètres, apparence et connexions
description: Configurez l’apparence, les comptes, marketplaces et préférences de l’application.
section: Réglages et maintenance
order: 21
sourceRevision: 2
---
# Paramètres, apparence et connexions {#settings-appearance-and-connections}

Paramètres distingue réglages généraux, profils spécifiques Unreal et téléchargements moteurs. Utilisez les sous-pages de chaque catégorie plutôt que mélanger les services. Certains champs sauvegardent immédiatement ; d’autres exigent leur bouton Enregistrer. Vérifiez le message de confirmation.

## Apparence et langue {#appearance-and-language}

Apparence propose thèmes, densité et langue des composants CyUI. L’aperçu du thème est immédiat ; enregistrez pour le conserver. Les libellés généraux ne sont pas intégralement traduits. Ce manuel possède un sélecteur English/Français indépendant, anglais par défaut, sauvegardé automatiquement. Le chapitre courant est conservé et aucun INI projet n’est modifié.

## Connexions {#connections}

Epic, Fab, Git, Unity et Megascans ont leurs sous-pages/états. Activer expose le service sans connecter le compte. Epic/Fab peuvent partager leur session directe ; les connecteurs web ont des profils distincts. Git activé ne prouve pas l’authentification d’un remote. Reconnectez le service expiré ; ne partagez jamais son jeton pour le dépanner.

## Marketplaces et téléchargements {#marketplace-and-download-settings}

Choisissez le sous-onglet pour régler cache, temporaire, compression, bibliothèques/quotas et connexions natives propres au market. Parallélisme global/reprise sont dans Téléchargements ; connexions de chunks moteurs dans Unreal. Le navigateur n’hérite pas des connexions natives Fab.

Les dossiers Epic Vault/Bridge peuvent être référencés en lecture seule lorsqu’ils sont pris en charge. Retirer leur enregistrement ne supprime pas l’original. Contrôlez la limite distincte du cache de reconstruction. Changer une bibliothèque finale ne migre pas les assets existants.

## Avancé et données {#advanced-and-data}

Dépôt/canal/recherche initiale des mises à jour de l’application sont distincts des révisions moteur. Les permissions IA/MCP restent indépendantes et doivent être minimales. Journaux et catalogues locaux résident dans les données de l’application, pas le moteur. Le mode portable utilise un portable.flag explicite ; ne l’ajoutez pas à une installation si vous souhaitez garder son profil actuel.

Utilisez [Stockage](doc:storage) pour analyser/nettoyer, pas un simple aperçu de paramètres comme analyse complète. Voir [Fab](doc:fab), [téléchargements](doc:downloads), [MCP](doc:ai-mcp) et [mises à jour du logiciel](doc:app-updates).
