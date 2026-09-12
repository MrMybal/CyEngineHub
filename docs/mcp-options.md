# Options MCP

Dans Paramètres > IA / MCP, les options sont enregistrées explicitement. Elles s'appliquent aux nouvelles requêtes ; les propositions déjà en attente restent à valider.

## Limites et autonomie

- Le plafond de 30 propositions est supprimé : les propositions en attente sont **illimitées par défaut**. Une demande de 45 packs peut donc être proposée sans ce blocage.
- Les tailles de pages, descriptions, historiques, chemins et fichiers inspectés sont réglables. `0` signifie illimité. Une requête avec `limit: 0` utilise le maximum configuré. Le maximum de page doit être supérieur ou égal à la taille par défaut.
- Les propositions en attente ne sont jamais supprimées pour réduire l'historique.
- Catégoriser, télécharger et installer sans validation sont trois permissions indépendantes, désactivées par défaut. L'installation automatique utilise les scripts configurés et peut modifier les projets. Le client IA ne peut pas changer ces permissions par MCP.
- La concurrence des téléchargements, les quotas disque et les protections de chemins ne sont pas supprimés. Les téléchargements natifs automatiques renvoient leurs identifiants de tâches dès la mise en file, sans attendre le transfert. La préparation automatique des installations reste sérialisée.
- Les autorisations du client IA et les interactions requises par un site restent indépendantes. Pour FabWeb, le lot renvoie `requires_browser` et l'URL officielle ; la proposition reste disponible à valider pour ouvrir Fab. Ce n'est pas un téléchargement silencieux du site.
- Désactiver le MCP empêche les nouvelles actions, mais n'annule pas les tâches déjà soumises.

Des réponses illimitées peuvent consommer beaucoup de mémoire ou dépasser le contexte du client IA. La pagination reste recommandée. Les requêtes sont toujours limitées à 1 Mio ; les réponses du proxy n'ont plus ce plafond. Aucun accès libre au système ni aux secrets n'est ajouté. Les protections d'inspection des archives restent actives.

## Estimer les téléchargements

`hub_options` expose les options effectivement enregistrées. `hub_download_plan` reçoit `items: [{assetId, source, variantId}]` et éventuellement `bytesPerSecond`.

Pour les variantes Fab natives, le hub consulte le manifest sans télécharger les fichiers du pack : taille de transfert, taille décompressée et nombre de fichiers. Les doublons de variantes sont comptés une seule fois. Le transfert estimé est complet, sans déduction des chunks déjà présents ; la taille du ZIP final après recompression reste inconnue.

Les autres fournisseurs ou manifests indisponibles renvoient une taille inconnue, jamais zéro. Le résultat distingue le total connu, le nombre de tailles inconnues et le total complet (null s'il manque des tailles). La durée de transfert est calculée seulement si toutes les tailles sont connues et si une vitesse positive est fournie ; elle n'inclut pas extraction, compression ou installation.

Les tests utilisent des données simulées : aucun téléchargement réel de packs ou installation dans un projet utilisateur n'est déclenché.

## Envoyer un lot et continuer les recherches

`hub_download_batch` accepte `items: [{assetId, source, variantId}]` et `reason`. Le résultat comprend un identifiant de lot et une réponse par index : mise en file avec `taskIds`, déjà en file, validation nécessaire, navigation web requise ou rejet. Un doublon dans le même lot est ignoré ; une erreur sur un élément n'efface pas les autres. Le plafond est celui de la taille du message MCP, pas un nombre de 30 ou 45 packs.

Après réception, le client peut immédiatement appeler `hub_search` ou `hub_asset`. `hub_tasks` donne les états des tâches, et `hub_proposals` conserve le lien entre proposition et identifiants de tâches. Le lot lui-même n'est pas persisté : conserver sa réponse et suivre les tâches individuellement. La réponse `queued` n'est pas une confirmation de réussite du téléchargement.

Les recherches/fiches lisent les catalogues chargés depuis les caches locaux du hub. L'instantané MCP est réutilisé pendant au plus cinq secondes, avec invalidation lorsque les marketplaces activées changent. Aucun chargement d'image ni ouverture de fiche visuelle n'est déclenché pour une recherche, une fiche MCP ou une mise en file native. Les URL des miniatures sont des métadonnées : un client qui choisit de les ouvrir lui-même gère ses propres accès réseau.

Les grandes réponses du proxy utilisent une lecture par ligne tamponnée. Une erreur interne de fournisseur renvoie une erreur d'outil, sans fermer la session MCP. Cela ne peut pas maintenir une connexion si le hub est arrêté ou si le client déconnecte volontairement.
