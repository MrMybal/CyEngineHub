// Stage only the checked generated export; never app profiles or authoring inputs.
import {readFile,lstat,mkdir,rm,cp,writeFile,realpath} from 'node:fs/promises';
import {resolve,relative,dirname,join} from 'node:path';
import assert from 'node:assert/strict';
await import('./verify.mjs');
const work=import.meta.dirname,source=resolve(work,'../Documentation'),target=resolve(work,'.cache/pages');
const manifest=JSON.parse(await readFile(join(source,'DocData/manifest.json'),'utf8'));
const files=new Set(['Documentation.html','DocAI.md','index.html',...['data.js','manifest.json','translation-report.json','viewer.js','viewer.css','app-reader.css','media-image.js','media-gallery.js','legacy-route.js','CYDOC-LICENSE.txt','CYENGINEHUB-LICENSE.txt'].map(f=>'DocData/'+f)]);
for(const asset of [manifest.logo,...manifest.pages.flatMap(p=>p.assets||[])]){
 assert.match(asset,/^assets\/[a-zA-Z0-9_./%~-]+$/);assert(!decodeURIComponent(asset).split('/').includes('..'));
 files.add('DocData/'+decodeURIComponent(asset));
}
assert.equal(dirname(target),resolve(work,'.cache'));
await rm(target,{recursive:true,force:true});await mkdir(target,{recursive:true});
for(const name of files){
 const input=resolve(source,name);assert(!relative(source,input).startsWith('..'));
 assert((await lstat(input)).isFile()&&!(await lstat(input)).isSymbolicLink());assert(!relative(await realpath(source),await realpath(input)).startsWith('..'));
 const output=join(target,name);await mkdir(dirname(output),{recursive:true});await cp(input,output);
 assert((await readFile(output)).equals(await readFile(input)));
}
await writeFile(join(target,'.nojekyll'),'');
console.log(`Pages staging: ${files.size} byte-identical exported files + .nojekyll. No application data or documentation tooling.`);
