# Dérivés de packages Unreal

Un dérivé est une version locale rattachée à la fiche du produit Fab ou Megascans d’origine.
Il ne remplace et ne modifie jamais le téléchargement fournisseur.

## Workflow

1. Ouvrir une fiche produit possédant au moins une version locale.
2. Cliquer sur `Create Derived`, globalement ou depuis la version choisie.
3. Sélectionner la source locale, la version d’Unreal Engine, le type final, un nom et des notes.
4. CyEngineHub crée un projet temporaire et installe le pack en conservant son arborescence `Content`.
5. Le projet s’ouvre dans la version d’Unreal sélectionnée pour permettre les corrections ou optimisations.
6. `Package Data` crée une nouvelle révision dans le stockage de la marketplace et l’affiche sur la même fiche produit.
7. Le bouton `Installer` de cette révision utilise le package local à la place de la variante fournisseur.
8. Le projet temporaire peut ensuite être supprimé explicitement. Ses packages déjà créés sont conservés.

Un package dérivé peut lui-même servir de source à un nouvel atelier.

## Métadonnées

Le sidecar `cyenginehub-package.json` utilise le schéma 2 pour conserver :

- l’identifiant du produit d’origine ;
- l’artefact source ;
- le nom et les notes du dérivé ;
- son type final et sa version moteur ;
- la date de création, les tailles et le profil de compression.

## Sécurité et limites Alpha

- Les fichiers fournisseur ne sont jamais modifiés en place.
- Les archives sont extraites avec validation des chemins.
- La suppression est limitée aux racines temporaires détenues par CyEngineHub et refuse les jonctions.
- Pour un AssetPack, seuls les fichiers de `Content` sont empaquetés ; les dossiers temporaires Unreal sont exclus.
- Un éditeur Unreal encore ouvert peut empêcher la suppression du projet temporaire.
- Cette fonctionnalité reste expérimentale : sauvegardez les sources importantes et contrôlez le package produit.
