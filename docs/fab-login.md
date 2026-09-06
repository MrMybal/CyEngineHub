# Connexion Epic / Fab

Dans **Paramètres → Connexions → Fab** (ou Epic), deux modes restent disponibles.

## Fenêtre intégrée

Le bouton **Se connecter dans CyEngineHub** ouvre la page officielle Epic dans une
fenêtre modale WebView2 (Chromium). L'utilisateur effectue lui-même sa connexion,
son éventuelle double authentification et les validations demandées par Epic.
Le hub récupère ensuite le code de la réponse JSON officielle, ferme la fenêtre,
échange le code avec le connecteur Fab existant et actualise le catalogue.

Windows et le **Microsoft Edge WebView2 Evergreen Runtime** sont nécessaires.
Le SDK/chargeur est distribué avec le hub, mais le runtime Chromium n'est pas
embarqué ni installé silencieusement. Si nécessaire, l'installer depuis la
[page Microsoft WebView2](https://developer.microsoft.com/microsoft-edge/webview2/).

## Mode manuel conservé

Déplier **Mode manuel — navigateur externe / copier-coller** puis utiliser
**Ouvrir Epic dans mon navigateur**. Après connexion, copier la réponse JSON :
la détection du presse-papiers reste active pendant deux minutes. Il est aussi
possible de coller directement le code/JSON puis de cliquer sur **Connecter**.

Le bouton **Mode manuel** de la fenêtre intégrée ferme celle-ci et déplie ce
panneau. Un runtime absent, un problème de réseau ou un fournisseur SSO qui
refuse un navigateur intégré ne supprime jamais cette possibilité. Les fenêtres
secondaires demandées par certains fournisseurs SSO ne sont pas prises en
charge : utiliser la connexion Epic directe ou le navigateur externe.

## Sécurité et cycle de vie

- Profil WebView2 privé dédié, sous `<données du hub>/WebView2/Fab`, sans réutiliser
  le profil du navigateur personnel. Ne pas exécuter le hub en administrateur.
- Le hub n'inspecte ni les champs de connexion ni le magasin de cookies. Il ne
  sauvegarde pas les mots de passe ; la session du connecteur Fab continue
  d'utiliser son stockage sécurisé Windows existant.
- Aucun pont JavaScript/natif exposé, pas de devtools, téléchargement ou permission
  web accordée. Navigation HTTP et erreurs TLS refusées, sans contournement.
- Seul un GET réussi du callback HTTPS `www.epicgames.com/id/api/redirect`, avec
  le même client et `responseType=code`, peut fournir le code. La navigation
  principale doit elle aussi être sur ce callback.
- JSON limité à 64 Kio, parsing strict, lecture asynchrone avec buffers à offset
  zéro pour le flux COM WebView2. Une navigation obsolète ou une fenêtre fermée
  ne peut pas terminer la connexion. Le code ne remplit pas le champ manuel et
  les erreurs d'authentification ne sont pas recopiées dans les logs.
- Une seule fenêtre de connexion ; annulation par bouton, croix ou Échap.
  Délai de dix minutes pour la fenêtre et d'une minute pour l'échange du code.
  La capture du presse-papiers du mode manuel est arrêtée pendant le mode intégré.

L'autorisation utilise le parcours du connecteur de compatibilité Epic/Fab déjà
présent dans le hub. Epic peut modifier ou refuser ce parcours ; aucun mécanisme
ne contourne les restrictions du compte ou de son fournisseur de connexion.

## Vérification développeur

```powershell
dotnet test tests/CyEngineHub.Epic.Tests/CyEngineHub.Epic.Tests.csproj -c Release
dotnet run --project tools/qa/FabLoginSmoke/FabLoginSmoke.csproj -c Release -p:SkipBuiltInExtensionCopy=true
```

Le second test nécessite Windows/WebView2. Il utilise une base et un profil
temporaires sous `artifacts`, un faux client Fab et des réponses web simulées.
Il contrôle les modes automatique et manuel, l'annulation, les erreurs,
l'absence de code dans les messages, la récupération réelle du flux WebView2,
le redimensionnement et la libération du navigateur. Des captures des panneaux
sont également produites. Aucun compte Epic réel n'est utilisé : les parcours
MFA/SSO réels restent à valider manuellement avec un compte utilisateur.
