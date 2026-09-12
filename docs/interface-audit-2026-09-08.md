# Revue et corrections — 8 septembre 2026

## Changements livrés

- Liens explicites vers le produit, le projet et/ou le moteur dans les tâches de téléchargement et d’installation. Lorsqu’une cible n’est pas encore découverte (moteur en cours d’installation), le lien ouvre sa section et précise pourquoi sa fiche n’est pas encore disponible.
- Les lignes des tâches sont conservées pendant les mises à jour de progression, au lieu de vider et reconstruire la liste. Les notifications UI sont regroupées.
- Annulation immédiate d’une tâche en attente. Elle ne s’exécute plus après le travail précédent. La fermeture résout également les tâches encore en attente.
- Interrupteurs persistants par marketplace : sources masquées, nouvelles actualisations et opérations distantes bloquées, téléchargements déjà lancés et fichiers locaux conservés. Une collection initiale ignore les sources désactivées en l’indiquant dans la progression.
- Le téléchargement d’un moteur capture son dossier cible avant la mise en file ; une modification ultérieure du champ ne redirige plus cette tâche.
- Les téléchargements préparés des variantes précédentes d’un lot d’installation sont enregistrés même si une variante suivante échoue ou est annulée.
- Un projet temporaire préparé et enregistré n’est plus supprimé si seul le lancement d’Unreal échoue. Le statut de projet de test est enregistré dès sa création.
- Le manifeste intégré Fab Pack Relocator est désormais lisible : les capacités `AssetImport` et `DependencyValidation` sont déclarées dans le contrat des extensions.
- Plugins de marketplaces de type inventaire web, dont Cosmos. Voir [les capacités et limites](marketplace-connectors.md).

## Validation

- Compilation Desktop Release dans `artifacts/task-navigation-fix`.
- `dotnet test CyEngineHub.sln -c Release --no-restore --logger "trx;LogFilePrefix=audit" --results-directory artifacts/audit-tests -p:SkipBuiltInExtensionCopy=true` : **279 tests réussis, 0 échec, 12 projets de tests**.
- Tests ajoutés pour les liens conservés pendant la progression, l’annulation en attente, l’arrêt de la file, les manifests intégrés et le rejet des URL d’inventaire incorrectes ou contenant des identifiants.

## Limites et suites nécessaires

Cette revue combine lecture du code et tests automatisés ; elle ne garantit pas que tous les écrans ou tous les services externes soient exempts de bugs. La vérification interactive Windows n’a pas pu être effectuée avec l’outil de contrôle disponible dans cet environnement. L’application ouverte n’a pas été arrêtée pour installer ce binaire.

- À tester visuellement : clics des liens pendant un téléchargement, scroll de l’historique, désactivation/réactivation et restauration des choix après redémarrage.
- Cosmos : aucune synchronisation authentifiée native n’est implémentée ; le plugin ouvre uniquement l’inventaire officiel. Il reste à valider une interface autorisée du service avant d’importer fiches, images et téléchargements.
- Les logs locaux contiennent des réponses HTTP 403 de Fab sur certaines fiches détaillées, alors que le résumé du cache reste utilisable. L’origine de ces refus côté authentification/service n’est pas établie par cette revue.
- Les ateliers dérivés contenant des plugins avec leur propre dossier `Content` méritent une correction dédiée : la sélection actuelle de la branche d’import peut traiter ce contenu comme un pack et omettre le plugin. Ne pas considérer ce parcours comme validé par les tests génériques de packaging.
- Les imports Unreal réels, les conversions entre versions, les scripts tiers et les gros transferts nécessitent toujours des tests avec des moteurs et assets représentatifs.
