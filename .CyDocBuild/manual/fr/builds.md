---
id: builds
title: Builds, packaging et toolchains
description: Générez les fichiers IDE, compilez des cibles et préparez les presets de packaging.
section: Moteurs et projets
parent: projects
order: 7
sourceRevision: 2
---
# Builds, packaging et toolchains {#builds-packaging-and-toolchains}

Un build compile une cible ; générer les fichiers IDE produit une solution/espace de travail. Ce n’est pas la même chose que cuire/packager une distribution. Les outils Unreal et le compilateur/SDK compatibles doivent être installés séparément.

## Compiler un projet {#build-a-project}

1. Ouvrez sa fiche et vérifiez l’association moteur.
2. Ouvrez Builds & Packaging. Choisissez un preset ou créez-en un avec cible, plateforme, configuration et opération.
3. Pour un éditeur natif, utilisez la cible Editor du projet, la plateforme requise et Development/DebugGame selon le besoin. Shipping vise normalement le jeu, pas l’éditeur.
4. Exécutez et suivez la progression de la fiche ou Actions. Cherchez la première erreur significative dans la sortie conservée, pas seulement le code final.
5. Retour au projet quitte la sous-page. Le double-clic sur une action liée ou son lien projet rouvre la fiche pendant le travail.

Les presets de packaging exposent cook, stage, package, archive, Pak/IoStore, compression et prérequis lorsqu’ils sont pris en charge. Vérifiez chemins et réglages : cocher une option ne garantit pas son acceptation pour chaque moteur/plateforme. La cross-compilation nécessite la toolchain externe correspondante.

## Outils et prérequis {#tools-and-prerequisites}

Lancez l’analyse explicite des toolchains pour inventorier Visual Studio/MSVC, .NET et les SDK requis. La détection lit chemins connus, registre/configurations et commandes de versions bornées. Ce n’est pas un installateur de SDK et elle ne réécrit pas les variables d’environnement. Les plages de compilateurs recommandées/interdites par moteur priment sur le choix du MSVC le plus récent.

Les valeurs Processus/Utilisateur/Machine peuvent différer ; redémarrez les applications concernées après modification volontaire de l’environnement via vos outils/installateurs. La découverte Android/Quest peut lancer le serveur local ADB normal, sans installer ni démarrer d’applications sur l’appareil.

## Solution ou compilation initiale absente {#missing-solution-or-initial-build}

Vérifiez si le projet est Blueprint seul, si la génération C++ a fini et quel moteur/compilateur a été utilisé. Compiler ne génère pas à lui seul la solution. Corrigez d’abord l’erreur de génération, puis relancez le build Editor. Ne supprimez pas Source ou Config pour réparer des fichiers générés. Voir [création](doc:projects), [stockage](doc:storage) et [dépannage](doc:troubleshooting).
