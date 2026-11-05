# Mettre à jour un moteur Unreal officiel

Les patchs moteur sont distincts des [mises à jour de CyEngineHub](help:app-updates). Une installation officielle utilise les métadonnées de distribution Epic ; un moteur source/Git suit son dépôt. L’affichage d’une nouvelle révision ne prouve pas son installation.

## Appliquer un patch

1. Connectez Epic/Fab, ouvrez la fiche du moteur précis et vérifiez sa révision officielle. Utilisez l’action de mise à jour de la fiche ou du menu contextuel lorsqu’une révision supérieure est disponible.
2. Fermez les éditeurs et compilations utilisant cette installation. Contrôlez le dossier existant affiché, pas le chemin d’une seconde installation.
3. Confirmez. Suivez Actions et le plan de patch dans la fiche moteur.
4. Après réussite, actualisez la découverte et vérifiez Build.version/la version du moteur. Rouvrez l’éditeur seulement lorsque la tâche est terminée.

:::flow
Lire le manifest cible et vérifier les empreintes des fichiers installés
Réutiliser les blocs locaux vérifiés ; télécharger les manquants avec un cache borné
Préparer les plages modifiées et leurs octets d’origine pour l’annulation
Vérifier les empreintes cibles, verrouiller les originaux et publier le journal
Appliquer les plages ; enregistrer Build.version en dernier
Succès : nettoyer le temporaire / Échec : restaurer les octets journalisés
:::

## Comprendre l’espace disque

L’outil ne reconstruit plus chaque binaire modifié sous forme d’une copie complète. Il prépare les différences et les octets de retour arrière. Le premier chiffre est explicitement une réserve minimale avant mesure de ces différences. L’espace libre est contrôlé pendant la préparation ; le plan mesuré distingue transfert réseau, deltas réversibles, cache borné, croissance, métadonnées et marge. Les deltas déjà préparés et vérifiés peuvent être réutilisés.

Un patch peut néanmoins apporter beaucoup de données nouvelles ou entièrement différentes. Son téléchargement n’est pas son espace supplémentaire, et aucun des deux n’équivaut automatiquement à la taille du moteur. Les tags installés servent à éviter l’ajout de plateformes optionnelles absentes. Ajouter des composants reste un choix d’installation explicite.

## Interruption et restrictions

Le patch en place sécurisé requiert actuellement Windows. Empreintes et verrous exclusifs empêchent une validation sur des binaires modifiés ou utilisés ; les liens physiques multiples sont refusés. Une annulation ou un échec tente la restauration par journal. La tentative suivante récupère une transaction interrompue avant de travailler. Ne supprimez pas manuellement un staging contenant un journal : il peut conserver les seuls octets de retour arrière. Un fichier lock restant ne prouve ni tâche active ni révision installée.

Consultez le [dépannage](help:troubleshooting) en cas de manque d’espace, verrouillage ou échec de récupération. Gardez des sauvegardes indépendantes ; les tests ne qualifient pas tous les patchs commerciaux.
