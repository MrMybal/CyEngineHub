# Documentation maintenance (not reader content)

The human manual is exclusively `../manual/en` and `../manual/fr`. Keep audits,
release histories and AI/build instructions here, never in those content roots.
Existing engineering notes elsewhere in the repository are deliberately not
auto-imported. `Documentation/DocAI.md` is CyDoc's separate generated AI sidecar.

CyDoc 0.1.0 is locked to Git revision
`6ddf9a582defb2f11fbb3f992afef6f98dcadc93`. Its documented distribution is a
locally built `npm pack` archive, installed with the application's lockfile.
No registry package named cydoc is assumed. `.cache` and `node_modules` are local
build dependencies; neither is distributed. The optional Three.js peer is absent
from the application's documentation install and reader. The dependency checkout's
development/demo build uses its own lockfile, separately from the installed package.

From the application root:

```powershell
./.CyDocBuild/build.ps1
./.CyDocBuild/build.ps1 -Check
node .CyDocBuild/verify.mjs
node .CyDocBuild/prepare-pages.mjs
```

`-Check` regenerates the export; it is not read-only. The generator owns and replaces
only the explicitly validated `Documentation/` output. Edit sources and regenerate,
never repair generated scripts/manifests. SDK annotations are extracted from exactly
two allowlisted C# files copied to `.cache/sdk`; apiIndex emits documented symbols
and lexical imports, never implementations. XML doc comments are not a C# parser.

The public-API embedding uses the current unmodified reader/modules and local
asset providers, with an application CSP, dark/amber default and retained position
storage. It does not reintroduce removed CyDoc sidebar cards or license badges.
Image and gallery are selected for three actual application captures. Nine paired
workflows compile to SVG without graph; no video, GIF or mesh content is required.

Desktop copies the complete `Documentation` directory. F1/context help resolves
manifest-backed IDs through `DocumentationLocation`. Pages stages the exact export
under `.cache/pages` and uses the same generator as the application. `index.html`
is only a compatibility redirect for previously published root links; the actual
reader is `Documentation.html`. Existing page IDs and explicit anchors are stable;
old internal/reference URLs resolve to relevant current human chapters via aliases.

English/French prose, media captions and explicit anchors are paired. Bump the
original revision on semantic change and update sourceRevision only after review.
CyDoc reports extracted API references as untracked (annotations have no revision
tag), so review both annotation blocks together when changing their contract.

Do not publish without the user's request. A local update does not change the
already deployed GitHub Pages site. Do not touch actual engine installations while
testing documentation; use fictional demonstration data or a disposable help profile.
