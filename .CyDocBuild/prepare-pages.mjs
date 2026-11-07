// Stage only the checked generated export; never app profiles or authoring inputs.
import {readFile,lstat,mkdir,rm,cp,writeFile,realpath,readdir} from 'node:fs/promises';
import {resolve,relative,dirname,join} from 'node:path';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {verify as verifyShowcase} from '../website/verify.mjs';
await import('./verify.mjs');
const work=import.meta.dirname,source=resolve(work,'../Documentation'),target=resolve(work,'.cache/pages');
const showcase=resolve(work,'../dist/website');
await verifyShowcase(showcase);
const inventory=JSON.parse(await readFile(join(showcase,'site-manifest.json'),'utf8'));
const manifest=JSON.parse(await readFile(join(source,'DocData/manifest.json'),'utf8'));
const files=new Set(['Documentation.html','DocAI.md','index.html',...['data.js','manifest.json','translation-report.json','viewer.js','viewer.css','app-reader.css','media-image.js','media-gallery.js','legacy-route.js','CYDOC-LICENSE.txt','CYENGINEHUB-LICENSE.txt'].map(f=>'DocData/'+f)]);
for(const asset of [manifest.logo,...manifest.pages.flatMap(p=>p.assets||[])]){
 assert.match(asset,/^assets\/[a-zA-Z0-9_./%~-]+$/);assert(!decodeURIComponent(asset).split('/').includes('..'));
 files.add('DocData/'+decodeURIComponent(asset));
}
async function copyDeclared(inputRoot,outputRoot,names){for(const name of names){
 const source=inputRoot,target=outputRoot;
 const input=resolve(source,name);assert(!relative(source,input).startsWith('..'));
 assert((await lstat(input)).isFile()&&!(await lstat(input)).isSymbolicLink());assert(!relative(await realpath(source),await realpath(input)).startsWith('..'));
 const output=join(target,name);await mkdir(dirname(output),{recursive:true});await cp(input,output);
 assert((await readFile(output)).equals(await readFile(input)));
}}
assert.equal(dirname(target),resolve(work,'.cache'));
const previous=await lstat(target).catch(error=>{if(error.code==='ENOENT')return null;throw error;});
assert(!previous?.isSymbolicLink(),'Refusing a symlink as the Pages staging directory');
await rm(target,{recursive:true,force:true});await mkdir(target,{recursive:true});
await copyDeclared(source,target,files);
const showcaseFiles=[...Object.keys(inventory.files),'site-manifest.json'];
await copyDeclared(showcase,join(target,'showcase'),showcaseFiles);
await writeFile(join(target,'.nojekyll'),'');
await verifyShowcase(join(target,'showcase'));
async function walk(path,prefix=''){const names=[];for(const entry of await readdir(path,{withFileTypes:true})){
 assert(!entry.isSymbolicLink());if(entry.isDirectory())names.push(...await walk(join(path,entry.name),prefix+entry.name+'/'));
 else {assert(entry.isFile());names.push(prefix+entry.name);}
}return names.sort();}
assert.deepEqual(await walk(target),[...files,...showcaseFiles.map(name=>'showcase/'+name),'.nojekyll'].sort(),'Only declared public exports may be uploaded');
for(const [name,sha] of Object.entries(inventory.files))assert.equal(createHash('sha256').update(await readFile(join(target,'showcase',name))).digest('hex'),sha);
console.log(`Pages staging: ${files.size} byte-identical reader files + ${showcaseFiles.length} verified showcase files + .nojekyll. No application data or documentation tooling.`);
