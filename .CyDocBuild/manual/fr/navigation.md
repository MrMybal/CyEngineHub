---
id: navigation
title: Navigation et états des tâches
description: Retrouvez les catalogues et les fiches, et comprenez les états des tâches.
section: Bien démarrer
parent: welcome
order: 1
sourceRevision: 2
---
# Navigation et états des tâches {#navigation-and-task-states}

La barre latérale distingue Moteurs, Projets, Bibliothèque, Extensions et Outils des pages d’activité : Stockage, Actions, Téléchargements, Journal et Paramètres. Aide / Documentation et F1 ouvrent ce manuel sans changer le projet ou produit sélectionné.

## Catalogues et fiches {#catalogs-and-detail-sheets}

Utilisez recherche et filtres pour limiter un catalogue. Les présentations cartes, compactes/images et lignes dépendent du catalogue ; leur changement ne déplace aucun fichier. Les favoris sont un classement local, pas des favoris fournisseur. Une fiche expose les actions de cette installation, ce projet ou cet asset précis. Les noms personnalisés des moteurs restent des étiquettes ; chemins et associations déterminent la cible réelle.

Le bouton retour d’une fiche ramène à son catalogue. Revenir d’un produit Fab restaure le parcours des assets plutôt qu’un autre onglet principal. Les sous-pages de projet conservent une route de retour au projet. Le lecteur d’aide possède son propre historique ; le fermer ne modifie pas l’historique de l’application.

## Actions et Téléchargements {#actions-versus-downloads}

Actions suit builds, installations, analyses et conversions. Téléchargements suit les transferts de bibliothèque pris en charge. Les opérations continuent pendant la navigation. Utilisez le lien projet/produit/moteur d’une ligne, ou le double-clic sur une action liée lorsqu’il est disponible, pour retrouver la cible. Une tâche multicible peut proposer plusieurs liens.

- En attente : demande acceptée, mais un budget du planificateur ou un verrou de source/destination empêche encore le départ.
- En cours : travail actif ; lisez la phase actuelle, pas seulement le pourcentage.
- Terminé : l’opération a retourné un succès et l’indicateur prend son état de fin.
- Échec : consultez le motif et les logs ; des étapes antérieures peuvent déjà avoir eu lieu.
- Annulé : la demande d’annulation a été traitée ; les étapes déjà validées peuvent subsister.

100 % pour une phase de transfert ne signifie pas que l’extraction, compression, validation ou installation est finie. Fiez-vous à l’état terminal de la tâche. Certains outils externes ne fournissent pas de pourcentage précis et utilisent une progression indéterminée.

Les badges de connexion ouvrent leurs paramètres respectifs. Activé, connecté et accès Git vérifié sont des états distincts. Voir [téléchargements et planification](doc:downloads), [connexions](doc:settings) et [dépannage](doc:troubleshooting).
