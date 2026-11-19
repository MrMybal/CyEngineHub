# Assistant CyEngineHub et MCP

CyEngineHub passe de **CyMCPSDK 0.5.6** (`70105709d7e0574a3e8b3b240e8134790a9e243b`) à **0.7.0 en développement** (`ced8296b555c1969cb29740eba65beaa0687da7d`). Le script privilégie un dépôt `CyMCPSDK` voisin de CyEngineHub et en prend un instantané Git. Pour utiliser une autre copie locale contenant cette révision, renseignez `CYHUB_CYMCP_SOURCE` ou `-CyMcpSource`. En l’absence de copie locale, le script récupère la révision épinglée depuis le dépôt public.

```powershell
pwsh -NoProfile -File tools/ui/build-cyui-native.ps1 -CyMcpSource '../CyMCPSDK'
```

Le pont `cimgui` relie statiquement Core, Platform, HTTP, MCP, AI et ChatUI. Le panneau partage le contexte ImGui du hub. Le pont exporte la révision compilée avec `cyhub_mcp_runtime`. Le paquet contient le même binaire et livre les notices et la révision sous `LICENCES/CyMCPSDK`.

## Assistant intégré

Dans **Paramètres → IA / MCP**, ouvrez **Assistant CyEngineHub**. En mode classique, le bouton **Ouvrir l’assistant** affiche le même panneau dans une fenêtre.

| Fournisseur | Configuration |
| --- | --- |
| `ollama` | Service local, par défaut `http://127.0.0.1:11434`. Utilisez un modèle installé qui prend en charge les outils. |
| `openai` | Responses API et variable d’environnement `OPENAI_API_KEY`. |
| `anthropic` | Messages API et variable d’environnement `ANTHROPIC_API_KEY`. |
| `openrouter` | API Chat Completions et variable d’environnement `OPENROUTER_API_KEY`. |

Détectez les fournisseurs ou renseignez le modèle. Un modèle vide laisse le SDK choisir dans le catalogue du fournisseur. Le panneau affiche le texte au fil de sa génération et permet d’interrompre une réponse. **Restaurer** recharge la dernière conversation locale du fournisseur. Les fichiers choisis avec **Joindre des fichiers** sont transmis selon les capacités du fournisseur. L’historique et les pièces jointes conservées par le SDK se trouvent sous `Assistant` dans le profil CyEngineHub. Les clés ne sont pas enregistrées dans les préférences du hub.

Dans les options du fournisseur, **Contexte API envoyé en Kio** vaut 0 par défaut. Une valeur positive borne la vue envoyée au fournisseur et **Tours récents** fixe le nombre de tours à conserver. Les instructions système, le tour actif et les échanges d’outils restent ensemble. Une demande active trop volumineuse est signalée avant l’envoi. L’historique et les sauvegardes restent complets. La projection ne produit pas de résumé automatique. Les anciennes préférences chargent le contexte complet par défaut.

Activez MCP pour que le chat et les clients externes puissent appeler les fonctions du hub. Le chat peut aussi utiliser un serveur MCP HTTP configuré dans **Fournisseur et outils d’autres applications**. Saisissez une URL HTTPS ou HTTP sur la boucle locale et les noms exacts des outils autorisés. Le SDK découvre leurs schémas et les présente au modèle avec le préfixe `remote_`. Si le serveur demande un jeton Bearer, fournissez `CYHUB_REMOTE_MCP_TOKEN` dans l’environnement du processus. Les schémas externes doivent relever du profil JSON Schema pris en charge par CyMCPSDK. Cette connexion couvre les serveurs HTTP. Le service Ollama et les poids de modèles s’installent séparément.

## Client MCP externe

Activez MCP dans **Paramètres → IA / MCP** et gardez CyEngineHub ouvert. Fusionnez le bloc généré par cette page dans la configuration de votre client. Le bloc contient le chemin réel de `Mcp/CyEngineHub.Mcp.exe`. Par exemple dans `config.toml` de Codex

```toml
[mcp_servers.cyenginehub]
command = 'C:\Program Files\CyEngineHub\Mcp\CyEngineHub.Mcp.exe'
args = []
```

Les blocs Claude Desktop, Claude Code et OpenCode sont également disponibles dans cette page. Le proxy stdio relaie vers un pipe nommé limité au même compte système. Le registre CyMCPSDK de l’application valide les schémas et appelle le même code métier que le chat.

## Commandes CLI

Sans verbe, les exécutables conservent leur transport MCP stdio et leurs anciennes options. Avec un verbe, la sortie standard contient un seul résultat JSON.

```powershell
& .\Mcp\CyEngineHub.Mcp.exe --help
& .\Mcp\CyEngineHub.Mcp.exe list
& .\Mcp\CyEngineHub.Mcp.exe describe hub_targets
& .\Mcp\CyEngineHub.Mcp.exe call hub_targets
& .\Mcp\CyEngineHub.Mcp.exe call hub_propose --json-file .\proposition.json
```

Les opérations du hub utilisent l’instance ouverte avec MCP activé. Pour cibler un profil isolé, lancez son application avec `--mcp-pipe CyEngineHub.ProfilQA` et placez `--pipe CyEngineHub.ProfilQA` avant le verbe côté CLI. Les blocs de connexion de cette instance incluent ce même nom.

Le compagnon de réparation peut fonctionner sans interface. Il exige `--workspace` avec le dossier d’une tâche préparée par le hub. `call op_…` attend un état terminal et imprime le job, avec ses résultats ou son erreur. Les anciens clients stdio reçoivent toujours immédiatement le job à suivre.

| Code | Résultat |
| --- | --- |
| 0 | Succès |
| 1 | Transport, protocole ou démarrage |
| 2 | Usage ou argument JSON invalide |
| 3 | Outil inconnu, refus métier ou job en échec |
| 130 | Arrêt de la commande |

## Catalogue et options du SDK

CyMCPSDK active la découverte à la demande au-delà de 32 outils par défaut. Le hub conserve le catalogue complet de dix outils et des outils tiers sélectionnés, pour garder les mêmes définitions dans le chat et les clients existants. L’ouverture ou la restauration du chat redécouvre le catalogue tiers. Le SDK borne son cache de catalogues à 30 secondes. Un appel métier est exécuté à chaque demande. Le registre du hub est fixe pendant une connexion, donc il utilise le TTL et la reconnexion pour le rafraîchissement.

Les inspections et recherches conservent la pagination et les limites du hub. Les résultats publics gardent leur format JSON en ligne, y compris pour les anciens clients. Le hub ne configure ni stockage temporaire des résultats sur disque, ni ressources grep, ni abonnements aux changements, ni journal de performances. Les jobs persistés continuent à utiliser le stockage de la copie de réparation.

## Opérations du hub

| Outil | Utilisation |
| --- | --- |
| `hub_search` | Chercher par mots dans les catalogues de packs possédés et locaux, avec pagination. |
| `hub_asset` | Lire les métadonnées et identifiants de variantes d’une fiche. |
| `hub_pack_files` | Rechercher les noms dans un dossier ou une archive enregistrés, sans extraction. |
| `hub_targets` | Lister projets et moteurs connus. |
| `hub_tasks` | Suivre les tâches, statuts et progressions. |
| `hub_options` | Lire les limites et les permissions enregistrées. |
| `hub_download_plan` | Estimer un transfert depuis les manifests disponibles. |
| `hub_download_batch` | Proposer ou mettre en file un lot selon les permissions enregistrées. |
| `hub_propose` | Proposer un classement, un téléchargement ou une installation pour une fiche connue. |
| `hub_proposals` | Lire l’état des propositions et les identifiants de tâches associés. |

Pour les outils de liste `hub_targets`, `hub_tasks` et `hub_proposals`, le contenu structuré contient `items`. La réponse textuelle conserve le tableau utilisé par les clients existants.

Les options de classement, de téléchargement et d’installation automatiques sont indépendantes et désactivées par défaut. Les opérations nécessitant une approbation apparaissent dans la page IA / MCP. Les confirmations existantes s’appliquent, et le hub vérifie encore les marketplaces activées, les variantes et les cibles. Désactiver MCP révoque les propositions en attente et laisse les tâches déjà confirmées suivre leur cycle normal.

| Fonctions en dehors du registre général | Motif |
| --- | --- |
| Achats, comptes, cookies et jetons marketplace | Les transactions et l’authentification appartiennent au site officiel et à la session de l’utilisateur. |
| Création de projets, installation de moteurs, gestion de plugins, compilation et nettoyage | Ces flux ont leurs propres choix de cibles, prérequis et confirmations dans le hub. |
| Droits IA, suppression de fichiers, accès par chemin arbitraire et commandes système | Les droits restent sous contrôle de l’utilisateur et les appels sont limités au catalogue et aux opérations nommées. |
| Scripts de réparation Unreal | Ils exigent une copie de réparation précise et un consentement d’exécution distinct. |

## Réparation Unreal Version Bridge

Le compagnon de réparation utilise un registre CyMCPSDK propre à la copie de réparation. Utilisez le `mcp.json` créé par le hub. Les contrôles d’écriture et de compilation exigent le consentement distinct déjà enregistré dans la tâche. Pour une copie existante, la reprise dans le hub rafraîchit `mcp.json` avant la revalidation. Son format de tâche reste compatible.

Les opérations `op_*` retournent un job. Utilisez `cy_get_job` pour récupérer son statut et ses résultats, `cy_cancel_job` pour demander l’arrêt, `cy_list_jobs` pour retrouver les jobs et `cy_finish_task` pour nettoyer les rapports temporaires des jobs terminés. Les résultats historiques de `McpRuntime/state.sqlite3` restent lisibles. La publication reste une étape explicite du hub après validation dans le moteur cible. [Détail des opérations de réparation](../extensions/builtin/cyenginehub.unreal-version-bridge/mcp/README.md)
