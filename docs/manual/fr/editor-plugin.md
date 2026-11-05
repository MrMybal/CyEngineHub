# Utiliser CyEngineHub dans Unreal Editor

Le plugin éditeur CyEngineHub recherche dans la bibliothèque possédée et installe dans le projet ouvert pendant que le hub desktop fonctionne. Il utilise Slate et un pipe nommé limité au même compte Windows, pas un mot de passe marketplace dans Unreal ni un service HTTP/shell. Il est indépendant de l’IA/MCP.

## Installer et connecter

1. Fermez les éditeurs utilisant le moteur. Dans sa fiche CyEngineHub, choisissez Installer le plugin éditeur et confirmez le chemin exact.
2. Le hub compile pour ce moteur avec Visual Studio C++/SDK Windows, puis installe dans Engine/Plugins/Marketplace/CyEngineHub. Aucune copie complète du moteur n’est créée.
3. Le descripteur active par défaut un module Editor. Un projet désactivant explicitement ce plugin/les plugins moteur par défaut doit encore l’activer. Ce n’est pas un module runtime inclus dans le jeu.
4. Enregistrez le .uproject dans le hub avec la bonne association et activez la connexion éditeur locale.
5. Dans Unreal, ouvrez Fenêtre → CyEngineHub · Alpha puis Connecter/Actualiser. Laissez le hub desktop ouvert.

Les ZIP précompilés exigent le build moteur/identifiant de module exact. En cas d’incompatibilité, compilez les sources originales pour votre installation plutôt que masquer l’erreur en éditant la version du descripteur.

## Installer depuis l’éditeur

Recherchez dans les produits possédés, examinez la variante et confirmez le projet ouvert affiché. L’import direct couvre le contenu/plugin natif compatible et reste strictement additif : aucun asset/plugin existant n’est remplacé. Pour conversion ou chaînes avancées, Options dans le Hub ouvre la fenêtre desktop de contrôle sans démarrer implicitement.

La progression concerne le projet. Fermer l’onglet n’annule pas les tâches confirmées. Si le pipe coupe après confirmation, vérifiez Actions avant de réessayer. L’import réussi actualise le registre des assets ; redémarrage/recompilation peut rester nécessaire pour les modules natifs.

Voir [installation](help:install), [plugins](help:plugins) et [dépannage](help:troubleshooting).
