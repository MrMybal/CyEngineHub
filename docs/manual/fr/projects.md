# Créer et ouvrir des projets

Utilisez Projets → Nouveau projet. La création Unreal comprend Projet & profil, Fonctionnalités, Plugins, Configuration INI, Collections et Post-création. Créer un projet est distinct de créer un profil réutilisable.

![Véritable formulaire de création avec projet et emplacements fictifs.](image:project-creation)

## Choix nécessaires

1. Choisissez la famille de moteur et le type Unreal : C++ ou Blueprint uniquement. Un projet Blueprint seul ne nécessite pas le même module natif initial/build que C++.
2. Sélectionnez l’installation réelle. Nom personnalisé, version et infobulle du chemin distinguent les doublons. L’identifiant d’association n’est pas le nom du projet.
3. Indiquez un nom technique et un dossier parent existant. Saisissez le chemin ou utilisez Parcourir avec le sélecteur Windows. Le projet reçoit son sous-dossier ; un projet existant ne doit pas être écrasé.
4. Choisissez profil, packs de fonctionnalités et plugins. Relisez INI et dépendances avant création.
5. Configurez collections et actions post-création si nécessaire, puis confirmez.

:::flow
Valider nom, dossier parent et moteur précis
Écrire le descripteur, les sources/configurations et le template de plugin demandé
Générer les fichiers de projet pour C++
Mettre en file le build éditeur initial si nécessaire
Ouvrir la fiche du nouveau projet ; suivre préparation et compilation
:::

## Après création

La fiche projet s’ouvre directement. La préparation C++ génère les fichiers de projet avant la compilation initiale ; recherchez la solution/les fichiers générés et consultez Actions si cette étape échoue. La présence du .uproject ne prouve pas que la préparation native est terminée. Le compilateur et le SDK Windows du moteur sélectionné doivent être disponibles.

La génération de plugin post-création propose les templates disponibles dans le moteur : Blank, Content Only, Blueprint Library ou modèles éditeur, par exemple. Ils dépendent des templates Plugin Browser de cette version. Un plugin C++ généré doit être compilé ; Content Only ne crée pas de module natif.

L’ouverture d’un projet existant utilise son association détectée. Corrigez volontairement une association manquante au lieu d’ouvrir avec une version choisie au hasard. Voir [profils](help:profiles), [builds](help:builds), [plugins](help:plugins) et [dépannage](help:troubleshooting).
