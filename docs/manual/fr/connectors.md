# Plugins marketplace et imports locaux

Les connecteurs intégrés comprennent Cosmos, KitBash3D/Cargo, itch.io, BlenderKit/Blendkit, ArtStation, Gumroad et CGTrader. Chacun ajoute une source et son panneau Marketplace. BlenderKit et Blendkit désignent le même service, pas deux intégrations indépendantes.

## Parcours commun

1. Activez le connecteur et choisissez dossier de téléchargement, bibliothèques ordonnées, quotas et compression.
2. Ouvrez son inventaire/site officiel. Connectez-vous et effectuez les vérifications fournisseur dans sa session WebView2 isolée.
3. Téléchargez un fichier autorisé sur le site. Les transferts pris en charge utilisent le dossier choisi ; seuls les fichiers terminés sont indexés, pas les échecs/partiels.
4. Ouvrez Mes assets téléchargés ou la dernière fiche. Vous pouvez aussi ajouter un ZIP existant ou un dossier de kit local complet.
5. Vérifiez type et contenu, personnalisez nom/notes, puis configurez un script d’installation ou créez un dérivé.

:::flow
Site officiel ou kit local complet existant
Téléchargement terminé / référencement local explicite
Fiche native et stockage JSON/package commun
Examiner le contenu et choisir un convertisseur si nécessaire
Contrôler la cible puis confirmer l’installation
:::

## Limites propres aux services

Cargo ouvre My Assets pour les kits complets achetés ; il ne remplace pas sa liaison DCC/add-on officielle. Les transferts Blendkit exigeant son add-on restent dans ce client ; ajoutez ensuite le dossier complet obtenu. Jeux/outils itch.io et documents/exécutables des stores restent des fichiers, pas des assets Unreal. Les sources propriétaires .max/.c4d/Maya nécessitent un exporteur/convertisseur adapté et autorisé.

Ces connecteurs offrent accès web officiel et catalogue local, pas une synchronisation OAuth/API universelle de tous les inventaires. Ils n’extraient pas droits, cookies privés ou URL signées des cartes. Se connecter au site n’authentifie pas automatiquement Fab ou un autre connecteur.

Ajoutez un kit complet, pas une bibliothèque mélangée entière. Conservez textures et dépendances ensemble. Réunissez les archives multiparties avec vos outils ; ce parcours n’extrait pas RAR/7z. Les transferts navigateur n’utilisent pas le réglage des connexions Fab. Changer la destination concerne les prochains transferts, pas les originaux déjà présents. Voir [conversion](help:conversion), [téléchargements](help:downloads) et [extensions](help:extensions-sdk).
