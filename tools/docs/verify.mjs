import { readFile, readdir, stat } from 'node:fs/promises';
import { dirname, resolve, join, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
const repo=resolve(dirname(fileURLToPath(import.meta.url)),'../..');
const root=resolve(process.env.CYENGINEHUB_HELP_ROOT||join(repo,'artifacts/cydoc/help'));
const read=file=>readFile(join(root,file),'utf8');
const manifest=JSON.parse(await read('manifest.json')), report=JSON.parse(await read('migration-report.json'));
assert.equal(manifest.defaultLocale,'en'); assert.deepEqual(manifest.locales.map(l=>l.code),['en','fr']);
assert.deepEqual(report.modules,['image']);
assert.equal(report.unavailableLocalLinks.length,0,'unresolved local Markdown links');
const files=await readdir(root);
assert.deepEqual(files.filter(f=>/^media-.*\.js$/.test(f)),['media-image.js']);
for (const file of ['index.html','data.js','app-reader.js','viewer.css','app-reader.css','CYDOC-LICENSE.txt','CYENGINEHUB-LICENSE.txt']) assert((await stat(join(root,file))).size>0,file);
const html=await read('index.html'); assert(html.includes("connect-src 'none'")); assert(!/<(?:script|link)[^>]+(?:src|href)="https?:/i.test(html));
assert(!/three\.module|three\.js|WebGLRenderer/.test(await read('app-reader.js')));
assert(!/three\.module|three\.js|WebGLRenderer/.test(await read('media-image.js')));
const expected=['welcome','navigation','engines','engine-updates','git','projects','profiles','builds','library','fab','megascans','connectors','downloads','install','conversion','derivatives','plugins','editor-plugin','extensions-sdk','ai-mcp','storage','settings','app-updates','troubleshooting','glossary'];
for(const id of expected){
  const en=manifest.pages.find(p=>p.id===id&&p.locale==='en'),fr=manifest.pages.find(p=>p.id===id&&p.locale==='fr');
  assert(en&&fr,id); assert.deepEqual(en.headings.map(h=>h.id),fr.headings.map(h=>h.id),id+' anchors');
}
for(const page of manifest.pages){
  assert(!/<(?:img|script|iframe)[^>]+src="https?:/i.test(page.html),page.id+' network resource');
  assert(!/[A-Z]:\\(?:Users|_Project)\\/.test(page.source||''),'machine-specific source');
  for(const asset of page.assets||[]){ const path=resolve(root,decodeURIComponent(asset)); assert(!relative(root,path).startsWith('..'));assert((await stat(path)).isFile(),asset); }
}
assert.equal(report.sourceIndex.length,2); assert(manifest.pages.some(p=>p.kind==='source'),'public source index');
assert.equal(manifest.pages.filter(p=>expected.includes(p.id)).reduce((sum,p)=>sum+[...p.html.matchAll(/<svg role="img"/g)].length,0),18,'nine static workflows in each language');
const translations=JSON.parse(await read('translation-report.json'));
assert(!translations.missing.some(p=>expected.includes(p.id)));
if(translations.outdated.length) console.warn(`Translation review required: ${translations.outdated.map(p=>p.id).join(', ')}`);
console.log(`Verified offline CyDoc export: ${manifest.pages.length} pages, 25 bilingual chapters, image only, local resources, tracked translations.`);
