# Branches Git et moteurs sources

Le support Git privilégie l’observation. Activer Git dans Connexions expose l’intégration ; cela ne connecte pas automatiquement chaque dépôt distant. Git et son gestionnaire d’identifiants conservent l’authentification, pas une connexion universelle CyEngineHub.

## Inspecter et vérifier les nouveautés

1. Enregistrez le projet ou moteur source, puis ouvrez son inspection Git.
2. Lancez l’analyse pour lire branche actuelle, compteurs de modifications et remotes. Un dépôt partagé n’est inspecté qu’une fois par instantané.
3. Sélectionnez le remote prévu et lancez explicitement Fetch pour actualiser les références distantes. Cette opération peut utiliser le réseau et demander une authentification.
4. Lisez le résultat avant de fusionner/compiler avec votre workflow Git habituel. Une comparaison exige des références à jour et un upstream pertinent ; non vérifié ne signifie pas « à jour ».

Fetch utilise le nom de remote validé. Il ne fait ni pull, checkout, merge, rebase ni reset de votre copie de travail. CyEngineHub ne supprime pas implicitement vos modifications pour réussir une mise à jour. Après une modification des sources avec vos outils, régénérez/compilez le moteur avec sa toolchain avant de considérer ses binaires actualisés.

## Gros dépôts et confidentialité

Les instantanés utilisent des commandes de statut bornées/en lecture seule, sans prompt terminal. Les compteurs sont enregistrés plutôt que d’immenses listes de fichiers. Les URL affichées sont débarrassées des identifiants, paramètres et fragments avant cache. Ne placez pas de secrets dans les URL ni dans les captures.

La mesure LFS est bornée ; une valeur précédée de ≥ est partielle. Il n’existe ni polling permanent ni purge automatique LFS/objets. Les données Git affichées dans [Stockage](help:storage) n’autorisent pas leur suppression.

En cas d’échec d’authentification, vérifiez le même remote dans votre client Git et son gestionnaire d’identifiants Windows. Ne transmettez pas vos jetons dans les logs. Voir [moteurs](help:engines) et [dépannage](help:troubleshooting).
