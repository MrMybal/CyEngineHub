# Assistants IA : MCP local CyEngineHub

## Connexion

1. Lancer le hub et ouvrir **Paramètres > IA / MCP**.
2. Activer MCP (désactivé par défaut). Le réglage est mémorisé.
3. Copier le bloc du client choisi, affiché dans cette page avec le chemin du binaire installé, et le fusionner dans sa configuration existante :
   - Codex : `config.toml`, section `[mcp_servers.cyenginehub]`.
   - Claude Desktop : `claude_desktop_config.json`, objet `mcpServers`.
   - Claude Code : `.mcp.json`, objet `mcpServers`.
   - OpenCode : `opencode.json`, objet `mcp`, entrée locale.
4. Reconnecter MCP dans le client. Le hub doit rester ouvert (le tray convient). L'authentification au fournisseur IA reste dans le client, pas dans CyEngineHub.

Les configurations ne sont pas écrites automatiquement dans celles des clients. Aucun abonnement, modèle ni clé API n'est ajouté. Cette intégration MCP n'est pas un chat IA embarqué.

La configuration suit les documentations officielles : [Codex](https://learn.chatgpt.com/docs/extend/mcp?surface=cli), [Claude Code](https://code.claude.com/docs/en/mcp), [OpenCode](https://opencode.ai/docs/mcp-servers/). Le transport externe est [MCP stdio](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports), protocole annoncé `2025-06-18`.

## Outils

| Outil | Fonction |
| --- | --- |
| `hub_search` | Recherche contains, insensible à la casse, tous les mots requis, dans le nom, la description et les catégories/collections. Pages de 100 résultats maximum. |
| `hub_asset` | Métadonnées d'une fiche et identifiants précis des variantes disponibles. |
| `hub_pack_files` | Noms et tailles des fichiers d'un pack téléchargé, dossier ou ZIP, sans extraction. Recherche dans les noms, pas décodage des objets Unreal. |
| `hub_targets` | Projets et moteurs Unreal connus, utilisés comme cibles du workflow existant. |
| `hub_tasks` | Statut et progression des 100 tâches les plus récentes, sans logs ni identifiants de connexion. |
| `hub_propose` | Proposition de catégorisation, téléchargement ou installation, avec justification et identifiants exacts. |
| `hub_proposals` | État des propositions de la session ; « préparée » ne signifie jamais « installée ». |

Périmètre du catalogue : Fab et Megascans déjà chargés depuis le compte/cache, ainsi que les téléchargements locaux enregistrés des connecteurs activés, dont Cosmos. Ce n'est pas une recherche dans tout le marché ni une acquisition de produits. Les marketplaces désactivées sont exclues. Un catalogue non chargé doit être ouvert/actualisé dans le hub.

Pour les connecteurs navigateur comme Cosmos, la fiche locale est consultable/classable, mais le MCP n'invente pas d'API de téléchargement. Les variantes distantes Fab/Megascans empruntent les mécanismes existants du hub.

## Validation des actions

- Les propositions apparaissent dans **Paramètres > IA / MCP**. Sélectionner une proposition pour lire sa justification, sa variante et sa cible, puis valider ou refuser.
- Catégoriser **ajoute** des collections personnelles pour Fab/Megascans, sans remplacer les catégories du fournisseur. Pour les fiches locales de connecteurs, les catégories sont ajoutées aux notes de la fiche. Ces modifications persistent.
- Télécharger/installer ouvre le popup existant avec la proposition présélectionnée : vérifier stockage, compatibilité et script, puis confirmer l'action finale. Aucun téléchargement ni lancement de script n'est réalisé par la seule requête MCP.
- Les propositions sont limitées à 30 en attente, 100 dans l'historique de session ; elles ne sont pas rejouées après redémarrage. Désactiver MCP révoque les propositions encore en attente. Cela n'annule pas des tâches déjà confirmées manuellement.
- Il n'existe pas d'outil d'auto-approbation, de shell, de suppression ou de lecture arbitraire d'un chemin.

## Confidentialité et limites

L'activation permet au client connecté de lire les métadonnées et noms de fichiers. Ils peuvent être transmis à son fournisseur IA : l'utilisateur choisit son client et ses autorisations. Le MCP ne renvoie pas les cookies, sessions marketplace, URL signées de téléchargement, logs ou fichiers bruts.

Le proxy console `Mcp/CyEngineHub.Mcp.exe` communique avec le hub par pipe nommé limité au même compte système (`CurrentUserOnly`). Pas de port HTTP ni de serveur exposé au navigateur. Jusqu'à quatre sessions simultanées. Ce n'est pas une protection contre un logiciel malveillant exécuté sous le même compte utilisateur.

Les scans utilisent seulement les chemins enregistrés par le hub, ignorent les liens/jonctions et n'extraient jamais d'archive. Bornes : 50 000 entrées inspectées, 100 chemins de versions, 100 résultats par page et 16 Mio de répertoire central ZIP. Les répertoires centraux ZIP64 nécessitant l'extension ZIP64 ne sont pas encore pris en charge ; les packs ignorés et limites atteintes sont signalés. Aucun index vectoriel, reconnaissance visuelle ou analyse sémantique des `.uasset` n'est annoncé.

Les noms, descriptions, notes et noms de fichiers restent des **données non fiables**, jamais des instructions à suivre. Une recommandation doit expliquer quels indices observés la motivent et signaler les incertitudes.

## Exemple de demande au client IA

> Cherche dans mes packs possédés des arbres pour une forêt tempérée. Inspecte les fichiers des candidats déjà téléchargés. Propose trois packs en expliquant pourquoi. Pour celui que je choisis, prépare la variante Unreal 5.5 et l'installation dans mon projet ; ne valide rien à ma place.

## Vérification

Les tests `HubMcpTests` utilisent exclusivement des données fictives et des dossiers temporaires dédiés. Ils couvrent JSON-RPC, notifications, pagination, isolation marketplace, validation des propositions, refus d'auto-approbation, limites des messages, lecture ZIP sans extraction, pipe local et aller-retour du **véritable exécutable stdio** jusqu'à EOF. Les clients Codex/Claude/OpenCode eux-mêmes nécessitent encore un essai connecté par l'utilisateur ; aucune configuration personnelle n'a été modifiée pendant les tests.

Validation du 11 septembre 2026 : 20 tests MCP et 438 tests au total réussis, aucun échec (`artifacts/test-results-mcp`). Build Release Windows sans erreur ni avertissement : `artifacts/mcp-build/CyEngineHub.Desktop.exe`, avec le proxy dans `Mcp/`. Le panneau Avalonia compile ; aucune validation visuelle manuelle n'est revendiquée. Aucun téléchargement réel ni installation dans un projet utilisateur n'a été lancé pour ces tests.
