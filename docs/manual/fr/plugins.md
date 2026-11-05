# Plugins moteur et projet

Les packages Unreal .uplugin ne sont pas des extensions CyEngineHub. Ouvrez l’inventaire, choisissez la cible et analysez les bibliothèques locales connues. Les scans sont bornés, ignorent les caches/générés et ne suivent pas les jonctions. Un descripteur invalide reste une entrée diagnostiquée, pas la preuve que toute la bibliothèque est inutilisable.

## Installer ou compiler un plugin Unreal

1. Sélectionnez source et cible moteur/projet ; vérifiez chemins, version déclarée et plateformes.
2. Préparez l’installation pour lire le plan sans modifier la cible.
3. Confirmez après contrôle de la compatibilité et du remplacement éventuel. Le parcours pris en charge prépare les fichiers, revalide le descripteur, conserve l’ancienne version et publie le nouveau dossier.
4. Compilez les sources pour le moteur réel. Les Binaries générés sont exclus lorsqu’une recompilation source est requise ; copier une DLL d’un autre moteur ne la recompile pas.
5. Ouvrez l’éditeur cible et vérifiez chargement et fonctionnalités.

Une source d’une autre version majeure/mineure peut être tentée avec avertissement de recompilation ; l’API peut encore nécessiter adaptation. Un plugin binaire seul d’une autre branche n’est pas interchangeable. Les restrictions de plateforme comptent également. Métadonnée de version absente signifie inconnue, pas compatible.

BuildPlugin utilise une sortie unique vide/inexistante, car l’outil Unreal peut la vider. Ne choisissez jamais le moteur ou la source du plugin comme sortie. Gardez les sauvegardes jusqu’à validation ; elles ne sont pas purgées automatiquement.

## Autres fournisseurs

L’installation Unity locale valide package.json et place un package embarqué dans Packages sans reprendre le manifest du gestionnaire. Un addon éditeur Godot valide plugin.cfg et n’est pas activé automatiquement dans project.godot. Les reçus conservés permettent un rollback contrôlé lorsqu’il est proposé. CRYENGINE expose actuellement l’inventaire déclaré, pas un installateur universel.

Voir [projets](help:projects) pour les templates, [plugin éditeur](help:editor-plugin) pour le client Unreal du hub et [extensions/SDK](help:extensions-sdk) pour les outils CyEngineHub.
