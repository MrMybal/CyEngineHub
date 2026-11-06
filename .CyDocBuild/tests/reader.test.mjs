// DOM-level regression fixtures, not a substitute for native visual/browser QA.
import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {Window} from 'happy-dom';
import {createDocumentation,hasAnchor,parseLink} from 'cydoc';
const work=new URL('../',import.meta.url), out=new URL('../../Documentation/',import.meta.url);
const manifest=JSON.parse(await readFile(new URL('DocData/manifest.json',out),'utf8'));
const docs=createDocumentation(manifest);
test('Every published page is fully translated with stable sections and a valid parent',()=>{
 for(const en of manifest.pages.filter(p=>p.locale==='en')){
  const fr=docs.resolve(en.id,'fr');assert(fr&&!fr.fallback);assert.equal(en.parent,fr.page.parent);
  assert.deepEqual(en.headings.map(h=>h.id),fr.page.headings.map(h=>h.id));
  assert.notEqual(en.text,fr.page.text);
 }
});
test('All old human/contextual page identifiers have valid canonical destinations',async()=>{
 const config=JSON.parse(await readFile(new URL('cydoc.config.json',work),'utf8'));
 for(const alias of Object.keys(config.aliases)){const route=docs.canonicalRoute({locale:'fr',pageId:alias});assert(docs.resolve(route.pageId,'fr'));}
 for(const [id,anchor] of [['projects','required-choices'],['settings','appearance-and-language'],['settings','connections']])assert(hasAnchor(docs.resolve(id,'fr').page,anchor));
 assert(docs.resolve('projects','fr').page.html.includes('data-cydoc-id="project-creation"'));
});
test('Native F1/help bindings target only existing chapters, sections and media',async()=>{
 const vm=await readFile(new URL('../../src/CyEngineHub.Desktop/ViewModels/MainWindowViewModel.Documentation.cs',import.meta.url),'utf8');
 for(const match of vm.matchAll(/OpenDocumentation\("([a-z0-9-]+)"(?:, "([a-z0-9-]+)")?(?:, "([a-z0-9-]+)")?\)/g))for(const locale of ['en','fr']){
  const page=docs.resolve(match[1],locale)?.page;assert(page,match[1]);if(match[2])assert(hasAnchor(page,match[2]));if(match[3])assert(page.html.includes('data-cydoc-id="'+match[3]+'"'));
 }
 const main=await readFile(new URL('../../src/CyEngineHub.Desktop/Views/MainWindow.axaml.cs',import.meta.url),'utf8');
 assert(main.includes('Key.F1')&&main.includes('ShowContextualDocumentationCommand.Execute(null)'));
 assert(main.includes('Path.Combine(AppContext.BaseDirectory, "Documentation")'));
});
test('SDK API contains signatures and imports without implementation bodies',()=>{
 for(const locale of ['en','fr'])for(const id of ['api-source-converter','api-installation-script']){
  const page=docs.resolve(id,locale).page;for(const anchor of ['signature','parameters','example'])assert(hasAnchor(page,anchor));
  assert(page.text.includes('using CyEngineHub.Fab.'));assert(page.text.includes('CancellationToken'));
 }
 assert(!manifest.pages.some(p=>p.kind==='source'||p.text.includes('public sealed class AssetSourceConverterRegistry')));
 assert(!docs.search('migration-report','en').length);
 assert(!docs.search('DocAI','fr').length);
});
test('Generated bootstrap mounts the current reader, hierarchy, search, theme and both locales in a DOM fixture',async()=>{
 const window=new Window({url:'https://manual.fixture.invalid/Documentation.html#/en/engines',settings:{enableJavaScriptEvaluation:true,disableJavaScriptFileLoading:true,disableCSSFileLoading:true}});
 try{
  window.document.body.innerHTML='<div id="app"></div>';
  window.eval(await readFile(new URL('DocData/data.js',out),'utf8'));
  window.eval(await readFile(new URL('DocData/viewer.js',out),'utf8'));
  await new Promise(resolve=>setTimeout(resolve,30));
  const document=window.document,root=document.querySelector('#app');
  assert.equal(document.querySelector('h1').textContent,'Engines and associations');
  assert.equal(root.dataset.theme,'dark');
  assert(document.querySelector('.header-search input'),'Global search is in the header');
  assert(document.querySelector('.side-nav details'),'Nested native disclosure navigation');
  assert(!document.querySelector('.sidebar-card,.side-footer,.mit-tag'),'No obsolete status/license cards');
  document.dispatchEvent(new window.KeyboardEvent('keydown',{key:'k',ctrlKey:true,bubbles:true}));
  assert(document.querySelector('dialog.search-dialog').open,'Ctrl+K');
  const input=document.querySelector('dialog.search-dialog input');input.value='plugin';input.dispatchEvent(new window.Event('input',{bubbles:true}));
  assert(document.querySelectorAll('.search-result').length>0);
  document.querySelector('dialog.search-dialog').close();
  const select=document.querySelector('select[aria-label="Documentation language"]');assert(select);
  select.value='fr';select.dispatchEvent(new window.Event('change',{bubbles:true}));
  await new Promise(resolve=>setTimeout(resolve,30));
  assert.equal(document.querySelector('h1').textContent,'Moteurs et associations');
  assert.equal(document.documentElement.lang,'fr');
  const theme=document.querySelector('[data-set-theme="paper"]');assert(theme);theme.click();assert.equal(root.dataset.theme,'paper');
  assert(document.querySelector('.brand img').getAttribute('src').includes('DocData/assets/app-logo.png'));
 }finally{await window.happyDOM.close();}
});
test('Reader assets, media modules and gallery labels are locally complete',async()=>{
 for(const page of manifest.pages)for(const asset of page.assets||[])assert((await readFile(new URL('DocData/'+asset,out))).length>0);
 for(const locale of ['en','fr']){
  const page=docs.resolve('welcome',locale).page;
  assert(page.html.includes('data-cydoc-id="workspace-gallery"'));assert(page.html.includes('description'));
 }
 const css=await readFile(new URL('DocData/viewer.css',out),'utf8');assert(css.includes('--sidebar-width:330px'),'Sidebar is 25% wider than the previous 264px');
 assert(css.includes('tooltip'));assert(css.includes('prefers-reduced-motion'));
 const html=await readFile(new URL('Documentation.html',out),'utf8');assert(html.includes("connect-src 'none'"));assert(!html.includes('<iframe'));
});
test('Image and gallery modules mount with local asset URLs, item switching and in-window enlargement in a DOM fixture',async()=>{
 const window=new Window({url:'https://manual.fixture.invalid/Documentation.html#/fr/welcome?media=workspace-gallery',settings:{enableJavaScriptEvaluation:true,disableJavaScriptFileLoading:true,disableCSSFileLoading:true}});
 try{
  const scripts=new Map(await Promise.all(['image','gallery'].map(async name=>['DocData/media-'+name+'.js',await readFile(new URL('DocData/media-'+name+'.js',out),'utf8')])));
  const document=window.document;document.body.innerHTML='<div id="app"></div>';
  const append=document.head.append.bind(document.head);
  document.head.append=(...nodes)=>{for(const node of nodes){const src=node.getAttribute?.('src');if(scripts.has(src))setTimeout(()=>{window.eval(scripts.get(src));node.onload?.(new window.Event('load'));},0);else append(node);}};
  window.eval(await readFile(new URL('DocData/data.js',out),'utf8'));window.eval(await readFile(new URL('DocData/viewer.js',out),'utf8'));
  await new Promise(resolve=>setTimeout(resolve,60));
  const gallery=document.querySelector('[data-cydoc-id="workspace-gallery"]');assert(gallery);
  assert(gallery.querySelector('img'),gallery.outerHTML);
  assert(gallery.querySelector('img').getAttribute('src').startsWith('DocData/assets/'));
  const tabs=gallery.querySelectorAll('[role="tab"]');assert.equal(tabs.length,3);tabs[1].click();
  await new Promise(resolve=>setTimeout(resolve,30));
  assert(gallery.querySelector('.image-layer img').getAttribute('src').includes('project-creation.png'));
  const expand=gallery.querySelector('.gallery-content [data-action="expand"]');assert(expand);expand.click();
  assert(gallery.querySelector('.media-expanded'));assert(!document.fullscreenElement);
  document.dispatchEvent(new window.KeyboardEvent('keydown',{key:'Escape',bubbles:true}));assert(!gallery.querySelector('.media-expanded'));
 }finally{await window.happyDOM.close();}
});
