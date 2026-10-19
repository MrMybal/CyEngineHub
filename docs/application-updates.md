# Mises à jour de CyEngineHub

CyEngineHub recherche les releases de `MrMybal/CyEngineHub` par défaut.
Le dépôt et la vérification au lancement se configurent dans **Paramètres → Avancé → Mises à jour**.
L'option **Inclure les préversions Alpha / Bêta** est activée par défaut dans une build Alpha,
et mémorisée séparément. Sans cette option, seules les releases stables sont proposées.
Les anciennes configurations sans dépôt sont initialisées avec le dépôt officiel.

## Contrat d’une release installable

- tag de version : `vMAJOR.MINOR.PATCH` ou `vMAJOR.MINOR.PATCH-alpha.N` ;
- release publiée, non brouillon ; les préversions nécessitent le canal Alpha/Bêta ;
- un asset installateur Windows `.exe` ou `.msi` ;
- une empreinte SHA-256 fournie par le champ `digest` de l’asset GitHub, ou par l’un des assets :
  - `<nom-installateur>.sha256` ;
  - `<nom-installateur>.sha256.txt` ;
  - `SHA256SUMS`, `SHA256SUMS.txt` ou `checksums.txt`.

Le fichier de sommes peut contenir la seule empreinte, ou une ligne au format :

```text
<sha256>  CyEngineHub-Setup-x64.exe
```

Sans installateur compatible ou sans SHA-256 valide, la release et ses notes restent visibles mais
l’installation automatique est bloquée.

## Déroulement

1. CyEngineHub interroge la dernière release stable (ou les 100 releases les plus récentes
   si les préversions sont activées) et choisit la version la plus élevée, pas simplement
   la dernière date. `alpha.10 > alpha.2`, et une stable est supérieure à son Alpha.
2. L’installateur est téléchargé en flux dans le dossier de données `Updates`.
3. Le fichier est relu pour calculer son SHA-256 ; une différence supprime le fichier partiel.
4. Le lanceur `CyEngineHub.Updater` est copié dans un dossier temporaire et reçoit le PID courant.
5. CyEngineHub demande son arrêt explicite, termine ses tâches et ferme ses services.
6. Le lanceur attend la disparition effective du PID, puis ouvre l’installateur vérifié.

Le lanceur refuse de démarrer l’installateur si CyEngineHub ne s’arrête pas dans les cinq minutes.
Son dernier échec est écrit dans `%TEMP%\CyEngineHub\Updater\last-error.log`.

## Préparer une publication

1. Mettre à jour `Version`, `AssemblyVersion` et `FileVersion` dans `Directory.Build.props`.
2. Ajouter les notes sous `docs/releases`. Construire les quatre adaptateurs avec
   `tools/release/build-native-adapters.ps1` dans un nouveau dossier sous `artifacts`,
   puis exécuter `tools/release/build-windows.ps1` avec `-NativeAdapterRoot` pointant
   vers ce dossier et le chemin d'Inno Setup `ISCC.exe`. Le script teste et fabrique les trois assets
   (installateur, ZIP, SHA256SUMS.txt) dans un nouveau dossier ignoré par Git.
3. Vérifier l'application empaquetée, committer les sources et créer le tag correspondant.
4. Créer une release GitHub sur ce tag, joindre les trois assets et publier.
   Par défaut, cocher **pre-release** pour une Alpha/Bêta. À la demande explicite
   du responsable, l’alpha.32 est publiée comme Release courante avec le badge
   Latest : GitHub n’autorise pas ce badge sur une préversion. Son nom Alpha et
   ses limites restent visibles ; il ne s’agit pas d’une qualification de stabilité.

Le compilateur d'installateur n'est ni embarqué ni téléchargé automatiquement par le script.
Les versions autonomes embarquent .NET ; l'updater est lui aussi autonome.
En mode `portable.flag`, préférer le ZIP pour conserver l'emplacement portable ;
l'installation automatique ouvre l'installateur Windows classique, sans effacer les données portables.
