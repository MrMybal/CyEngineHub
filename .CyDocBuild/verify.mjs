import {readFile,readdir,stat} from 'node:fs/promises';
import {resolve,join,relative} from 'node:path';
import assert from 'node:assert/strict';
import {createDocumentation,hasAnchor,parseLink} from 'cydoc';
const work=import.meta.dirname, repo=resolve(work,'..');
const root=resolve(process.env.CYENGINEHUB_HELP_ROOT||join(repo,'Documentation'));
const read=file=>readFile(join(root,file),'utf8');
const config=JSON.parse(await readFile(join(work,'cydoc.config.json'),'utf8'));
const manifest=JSON.parse(await read('DocData/manifest.json')),docs=createDocumentation(manifest);
assert.equal(manifest.defaultLocale,'en');assert.deepEqual(manifest.locales.map(l=>l.code),['en','fr']);
assert.deepEqual(config.modules,['image','gallery']);
const dataFiles=await readdir(join(root,'DocData'));
assert.deepEqual(dataFiles.filter(f=>/^media-.*\.js$/.test(f)).sort(),['media-gallery.js','media-image.js']);
for(const file of ['Documentation.html','DocAI.md','DocData/data.js','DocData/viewer.js','DocData/viewer.css','DocData/app-reader.css','DocData/CYDOC-LICENSE.txt','DocData/CYENGINEHUB-LICENSE.txt'])assert((await stat(join(root,file))).size>0,file);
const html=await read('Documentation.html');assert(html.includes("connect-src 'none'"));assert(!/<(?:script|link)[^>]+(?:src|href)="https?:/i.test(html));assert(!/<iframe|http-equiv="refresh"/.test(html));
const ids=[...new Set(manifest.pages.map(p=>p.id))];
for(const id of ids){
 const en=docs.resolve(id,'en'),fr=docs.resolve(id,'fr');
 assert(en&&!en.fallback&&fr&&!fr.fallback,id+' complete translations');
 assert.deepEqual(en.page.headings.map(h=>h.id),fr.page.headings.map(h=>h.id),id+' shared anchors');
 assert.equal(en.page.parent,fr.page.parent,id+' parent');
 assert.notEqual(en.page.text,fr.page.text,id+' genuinely distinct translation');
}
assert(manifest.pages.filter(p=>p.parent).length>=34,'Real parent/child topic navigation');
for(const page of manifest.pages){
 assert(!page.id.startsWith('reference-')&&!page.id.startsWith('example-')&&page.id!=='translation-report','No maintenance/history pages');
 assert(page.kind!=='source','No source browsing');
 assert(!/Preserved reference|Référence préservée|translation is not yet available|Source Files|SourceFile|CYENGINEHUB_REFERENCE_LANGUAGES/.test(page.text));
 assert(!/<(?:img|script|iframe)[^>]+src="https?:/i.test(page.html),page.id+' local media');
 for(const asset of page.assets||[]){const path=resolve(root,'DocData',decodeURIComponent(asset));assert(!relative(join(root,'DocData'),path).startsWith('..'));assert((await stat(path)).isFile(),asset);}
 for(const href of page.links||[]){if(!href.startsWith('#/'))continue;const route=docs.canonicalRoute(parseLink(href,'en','welcome'));const target=docs.resolve(route.pageId,route.locale);assert(target&&!target.fallback,href);if(route.anchor)assert(hasAnchor(target.page,route.anchor),href);}
}
for(const file of ['DocData/viewer.js','DocData/media-image.js','DocData/media-gallery.js'])assert(!/WebGLRenderer|three\.module|three\.js/.test(await read(file)),'No 3D dependency');
const translations=JSON.parse(await read('DocData/translation-report.json'));assert.equal(translations.missing.length,0);assert.equal(translations.outdated.length,0);
const serialized=JSON.stringify(manifest);
assert(!serialized.includes('public sealed class AssetSourceConverterRegistry'),'No implementation body');
assert(!serialized.includes('ReadPackageEngineVersions'),'Unannotated implementation remains private');
assert(!manifest.pages.some(p=>/DocAI|migration-report|internal\//i.test(p.id+' '+p.title)),'AI/work records are not human content');
assert.equal(manifest.pages.reduce((sum,p)=>sum+[...p.html.matchAll(/<svg role="img"/g)].length,0),18,'Nine static workflows per language');
assert.equal(manifest.pages.filter(p=>p.html.includes('data-cydoc-module="gallery"')).length,2,'Bilingual workspace gallery');
async function walk(path){return(await Promise.all((await readdir(path,{withFileTypes:true})).map(e=>e.isDirectory()?walk(join(path,e.name)):[join(path,e.name)]))).flat();}
for(const path of await walk(root))assert(!/\.(cs|cpp|py|ps1|ts|tgz)$/i.test(path)&&!path.includes('node_modules')&&!path.includes('.CyDocBuild'),'No toolchain/source tree distributed');
console.log(`Verified ${manifest.pages.length} pages (${ids.length} complete EN/FR pairs), shared anchors, nested navigation, API-only references, local image/gallery resources and no internal pages.`);
