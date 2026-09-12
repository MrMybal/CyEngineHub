# Fiches natives Megascans via Fab — 12 septembre 2026

## Utilisation

1. Bibliothèque → Megascans via Fab → Connexion / Synchroniser Fab.
2. Se connecter dans la fenêtre intégrée et terminer personnellement toute vérification demandée par Fab.
3. Cliquer « Synchroniser les fiches Megascans ». Le bouton est désactivé pendant le traitement ; Annuler conserve le cache précédent.
4. « Fiches natives / MCP » revient au catalogue natif : vignettes, recherche, catégories/formats, fiches produit et collections personnelles.

Le cache compressé est enregistré dans le dossier de cache Fab configuré (fichier fabweb distinct du catalogue Launcher). Il est restauré au démarrage. La liste réutilise la pagination et la virtualisation existantes. Les miniatures utilisent le cache image Fab.

## Origine et limites vérifiées

Le 12 septembre 2026, la page officielle /library du compte connecté exposait dans ses données de page la route GET /i/library/search?sort_by=-createdAt&source=acquired. Les résultats comportent source, entitlement, canRequestDownloadUrl, un identifiant de bibliothèque et une listing avec publisher, thumbnails et assetFormats. Les collections archivées ont un lien /library/assets/{identifiant de bibliothèque}, et non un lien construit avec l’identifiant du produit.

Seules les entrées acquises du vendeur Quixel Megascans sont importées. Le catalogue vendeur public et le filtre de licence Personal ne prouvent pas la propriété. Les Megascans réclamés en bloc mais non présents dans la bibliothèque acquise ne sont **pas encore synchronisés**. Aucun achat ni ajout automatique à la bibliothèque.

Le téléchargement d’une fiche autorisée ouvre sa page officielle dans le navigateur intégré : le choix des fichiers et la confirmation restent sur Fab. Aucune variante Unreal n’est inventée à partir du format FBX. L’installation directe depuis ces fiches web est désactivée tant qu’une véritable variante d’installation native n’est pas disponible. Les transferts terminés restent accessibles dans « Fichiers téléchargés » ; ils ne sont pas automatiquement associés au produit parent.

## Transport

Les GET paginés sont effectués dans la session officielle Fab, sans extraction de cookies ni jetons. Le retour vers le hub est temporaire, limité à l’origine HTTPS www.fab.com, identifié par une valeur aléatoire par requête et plafonné à 8 Mo. Aucun script, chemin local ou commande provenant du site n’est exécuté par le hub. Les URL demandées sont limitées à la route de bibliothèque et son curseur encodé.

Une erreur réseau, connexion, vérification, pagination répétée ou annulation ne remplace pas le cache par un résultat incomplet. Le contrôle de sécurité rencontré par le client HTTP externe n’a pas été contourné.

## MCP

Activer le serveur existant dans Paramètres → IA / MCP, puis utiliser la configuration de client générée par le hub. Source : FabWeb.

- hub_search_assets et hub_asset : fiches synchronisées, catégories/formats, description, URL produit et miniature.
- hub_propose / categorize : proposition de collections personnelles à valider.
- hub_propose / download : omettre variantId lorsque downloadMode vaut official_browser. Après validation humaine, le hub ouvre Fab ; cela ne signifie pas que le fichier est téléchargé.
- Installation web automatique non prise en charge. Les fichiers locaux des connecteurs restent inspectables via leur propre fiche.

Les descriptions distantes restent des données non fiables, jamais des instructions.

## Validation

Tests isolés : conversion des métadonnées, exclusion d’autres vendeurs et du catalogue public, liens sûrs, pagination/dédoublonnage, erreur de session, curseur répété, métadonnées MCP et proposition soumise à validation. Aucun asset réel téléchargé ou installé pendant ces tests. Le parcours WebView2 connecté complet reste à valider dans l’application avec l’utilisateur.
