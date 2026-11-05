# Bundled CyDoc manual / Manuel CyDoc embarqué

**Help / Documentation** opens the local welcome page. **F1** and the header's
help button open the guide for the current application screen. The reader is a
separate owned window; closing it leaves the selected project/product untouched.
English is the default, French covers the same 25 user chapters, and help language
is independent of the application's partial interface localization.

**Aide / Documentation** ouvre l’accueil ; **F1** ouvre l’aide contextuelle.
Le lecteur CyDoc est distribué dans Help et s’ouvre en `file://`, sans serveur,
CDN, compte ni accès réseau. Sur Windows il utilise le WebView2 déjà référencé ;
le bouton Navigateur permet d’ouvrir les mêmes fichiers hors ligne sans WebView2.
La fermeture de l’aide ne change ni la fiche ouverte ni les tâches en cours.

- [English starting page](en/welcome.md)
- [Accueil français](fr/welcome.md)
- [Generation, integration, limits and verification](../cydoc-integration.md)

## Authoring

Keep the UTF-8 chapter files in en/fr with the same stable filenames and matching
heading structure. `tools/docs/manual-map.json` fixes their section IDs and the
English revision last reviewed for each French translation. Do not update its
`frenchSourceRevision` automatically after an English-only edit.

The migration adapter accepts existing `help:<id>` links, standalone
`![caption](image:<id>)` screenshots and `:::flow` blocks. It converts them into
CyDoc `doc:` links, minimal image blocks and accessible static SVG diagrams.
Only image is enabled; no graph/mesh/Three.js dependency ships. References,
history, examples, schemas and the explicitly allowlisted SDK sources are also
included in the reader. Missing technical translations and unavailable research
artifacts are reported, not invented. Source Markdown remains the authoritative
content; do not hand-edit generated data.js or manifest.json.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/docs/build.ps1
node tools/docs/verify.mjs
dotnet run --project tools/qa/CyDocSmoke -c Release
dotnet run --project tools/qa/UsabilitySmoke -c Release -- --manual-only
```

The Desktop build/publish automatically generates and copies the complete Help
tree. The release ZIP gate checks every exported file by SHA-256. Do not retain
the former ImGui manual renderer or embedded chapter resources in parallel.
CyUI DocumentViews remains used by product descriptions, independently of help.

Keep procedures aligned with current code, including configurable MCP permissions,
partial provider support, explicit force limits, permanent Safe Clean deletion,
and local builds versus published releases. Screenshots use fictional demo data;
never capture private accounts or asset libraries for public documentation.
