# Packages dérivés et ateliers de conversion

Un dérivé est une version locale distincte liée au produit/artefact source. Il ne remplace pas le téléchargement fournisseur. Il conserve une conversion, correction ou optimisation pour éviter de refaire la préparation pour chaque projet.

## Deux parcours de création

Pour les fichiers sources pris en charge, Convertir en dérivé local exécute le convertisseur puis publie sa sortie validée. Pour un package Unreal natif, Create Derived ouvre un atelier : choisissez source locale, moteur cible, type final, nom et notes. Le hub prépare un projet temporaire en préservant l’arborescence Content. Ouvrez-le dans l’éditeur choisi et effectuez vos corrections.

Après sauvegarde/fermeture des travaux éditeur concernés, Package Data crée une révision stockée. Rechargez/vérifiez-la avant une installation importante. Un dérivé peut servir de source à un autre atelier. Les scripts de conversion peuvent aussi proposer de conserver leur résultat comme dérivé ; activez cette option volontairement.

:::flow
L’artefact original reste intact dans la bibliothèque
Atelier de conversion ou d’édition isolé
Validation moteur cible puis packaging
Nouveau dérivé local avec provenance
Réutilisation de cette révision locale précise dans les projets
:::

## Identité et nettoyage

Le sidecar conserve produit/artefact source, nom/notes du dérivé, type final, version moteur connue, date, tailles et compression. Gardez-le avec son contenu. La version doit refléter les données sauvegardées, pas une simple étiquette renommée.

Un AssetPack empaquette Content, pas les caches générés du moteur/projet. L’atelier temporaire peut être supprimé explicitement après publication ; les dérivés déjà stockés restent. Fermez Unreal avant nettoyage. Seules les racines temporaires du hub sont concernées ; ne supprimez pas un dossier ambigu contenant vos seules modifications.

Les contrôles transactionnels protègent le parcours pris en charge, pas tous les outils lancés dans l’éditeur. Lisez les avertissements et sauvegardez indépendamment les corrections importantes. Voir [conversion](help:conversion), [installation](help:install) et [stockage](help:storage).
