// Host integration through CyDoc's public viewer API. The exporter supplies the
// data, module bundles and asset-provider catalog; no fetch/file XHR is needed.
import { mountViewer } from 'cydoc/viewer';
const data = globalThis.CYDOC_DATA, options = globalThis.CYDOC_OPTIONS;
const pending = new Map();
function script(src, read) {
  return new Promise((resolve, reject) => {
    const element = document.createElement('script'); element.src = src;
    element.onload = () => { try { resolve(read()); } catch (error) { reject(error); } finally { element.remove(); } };
    element.onerror = () => { element.remove(); reject(new Error(`Missing local documentation resource: ${src}`)); };
    document.head.append(element);
  });
}
function loadAsset(src) {
  const file = options.assets?.[src];
  if (!file) return Promise.reject(new Error(`Asset not exported: ${src}`));
  if (!pending.has(src)) pending.set(src, script(file, () => {
    const asset = globalThis.CYDOC_ASSETS?.[src];
    if (!asset) throw new Error(`Invalid exported asset: ${src}`);
    delete globalThis.CYDOC_ASSETS[src]; return asset;
  }).finally(() => pending.delete(src)));
  return pending.get(src);
}
const modules = Object.fromEntries(options.modules.map(name => [name, () => script(`media-${name}.js`, () => globalThis[`CyDocMedia_${name}`].default)]));
// Accept the legacy help:<id> links and the old Markdown URLs when opened locally.
const legacy = location.hash.match(/^#(?:help:|help\/)([a-z0-9-]+)(?:#(.+))?$/);
if (legacy) location.replace(`#/en/${legacy[1]}${legacy[2] ? '/' + encodeURIComponent(legacy[2]) : ''}`);
const root = document.querySelector('#app');
const firstRoute = location.hash;
let savedPosition;
try { savedPosition = JSON.parse(localStorage.getItem('cyenginehub.help.position:' + firstRoute) || 'null'); } catch { /* Optional. */ }
let savedTheme;
try { savedTheme = localStorage.getItem('cyenginehub.help.theme'); } catch { /* Storage can be disabled. */ }
mountViewer(root, data, { homePage: 'welcome', initialTheme: savedTheme || 'dark', storageId: 'cyenginehub.help.v1', modules, loadAsset });
new MutationObserver(() => { try { localStorage.setItem('cyenginehub.help.theme', root.dataset.theme); } catch { /* Optional. */ } }).observe(root, { attributes:true, attributeFilter:['data-theme'] });
// Upstream's default shell labels all host content MIT. Only the reader is MIT;
// the application's GPLv3 content retains its actual license.
function licenseNotice() {
  const french = location.hash.startsWith('#/fr/');
  const pageId=decodeURIComponent(location.hash.split('/')[2]?.split('?')[0]||'welcome');
  const originalLanguage=CYENGINEHUB_REFERENCE_LANGUAGES[pageId];
  const article=root.querySelector('main article');
  if(article)article.lang=originalLanguage||(french?'fr':'en');
  if(originalLanguage==='fr') {
    const fallback=root.querySelector('main > .callout');
    const message=french?'Référence originale en français ; la traduction anglaise n’est pas encore disponible.':'Original French reference; an English translation is not yet available.';
    if(fallback&&fallback.textContent!==message)fallback.textContent=message;
  }
  const notice = root.querySelector('.footer > div > span');
  const text = french ? 'CyEngineHub : GPLv3 · Lecteur CyDoc : MIT' : 'CyEngineHub: GPLv3 · CyDoc reader: MIT';
  if (notice && notice.textContent !== text) notice.textContent = text;
  const badge = root.querySelector('.mit-tag'); if (badge && badge.textContent !== 'GPLv3 / MIT') { badge.textContent = 'GPLv3 / MIT'; badge.title = 'See the bundled license notices'; }
  document.documentElement.lang = french ? 'fr' : 'en';
}
new MutationObserver(licenseNotice).observe(root, { childList: true, subtree: true });
licenseNotice();
// CyDoc preserves reading views during article navigation. This host additionally
// retains the initial page's scroll position across closing/reopening its window.
let restoreTimer;
let restoring = false;
function stopRestoring() { clearInterval(restoreTimer); restoring=false; }
window.addEventListener('hashchange',stopRestoring);
if (savedPosition && !location.hash.split('?')[0].split('/')[3] && !/[?&]media=/.test(location.hash)) {
  restoring = true;
  let attempts=0;
  restoreTimer=setInterval(()=>{
    if(location.hash!==firstRoute) { stopRestoring(); return; }
    window.scrollTo({left:savedPosition.left||0,top:savedPosition.top||0,behavior:'instant'});
    // Let CyDoc finish its own lazy-media restoration before yielding ownership.
    if (++attempts>20) { clearInterval(restoreTimer); restoring=false; }
  },100);
  for(const event of ['wheel','pointerdown','touchstart','keydown']) window.addEventListener(event,stopRestoring,{once:true,passive:true});
}
let saveTimer;
function savePosition() {
  if(restoring)return;
  try { localStorage.setItem('cyenginehub.help.position:'+location.hash,JSON.stringify({top:window.scrollY,left:window.scrollX})); } catch { /* Optional. */ }
}
window.addEventListener('scroll',()=>{
  clearTimeout(saveTimer); saveTimer=setTimeout(savePosition,100);
},{passive:true});
window.addEventListener('pagehide',savePosition);
// External links are user-initiated only. No external media or automatic request
// is permitted by the export CSP or the native host's resource policy.
