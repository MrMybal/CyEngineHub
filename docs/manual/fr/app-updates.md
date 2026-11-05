# Mettre à jour CyEngineHub

L’outil recherche les releases GitHub publiées, pas les fichiers non commités, les tags seuls ou les builds locales. Le dépôt initial est MrMybal/CyEngineHub. Dépôt, recherche initiale et canal de préversions se règlent dans Paramètres → Avancé → Mises à jour.

## Vérifier et décider

La recherche initiale commence après lecture des préférences, indépendamment des catalogues/comptes, avec délai maximal non bloquant. La barre indique recherche, à jour, disponible ou réessai après erreur. Hors ligne/erreur ne signifie pas « à jour ». La recherche manuelle reste accessible lorsque l’automatique est désactivée.

Une nouvelle version ouvre une proposition avec notes et actions. Plus tard/Échap ferme sans télécharger ; la barre permet de rouvrir. Une installation normale ne télécharge/lance pas implicitement un installateur. Une build locale supérieure aux releases ne reçoit pas un downgrade.

:::flow
Trouver une release publiée plus récente autorisée par le canal
Lire les notes et choisir Télécharger et installer
Télécharger l’installateur Windows et vérifier SHA-256
Arrêter proprement le hub ; le lanceur attend la fin du processus
Lancer l’installateur vérifié ; rouvrir et contrôler la version
:::

## Proposition absente ou installation indisponible

Vérifiez dépôt, canal et préférence enregistrée. Les Alpha/Bêta nécessitent leur canal, sauf publication explicite comme release courante non-préversion. Le badge Latest n’est pas une certification de stabilité.

L’installation automatique exige un installateur Windows compatible et une empreinte valide via digest GitHub ou fichier de sommes. À défaut, release/notes restent consultables mais l’installation automatique peut être indisponible. Une empreinte différente rejette l’installateur. Ne contournez pas ce contrôle en renommant un fichier.

Terminez/annulez les tâches importantes et sauvegardez avant mise à jour. Le lanceur refuse l’installation si l’ancien hub ne s’arrête pas dans le délai. En portable, préférez le ZIP pour conserver emplacement/profil ; l’installateur suit le parcours Windows normal. Construire des binaires ne les publie pas automatiquement. Voir [patchs moteur](help:engine-updates) et [dépannage](help:troubleshooting).
