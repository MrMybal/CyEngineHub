import { readFile, writeFile, mkdir, readdir, cp, rm, stat } from 'node:fs/promises';
import { resolve, relative, dirname, basename, extname, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
const here = dirname(fileURLToPath(import.meta.url)), repo = resolve(here, '../..');
const dependency = resolve(process.argv[2]), stage = resolve(repo, 'artifacts/cydoc'), output = join(stage, 'help');
const read = path => readFile(path, 'utf8');
const hash = text => createHash('sha256').update(text).digest('hex');
const slug = value => value.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
const slash = value => value.replaceAll('\\', '/');
async function walk(root) {
  return (await Promise.all((await readdir(root, { withFileTypes: true })).filter(e => !['bin','obj','.git','node_modules','artifacts','__pycache__'].includes(e.name)).map(async e => e.isDirectory() ? walk(join(root,e.name)) : [join(root,e.name)]))).flat().sort();
}
const topics = ['welcome','navigation','engines','engine-updates','git','projects','profiles','builds','library','fab','megascans','connectors','downloads','install','conversion','derivatives','plugins','editor-plugin','extensions-sdk','ai-mcp','storage','settings','app-updates','troubleshooting','glossary'];
const sdk = ['src/CyEngineHub.Fab/Conversion/AssetSourceConverters.cs','src/CyEngineHub.Fab/Installation/FabInstallationScriptModels.cs'];
const documentation = [...await walk(join(repo,'docs')), ...await walk(join(repo,'examples')), ...await walk(join(repo,'extensions'))].filter(f => /\.md$/i.test(f));
const references = [...documentation.filter(f => !slash(relative(repo,f)).startsWith('docs/manual/en/') && !slash(relative(repo,f)).startsWith('docs/manual/fr/')), ...['README.md','README.fr.md','LICENSING.md','THIRD_PARTY_NOTICES.md'].map(f=>join(repo,f))];
const examples = (await walk(join(repo,'examples'))).filter(f=>/\.(json|cs|cpp|h|py|ps1|js|ts)$/i.test(f));
const schemaFiles = (await walk(join(repo,'extensions'))).filter(f=>/schema[^/\\]*\.json$/i.test(f));
const licenses=['LICENSE','extensions/unreal/CyEngineHub/LICENSE','extensions/builtin/cyenginehub.unreal-version-bridge/native-source/CyHubNativeBridge/LICENSE'].map(f=>join(repo,f));
const inputs = [...new Set([...documentation,...references,...examples,...schemaFiles,...licenses,...sdk.map(f=>join(repo,f)),...await walk(here),join(repo,'Directory.Build.props'),...await walk(join(repo,'docs/screenshots'))])].sort();
const fingerprint = hash((await Promise.all(inputs.map(async file=>slash(relative(repo,file))+'\n'+hash(await readFile(file))))).join('\n'));
if (!process.argv.includes('--force') && await read(join(output,'input.sha256')).catch(()=>null) === fingerprint) {
  await import(pathToFileURL(join(here,'verify.mjs')).href); console.log('CyEngineHub documentation is current.'); process.exit(0);
}
// Only these generated, validated descendants are replaced; original documentation
// and the private dependency checkout are never deleted or rewritten.
for (const name of ['content','assets','source-sdk','help']) {
  const target=resolve(stage,name);
  if(dirname(target)!==stage || relative(repo,target).startsWith('..'))throw new Error('Generated cleanup escaped its owned staging directory');
  await rm(target,{recursive:true,force:true});
}
for (const locale of ['en','fr']) await mkdir(join(stage,'content',locale),{recursive:true});
await mkdir(join(stage,'assets/screenshots'),{recursive:true});
await cp(join(repo,'docs/screenshots'),join(stage,'assets/screenshots'),{recursive:true});
const report = { dependency:JSON.parse(await read(join(here,'dependency.json'))), inputs:[], untranslatedReferences:[], unavailableLocalLinks:[], modules:['image'], sourceIndex:sdk, historical:[] };
const tracking=JSON.parse(await read(join(here,'manual-map.json')));
// Reviewed original languages, not guessed from the selected reader locale.
const frenchReferences=new Set(['ai-mcp','application-updates','complex-downgrade-validation-2026-09-11','derived-packages','fab-login','inter-engine-validation-2026-09-11','interface-audit-2026-09-08','marketplace-connectors','mcp-options','megascan-collections','megascans-fab-native-library','megascans-via-fab','native-57-bridge-validation','native-texture-bridge','persistent-downloads','project-creation-profiles','single-instance','source-converters','unreal-asset-conversion-profiles','unreal-validation-2026-09-10']);
const originalLanguages={};
const pagePaths = new Map();
for (const locale of ['en','fr']) for (const id of topics) pagePaths.set(resolve(repo,`docs/manual/${locale}/${id}.md`),id);
for (const file of references) pagePaths.set(file, basename(file)==='README.fr.md' ? 'reference-readme' : 'reference-'+slug(relative(repo,file).replace(/\.md$/i,'')));
pagePaths.set(join(repo,'README.md'),'reference-readme');
for (const file of [...examples,...schemaFiles,...licenses]) pagePaths.set(file,'example-'+slug(relative(repo,file)));
for (const file of references.filter(file=>slash(file).includes('/examples/')&&basename(file)==='README.md'))pagePaths.set(dirname(file),pagePaths.get(file));
const anchors = new Map();
for (const file of [...pagePaths.keys()].filter(file=>!references.some(ref=>dirname(ref)===file))) {
  const text = await read(file);
  anchors.set(file,[...text.matchAll(/^#{1,6}\s+(.+)$/gm)].map(m=>slug(m[1])));
}
async function savePage(locale,id,title,section,order,body,revision,sourceRevision) {
  const fm = {id,title:title.replaceAll('\n',' '),description:title.replaceAll('\n',' '),section,order,revision,...(sourceRevision?{sourceRevision}:{})};
  await writeFile(join(stage,'content',locale,id+'.md'),'---\n'+Object.entries(fm).map(([k,v])=>`${k}: ${v}`).join('\n')+'\n---\n'+body+'\n');
}
function adaptLinks(body,file) {
  return body.replace(/(!?)\[([^\]\n]*)\]\(([^\n)]+)\)/g,(full,image,label,href)=>{
    if (href.startsWith('help:')) return `[${label}](doc:${href.slice(5)})`;
    if (/^(?:https?:|mailto:|doc:|#)/.test(href)) {
      // Do not fetch remote README badges/media while reading offline.
      return image ? `${label} (external illustration: \`${href}\`)` : full;
    }
    if (href.startsWith('image:')) return full;
    const [path,anchor] = href.replace(/^<|>$/g,'').split('#');
    const target = resolve(dirname(file),decodeURIComponent(path));
    const id = pagePaths.get(target);
    if (id) return `[${label}](doc:${id}${anchor ? '#'+anchor : ''})`;
    const screenshot = path.replaceAll('\\','/').match(/(?:^|\/)screenshots\/([a-z-]+)\.png$/);
    if (image && screenshot) return `![${label}](assets/screenshots/${screenshot[1]}.png)`;
    report.unavailableLocalLinks.push({source:slash(relative(repo,file)),target:href});
    return `${label} (\`${href}\`; source/research resource, not included in the offline reader)`;
  });
}
for (let i=0;i<topics.length;i++) {
  const id=topics[i], englishFile=join(repo,`docs/manual/en/${id}.md`), en=await read(englishFile), revision=hash(en.replaceAll('\r\n','\n')).slice(0,12);
  const sectionIndex = i<2?0:i<8?1:i<16?2:i<20?3:i<23?4:5;
  for (const locale of ['en','fr']) {
    const file=join(repo,`docs/manual/${locale}/${id}.md`), original=await read(file), headingIds=tracking[id].anchors;
    let heading=0, flow=0;
    let body=adaptLinks(original.replaceAll('\r\n','\n'),file).replace(/^#{1,6}\s+(.+)$/gm,(line)=>`${line} {#${headingIds[heading++]}}`);
    if (heading!==headingIds.length) throw new Error(`Translation headings differ: ${locale}/${id}`);
    body=body.replace(/!\[([^\]]*)\]\(image:([a-z-]+)\)/g,(_,alt,image)=>'```image\n'+JSON.stringify({id:image,src:`assets/screenshots/${image}.png`,alt,controls:'minimal'})+'\n```');
    body=body.replace(/:::flow\n([\s\S]*?)\n:::/g,(_,steps)=>{
      const labels=steps.trim().split('\n'), width=Math.max(400,...labels.map(l=>l.length*8+40));
      return '```diagram\n'+JSON.stringify({type:'flow',title:locale==='fr'?'Étapes du processus':'Workflow steps',nodes:labels.map((label,j)=>({id:`step-${j}`,label,x:20,y:20+j*110,width})),edges:labels.slice(1).map((_,j)=>({from:`step-${j}`,to:`step-${j+1}`}))})+'\n```';
    });
    const section=[['Getting started','Bien démarrer'],['Engines and projects','Moteurs et projets'],['Assets and libraries','Assets et bibliothèques'],['Plugins and automation','Plugins et automatisation'],['Settings and maintenance','Réglages et maintenance'],['Reference','Référence']][sectionIndex][locale==='fr'?1:0];
    await savePage(locale,id,original.split('\n')[0].replace(/^# /,'').trim(),section,i,body,revision,locale==='fr'?tracking[id].frenchSourceRevision:null);
    report.inputs.push({file:slash(relative(repo,file)),sha256:hash(original),page:id,locale});
  }
}
for (const file of references) {
  const original=await read(file), id=pagePaths.get(file), locale=basename(file)==='README.fr.md'?'fr':'en';
  const originalLanguage=frenchReferences.has(basename(file,'.md')) || (slash(file).includes('/docs/releases/')&&!['v2.0.0-alpha.30.md','v2.0.0-alpha.38.md'].includes(basename(file))) ? 'fr' : locale;
  if(id!=='reference-readme')originalLanguages[id]=originalLanguage;
  const historical=/releases[\\/]|validation|feasibility|audit|roadmap|docs[\\/]ai-mcp\.md/.test(file);
  const title=(original.match(/^#\s+(.+)$/m)?.[1]||basename(file)).trim();
  const banner=locale==='fr'?'Référence préservée dans sa langue d’origine. Les captures et résultats historiques ne décrivent pas nécessairement la version actuelle.':'Preserved reference in its original language (English or French). Historical results and screenshots are not a guarantee of current capabilities. This page has not been newly translated.';
  let body=adaptLinks(original,file);
  // Old diagrams in engineering notes remain verbatim code, never runtime scripts.
  body=`> ${banner}\n\n${body}`;
  await savePage(locale,id,title,historical?'History / Historique':'Technical references / Références',300+references.indexOf(file),body,hash(original).slice(0,12));
  report.inputs.push({file:slash(relative(repo,file)),sha256:hash(original),page:id,locale,originalLanguage});
  report.untranslatedReferences.push(id); if(historical)report.historical.push(id);
}
for (const file of [...examples,...schemaFiles,...licenses]) {
  const original=await read(file), id=pagePaths.get(file), extension=extname(file).slice(1);
  await savePage('en',id,slash(relative(repo,file)),licenses.includes(file)?'Licenses':'Examples and schemas',600+examples.indexOf(file),`Original distributable example, schema or license. Its original language is preserved.\n\n\`\`\`${extension||'text'}\n${original}\n\`\`\``,hash(original).slice(0,12));
  report.inputs.push({file:slash(relative(repo,file)),sha256:hash(original),page:id,locale:'en'});
}
for (const file of sdk) {
  const destination=join(stage,'source-sdk',file); await mkdir(dirname(destination),{recursive:true}); await cp(join(repo,file),destination);
}
const version=(await read(join(repo,'Directory.Build.props'))).match(/<Version>([^<]+)/)?.[1]||'development';
const config={name:'CyEngineHub',version,defaultLocale:'en',locales:[{code:'en',label:'English'},{code:'fr',label:'Français'}],homePage:'welcome',content:'content',assets:'assets',out:'help',modules:['image'],sources:['source-sdk'],sourceIndex:{enabled:true,includeDirs:['source-sdk']},aliases:{overview:'welcome',quickstart:'welcome',installation:'install','engine-update':'engine-updates'},translationReport:true};
await writeFile(join(stage,'cydoc.config.json'),JSON.stringify(config,null,2));
execFileSync(process.execPath,[join(dependency,'tools/cli.mjs'),'build',join(stage,'cydoc.config.json')],{stdio:'inherit'});
const {build}=await import(pathToFileURL(join(dependency,'node_modules/esbuild/lib/main.js')).href);
await build({entryPoints:[join(here,'reader.mjs')],bundle:true,format:'iife',minify:true,target:'es2020',outfile:join(output,'app-reader.js'),alias:{'cydoc/viewer':join(dependency,'dist/viewer.js')},define:{CYENGINEHUB_REFERENCE_LANGUAGES:JSON.stringify(originalLanguages)}});
await cp(join(here,'reader.css'),join(output,'app-reader.css'));
await cp(join(dependency,'LICENSE'),join(output,'CYDOC-LICENSE.txt'));
await cp(join(repo,'LICENSE'),join(output,'CYENGINEHUB-LICENSE.txt'));
await writeFile(join(output,'index.html'),`<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta http-equiv="Content-Security-Policy" content="default-src 'self' file:; script-src 'self' file:; style-src 'self' file: 'unsafe-inline'; img-src 'self' file: data: blob:; connect-src 'none'; media-src 'none'; object-src 'none'; frame-src 'none'; base-uri 'none'"><title>CyEngineHub · Documentation</title><link rel="stylesheet" href="viewer.css"><link rel="stylesheet" href="app-reader.css"><script src="data.js" defer></script><script src="app-reader.js" defer></script></head><body><div id="app"></div></body></html>`);
// The standard export's demonstration bootstrap is replaced by our public-API
// entry point, not shipped as a second unused implementation.
await rm(join(output,'viewer.js'));
await writeFile(join(output,'migration-report.json'),JSON.stringify(report,null,2));
await writeFile(join(output,'input.sha256'),fingerprint);
await import(pathToFileURL(join(here,'verify.mjs')).href);
