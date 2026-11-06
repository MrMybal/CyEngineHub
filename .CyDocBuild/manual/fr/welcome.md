---
id: welcome
title: Bienvenue dans CyEngineHub
description: Préparez votre espace de travail et apprenez à utiliser le manuel.
section: Bien démarrer
order: 0
sourceRevision: 2
---
# Bienvenue dans CyEngineHub {#welcome-to-cyenginehub}

CyEngineHub est un espace de travail Windows centré sur vos moteurs, projets et bibliothèques locales. Ce manuel décrit les fonctions Alpha livrées, pas toutes les possibilités des moteurs tiers. Il est embarqué : lecture, recherche, schémas et illustrations fonctionnent sans compte ni réseau.

## Première utilisation {#first-session}

1. Ouvrez Paramètres. Configurez les fournisseurs de moteurs et les emplacements réellement utilisés. Évitez de faire découvrir un disque entier.
2. Actualisez Moteurs et Projets. Vérifiez les versions, chemins et associations détectés avant de lancer quoi que ce soit.
3. Connectez Epic/Fab uniquement si vous utilisez votre bibliothèque en ligne. Les autres plugins marketplace possèdent leurs propres sessions web.
4. Configurez, pour chaque marketplace, le dossier de téléchargement, les bibliothèques ordonnées, quotas et compression avant les gros transferts.
5. Essayez un petit pack de confiance dans un projet jetable avant d’installer ou convertir des contenus importants.

```diagram
{"type":"flow","title":"Étapes du processus","nodes":[{"id":"step-0","label":"Configurer les emplacements connus des moteurs et projets","x":20,"y":20,"width":536},{"id":"step-1","label":"Vérifier les installations et leurs associations","x":20,"y":130,"width":536},{"id":"step-2","label":"Choisir une variante précise ou une source locale","x":20,"y":240,"width":536},{"id":"step-3","label":"Contrôler la cible, le script et ses options","x":20,"y":350,"width":536},{"id":"step-4","label":"Confirmer, suivre la tâche, puis vérifier dans l’éditeur cible","x":20,"y":460,"width":536}],"edges":[{"from":"step-0","to":"step-1"},{"from":"step-1","to":"step-2"},{"from":"step-2","to":"step-3"},{"from":"step-3","to":"step-4"}]}
```

## Utiliser ce manuel {#how-this-manual-works}

Ce manuel utilise le lecteur CyDoc distribué avec l’application, dans sa propre fenêtre, par une URL locale file://. Choisissez English ou Français dans sa barre d’outils. L’anglais est la langue initiale ; ce choix est mémorisé indépendamment de la traduction partielle de l’interface générale. Ctrl+K ouvre la recherche locale. Précédent et Suivant parcourent les articles ; Retour ferme l’aide sans changer l’écran de l’application. F1 ouvre l’aide de l’écran courant. Échap ferme l’agrandissement, la recherche ou les menus, pas tout le manuel. Si WebView2 est absent, Navigateur ouvre les mêmes fichiers hors ligne dans votre navigateur installé. Aucun serveur, CDN ou compte n’est nécessaire.

Utilisez la commande d’agrandissement de l’image : elle reste dans la fenêtre. Molette pour zoomer, glisser pour déplacer, Recentrer pour réinitialiser. La barre minimale donne aussi accès aux annotations. Les schémas sont des SVG statiques accessibles, pas des éditeurs de graphes. Les captures contiennent des données fictives et montrent les libellés français de l’application ; chaque légende explique la vue dans la langue du manuel. Le lecteur conserve les positions pendant la navigation et à la réouverture. Préférences, annotations et favoris restent dans le profil local du lecteur.

Recherchez depuis le champ en haut ou avec Ctrl+K. Dépliez une rubrique latérale pour voir ses sous-pages. Survolez un titre ou donnez-lui le focus pour lire son titre complet et sa description. La barre d’outils propose six thèmes ; changer de thème ne modifie pas l’apparence de l’application. Mon espace contient vos favoris et notes privés.

```gallery
{"id":"workspace-gallery","title":"Choisir l’espace adapté à votre tâche","controls":"minimal","items":[{"type":"image","src":"assets/screenshots/engines.png","alt":"Catalogue moteur : distinguer les installations par leur libellé et leur chemin.","title":"Installations moteur","description":"Utilisez le catalogue pour choisir l’installation officielle ou source précise."},{"type":"image","src":"assets/screenshots/project-creation.png","alt":"Création de projet : choisir le moteur, le type de projet et le dossier parent.","title":"Créer un projet","description":"Vérifiez le type de projet et sa destination avant de confirmer sa création."},{"type":"image","src":"assets/screenshots/source-conversion.png","alt":"Conversion des sources : choisir un convertisseur et examiner les options moteur cible.","title":"Convertir des assets sources","description":"Préparez un dérivé sans modifier les fichiers originaux du fournisseur."}]}
```

## Avant de modifier des données {#before-making-changes}

Conservez des sauvegardes indépendantes ou un suivi de versions. Une opération de fichiers réussie ne prouve pas que chaque shader, Blueprint ou plugin se comporte à l’identique dans un autre moteur. Lisez avertissements et rapports. Un connecteur activé n’est pas un compte connecté. Une build locale n’est pas nécessairement publiée sur GitHub.

Poursuivez avec [la navigation](doc:navigation), [la création de projet](doc:projects) ou [l’installation de packs](doc:install). En cas d’échec, consultez le [dépannage](doc:troubleshooting).
