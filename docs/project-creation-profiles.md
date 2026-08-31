# Création Unreal : profils et packs de fonctionnalités

La fenêtre de création possède six onglets : Projet & profil, Fonctionnalités,
Plugins, Configuration INI, Collections et Post-création.

## Préparer un profil sans créer de projet

Le bouton **Projets → Profils de création** ouvre directement la page
**Paramètres → Profils de projet**. Choisissez un moteur de référence pour
inventorier ses plugins, puis créez un profil ou sélectionnez-en un à modifier.
Les profils intégrés sont personnalisés par duplication ; les profils
personnels peuvent être modifiés, dupliqués et supprimés depuis leur éditeur.
L’import des plugins et INI depuis un projet existant reste disponible.

Aucun nom ou dossier de nouveau projet n’est nécessaire. Enregistrer revient
à cette page et rend le profil disponible dans **Nouveau projet → Projet & profil**.
Parcourir les profils ou enregistrer un autre profil conserve les choix manuels
et les INI d’un brouillon de création déjà préparé. Modifier le profil même
utilisé par ce brouillon réapplique en revanche sa nouvelle configuration.

## Composition

1. Le profil initialise les plugins et les réglages INI.
2. Les packs cochés s’appliquent dans l’ordre de la liste. Le dernier gagne pour
   une même clé INI scalaire ou un même plugin. Les flèches changent cet ordre.
3. Les choix manuels de l’onglet Plugins sont prioritaires. Le bouton de remise
   à zéro rétablit la valeur du profil et des packs.
4. Un fichier INI édité manuellement remplace entièrement le fichier généré.

Changer de profil réinitialise les choix manuels et les éditions INI, mais
conserve les packs sélectionnés. Les fichiers modifiés restent prioritaires
lorsqu’un pack est coché, décoché ou déplacé. « Réinitialiser ce fichier »
reprend le contenu généré ; pour un fichier ajouté manuellement, cela le retire.

## Profils minimaux

UE5 Lite et UE5 mode UE4 écrivent `DisableEnginePluginsByDefault: true` dans le
nouveau `.uproject`. Les plugins essentiels sont explicitement activés, et
l’inventaire affiche également leurs dépendances. Les plugins installés dans
le moteur ne sont ni modifiés ni supprimés. Une dépendance obligatoire absente
ou explicitement désactivée bloque la création.

Le mode UE4 reste un projet UE5 : rendu Deferred, TAA, SSR, éclairage statique,
sans Lumen, Nanite, Virtual Shadow Maps ni ray tracing. La compression
d’animation native évite les références à un plugin ACL désactivé.

## Packs personnalisés et INI

Quinze packs intégrés couvrent notamment VR/OpenXR, Niagara, PCG, Gameplay
Abilities, Common UI, animation, audio, cinéma, modélisation et Python. Les
modèles intégrés sont dupliqués avant modification. Les packs personnels sont
enregistrés dans les réglages locaux (`projects.creation.feature-packs.v1`).
L’éditeur expose les choix activer/désactiver/ne pas modifier et les clés INI.

L’onglet Configuration INI montre les fichiers écrits dans `Config`, avant les
scripts post-création. Il ne représente pas tous les INI hérités d’Unreal.
L’aperçu et la création partagent `UnrealProjectIni`. Les opérations de tableaux
Unreal (`+`, `-`, `.`, `!`) conservent leur ordre ; les valeurs scalaires sont
remplacées par la couche prioritaire. Les noms de fichiers sont limités à des
fichiers `Default*.ini` dans `Config`, sans chemin extérieur.

## Vérifications

- Tests unitaires : `dotnet test CyEngineHub.sln -c Release`.
- Banc d’interface isolé : `artifacts/project-creation-qa-20260831` ; ses données
  restent dans un sous-dossier de test, sans accès aux comptes marketplace.
- Validation réalisée avec l’inventaire UE5.8 local : 895 plugins détectés,
  246 actifs avec le profil standard et 8 avec Lite, dépendances incluses.
- Démarrage du projet Lite via UnrealEditor-Cmd/ResavePackages : 0 erreur,
  0 avertissement. Ce test ne constitue pas une compilation complète d’un jeu
  C++ ni une validation des fonctionnalités de tous les packs.
