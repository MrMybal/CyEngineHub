# Convertisseurs de sources de packs — Alpha

## Utilisation

Le plugin intégré `cyenginehub.source-converters` est distribué avec le hub. Il réutilise les packages et les bibliothèques des connecteurs, sans rattacher un format à un nom de market : un achat ArtStation, CGTrader ou Gumroad peut déjà être natif Unreal, contenir des FBX/OBJ, du Blender ou seulement des textures.

Dans la fiche d’un fichier/package local, **Convertir en dérivé local…** propose :

- **Blender → GLB/FBX** : chemin de Blender (vide = installation standard détectée), format, fichiers à traiter et animations. Blender est installé par l’utilisateur ; il n’est ni téléchargé ni embarqué.
- **Sources RAW → package UAsset** : moteur Unreal cible installé et accord explicite sur les limites du format d’échange. Un projet temporaire reçoit les imports, les fichiers sont sauvegardés/chargés avec ce moteur, puis un package local est créé. Aucun projet utilisateur n’est installé/modifié par cette action.
- Avec Blender, **Convertir ensuite en pack UAsset avec Unreal** enchaîne les deux opérations.

Le nouveau package respecte compression, ordre et quotas des bibliothèques du connecteur. Son nom est distinct, le manifeste contient `derivation.sourceArtifactId` et la version Unreal cible lorsque pertinente. Les options sont conservées par asset. La fiche du dérivé s’ouvre après publication ; l’original reste accessible. Suivi et annulation passent par Actions. Les copies temporaires sont nettoyées après réussite/échec/annulation.

**Configurer l’installation…** suggère une chaîne selon `fileExtensions` du manifeste :

| Contenu réel | Chaîne proposée |
| --- | --- |
| `.uplugin` | Installation du plugin Unreal existante |
| `.uasset` / `.umap` | Installation native ; Version Bridge à ajouter si nécessaire |
| `.blend` / USD | Conversion Blender, puis conversion/import RAW Unreal |
| FBX/OBJ/glTF, images et sons pris en charge | Conversion/import RAW Unreal |
| DCC propriétaire sans export interopérable, document ou exécutable | Pas de conversion inventée ; choisir un plugin adapté |

La chaîne reste modifiable. Une préparation doit être suivie d’une installation dans ce parcours ; utiliser la création de dérivé pour convertir sans installer. Le type de sortie déclaré par le convertisseur est transmis à l’étape suivante. Les versions des packages natifs dérivés sont relues pour éviter une compatibilité moteur supposée.

## Blender : données et limites

Entrées déclarées : `.blend`, `.usd`, `.usda`, `.usdc`, `.usdz`, FBX, OBJ, glTF et GLB, selon les importeurs disponibles dans le Blender choisi. Sorties GLB/FBX. Tous les fichiers supportés sont traités par défaut ; `blendOnly` / `interchangeOnly` excluent explicitement les autres formats et l’exclusion apparaît au rapport. Chaque scène contenant de la géométrie reçoit sa sortie, avec noms uniques.

GLB est privilégié pour les matériaux PBR et textures embarquées. FBX représente un sous-ensemble des matériaux : l’accord sur les pertes est demandé. Les exports sont réimportés dans Blender et contrôlés pour leur géométrie, UV, matériaux et armatures présentes. Ce contrôle n’est pas une comparaison visuelle/pixel-perfect ni une garantie de tous les rigs ou animations.

Les textures doivent être embarquées ou retrouvables sans ambiguïté dans le pack. Les bibliothèques liées hors pack/missing sont refusées avant export. Les shaders procéduraux/groupes complexes, UDIM/vidéo, Geometry Nodes, contraintes et objets non mesh sont signalés. Sans accord explicite, ces cas interrompent la publication au lieu de publier silencieusement une variante partielle. L’accord autorise une approximation, **pas un bake universel**. Les rapports JSON gardent les fichiers, exclusions, avertissements et statistiques de réouverture.

Blender est lancé avec `--background --factory-startup --disable-autoexec --python-exit-code`, puis le script du hub. Les textes Python embarqués et autoexec sont désactivés ; la source n’est pas sauvegardée. Blender et ses codecs restent des processus natifs, **pas un bac à sable de sécurité** : ne convertir que des assets de confiance. Le choix d’un exécutable ou d’un script externe donne à cet outil les droits de l’utilisateur.

Validation Windows : Blender 3.6 et 5.2, fixtures texturées/animées avec morph, export USD, sorties GLB/FBX et réouverture. Chaîne GLB → Unreal 5.5 : six assets produits, puis rechargement dans un second processus Unreal avec références matériau/texture vérifiées. Le parcours UI teste la vraie conversion Blender et le package dérivé. Pas de validation universelle des packs achetés, versions Unreal 4.27–5.8 ou autres OS. `.max`, Cinema4D, Maya natif, PSD/SBSAR et formats CAD exigent un convertisseur/outillage supplémentaire approprié ; ils ne sont pas annoncés convertibles par Blender.

Références : [CLI Blender](https://docs.blender.org/manual/en/3.6/advanced/command_line/arguments.html), [sécurité des scripts](https://docs.blender.org/manual/en/3.6/advanced/scripting/security.html), [export FBX/glTF](https://docs.blender.org/api/5.1/bpy.ops.export_scene.html).

## SDK de conversion

`CyEngineHub.Fab.Conversion.IAssetSourceConverter` expose un descripteur, les extensions, options et `ConvertAsync`. La requête donne un dossier source, un espace de travail distinct, les options et éventuellement le moteur cible. Le résultat donne un dossier réel, un rapport et `LibraryPackageContentKind`. Enregistrer l’implémentation avec `AssetSourceConverterRegistry.Register` ; l’hôte reste propriétaire de la publication et du nettoyage. Ce SDK C# n’est pas un chargeur automatique de DLL non fiables.

Pour une étape de chaîne, implémenter `IFabPreparationScript` : déclarer `OutputContentKind`, ne pas installer dans le projet et retourner `Outputs["variant"]`. La validation anticipe le type pour les étapes suivantes ; aucune étape ne démarre si la chaîne est invalide. Les outils locaux externes s’installent dans le dossier des scripts d’installation déjà disponible dans l’interface, avec ce manifeste :

```json
{
  "schemaVersion": 1,
  "id": "studio.source-converter",
  "displayName": "Mon convertisseur DCC",
  "description": "Conversion avec mon outil installé",
  "publisher": "Mon studio",
  "executable": "convert.ps1",
  "arguments": ["-Source", "{SourceDirectory}", "-Output", "{OutputDirectory}", "-Result", "{ResultFile}"],
  "supportedContentKinds": ["Unknown", "RawAsset"],
  "supportedTargetKinds": ["Project"],
  "preparedContentKind": "RawAsset",
  "options": []
}
```

Le script doit convertir les fichiers dans **OutputDirectory**, puis écrire dans **ResultFile** : `{"variant":"Content"}`. `Content` est le sous-dossier de sortie fourni par le hub. La réponse (1 Mio maximum) ne peut pas rediriger vers les originaux/le projet ; sorties vides, liens et chemins hors espace de travail sont refusés. Le manifeste doit déclarer un type de sortie connu. Le prochain script peut ainsi importer les FBX/GLB issus d’un DCC propriétaire. Les arguments sont structurés, sans concaténation dans un shell. Le script est un outil utilisateur pleinement privilégié, pas un sandbox ; seul le **chemin de résultat déclaré** est contrôlé. Le hub n’exécute pas un script contenu dans un pack simplement parce qu’il a été téléchargé.

Ces scripts apparaissent dans la configuration de chaîne, avec avertissement de code externe. Ils ne sont pas automatiquement ajoutés au panneau des convertisseurs C# ni supposés présents/licenciés. Aucun convertisseur propriétaire non disponible n’est déclaré testé.
