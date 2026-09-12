# Plugins de marketplaces — Alpha

Les plugins de marketplace donnent accès aux assets déjà possédés. CyEngineHub ne fournit pas de panier, de recherche commerciale ou de fonction d’achat.

## Cosmos : périmètre actuel

Depuis la version 0.3.0 du plugin intégré, **Cosmos · mes assets** ouvre par défaut la bibliothèque native des fichiers téléchargés. Le bouton **Télécharger depuis mon inventaire web** ouvre `https://cosmos.leartesstudios.com/inventory` dans une fenêtre **WebView2 hébergée par CyEngineHub**. Elle possède retour/avance, actualisation, retour à l’inventaire et un panneau de téléchargements. Un bouton distinct **Inventaire en externe** reste disponible, uniquement sur demande.

Les fenêtres déclenchées par un clic (dont les popups OAuth) utilisent des WebView2 enfants internes, avec le même environnement et la même session. Les fenêtres automatiques sont bloquées. Un fournisseur d’identité peut refuser les navigateurs intégrés : CyEngineHub ne modifie pas son identité navigateur pour contourner ce refus.

La session du site est conservée par WebView2 sur ce PC dans `WebView2/Marketplaces/<identifiant-du-plugin>` sous le dossier de données du hub. L’application ne lit ni n’exporte les cookies ou réponses d’authentification. L’enregistrement des mots de passe et le remplissage automatique sont désactivés. Utiliser la déconnexion du site pour terminer sa session.

Les téléchargements sont ceux déclenchés sur le site officiel. Depuis la version 0.2.1, CyEngineHub impose leur destination : **Paramètres → Marketplaces → connecteur → Dossier de téléchargement**. Par défaut, chaque connecteur utilise `<bibliothèque du hub>/<identifiant du connecteur>/Downloads`. Enregistrer les marketplaces conserve le chemin au prochain lancement ; une modification s’applique aux prochains téléchargements sans déplacer ceux déjà reçus ou en cours.

Chaque transfert dispose d’un sous-dossier unique et d’un nom de fichier nettoyé pour éviter les collisions et sorties du dossier prévu. Si le chemin n’est pas accessible ou si la marketplace a été désactivée, le transfert est annulé explicitement : aucun repli vers le dossier Téléchargements de Windows. Le bouton **Dossier des téléchargements** ouvre cette destination et **Téléchargements** conserve le suivi fourni par WebView2. Une fenêtre avec des téléchargements actifs refuse sa fermeture : les terminer ou les annuler d’abord. Quitter complètement le hub termine aussi ses navigateurs.

Chaque téléchargement terminé crée une **fiche locale persistante** (nom modifiable, format, type déduit du ZIP, taille, date, chemin et notes). Aucun contenu n’est exécuté ou extrait pour créer la fiche. Les transferts échoués ou annulés ne sont pas indexés. Une répétition de l’enregistrement du même fichier ne duplique pas sa fiche et conserve les modifications personnelles. Les téléchargements distincts ne sont pas fusionnés arbitrairement sur leur nom : sans identifiant produit officiel, une fiche correspond à un fichier téléchargé.

La barre native **au-dessus du site** propose **Mes assets téléchargés** et **Voir la fiche du dernier téléchargement**, également pour les téléchargements issus d’une fenêtre enfant. Le retour à la bibliothèque minimise le navigateur sans fermer sa session ni interrompre ses transferts. La liste native utilise une ListBox virtualisée ; chargement du catalogue, contrôles de présence et lecture des ZIP se font en arrière-plan.

**Ajouter un fichier existant** référence une archive déjà présente, notamment un ancien téléchargement dans Téléchargements Windows. L’original reste sur place : aucun déplacement ni compression implicite. Un fichier supprimé ou déplacé en dehors du hub reste signalé comme introuvable dans sa fiche. L’index est conservé dans `<DataDirectory>/Library/MarketplaceDownloads/catalog.json`, indépendamment des réglages du dossier des prochains transferts.

La page officielle n’est pas modifiée : aucune lecture DOM d’inventaire, injection de boutons ou comparaison automatique de cartes web n’est implémentée. **Ce n’est pas une synchronisation de l’inventaire en ligne** : aucune miniature, description officielle, version moteur ni URL privée n’est copiée depuis le site. Les boutons ajoutés appartiennent à CyEngineHub, pas au site. Les fiches offrent l’accès aux fichiers ; elles ne prétendent pas disposer de l’installation Fab ni d’une compatibilité Unreal validée. Un ZIP illisible est clairement signalé.

Références consultées le 8 septembre 2026 : [inventaire](https://cosmos.leartesstudios.com/inventory), [FAQ officielle](https://cosmos.leartesstudios.com/frequently-asked-questions).

## Ajouter un autre plugin

1. Copier le dossier `extensions/builtin/cyenginehub.marketplace-cosmos` dans un nouveau dossier.
2. Modifier `extension.json` : identifiant unique, nom, éditeur, description, URL HTTPS de l’inventaire dans `website` et `marketplace.inventoryUrl`.
3. Garder `kind: Marketplace`, `executionKind: WebLink` et `capabilities: OwnedInventory`.
4. Dans **Extensions Hub**, installer le dossier via le mécanisme d’installation d’extension et confirmer le plan.

Le plugin apparaît dans **Paramètres → Marketplaces** et dans les sources de la **Bibliothèque**. L’interrupteur masque son accès et bloque sa commande. Utiliser **Enregistrer les marketplaces** pour conserver le choix au prochain lancement. Les fichiers locaux ne sont pas supprimés.

Le manifeste ne doit contenir aucun jeton, mot de passe ou cookie. Les liens d’inventaire doivent être en HTTPS, sur le même hôte que `website`, sans identifiants, paramètres ni fragment. Le mode actuel ne charge aucun exécutable de connecteur tiers.

## Validation

Correctif 0.3.1 : le dump Windows du 8 septembre montrait `COMException 0x8000000E` dans `CoreWebView2Deferral.Dispose`, après `OnDownloadStarting`. Le code appelait `Complete()` puis `Dispose()`, qui complète lui-même l’événement. Les événements téléchargement et popup libèrent désormais leur deferral une seule fois. Le test `tools/qa/CosmosLibrarySmoke` reproduit l’exception avant correctif via un vrai téléchargement WebView2, avec une archive et un site fictifs isolés, sans compte utilisateur.

Compilation Release Desktop ; tests des règles de navigation HTTPS, de masquage des paramètres d’authentification, de destination des téléchargements, de persistance du catalogue, de doublons, d’enregistrements concurrents, de classification ZIP et de navigation/recherche des fiches. Le contrôle visuel Windows automatisé n’a pas pu être utilisé (erreur du helper sandbox). Le cycle navigateur réel → téléchargement → fiche doit être vérifié par l’utilisateur dans cette version ; il n’est pas déclaré validé par les tests unitaires.
