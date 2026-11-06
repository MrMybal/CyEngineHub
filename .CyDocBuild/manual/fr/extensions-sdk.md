---
id: extensions-sdk
title: Extensions du hub, scripts et SDK de conversion
description: Installez des extensions du hub et implémentez des étapes de préparation ou conversion.
section: Plugins et automatisation
parent: plugins
order: 18
sourceRevision: 2
---
# Extensions du hub, scripts et SDK de conversion {#hub-extensions-scripts-and-conversion-sdk}

Trois mécanismes ont des contrats distincts. Une extension du hub décrit une fonction CyEngineHub ; un script d’installation/préparation traite un pack sélectionné ; un .uplugin est chargé par Unreal. Installer l’un ne lui donne pas les capacités des autres.

## Packages déclaratifs du hub {#declarative-hub-packages}

Un dossier extrait contient extension.json validé selon le schéma du dépôt. Ouvrez Extensions → Installer un package, choisissez le dossier, vérifiez cible/remplacement et confirmez. Le hub calcule les empreintes, prépare et reparcourt le package, conserve la version remplacée puis publie la nouvelle. N’incluez ni identifiants, cookies ni chemins de profil utilisateur.

Les manifestes marketplace utilisent ID unique, déclarations Marketplace/WebLink et URL HTTPS d’inventaire autorisée. Les options de stockage commun ajoutent la bibliothèque locale. Un manifeste n’implémente pas à lui seul une API fournisseur. Installer un descripteur n’autorise pas automatiquement l’exécution d’un binaire arbitraire.

## Scripts de préparation/installation {#preparation-installation-scripts}

Utilisez le dossier exposé par la fenêtre d’installation ; ajoutez manifeste/exécutable de confiance, puis actualisez. Types de contenu/cible et options déclarés déterminent les utilisations possibles. Une préparation doit retourner une variante contenue du type annoncé pour l’étape suivante, sans installer implicitement dans un projet utilisateur.

Un script externe possède vos permissions Windows ; ce n’est pas un bac à sable. Relisez code et chemins d’exécutables. Télécharger un pack n’autorise pas son code embarqué. Le contrôle des chemins de résultat ne contrôle pas tous les comportements d’un programme arbitraire.

## SDK de convertisseurs de sources {#source-converter-sdk}

IAssetSourceConverter expose descripteur, extensions/options et ConvertAsync. L’enregistrement utilise AssetSourceConverterRegistry.Register. La requête fournit source, atelier isolé, options et moteur cible facultatif ; le résultat fournit sortie, rapport et type. Le hub gère packaging/nettoyage. Ce SDK C# n’est pas un chargeur automatique de DLL non fiables.

IFabPreparationScript intègre une étape via OutputContentKind et Outputs["variant"]. Consultez le [contrat des convertisseurs](doc:api-source-converter#signature) et le [contrat des scripts d’installation](doc:api-installation-script#parameters) pour les signatures publiques, paramètres, résultats, imports et courts exemples C#. Ce manuel ne présente pas les sources d’implémentation. Voir [conversion](doc:conversion) et [IA/MCP](doc:ai-mcp).
