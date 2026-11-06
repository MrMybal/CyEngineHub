---
id: app-updates
title: Mettre à jour CyEngineHub
description: Vérifiez les versions publiées et choisissez quand mettre le hub à jour.
section: Réglages et maintenance
parent: settings
order: 22
sourceRevision: 2
---
# Mettre à jour CyEngineHub {#update-cyenginehub-itself}

L’outil recherche les releases GitHub publiées, pas les fichiers non commités, les tags seuls ou les builds locales. Le dépôt initial est MrMybal/CyEngineHub. Dépôt, recherche initiale et canal de préversions se règlent dans Paramètres → Avancé → Mises à jour.

## Vérifier et décider {#check-and-decide}

La recherche initiale commence après lecture des préférences, indépendamment des catalogues/comptes, avec délai maximal non bloquant. La barre indique recherche, à jour, disponible ou réessai après erreur. Hors ligne/erreur ne signifie pas « à jour ». La recherche manuelle reste accessible lorsque l’automatique est désactivée.

Une nouvelle version ouvre une proposition avec notes et actions. Plus tard/Échap ferme sans télécharger ; la barre permet de rouvrir. Une installation normale ne télécharge/lance pas implicitement un installateur. Une build locale supérieure aux releases ne reçoit pas un downgrade.

```diagram
{"type":"flow","title":"Étapes du processus","nodes":[{"id":"step-0","label":"Trouver une release publiée plus récente autorisée par le canal","x":20,"y":20,"width":560},{"id":"step-1","label":"Lire les notes et choisir Télécharger et installer","x":20,"y":130,"width":560},{"id":"step-2","label":"Télécharger l’installateur Windows et vérifier SHA-256","x":20,"y":240,"width":560},{"id":"step-3","label":"Arrêter proprement le hub ; le lanceur attend la fin du processus","x":20,"y":350,"width":560},{"id":"step-4","label":"Lancer l’installateur vérifié ; rouvrir et contrôler la version","x":20,"y":460,"width":560}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

## Proposition absente ou installation indisponible {#missing-proposal-or-unavailable-installation}

Vérifiez dépôt, canal et préférence enregistrée. Les Alpha/Bêta nécessitent leur canal, sauf publication explicite comme release courante non-préversion. Le badge Latest n’est pas une certification de stabilité.

L’installation automatique exige un installateur Windows compatible et une empreinte valide via digest GitHub ou fichier de sommes. À défaut, release/notes restent consultables mais l’installation automatique peut être indisponible. Une empreinte différente rejette l’installateur. Ne contournez pas ce contrôle en renommant un fichier.

Terminez/annulez les tâches importantes et sauvegardez avant mise à jour. Le lanceur refuse l’installation si l’ancien hub ne s’arrête pas dans le délai. En portable, préférez le ZIP pour conserver emplacement/profil ; l’installateur suit le parcours Windows normal. Construire des binaires ne les publie pas automatiquement. Voir [patchs moteur](doc:engine-updates) et [dépannage](doc:troubleshooting).
