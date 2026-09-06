# Collections officielles Megascans

Dans **Bibliothèque → Megascans**, les onglets **Mes assets** et **Collections
Megascans** séparent le catalogue personnel des sélections officielles Quixel.
Ces sélections ne sont pas ajoutées aux collections personnelles du hub.

- Recherche `contains` sans distinction de casse sur le nom, la catégorie et la
  description disponible. Les collections sont triées par nom.
- Liste virtualisée : seules les lignes visibles et une petite marge sont
  construites. Les couvertures sont chargées/décodées en arrière-plan, mises en
  cache puis libérées au recyclage des lignes.
- Ouvrir une collection charge ses références officielles, puis affiche uniquement
  celles présentes dans le catalogue Megascans de l'utilisateur. Le compteur
  indique cette intersection, sans faire passer les assets absents ou non résolus
  pour des assets acquis. Les téléchargements/installations conservent leurs
  vérifications habituelles et leur affichage virtualisé.
- **Mes assets** retire le filtre de collection. **← Collections** revient à la
  liste. Un résultat réseau tardif ne ramène pas l'utilisateur sur une page quittée.

## Disponibilité

Le catalogue public provient de l'index `collections` du moteur de recherche Quixel,
par pages de 100. Les détails viennent de `https://quixel.com/v1/packs/{id}`,
l'endpoint utilisé par le client web officiel Quixel. Aucun jeton/cookie/session
utilisateur n'est nécessaire pour ces métadonnées publiques. Les images proviennent
du CDN de collections Quixel. Les entrées internes préfixées `x -` / `x |` sont
exclues, comme sur le site.

Vérifié le 31 août 2026 : 208 entrées brutes, dont 9 internes, soit **199 collections
affichables** ; la collection Xeric Shrublands expose 180 références d'assets.
Les services historiques peuvent changer ou devenir indisponibles. Dans ce cas,
le hub conserve le dernier cache et affiche une erreur lisible, sans collection
inventée et sans basculer les téléchargements vers Fab.

## Cache et actualisation

Le dossier configuré dans **Paramètres → Marketplaces → Megascans → Cache** contient :

- `Catalog/collections-v1.json.gz` : catalogue et contenus des collections consultées ;
- `Images/` : couvertures dans le cache d'images Megascans existant.

Le cache est affiché dès l'ouverture de l'onglet. Sans cache, le catalogue est chargé
automatiquement. Un cache de plus de 24 h est actualisé en arrière-plan à l'ouverture
si l'actualisation automatique Megascans est activée. Le bouton d'actualisation est
grisé avec une roue pendant une lecture. L'actualisation manuelle remet à jour la
liste et force la relecture du contenu à la prochaine ouverture d'une collection.
Une collection déjà consultée reste navigable hors ligne tant que son contenu est
en cache et que le catalogue personnel est disponible dans le hub.

## Tests

```powershell
dotnet test tests/CyEngineHub.Epic.Tests/CyEngineHub.Epic.Tests.csproj -c Release
dotnet run --project tools/qa/QuixelCollectionsSmoke/QuixelCollectionsSmoke.csproj -c Release -p:SkipBuiltInExtensionCopy=true
```

Le smoke test utilise 208 collections, 18 800 assets simulés et un stockage isolé
sous `artifacts`. Il contrôle recherche, virtualisation, navigation, distinction
des assets possédés, isolation de Fab, rechargement hors ligne et échec réseau.
L'option `-- --live` vérifie les métadonnées/couvertures publiques réelles, sans
ouvrir de compte ni télécharger de pack.
