---
id: downloads
title: Téléchargements, reprise et concurrence
description: Réglez les limites de transfert, reprenez les téléchargements et analysez les attentes.
section: Assets et bibliothèques
parent: library
order: 12
sourceRevision: 2
---
# Téléchargements, reprise et concurrence {#downloads-resume-and-concurrency}

Deux réglages visent des ressources différentes : nombre global de téléchargements de bibliothèque simultanés et connexions natives Fab/Megascans par transfert. Plus de connexions n’assure pas un meilleur débit ; serveur, compression, CPU et disque peuvent limiter le résultat.

## Configurer les transferts {#configure-transfers}

Paramètres → Téléchargements règle parallélisme global et reprise au lancement. Chaque panneau Marketplace règle ses connexions natives (1–32 lorsqu’elles sont proposées), stockage et compression. Réduire une limite ne coupe pas les transferts actifs ; cela limite les prochains départs. Les navigateurs suivent le comportement de leur site, pas le contrôle de chunks Fab.

Les demandes natives Fab/Megascans sont journalisées avec asset/variante exacts et options de stockage/compression au moment de leur mise en file. Les interruptions peuvent reprendre ; les échecs passent par Reprendre/réessayer plutôt qu’une boucle automatique infinie. Une annulation volontaire ne redémarre pas implicitement. Un journal corrompu doit être signalé, pas remplacé par une file vide.

## Pourquoi une tâche attend {#why-a-task-may-wait}

Des installations indépendantes peuvent se chevaucher selon les budgets d’E/S. Deux tâches écrivant le même projet ou des destinations imbriquées doivent être sérialisées. Staging commun d’un asset, quotas de placement et contention disque peuvent aussi retarder un départ. Des noms de projets différents ne prouvent pas des E/S indépendantes. En attente n’est pas un échec ; examinez liens et phase avant de créer des doublons.

## Réutilisation locale et limites de reprise {#local-reuse-and-restart-boundaries}

Choisissez l’artefact local correspondant lorsqu’il existe. Une variante Windows ne peut pas être remplacée par un téléchargement Mac sans rapport, même si le titre est identique. Fab réutilise des chunks complets vérifiés ; les incomplets sont retéléchargés. La reprise ne promet pas une continuation HTTP à l’octet près. Extraction/compression interrompues peuvent être répétées.

Le journal persistant de bibliothèque ne restaure pas toutes les installations projet, acquisitions de moteurs ou transferts navigateur après redémarrage. Examinez Actions et les sorties locales avant de les réessayer. Terminez/annulez les transferts actifs avant de fermer une fenêtre de connecteur ; quitter le hub arrête ses navigateurs.

Une sauvegarde de file contient des métadonnées privées. Importez sans lancer, vérifiez les identifiants reconnus, puis reprenez explicitement. Ne partagez pas identifiants ou URL signées. Voir [stockage](doc:storage) et [dépannage](doc:troubleshooting).
