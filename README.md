# CyEngineHub — Alpha

Hub de développement multi-moteur, local-first, pour Windows. Unreal Engine est
le moteur le plus intégré ; Unity, Godot et CRYENGINE disposent de providers locaux.

**Version de test : `2.0.0-alpha.1`.** Sauvegardez vos projets avant d'essayer les
opérations d'installation, de déplacement ou de conversion d'assets.

## Interface V2

L’interface utilise Dear ImGui : panneaux anthracite, listes compactes à colonnes
redimensionnables, inspecteur latéral, recherche et formulaires à onglets. La densité
est réglable et mémorisée. Les téléchargements, profils, scripts d’installation,
connexions et outils MCP utilisent les services existants.

Avalonia assure les fenêtres natives, le rendu GPU, les sélecteurs de fichiers et
l’hébergement de WebView2. Les pages web restent affichées par WebView2 ; les
barres de contrôle du hub sont dessinées avec ImGui. En cas de problème graphique,
l’argument `--classic` permet de démarrer temporairement l’ancienne interface.

## Télécharger

[Releases officielles](https://github.com/MrMybal/CyEngineHub/releases)

- `CyEngineHub-…-Setup-win-x64.exe` : installateur par utilisateur, sans droits administrateur.
- `CyEngineHub-…-win-x64.zip` : version autonome à extraire entièrement avant de lancer `CyEngineHub.Desktop.exe`.
- `SHA256SUMS.txt` : empreintes des deux téléchargements.

Windows 10/11 x64. Le runtime .NET est inclus dans les paquets ; aucun SDK n'est
nécessaire pour utiliser l'application. Cette première Alpha n'est pas signée avec
un certificat Authenticode et Windows peut afficher un avertissement.

## Fonctionnalités de cette Alpha

- Découverte et lancement des moteurs/projets installés, outils Unreal de build et packaging.
- Bibliothèque Fab et Megascans historique Quixel : connexion utilisateur, catalogue
  mis en cache, miniatures asynchrones, recherche, filtres et affichage virtualisé.
- Onglet Collections Megascans : sélections officielles Quixel, recherche, couvertures
  en cache et accès à leurs assets présents dans votre catalogue personnel.
- Téléchargements Unreal et RAW selon les formats réellement disponibles dans le compte.
- Compression ZIP configurable par marketplace, métadonnées JSON accessibles séparément,
  dossiers de cache/téléchargement et bibliothèques multiples avec quotas et ordre de priorité.
- Collections, packages locaux et scripts d'installation configurables par cible.
- Ateliers de dérivés Fab/Unreal : projet temporaire avec la version moteur choisie,
  correction dans l’éditeur, `Package Data`, versions locales installables et suppression contrôlée.
- Création de projets Unreal Blueprint/C++, profils éditables, packs de fonctionnalités,
  sélection des plugins du moteur et aperçu/édition des overrides INI.
- Éditeur de profils indépendant dans les paramètres, accessible aussi depuis Projets.
- Extensions intégrées Asset Bridge, installation Unreal avec déplacement dans un projet
  temporaire et conversion expérimentale entre versions Unreal.
- Logs, téléchargements en arrière-plan, zone de notification Windows et démarrage automatique optionnel.
- Mises à jour via les releases GitHub officielles avec notes de version et contrôle SHA-256.

## Limites importantes

Les connecteurs Fab/Quixel utilisent des interfaces de compatibilité communautaires
susceptibles de changer ; l'accès nécessite votre compte et vos droits sur les assets.
Les connecteurs ne contournent ni les licences ni les autorisations des marketplaces.
Unity Asset Store ne dispose pas encore d'un parcours distant complet équivalent.

Une conversion vers une ancienne version Unreal n'est **pas** un simple resave universel :
les extensions expérimentales procèdent à des exports/réimports pour les types pris en
charge et utilisent les moteurs installés. Blueprints, code, matériaux ou fonctionnalités
non pris en charge peuvent nécessiter une intervention manuelle.

Les scripts d'installation/post-création doivent provenir de sources de confiance.
CyEngineHub est indépendant d'Epic Games, Quixel, Unity et des autres éditeurs cités.

## Données et mises à jour

La connexion Fab propose une fenêtre Epic intégrée via WebView2 sur Windows,
avec récupération automatique du code JSON. Le **mode manuel** dans le navigateur
externe reste disponible, y compris si WebView2 est absent ou refusé par le
fournisseur de connexion. Voir [Connexion Epic / Fab](docs/fab-login.md).

Par défaut, les réglages, catalogues et logs sont dans `%LOCALAPPDATA%\CyEngineHub`.
Les paquets et caches peuvent être placés dans les dossiers définis dans les paramètres.
Créer un fichier vide `portable.flag` à côté de l'exécutable active un dossier local `Data`.
L'installateur et sa désinstallation ne suppriment pas vos bibliothèques ou projets.
En mode portable, privilégiez la mise à jour manuelle par ZIP en conservant `Data` et
`portable.flag` : l'installateur cible une installation Windows par utilisateur.

Le dépôt `MrMybal/CyEngineHub` est configuré par défaut. Dans **Paramètres → Avancé →
Mises à jour**, vous pouvez désactiver la vérification au lancement ou les préversions.
Le canal Alpha/Bêta est activé par défaut pour cette Alpha. Les téléchargements sont
vérifiés avant la fermeture de l'application et le lancement externe de l'installateur.

## Compiler et tester

- .NET SDK 9.0.306 (ou une révision compatible avec `global.json`).
- Cible runtime .NET 8 ; interface Avalonia.

```powershell
dotnet restore .\CyEngineHub.sln
dotnet build .\CyEngineHub.sln -c Release
dotnet test .\CyEngineHub.sln -c Release
dotnet run --project .\src\CyEngineHub.Desktop\CyEngineHub.Desktop.csproj
```

Créer les paquets Windows avec Inno Setup 7 installé :

```powershell
.\tools\release\build-windows.ps1 -InnoCompiler 'C:\chemin\Inno Setup 7\ISCC.exe'
```

Le script crée un nouveau dossier sous `artifacts`, exécute les tests, publie les
exécutables autonomes (application, updater et outils), embarque les extensions,
compile l'installateur et génère le ZIP ainsi que les empreintes. Il ne publie rien
sur GitHub et ne contient aucun identifiant.

Les sources ne comprennent aucun cache utilisateur, téléchargement de marketplace,
jeton, export d'annotation ou fichier de projet personnel. Les dépendances restent
soumises à leurs licences respectives : voir [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)
et les notices embarquées dans les binaires.

## Documentation

- [Connexion Epic / Fab : automatique et manuelle](docs/fab-login.md)
- [Collections officielles Megascans](docs/megascan-collections.md)
- [Dérivés de packages Unreal](docs/derived-packages.md)
- [Profils et création de projets](docs/project-creation-profiles.md)
- [Mises à jour et publication](docs/application-updates.md)
- [Extensions](docs/extensions.md)
- [Sécurité des packages](docs/package-install-safety.md)
- [Sécurité des plugins](docs/plugin-safety.md)
- [Stockage](docs/storage-safety.md)
- [Architecture](docs/architecture.md)

Certaines notes de conception historiques décrivent des étapes antérieures de l'Alpha.
Les limites de la release courante sont précisées dans ses notes de version.

## Optional Megascans catalog access

See [local catalog configuration](docs/quixel-local-configuration.md). Catalog search is disabled until an authorized key is configured locally.
