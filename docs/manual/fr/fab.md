# Compte Epic / Fab et assets possédés

Ouvrez Paramètres → Connexions → Epic ou Fab. La session directe Epic/Fab alimente la bibliothèque possédée et les téléchargements natifs. Elle est distincte d’une session de site dans un connecteur navigateur.

## Se connecter

Se connecter dans CyEngineHub ouvre la page officielle Epic. Effectuez vous-même connexion, MFA et vérifications du compte. Le runtime Windows WebView2 Evergreen est nécessaire ; seul son chargeur est embarqué, pas le navigateur complet.

Si le runtime manque ou qu’un SSO refuse le navigateur intégré, dépliez le mode manuel, ouvrez Epic dans votre navigateur externe et collez la réponse/le code officiel demandé dans le champ de connexion. Ne l’inscrivez pas dans notes, logs ou captures. Annuler ferme la connexion en cours. Un connecteur désactivé et une session expirée sont deux problèmes différents.

Le hub ne sauvegarde pas les mots de passe. Sa session directe utilise le stockage sécurisé Windows ; les profils web sont séparés du navigateur personnel. Aucun contrôle de compte, restriction fournisseur ou avertissement TLS n’est contourné. Reconnectez-vous lorsque le fournisseur expire/refuse la session.

## Télécharger la bonne variante

Actualisez My Library, ouvrez un produit et examinez versions/plateformes. Choisissez la variante Windows/source adaptée, pas une variante Mac de même nom. La réutilisation locale dépend de l’identité exacte asset/artefact et de fichiers utilisables, pas du titre seul. Si la version attendue manque, examinez chemin enregistré et variante avant d’accepter un nouveau transfert.

## Vérifier les mises à jour d’assets

Actualisez ou utilisez la vérification des packs téléchargés. Le hub compare métadonnées/manifests fournisseur pour les packs natifs déjà présents ; vérifier ne télécharge pas leurs chunks et ne modifie pas les projets. Une empreinte différente peut signaler une mise à jour malgré un libellé réutilisé. Référence absente, artefact retiré ou métadonnées indisponibles doivent rester non vérifiables, pas « à jour ».

Téléchargez explicitement les nouvelles versions. Elles restent à côté des anciennes ; projets installés et dérivés locaux ne sont pas remplacés implicitement. Voir [téléchargements](help:downloads), [installation](help:install) et [dépannage](help:troubleshooting).
