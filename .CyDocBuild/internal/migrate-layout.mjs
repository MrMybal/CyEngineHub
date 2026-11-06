// One-time mechanical move of application-owned authoring inputs. Not shipped.
import { mkdir, readFile, writeFile, rename, cp, readdir } from 'node:fs/promises';
import { resolve, join } from 'node:path';
const root=resolve(import.meta.dirname,'../..'), work=join(root,'.CyDocBuild');
const tracking=JSON.parse(await readFile(join(root,'tools/docs/manual-map.json'),'utf8'));
const groups=[['welcome','navigation'],['engines','engine-updates','git','projects','profiles','builds'],['library','fab','megascans','connectors','downloads','install','conversion','derivatives'],['plugins','editor-plugin','extensions-sdk','ai-mcp'],['storage','settings','app-updates'],['troubleshooting','glossary']];
const sections=[['Getting started','Bien démarrer'],['Engines and projects','Moteurs et projets'],['Assets and libraries','Assets et bibliothèques'],['Plugins and automation','Plugins et automatisation'],['Settings and maintenance','Réglages et maintenance'],['Reference','Référence']];
const parents={navigation:'welcome','engine-updates':'engines',git:'engines',profiles:'projects',builds:'projects',fab:'library',megascans:'library',connectors:'library',downloads:'library',install:'library',conversion:'install',derivatives:'conversion','editor-plugin':'plugins','extensions-sdk':'plugins','ai-mcp':'extensions-sdk',storage:'settings','app-updates':'settings'};
const descriptions={
welcome:['Set up your workspace and learn how to use the manual.','Préparez votre espace de travail et apprenez à utiliser le manuel.'],
navigation:['Find catalogs, return to detail sheets and understand task states.','Retrouvez les catalogues et les fiches, et comprenez les états des tâches.'],
engines:['Identify engine installations, rename labels and manage associations.','Identifiez les installations, renommez leurs libellés et gérez les associations.'],
'engine-updates':['Apply an official Unreal patch and understand its disk requirements.','Appliquez un patch Unreal officiel et comprenez ses besoins disque.'],
git:['Inspect branches, check remotes and fetch without replacing your work.','Inspectez les branches et récupérez les références sans remplacer votre travail.'],
projects:['Choose Blueprint or C++, select a folder and follow initial setup.','Choisissez Blueprint ou C++, un dossier et suivez la préparation initiale.'],
profiles:['Compose new-project plugins, feature packs and INI settings.','Composez les plugins, packs de fonctionnalités et réglages INI du nouveau projet.'],
builds:['Generate IDE files, build targets and prepare packaging presets.','Générez les fichiers IDE, compilez des cibles et préparez les presets de packaging.'],
library:['Browse assets, inspect exact variants and understand local packages.','Parcourez les assets, leurs variantes précises et leurs packages locaux.'],
fab:['Connect Epic/Fab, choose the correct variant and check asset updates.','Connectez Epic/Fab, choisissez la bonne variante et vérifiez les mises à jour.'],
megascans:['Distinguish historical Quixel content from direct and web Fab sources.','Distinguez les contenus Quixel historiques des sources Fab directes et web.'],
connectors:['Download entitled files and register complete kits from marketplace plugins.','Téléchargez les fichiers autorisés et enregistrez des kits complets par marketplace.'],
downloads:['Set transfer limits, resume supported downloads and diagnose waiting tasks.','Réglez les limites de transfert, reprenez les téléchargements et analysez les attentes.'],
install:['Select targets, variants, scripts and conflict options before installation.','Choisissez les cibles, variantes, scripts et options de conflit avant installation.'],
conversion:['Convert source formats or Unreal versions while preserving originals.','Convertissez les formats sources ou versions Unreal en conservant les originaux.'],
derivatives:['Keep a prepared revision and reuse it without changing the supplier download.','Conservez et réutilisez une révision préparée sans modifier le téléchargement original.'],
plugins:['Inspect, install and rebuild engine or project plugins.','Inspectez, installez et recompilez les plugins de moteur ou de projet.'],
'editor-plugin':['Connect Unreal Editor to the hub and install into its open project.','Reliez Unreal Editor au hub et installez dans son projet ouvert.'],
'extensions-sdk':['Install hub extensions and implement preparation or conversion steps.','Installez des extensions du hub et implémentez des étapes de préparation ou conversion.'],
'ai-mcp':['Connect an AI client, review permissions and follow assisted operations.','Connectez un client IA, examinez ses permissions et suivez les opérations assistées.'],
storage:['Read partial usage figures, plan quotas and safely clean generated data.','Lisez les mesures partielles, réglez les quotas et nettoyez les données générées.'],
settings:['Configure appearance, accounts, marketplaces and application preferences.','Configurez l’apparence, les comptes, marketplaces et préférences de l’application.'],
'app-updates':['Check published releases and decide when to install a hub update.','Vérifiez les versions publiées et choisissez quand mettre le hub à jour.'],
troubleshooting:['Identify common failures and recover without losing original data.','Identifiez les échecs courants et récupérez sans perdre les données originales.'],
glossary:['Understand package terminology, privacy boundaries and component licenses.','Comprenez le vocabulaire, la confidentialité et les licences des composants.']};
let order=0;
for(let group=0;group<groups.length;group++)for(const id of groups[group]){
 for(const [i,locale] of ['en','fr'].entries()){
  const old=join(root,'docs/manual',locale,id+'.md'), dest=join(work,'manual',locale,id+'.md');
  await mkdir(join(work,'manual',locale),{recursive:true});
  let text=(await readFile(old,'utf8')).replaceAll('\r\n','\n'), heading=0;
  const title=text.split('\n')[0].replace(/^# /,'');
  text=text.replace(/^#{1,6}\s+.+$/gm,line=>`${line} {#${tracking[id].anchors[heading++]}}`).replaceAll('(help:','(doc:');
  if(heading!==tracking[id].anchors.length)throw Error('Heading mismatch '+id);
  text=text.replace(/!\[([^\]]*)\]\(image:([a-z-]+)\)/g,(_,alt,image)=>'```image\n'+JSON.stringify({id:image,src:`assets/screenshots/${image}.png`,alt,controls:'minimal'})+'\n```');
  text=text.replace(/:::flow\n([\s\S]*?)\n:::/g,(_,body)=>{
   const labels=body.trim().split('\n'), width=Math.max(400,...labels.map(l=>l.length*8+40));
   return '```diagram\n'+JSON.stringify({type:'flow',title:i?'Étapes du processus':'Workflow steps',nodes:labels.map((label,j)=>({id:`step-${j}`,label,x:20,y:20+j*110,width})),edges:labels.slice(1).map((_,j)=>({from:`step-${j}`,to:`step-${j+1}`}))})+'\n```';
  });
  const metadata={id,title,description:descriptions[id][i],section:sections[group][i],...(parents[id]?{parent:parents[id]}:{}),order,...(i?{sourceRevision:2}:{revision:2})};
  await rename(old,dest);
  await writeFile(dest,'---\n'+Object.entries(metadata).map(([k,v])=>`${k}: ${v}`).join('\n')+'\n---\n'+text);
 }
 order++;
}
await mkdir(join(work,'assets/screenshots'),{recursive:true});
for(const name of ['engines','project-creation','source-conversion'])await cp(join(root,'docs/screenshots',name+'.png'),join(work,'assets/screenshots',name+'.png'));
await cp(join(root,'assets/branding/cyenginehub-logo.png'),join(work,'assets/app-logo.png'));
await mkdir(join(work,'.cache/packages'),{recursive:true});
await cp(join(root,'artifacts/cydoc-0.1.0.tgz'),join(work,'.cache/packages/cydoc-0.1.0.tgz'));
for(const name of await readdir(join(root,'tools/docs')))await rename(join(root,'tools/docs',name),join(work,name));
await rename(join(root,'docs/cydoc-integration.md'),join(work,'internal/previous-integration.md'));
await rename(join(root,'docs/manual/README.md'),join(work,'internal/previous-manual-readme.md'));
console.log('Moved 50 bilingual chapters, tools, 3 screenshots and branding into .CyDocBuild.');
