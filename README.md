# CyEngineHub — Alpha

Hub de développement multi-moteur, local-first, pour Windows. Unreal Engine est
le moteur le plus intégré ; Unity, Godot et CRYENGINE disposent de providers locaux.

**Version publiée : `2.0.0-alpha.15`.** Sauvegardez vos projets avant d'essayer les
opérations d'installation, de déplacement ou de conversion d'assets.

## Interface officielle V2

Dear ImGui est désormais l’interface officielle et l’interface par défaut de CyEngineHub.
Le logiciel reste distribué sur le canal alpha.
L’interface propose une palette gris moyen/orange inspirée de CyDAW, des listes compactes à colonnes
redimensionnables, inspecteur latéral, recherche et formulaires à onglets. La densité
est réglable et mémorisée. Les téléchargements, profils, scripts d’installation,
connexions et outils MCP utilisent les services existants.

Les boutons Nouveau projet et Ajouter un moteur se distinguent en vert. Les options
propres à un projet sont accessibles dans sa fiche via Paramètres du projet ;
la vue Projets conserve la création, les profils et l’import de projets.
Les vues Moteurs et Projets utilisent toute la largeur, sans inspecteur latéral ;
les profils se préparent dans Paramètres et les plugins se gèrent depuis les fiches
des projets ou moteurs. La navigation Stockage et Activité est ancrée en bas.
Les sources de bibliothèque sont sur une ligne, avec l’aide en bas ; les options
d’affichage sont regroupées sous la recherche et s’adaptent à la largeur disponible.
Les bibliothèques proposent quatre vues mémorisées : cartes détaillées, Image + nom,
lignes sans image et lignes avec miniature. Les colonnes de données se trient au clic
(ascendant/descendant). Le tri Fab/Megascans porte sur tous les résultats filtrés,
avant pagination. Les facettes sans résultat sont masquées et réapparaissent
automatiquement lorsque les autres filtres le permettent. La saisie clavier Windows
laisse passer les caractères, les accents et AltGr vers les champs de texte ImGui.
Les installations Unreal officielles sont distinguées en vert. Avec la connexion
Epic/Fab, le hub vérifie les révisions de la même branche et indique les mises à jour
disponibles, sans télécharger ni remplacer automatiquement le moteur.
Les dossiers absents sont exclus après validation du catalogue ; un lecteur
indisponible reste signalé comme tel. Ctrl/Maj + clic sélectionne plusieurs moteurs
en cartes ou en liste. Le clic droit permet de vérifier les installations,
rechercher leurs mises à jour officielles et les retirer du catalogue avec
confirmation, sans supprimer leurs fichiers. Le lecteur de manifests accepte
les catalogues binaires Unreal volumineux avec décompression bornée.
Le téléchargement d’un moteur officiel ferme le formulaire et crée immédiatement
une entrée de suivi dans Moteurs et Téléchargements. Le transfert continue pendant
la navigation, respecte la concurrence des téléchargements et peut être annulé en
attente ou en cours depuis les deux vues. Une destination active ne peut pas être
ajoutée deux fois. La fin du transfert ajoute le moteur validé sans changer de page.
Les variantes à télécharger affichent leurs libellés Unreal, plateformes et formats
RAW/Megascans ; le texte et la case permettent de sélectionner une variante.
Les moteurs et composants officiels téléchargent leurs chunks en parallèle :
12 connexions par moteur par défaut, réglables de 1 à 32 dans Paramètres → Téléchargements.
Le bouton Paramètres de téléchargement de la file ouvre directement ces réglages,
avec le nombre de transferts simultanés distinct du nombre de connexions par moteur.
Ce réglage est distinct du nombre de packs simultanés et s’applique aux prochains
transferts. Les chunks sont écrits progressivement, les fichiers incomplets ne sont
pas réutilisés, et les erreurs réseau temporaires font l’objet de nouvelles tentatives
bornées. Le débit agrégé exclut les octets réutilisés du cache ; la progression réseau
est rafraîchie au maximum quatre fois par seconde. Le débit effectif dépend aussi
du CDN, de la connexion, du disque et du processeur.
La reconstruction réutilise les chunks décompressés dans un cache mémoire LRU
limité à 128 Mio par transfert et calcule le contrôle d’intégrité pendant l’écriture,
sans relire les nouveaux fichiers. Sa progression est limitée à quatre mises à jour
par seconde, en plus du début et de la fin de chaque phase. La sélection d’un dossier
supprimé ou inaccessible affiche une erreur sans quitter l’application.
Les chunks tronqués, les interruptions réseau et les timeouts sont retentés jusqu’à
six fois avec attente progressive annulable. Après un échec ou une annulation,
Reprendre est disponible dans Téléchargements, les cartes et les lignes Moteurs,
ainsi que le menu contextuel. La reprise conserve la version et la destination
d’origine et revalide les chunks du cache avant de télécharger les manquants.
Pour une demande créée dans une ancienne version du hub, sélectionnez à nouveau
la même version officielle et le même dossier cible : le cache adjacent est réutilisé.

Les moteurs et projets disposent de modes Cartes/Liste mémorisés séparément. Les
cartes d’assets gardent une taille fixe ; un clic sélectionne la carte entière,
un double-clic ouvre sa fiche et les boutons d’action restent indépendants. Les
panneaux Sources et Paramètres occupent toute la hauteur, avec l’onglet actif
surligné. Les fiches produit Fab, Megascans et des connecteurs proposent un retour
explicite au catalogue, sans afficher les options de présentation de la liste.
Cliquez aussi sur la source active pour revenir de sa fiche à son catalogue.
Les variantes déjà téléchargées sont présentées dans des cadres séparés :
compatibilités et tailles en haut, actions propres à chaque variante en dessous.
Les extensions et plugins Unreal ont une liste avec inspecteur. Les miniatures
sont chargées et décodées en arrière-plan ; les transferts vers le GPU sont limités
par image rendue pour préserver la navigation. Le catalogue Megascans déjà en cache
reste consultable hors ligne, sans remplacer une configuration de recherche autorisée.

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
- Cible runtime .NET 8 ; interface Dear ImGui, hébergée par Avalonia.

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
