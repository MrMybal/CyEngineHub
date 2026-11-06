import { readFile, writeFile, mkdir, cp, rm, readdir, stat, lstat } from 'node:fs/promises';
import { resolve, dirname, join, relative } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
const work=import.meta.dirname, repo=resolve(work,'..'), output=join(repo,'Documentation');
const require=createRequire(import.meta.url), packageRoot=dirname(require.resolve('cydoc/compiler'));
const read=file=>readFile(file,'utf8');
async function walk(root){return (await Promise.all((await readdir(root,{withFileTypes:true})).map(e=>e.isDirectory()?walk(join(root,e.name)):[join(root,e.name)]))).flat().sort();}
const config=JSON.parse(await read(join(work,'cydoc.config.json')));
const sdk=['src/CyEngineHub.Fab/Conversion/AssetSourceConverters.cs','src/CyEngineHub.Fab/Installation/FabInstallationScriptModels.cs'];
const inputs=[...await walk(join(work,'manual')),...await walk(join(work,'assets')),...['cydoc.config.json','dependency.json','generate.mjs','reader.mjs','reader.css','reader.html','legacy-index.html','legacy-route.js','verify.mjs','package-lock.json'].map(f=>join(work,f)),...sdk.map(f=>join(repo,f)),join(repo,'Directory.Build.props')];
async function fingerprint(){return createHash('sha256').update((await Promise.all(inputs.map(async f=>relative(repo,f)+':'+createHash('sha256').update(await readFile(f)).digest('hex')))).join('\n')).digest('hex');}
const cache=join(work,'.cache');await mkdir(cache,{recursive:true});
if(!process.argv.includes('--force')&&await read(join(cache,'input.sha256')).catch(()=>null)===await fingerprint()&&await stat(join(output,'Documentation.html')).catch(()=>null)){
 await import('./verify.mjs');console.log('Documentation is current.');process.exit(0);
}
// Replace only this explicitly owned generated tree, never an arbitrary path.
if(dirname(output)!==repo||relative(repo,output)!=='Documentation')throw Error('Unsafe documentation output');
if(await lstat(output).catch(()=>null)){
 if((await lstat(output)).isSymbolicLink())throw Error('Documentation output must not be a link');
 const old=JSON.parse(await read(join(output,'DocData/manifest.json')));
 if(old.name!=='CyEngineHub'||old.schemaVersion!==1)throw Error('Refusing to replace an unrecognized documentation tree');
 const owned=new Set(['Documentation.html','DocAI.md','index.html',...['data.js','manifest.json','translation-report.json','viewer.js','viewer.css','app-reader.css','media-image.js','media-gallery.js','legacy-route.js','CYDOC-LICENSE.txt','CYENGINEHUB-LICENSE.txt'].map(f=>'DocData/'+f),...[old.logo,...old.pages.flatMap(p=>p.assets||[])].filter(Boolean).map(asset=>'DocData/'+decodeURIComponent(asset))]);
 for(const file of await walk(output))if((await lstat(file)).isSymbolicLink()||!owned.has(relative(output,file).replaceAll('\\','/')))throw Error('Unrecognized file in Documentation; preserve it outside the generated tree before rebuilding');
}
await rm(output,{recursive:true,force:true});
await mkdir(join(cache,'sdk'),{recursive:true});
for(const source of sdk)await cp(join(repo,source),join(cache,'sdk',source.split('/').at(-1)));
const version=(await read(join(repo,'Directory.Build.props'))).match(/<Version>([^<]+)/)?.[1]||config.version;
await writeFile(join(work,'cydoc.config.json'),JSON.stringify({...config,version},null,2)+'\n');
// Use the CLI in the locally packed/installed library, not repository-private imports.
execFileSync(process.execPath,[join(packageRoot,'cli.mjs'),process.argv.includes('--check')?'check':'build',join(work,'cydoc.config.json')],{stdio:'inherit'});
const {build}=await import('esbuild');
// Public-API embedding adds application persistence/CSP, not a forked reader UI.
await build({entryPoints:[join(work,'reader.mjs')],bundle:true,format:'iife',minify:true,target:'es2020',outfile:join(output,'DocData/viewer.js')});
await cp(join(work,'reader.html'),join(output,'Documentation.html'));
await cp(join(work,'reader.css'),join(output,'DocData/app-reader.css'));
await cp(join(work,'node_modules/cydoc/LICENSE'),join(output,'DocData/CYDOC-LICENSE.txt'));
await cp(join(repo,'LICENSE'),join(output,'DocData/CYENGINEHUB-LICENSE.txt'));
await cp(join(work,'legacy-index.html'),join(output,'index.html'));
await cp(join(work,'legacy-route.js'),join(output,'DocData/legacy-route.js'));
await import('./verify.mjs');
await writeFile(join(cache,'input.sha256'),await fingerprint());
