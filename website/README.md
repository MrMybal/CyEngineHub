# CyEngineHub showcase

Maintained static presentation sources, separate from the human CyDoc manual.
English is the default; `?lang=fr` selects the complete French edition. The site
uses the real CyEngineHub identity, three existing desktop demo captures and two
unretouched captures of the actual CyDoc reader. Capture provenance is recorded
in `content.mjs`; no application UI is fabricated or generated.

## Build

Use Node.js **22+**. No showcase npm dependency, Git repository or runtime API is
required. First generate the application's genuine bilingual manual if needed:

```powershell
.\.CyDocBuild\build.ps1 -Check
node website/build.mjs
node website/verify.mjs
node --test website/tests/*.test.mjs
```

The existing CyDoc generator has its own locked dependency/build requirements.
The showcase builder itself uses only Node.js built-ins and an existing
`Documentation/` export. It does not fetch release metadata or use Git.

Generated output: `dist/website/index.html`. Open it directly through `file://`,
or copy the **whole** `dist/website/` folder elsewhere. Keep `assets/`, `legal/`
and `manual/` with it. The actual reader remains at
`manual/Documentation.html#/en/welcome` or `#/fr/welcome`; every copied manual
file is verified byte-for-byte against the application export. `DocAI.md` remains
a separate sidecar, not a showcase or manual navigation page.

The builder refuses to replace an unrelated output directory or one containing
unowned files. `--out artifacts/new-output` is available for isolated QA. Styles,
scripts, logo and screenshots receive content-hashed names. Do not hand-edit
the generated output or CyDoc bundles/manifests.

## Loopback preview

```powershell
node website/preview.mjs --port 4789 --base /showcase/
```

Open `http://127.0.0.1:4789/showcase/` or
`http://127.0.0.1:4789/showcase/?lang=fr`. The server binds only to loopback,
serves only generated output and stops with Ctrl+C. `--port 0` chooses a free
port. The non-root mount exercises static subpath compatibility; no history
rewrite is required. Local preview responses use `Cache-Control: no-store`.

## Content and interaction checks

- `content.mjs`: complete matching EN/FR copy, media sources/provenance and
  verified optional GitHub/download destinations. These reuse the repository and
  alpha.45 assets already linked by the application READMEs.
- `template.mjs`, `styles.css`, `main.js`: English prerender, responsive graphite
  and amber presentation, chapter navigation, library tabs, FAQ and in-page
  image dialog. No native fullscreen API, external fonts or runtime content fetch.
- `verify.mjs`: generated local references, translations, manual routes,
  byte-identical resources, licenses and content-hashed assets.
- `tests/`: translation, build/output ownership, cache revisions, no-Git and
  static-server regression tests. These do not replace real browser checks.

Review 1280×720, 1366×768 and 1920×1080 in EN/FR at the top of the page; 360/390 px
and tablet widths; increased text/zoom; menu/tabs/FAQ; modal focus, Escape,
keyboard navigation and scroll restoration. Keep QA images/reports under ignored
`artifacts/`, not in the public presentation or the human manual. Do not call a
DOM fixture or HTTP response a visual/file-protocol verification.

## Hosting and scope

This task prepares a local artifact. **No push, deployment, release or GitHub
Pages workflow change is performed by these commands.** The existing Pages
documentation and all its old deep links remain unchanged.

On a future explicitly requested publication, host the complete generated folder
under an agreed path without replacing the existing documentation reader. For
production hosting, revalidate HTML separately from long-lived immutable caching
of hashed resources; a new asset hash cannot refresh stale HTML by itself.
External GitHub/download actions are supplemental and need a connection; viewing
the showcase and its bundled reader does not.

The showcase's original code/content is GPL-3.0-only with the application. Its
output includes the Hub license, separate MIT plugin/adapter notices and the
unchanged CyDoc reader notices. This does not relicense dependencies or assets.
