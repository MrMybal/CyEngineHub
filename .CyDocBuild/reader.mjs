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
  if (!pending.has(src)) pending.set(src, script('DocData/' + file, () => {
    const asset = globalThis.CYDOC_ASSETS?.[src];
    if (!asset) throw new Error(`Invalid exported asset: ${src}`);
    delete globalThis.CYDOC_ASSETS[src]; return asset;
  }).finally(() => pending.delete(src)));
  return pending.get(src);
}
const modules = Object.fromEntries(options.modules.map(name => [name, () => script(`DocData/media-${name}.js`, () => globalThis[`CyDocMedia_${name}`].default)]));
// Accept the legacy help:<id> links and the old Markdown URLs when opened locally.
const legacy = location.hash.match(/^#(?:help:|help\/)([a-z0-9-]+)(?:#(.+))?$/);
if (legacy) location.replace(`#/en/${legacy[1]}${legacy[2] ? '/' + encodeURIComponent(legacy[2]) : ''}`);
const root = document.querySelector('#app');
const firstRoute = location.hash;
let savedPosition;
try { savedPosition = JSON.parse(localStorage.getItem('cyenginehub.help.position:' + firstRoute) || 'null'); } catch { /* Optional. */ }
let savedTheme;
try { savedTheme = localStorage.getItem('cyenginehub.help.theme'); } catch { /* Storage can be disabled. */ }
mountViewer(root, data, { homePage: 'welcome', initialTheme: savedTheme || 'dark', storageId: 'cyenginehub.help.v1', assetBase: 'DocData/', modules, loadAsset });
new MutationObserver(() => { try { localStorage.setItem('cyenginehub.help.theme', root.dataset.theme); } catch { /* Optional. */ } }).observe(root, { attributes:true, attributeFilter:['data-theme'] });
// CyDoc owns document language and its new shell has no host-license badge.
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
