# Validation Unreal et statistiques de téléchargement — 10 septembre 2026

## Résultat

- **362 tests automatisés réussis, 0 échec** dans la passe finale (45 nouveaux cas : télémétrie 6, profils 22, téléchargement moteur simulé 6, pipeline d’installation/conversion 11).
- Compilation Windows Release réussie : **0 avertissement, 0 erreur**.
- Test natif Avalonia/WebView2 réussi avec site et archive factices, profil navigateur et base SQLite isolés.
- Tests réels avec **Unreal 5.6.1 et 5.5.4 déjà installés**, uniquement dans les projets QA générés.
- Aucun moteur complet téléchargé, aucun projet ou asset utilisateur employé/modifié.

## Informations de téléchargement

Les transferts Fab, Megascans, moteurs Epic (y compris composants), les téléchargements de collections et le navigateur interne affichent les données disponibles : volume reçu, volume total, pourcentage, débit observé, temps écoulé et temps restant estimé.

Fab et Epic publient les octets pendant la réception des chunks (rafraîchissement limité à environ 250 ms), plus seulement à leur fin. Le navigateur relève les données toutes les 500 ms. Le débit utilise une fenêtre glissante de cinq secondes. Pause, reprise et redémarrage ne réutilisent pas une ancienne vitesse.

Les tailles inconnues restent explicitement inconnues : aucune estimation du volume global à partir du nombre de fichiers. Megascans affiche aussi le pourcentage du fichier courant quand sa taille est fournie. Pour plusieurs URL, la taille globale n’est connue qu’au dernier fichier si sa taille est annoncée. L’ETA globale peut donc rester indisponible avant cela. Après le transfert, la reconstruction, la compression et l’installation gardent leur progression propre ; leur durée n’est pas inventée à partir du débit réseau.

## Matrice de validation

| Fonction | Vérification exécutée | Limite |
|---|---|---|
| Création de projet | 11 profils Unreal × Blueprint/C++ : association moteur, plugins, désactivation par défaut, overrides INI, reçu et fichiers C++ | Pas de compilation C++ dans cette passe |
| Projet Unreal réel | Projets Blueprint vides ; création d’un matériau, d’une instance liée et d’une texture TGA 4×4 dans UE 5.6.1 | Pas de test de rendu ou shaders lourds |
| Téléchargement moteur | Vrai backend avec HttpMessageHandler factice : manifest, chunk, SHA-1, reconstruction, publication, annulation, corruption, destination occupée, HTTP 401 et traversée de chemin | Aucun appel au service Epic ; moteur factice de quelques octets |
| Projet → pack dérivé | Création d’un pack de contenu à partir du projet QA, sans embarquer son .uproject et sans modifier la source | Petit pack synthétique |
| Installation de pack | Script intégré dans un nouveau projet ; assets rechargés dans Unreal | Pack simple matériau/instance/texture |
| Installation de plugin | Script intégré, plugin de contenu QA ; montage et chargement d’un matériau depuis /QaPlugin | Aucun module C++ compilé ou plugin tiers testé |
| Installation avec déplacement | Plugin de relocation : chemin /Game/OriginalPack conservé à l’entrée du projet temporaire, déplacement vers /Game/QA/OriginalPack, installation cible et rechargement | Référence enfant → matériau parent vérifiée, ancien chemin absent ; pas tous types de dépendances |
| Conversion 5.6 → 5.5 | Version Bridge, export/import d’une texture QA ; rechargement Texture2D dans Unreal 5.5.4 | Ne valide pas le downgrade universel de Blueprints, mondes ou plugins C++ |
| Erreurs de conversion/relocation | Rapports Unreal avec erreurs, log de dépendance manquante, exit code, annulation, chemin invalide et conflits de cible | Processus factices pour déclencher les erreurs de manière déterministe |
| Navigateur interne | Téléchargement ZIP, destination configurée, fiche native, navigation bibliothèque, destination indisponible et présence des statistiques dans les contrôles réels | Site factice intercepté ; aucune session utilisateur |

## Défauts corrigés grâce aux tests

1. Le Version Bridge pouvait publier/installer un résultat malgré des erreurs dans les rapports source ou cible. Ces rapports sont désormais requis et leurs erreurs bloquent la suite avant publication.
2. La validation après déplacement ne reconnaissait pas le message Unreal `Can't find file` à cause d’une apostrophe supplémentaire dans le motif.
3. Un chemin de déplacement absolu hors `/Game/` pouvait être normalisé implicitement vers un autre emplacement. Il est maintenant refusé avant lancement d’Unreal.

Le test navigateur confirme aussi la correction du double achèvement des deferrals WebView2 qui provoquait le crash après téléchargement.

## Isolation et espace disque

- Projets, bibliothèques de test, fichiers source, rapports et DDC : sous `artifacts/unreal-capabilities-<GUID>`.
- Trois essais conservés pour diagnostic : environ **11 Mio cumulés** ; essai final réussi environ **5 Mio**.
- Plafond automatique par exécution réelle : **8 Gio**, délai maximum de quatre minutes par commandlet, arrêt du processus en cas de dépassement.
- Seuls les exécutables Unreal explicitement passés et les .uproject générés sous la racine QA peuvent être lancés par le runner.
- Les deux outils QA compilés représentent environ **1,27 Go** ensemble, le build Windows dédié **0,78 Go** ; les données de tests restent très en dessous des **30 Go** demandés.
- Les fixtures unitaires temporaires sont supprimées à la fin de leurs tests. Les projets Unreal et rapports sont conservés pour inspection. Aucun processus Unreal de test ne restait actif à la fin.

## Rejouer

Depuis la racine du dépôt, sans télécharger de moteur :

```powershell
dotnet test CyEngineHub.sln -c Release --logger trx --results-directory artifacts/test-results
dotnet run --project tools/qa/CosmosLibrarySmoke -c Release
```

Validation réelle opt-in, avec deux moteurs existants (adapter les chemins) :

```powershell
dotnet run --project tools/qa/UnrealCapabilitiesSmoke -c Release -- --live $SourceEngine $TargetEngine
```

Preuves de cette passe :

- `artifacts/test-results-unreal-pass/*.trx` : 362/362.
- `artifacts/unreal-capabilities-b5cb5dda9fbd40d4bfea00dfb9ba4f61/summary.json` : contrôles réels réussis et moteurs utilisés.
- Sous ce même dossier : rapports JSON, logs Unreal et projets inspectables.
- `artifacts/cosmos-smoke-20260910-215210` : fixture navigateur, archive et catalogue isolés.
- `artifacts/unreal-validated-build/CyEngineHub.Desktop.exe` : version Windows compilée avec les correctifs. Non publiée sur GitHub dans cette passe.
