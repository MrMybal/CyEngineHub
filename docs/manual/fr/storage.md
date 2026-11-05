# Stockage, quotas et nettoyage sûr

Stockage est l’espace dédié à l’usage disque. Le démarrage lit son instantané ; l’analyse explicite parcourt les emplacements connus moteur/projet/DDC/Vault avec limites de temps/entrées, pas tous les disques. Mesures partielles et jonctions ignorées ne sont pas des totaux exacts.

## Lire les chiffres

L’empreinte moteur/projet inclut déjà ses sous-dossiers générés. N’additionnez pas DDC ou Safe Clean comme des consommations indépendantes. Un quota limite le placement, sans réserver physiquement l’espace. Le premier emplacement activé des bibliothèques ordonnées disposant de quota et d’espace reçoit un nouveau package. Cache, téléchargement temporaire, package final, décompression et retour arrière peuvent coexister.

Une compression supérieure peut réduire le package final mais consomme temps/CPU et n’élimine pas le staging. Changer un réglage de dossier ne déplace pas l’existant. Gardez manifests/sidecars avec les contenus. Ne déplacez pas manuellement transferts actifs ou journaux.

## Safe Clean

:::flow
Analyser les emplacements connus et vérifier les mesures partielles
Préparer le plan d’un dossier généré autorisé
Relire chemin exact, taille et suppression définitive
Confirmer ; nouvelle validation avant suppression
Réanalyser après réussite ou annulation
:::

Safe Clean supprime définitivement des fichiers générés ; ce n’est pas la Corbeille. Les cibles projet comprennent Intermediate, Binaries, DerivedDataCache, Saved/Logs, Saved/Cooked et Saved/StagedBuilds, plus Engine/DerivedDataCache du moteur. La régénération peut être longue et exiger une toolchain fonctionnelle. Fermez les processus concernés. L’annulation peut laisser un nettoyage partiel.

Racines moteur/projet, dossiers arbitraires, DDC/Zen globaux/partagés, Vault Cache et objets Git/LFS ne sont pas des cibles générales de nettoyage. Inventorier n’autorise pas à supprimer. Un plan incomplet, modifié ou devenu lien doit être refusé. Préservez Source, Content, Config et téléchargements originaux.

Un dossier .cyenginehub-update avec journal peut contenir les octets indispensables au rollback ; consultez [mises à jour moteur](help:engine-updates). Voir [téléchargements](help:downloads) pour maîtriser le temporaire parallèle.
