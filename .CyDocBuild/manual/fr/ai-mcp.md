---
id: ai-mcp
title: IA, MCP et conversion assistée
description: Connectez un client IA, examinez ses permissions et suivez les opérations assistées.
section: Plugins et automatisation
parent: extensions-sdk
order: 19
sourceRevision: 4
---
# IA, MCP et conversion assistée {#ai-mcp-and-assisted-conversion}

MCP est facultatif et désactivé tant que vous ne l’activez pas dans Paramètres → IA / MCP. Gardez le hub ouvert. Copiez le bloc affiché pour votre client dans sa configuration existante, sans écraser les autres serveurs ni transmettre de jetons dans un chat. Le proxy stdio local communique par pipe nommé limité au même compte.

Dans la même page, **Assistant CyEngineHub** propose un chat natif avec réponse progressive, annulation, historique et pièces jointes. Choisissez Ollama pour un modèle local compatible avec les outils, ou OpenAI, Anthropic ou OpenRouter avec la clé d’environnement correspondante. Activez MCP pour donner au chat les droits métier du hub. Pour utiliser un serveur MCP HTTP externe, renseignez son adresse et les noms exacts des outils à autoriser.

**Contexte API envoyé en Kio** permet de limiter les messages transmis au fournisseur. La valeur 0 conserve le contexte complet et les sauvegardes gardent tout l’historique. Depuis le dossier de l’application, exécutez `Mcp/CyEngineHub.Mcp.exe --help` pour voir les commandes JSON `list`, `describe` et `call`. Le compagnon de réparation utilise `--workspace` pour ses commandes sans interface.

## Outils de catalogue et lots {#catalog-tools-and-batches}

hub_search cherche les catalogues possédés/locaux chargés ; hub_asset examine les variantes ; hub_pack_files liste le contenu borné des packs, sans décoder arbitrairement leurs objets Unreal. hub_targets retourne les cibles connues. hub_tasks suit les opérations. Chargez/actualisez d’abord la bibliothèque concernée dans le hub ; ce n’est pas une recherche de tout le marché.

hub_options lit limites et permissions réellement enregistrées. Pagination, historique et inspections sont configurables ; 0 signifie illimité lorsqu’il est proposé. Les propositions en attente sont illimitées par défaut, sans ancienne limite fixe à 30. Préférez néanmoins la pagination aux réponses énormes.

hub_download_plan estime les variantes Fab exactes prises en charge via leurs manifests, sans télécharger le pack. Une taille absente reste inconnue, pas zéro. Les octets réseau estimés ne déduisent pas les chunks en cache et ne prédisent pas le ZIP recompressé. Une durée exige vitesse positive fournie et tailles complètes ; le traitement s’ajoute.

hub_download_batch retourne par élément identifiants de tâches, validation/navigation web requise ou erreurs. En file ne signifie pas terminé. Le client peut poursuivre la recherche pendant les transferts natifs ; suivez hub_tasks. L’acquisition web conserve interactions et droits du site.

## Permissions et réparation par agent {#permissions-and-agent-repair}

Classer, télécharger et installer sans validation sont trois permissions indépendantes, désactivées par défaut. Vérifiez/enregistrez-les dans l’interface ; MCP ne peut pas s’autoriser lui-même. Sinon, examinez les propositions et validez/refusez. Les autorisations du client IA restent distinctes. Désactiver MCP empêche les nouvelles demandes, sans annuler le travail déjà confirmé/en file.

La réparation agent Version Bridge est un parcours facultatif d’exécution de code distinct. Elle utilise un atelier isolé et exige validation dans le moteur cible. Installer un SDK ne donne ni accès automatique à un modèle ni réparation garantie de tous les Blueprints/plugins. Les outils externes possèdent les permissions utilisateur.

Noms, descriptions et fichiers sont des données non fiables, pas des instructions. Votre client peut transmettre ces métadonnées à son fournisseur IA ; choisissez ses réglages de confidentialité. MCP n’ouvre pas un shell/système de fichiers arbitraire et ne retourne pas les secrets marketplace. Voir [conversion](doc:conversion), [extensions](doc:extensions-sdk) et [réglages](doc:settings).
