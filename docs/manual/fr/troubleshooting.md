# Dépannage et diagnostics utiles

Partez de la tâche exacte, du chemin moteur/projet, de l’artefact/plateforme choisi et de la première erreur significative. Réessayez après correction, pas en remettant la même demande en file. Phase et état terminal comptent plus qu’un pourcentage isolé.

## Symptômes fréquents

- Moteur absent/noms identiques : contrôlez racines, actualisation, infobulles de chemin et alias. Renommer l’affichage ne change ni association registre ni dossier.
- Pas de solution/build C++ : vérifiez C++ ou Blueprint seul, génération des fichiers, moteur, Visual Studio C++/SDK et première erreur de préparation. Corrigez la génération avant compilation.
- Pack retéléchargé : comparez source/artefact précis, version/plateforme et chemin local. Une variante absente ou d’une autre plateforme ne peut pas en remplacer arbitrairement une autre.
- Installation en attente : examinez verrous de source/cible, budgets disque et tâches actives. Des noms de projet différents peuvent partager volume/staging.
- Conversion en échec : lisez classes absentes, dépendances manquantes, builds d’adaptateurs et erreurs d’outils. Forcer n’implémente pas une conversion manquante.
- Connexion web/page vide : vérifiez runtime WebView2, disponibilité fournisseur et mode manuel Epic proposé. Ne désactivez pas TLS et ne partagez pas les cookies.
- Une image/description de repli : les métadonnées détaillées peuvent manquer ; réessayez explicitement. Une couverture ne garantit pas une galerie distante complète.
- Mise à jour demandant de la place : distinguez réseau, deltas/cache temporaires et croissance. Vérifiez la version du hub exécuté ; modifier les sources ne corrige pas l’ancien binaire installé.
- Plugin éditeur en échec : vérifiez build moteur exact, compilateur, activation, hub lancé/connexion locale et association projet. Après déconnexion, examinez Actions avant nouvelle tentative.

## Récupération sûre

Ne supprimez pas journal/deltas moteur, sauvegarde de plugin ou seul atelier de dérivé pour débloquer une tâche. Fermez les utilisateurs des fichiers, gardez les rapports et laissez agir la récupération/annulation prise en charge. Si le rollback échoue lui-même, conservez journal et sauvegardes pour diagnostic. Aucun nettoyage global de Source, Content, Config ou assets achetés.

## Signaler un bug

Notez version du logiciel, opération, variante, version cible, phase et séquence minimale. Utilisez un projet/asset jetable si possible. Masquez noms d’utilisateur, chemins propriétaires, identifiants de compte, secrets, URL signées, inventaires de file et noms d’assets privés dans captures/logs. Ne publiez pas un pack acheté pour reproduire le problème.

Ouvrez le [suivi des problèmes du projet](https://github.com/MrMybal/CyEngineHub/issues) uniquement si vous choisissez le réseau. Le manuel reste hors ligne. Voir [stockage](help:storage), [téléchargements](help:downloads), [conversion](help:conversion) et [builds](help:builds).
