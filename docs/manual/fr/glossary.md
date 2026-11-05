# Glossaire, confidentialité et licences

## Termes du hub

- Installation moteur : dossier/build précis, pas seulement « Unreal 5.5 ». Alias : nom affiché local. Association : identifiant reliant un projet à une installation.
- Projet : espace/descripteur natif du moteur. Profil : valeurs réutilisables pour nouveaux projets, pas édition globale des projets existants.
- Asset/produit : identité du catalogue. Artefact/variante : distribution précise plateforme/version. Package local : contenu stocké/téléchargé et métadonnées.
- RAW/échange : sources FBX/GLB/images, pas encore UAssets sérialisés. Contenu natif : fichiers .uasset/.umap spécifiques au moteur. Plugin : code/contenu déclaré pour un hôte.
- Dérivé : version préparée distincte conservant sa provenance. Staging/atelier : traitement temporaire, pas forcément supprimable avant récupération d’une transaction échouée.
- Manifest/sidecar : métadonnées machine de fichiers/types/versions. Chunk : bloc de distribution. Delta : plages modifiées et retour arrière d’un patch moteur.
- Cache : données temporaires réutilisables. Quota : plafond de placement. Empreinte : contenu disque mesuré, parfois partiel. Octets téléchargés, espace supplémentaire et taille finale sont différents.
- Build : compilation d’une cible. Génération projet : espace IDE. Cook : préparation du contenu pour une plateforme. Package/archive : assemblage d’une distribution.
- Activé : intégration exposée. Connecté : compte/session disponible. Vérifié : contrôle précis réussi. En file/en cours/terminé : états distincts.
- Alpha/expérimental : validation limitée explicite, pas compatibilité universelle. Forcer : tenter malgré l’avertissement préalable, pas garantir le résultat.

## Données locales et confidentialité

Catalogues, préférences, reçus, files et journaux sont des données locales de l’application. Profils navigateur et sessions sécurisées Windows restent sensibles. Les clients IA peuvent transmettre les métadonnées consultées à leur fournisseur. Sauvegardez et masquez les diagnostics avant partage. Le lecteur d’aide n’utilise pas le réseau sauf clic volontaire sur un lien externe.

## Licences du projet

Code original du hub, scripts et documentation sont GPL-3.0-only, sauf composants séparés et notices tierces. Le client Unreal Editor et l’adaptateur natif de conversion originaux ont leurs notices MIT distinctes. Code moteur, assets marketplace et projets utilisateur conservent leurs droits propres ; ces licences ne donnent pas les droits des contenus fournisseur.

Consultez LICENSE, LICENSING.md, THIRD_PARTY_NOTICES.md et les dossiers LICENCES/Licenses embarqués pour les textes et périmètres réels. Une distribution doit conserver les notices applicables et fournir les sources correspondantes selon leurs exigences ; ce manuel ne les remplace pas et n’est pas une expertise juridique universelle de compatibilité.

Revenez au [démarrage](help:welcome) ou au [dépannage](help:troubleshooting).
