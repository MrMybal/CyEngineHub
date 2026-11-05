# Profils, fonctionnalités et INI

Un profil prépare plugins et configuration d’un nouveau projet ; son enregistrement ne crée pas de projet et ne réécrit pas les projets existants. Ouvrez Projets → Profils de création ou les profils Unreal dans Paramètres. Choisissez un moteur de référence pour inventorier ses plugins. Les profils intégrés sont conservés : personnalisez une copie. Les profils personnels peuvent être modifiés, dupliqués et supprimés ; l’import depuis un projet existant est disponible.

## Priorité des configurations

:::flow
Valeurs du moteur et plugins actifs hérités
Profil de création sélectionné
Packs de fonctionnalités cochés, dans leur ordre
Choix manuels des plugins
Fichier INI édité manuellement remplaçant son fichier généré
:::

Le dernier pack gagne pour un même plugin ou réglage scalaire ; les flèches changent l’ordre. Changer de profil réinitialise les choix manuels et INI, mais conserve les packs sélectionnés. Réinitialiser les overrides rétablit les choix générés. Réinitialiser un INI restaure son contenu généré ou retire un fichier ajouté manuellement.

L’onglet INI prévisualise les Default*.ini écrits dans Config du nouveau projet, pas tous les réglages hérités d’Unreal. Les scalaires suivent les couches de priorité. Les opérateurs de tableaux Unreal +, -, . et ! gardent leur ordre. Aucun chemin extérieur à Config ne doit être ajouté.

## Profils minimaux et spécialisés

Les profils Lite/style UE4 restent des projets UE5 ; un rendu de style UE4 ne change ni version moteur ni compatibilité des assets. Les profils minimaux peuvent désactiver par défaut les plugins moteur et activer explicitement les dépendances essentielles. Les dépendances obligatoires absentes ou désactivées doivent être résolues avant création. Choisir un profil ne supprime pas les plugins installés dans le moteur.

Les packs réutilisables couvrent notamment VR/OpenXR, Niagara, PCG, gameplay abilities, UI, animation, audio et cinéma. Leur présence ne garantit pas la prise en charge matérielle/runtime. Relisez la composition des plugins et INI pour le moteur choisi.

Pour utiliser le profil enregistré, revenez à la [création de projet](help:projects), sélectionnez-le et contrôlez le résultat avant confirmation.
