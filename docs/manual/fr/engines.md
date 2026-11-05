# Moteurs et associations

Le catalogue distingue installations officielles, moteurs compilés depuis les sources et autres fournisseurs. Actualiser lit les emplacements configurés et conventionnels ; découvrir un moteur ne l’installe pas. Vérifiez chemin et version exacte dans sa fiche avant une modification.

![Catalogue moteur avec installations officielles et sources fictives.](image:engines)

## Gérer une installation Unreal

1. Ouvrez Moteurs et choisissez l’installation. Son chemin et l’action Dossier permettent de distinguer les versions semblables.
2. Personnalisez le nom affiché si vous maintenez plusieurs builds. Cet alias doit aussi apparaître dans la création de projet ; il ne renomme pas le dossier et ne change pas la version d’Unreal.
3. Vérifiez l’identifiant d’association. Le modifier met à jour l’enregistrement Unreal de cette installation, pas automatiquement tous les descripteurs de projets. Contrôlez les projets concernés.
4. Lancer ouvre l’éditeur choisi ; l’accueil moteur ne sélectionne pas implicitement un projet récent. Fermez-le avant de modifier ses binaires ou plugins éditeur.
5. Consultez composants installés, plugins, toolchains et taille depuis leurs sous-pages. Une taille partielle est une borne basse, pas une mesure exacte.

Désenregistrer retire les informations d’association/catalogue ; ce n’est pas supprimer les fichiers. Lisez le plan de suppression distinct avant une désinstallation. Ne modifiez pas un moteur utilisé par un éditeur ou une compilation.

## Sources et autres fournisseurs

Un moteur source expose sa branche et l’inspection Git lorsqu’il possède un dépôt. Fetch n’est ni une compilation ni un pull. Consultez [Git](help:git) avant une mise à jour des sources.

Unity reconnaît la version du projet et son dossier Assets, et exige l’éditeur correspondant. Godot recherche la version majeure/mineure déclarée lorsqu’elle est disponible. CRYENGINE lit les descripteurs et l’identité du moteur requis. Leurs possibilités de lancement, inventaire et gestion de packages diffèrent de celles d’Unreal : la présence d’un fournisseur ne garantit ni installateur commun ni conversion universelle.

Voir [mises à jour moteur](help:engine-updates) pour les révisions officielles et [builds et toolchains](help:builds) pour les compilateurs/SDK manquants.
