# Instance unique

Le point d'entrée du hub acquiert un objet nommé Windows propre au compte utilisateur avant de construire Avalonia, les services, la base ou la file. Son nom ne dépend pas du dossier du binaire : deux copies de cette nouvelle version partagent le même verrou dans la session Windows.

Le second lancement envoie uniquement une demande d'activation sur un canal local réservé au même utilisateur, puis quitte. Aucun argument (dont la restauration de file) n'est transmis ni exécuté par ce canal. Une demande reçue pendant le démarrage est conservée jusqu'à la création de la fenêtre. L'instance existante réaffiche sa fenêtre depuis le tray et restaure sa taille si elle était minimisée ; la mise au premier plan reste soumise aux règles de focus de Windows.

Le verrou reste présent quand la fenêtre est masquée dans le tray et est libéré à la fin du processus. Il n'utilise pas de fichier témoin persistant susceptible de rester après un crash.

Important : les anciens exécutables ne possèdent pas cette protection et ne peuvent pas être corrigés rétroactivement. Fermer complètement les anciennes instances avant d'utiliser cette version et mettre à jour les raccourcis.

Tests isolés : démarrage simultané (12 candidats, un seul propriétaire), activation d'une instance existante, activation pendant le démarrage, relancement après fermeture. Aucun hub utilisateur ni téléchargement réel démarré pendant ces tests.
