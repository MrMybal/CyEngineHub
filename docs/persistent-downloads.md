# File persistante et téléchargements parallèles

## Périmètre

Les téléchargements natifs vers la bibliothèque **Fab et Megascans**, via l'interface ou le MCP, sont enregistrés dans `Downloads/queue-v1.json` du dossier de données du hub. Les installations dans des projets/moteurs, téléchargements de moteurs et transferts effectués dans un navigateur ne sont pas restaurés par ce journal.

Une demande comprend la fiche, la variante exacte, la compression, le profil de stockage et la date d'ajout. Elle est écrite avant la mise en file (écriture temporaire, flush disque, remplacement du journal). Un journal corrompu provoque une erreur, pas sa remise à zéro. Le journal ne contient pas de jetons d'authentification.

Les demandes interrompues restent Pending ; la fermeture de l'application ne les annule pas durablement. Une annulation par le bouton Annuler est enregistrée comme Canceled. Les échecs restent Failed et ne sont pas réessayés en boucle au lancement. Un résultat déjà placé en bibliothèque est enregistré avant de finir la tâche afin de pouvoir réconcilier l'index au prochain démarrage.

## Réglages

Paramètres > Bibliothèque :

- 3 téléchargements simultanés par défaut, de 1 à 16 ; Enregistrer applique le changement sans redémarrer. Une réduction ne coupe pas les transferts actifs mais limite les prochains départs.
- Reprise au lancement activée par défaut, désactivable.
- Reprendre / réessayer les échecs remet en file les demandes interrompues et les échecs, pas les annulations volontaires. Une marketplace désactivée bloque sa reprise.

La file des téléchargements est indépendante des tâches générales. Seuls les téléchargements vers la bibliothèque sont marqués parallélisables ; les installations restent sur la file générale. Les variantes d'un même asset partagent un verrou afin de protéger leurs fichiers temporaires communs. Le placement dans les bibliothèques conserve son verrou de quotas.

La limite concerne toute la tâche, y compris extraction/compression/placement. Elle ne garantit pas la saturation du réseau : serveurs distants, CPU, stockage et compression peuvent limiter le débit. Les téléchargements parallèles augmentent également l'espace temporaire nécessaire.

## Reprise des données

Fab réutilise les chunks complets déjà présents. Les nouvelles écritures de chunks utilisent un fichier temporaire, et un chunk dont la taille est incomplète est retéléchargé. La file est persistante, mais ce n'est pas une garantie de reprise HTTP à l'octet près. Megascans peut retélécharger un fichier/pack interrompu. Une interruption pendant compression ou déplacement peut également nécessiter un nouveau traitement.

Le profil de stockage et la compression sont ceux enregistrés lors de la demande ; modifier les paramètres des marketplaces concerne les nouvelles demandes. Les credentials sont relus par les clients officiels existants.

## Import d'une ancienne file

Une sauvegarde de file contient des informations privées sur les téléchargements.
Elle doit rester locale et n'est jamais fournie avec les sources ou la distribution.
L'import reste une action explicite de l'utilisateur.

Dans Paramètres > Bibliothèque, renseigner le chemin de ce JSON, puis **Importer la sauvegarde sans lancer**. L'import vérifie les identifiants contre le catalogue chargé ; il n'accepte pas de chemins ou commandes provenant du fichier. Cliquer ensuite sur **Reprendre / réessayer les échecs**. Les variantes avec un manifest local déjà enregistré sont ignorées. Les résultats terminés depuis la capture ne doivent pas être relancés. Les demandes déjà présentes dans le nouveau journal ne sont pas dupliquées.

## Vérification

Tests sur fichiers et tâches simulés : 169 entrées persistées/rechargées, états terminaux, journal corrompu, écritures concurrentes, limite de trois transferts, tâche générale indépendante, changement de limite sans redémarrage, annulation/fermeture. Aucun pack réel téléchargé dans ces tests. L'interface est compilée ; les performances réseau réelles restent à mesurer.
