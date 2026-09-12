# Megascans via Fab : accès web intégré

Mise à jour du 12 septembre 2026 : une synchronisation des fiches acquises est désormais ajoutée. Voir [fiches natives et MCP](megascans-fab-native-library.md). Le périmètre ci-dessous décrit la première version navigateur.

## Utilisation

- Dans Bibliothèque > Megascans, cliquer **Megascans via Fab**. Le même accès est proposé dans Paramètres > Connexions > Megascans.
- Se connecter au compte Epic sur **Fab dans cette fenêtre**. La session web est conservée dans le profil WebView2 du connecteur ; elle est distincte du jeton Epic/Launcher et du compte Quixel historique.
- La barre propose **Catalogue Megascans**, **Ma bibliothèque Fab**, les téléchargements et les fiches locales. Le catalogue vendeur inclut aussi des produits non possédés : ne pas l'interpréter comme une liste de droits.
- Utiliser le téléchargement officiel d'un produit auquel le compte a accès. Les contrôles du site, les licences et les éventuelles vérifications restent entièrement appliqués par Fab. Aucun achat ou ajout en masse à la bibliothèque n'est automatisé.
- À la fin, le fichier est enregistré dans le catalogue local du connecteur. Le bouton **Téléchargés via Fab** permet de retrouver ces fiches, même si Quixel historique refuse la connexion.
- Le dossier se règle dans Paramètres > Marketplaces > **Megascans via Fab**. Chaque transfert utilise un sous-dossier distinct, sans écrasement. Le connecteur peut être désactivé séparément.

## Périmètre livré

Ce premier accès réutilise le navigateur et le pipeline de téléchargement des connecteurs. Il conserve les ZIP/fichiers reçus, crée des fiches locales et les expose à la recherche MCP existante. Il ne supprime ni ne remplace le cache ou les acquisitions historiques Quixel.

Il ne synchronise **pas encore automatiquement tous les Megascans possédés sur le site**, ne récupère pas automatiquement leurs images et n'ajoute pas de nouvelle API native de téléchargement/installation Fab RAW. Le login Quixel historique n'est pas réparé : cette voie passe indépendamment par Fab. Une inspection authentifiée des réponses Fab reste nécessaire avant d'ajouter cette synchronisation. Aucun endpoint privé ni jeton web n'est inventé ou extrait par cette intégration.

## Validation

Le manifeste est validé par les tests des extensions. Le hub et le navigateur compilent sans avertissement. La connexion réelle et le téléchargement Fab doivent être testés par l'utilisateur dans la nouvelle fenêtre ; aucun fichier réel n'a été téléchargé et aucune session personnelle modifiée pendant les tests automatisés.

Le connecteur est distribué dans `Extensions/cyenginehub.marketplace-megascans-fab/extension.json` (build local et script de release). Le filtre `licenses=personal` n'est pas ajouté : il n'est pas un contrôle de propriété, et la page permet de choisir les filtres nécessaires.
