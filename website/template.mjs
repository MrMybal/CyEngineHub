import { copy, chapters, libraryTabs } from './content.mjs';
const escape = value => String(value).replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');
export function renderPage({ version, images, logo, stylesheet, script, destinations }) {
  const value = key => escape(copy.en[key].replaceAll('{version}', version));
  const text = (tag, key, extra = '') => `<${tag} data-i18n="${key}" ${extra}>${value(key)}</${tag}>`;
  const arrow = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12h14m-6-6 6 6-6 6"/></svg>';
  const manual = (page, key, classes = 'text-link') => `<a class="${classes}" data-doc="${page}" href="./manual/Documentation.html#/en/${page.replace('#', '/')}"><span data-i18n="${key}">${value(key)}</span>${arrow}</a>`;
  const shot = (id, priority = false) => {
    const image = images[id];
    return `<figure class="screen" data-image="${id}"><button class="shot" type="button" data-shot="${id}" aria-label="${value('viewer.open')}: ${value(`shot.${id}.caption`)}"><img src="${image.url}" alt="${value(`shot.${id}.alt`)}" data-i18n-alt="shot.${id}.alt" width="${image.width}" height="${image.height}" ${priority ? 'fetchpriority="high" loading="eager"' : 'loading="lazy"'} decoding="async"></button><figcaption><span data-i18n="shot.${id}.caption">${value(`shot.${id}.caption`)}</span><span aria-hidden="true">↗</span></figcaption></figure>`;
  };
  const source = destinations.repository ? `<a class="source-link" href="${escape(destinations.repository)}" data-i18n-label="nav.source" aria-label="${value('nav.source')}">GitHub${arrow}</a>` : '';
  const download = (name, key, classes = 'button') => destinations[name] ? `<a class="${classes}" href="${escape(destinations[name])}" data-external="${name}"><span data-i18n="${key}">${value(key)}</span>${arrow}</a>` : '';
  const faq = ['offline', 'download', 'convert', 'editor', 'language', 'license'];
  const localData = JSON.stringify({ version, copy }).replaceAll('<', '\\u003c');
  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="dark"><meta name="theme-color" content="#15171b">
<title>${value('meta.title')}</title><meta name="description" content="${value('meta.description')}"><meta property="og:title" content="${value('meta.title')}"><meta property="og:description" content="${value('meta.description')}"><meta property="og:type" content="website"><meta property="og:image" content="${images.engines.url}">
<link rel="icon" type="image/png" href="${logo}"><link rel="stylesheet" href="${stylesheet}"><script src="${script}" defer></script></head>
<body><a class="skip" href="#main" data-i18n="skip">${value('skip')}</a>
<header class="site-header"><div class="header-inner wrap">
  <a class="brand" href="#main"><img src="${logo}" width="30" height="30" alt=""><span>CyEngineHub</span></a>
  <nav class="primary-nav" id="site-navigation" data-open="false" data-i18n-label="nav.label" aria-label="${value('nav.label')}"><a href="#engines" data-i18n="nav.workflows">${value('nav.workflows')}</a>${manual('welcome', 'nav.docs', '')}<a class="mobile-download" href="#availability" data-i18n="nav.download">${value('nav.download')}</a></nav>
  <div class="header-actions">${source}<div class="language" role="group" data-i18n-label="nav.language" aria-label="${value('nav.language')}"><button type="button" data-lang="en" lang="en" aria-pressed="true">EN</button><button type="button" data-lang="fr" lang="fr" aria-pressed="false">FR</button></div><a class="button header-download" href="#availability" data-i18n="nav.download">${value('nav.download')}</a><button type="button" class="menu-button" aria-expanded="false" aria-controls="site-navigation" data-i18n-label="nav.menu" aria-label="${value('nav.menu')}"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h16"/></svg></button></div>
</div></header>
<main id="main">
<section class="hero wrap" aria-labelledby="hero-title"><div class="hero-copy">
  ${text('p', 'hero.status', 'class="eyebrow status"')}
  <h1 id="hero-title"><img src="${logo}" width="48" height="48" alt=""><span>CyEngineHub</span></h1>
  ${text('h2', 'hero.lead', 'class="hero-lead"')}${text('p', 'hero.body', 'class="lead"')}
  <div class="actions"><a class="button primary" href="#engines"><span data-i18n="hero.explore">${value('hero.explore')}</span>${arrow}</a>${manual('welcome', 'hero.docs', 'button secondary')}</div>${text('p', 'hero.note', 'class="fineprint"')}
</div><div class="hero-screen">${shot('engines', true)}</div></section>
<nav class="chapters" data-i18n-label="chapter.label" aria-label="${value('chapter.label')}"><div class="wrap">${chapters.map(id => `<a href="#${id}" data-chapter="${id}" data-i18n="chapter.${id}">${value(`chapter.${id}`)}</a>`).join('')}</div></nav>
<div class="wrap capture-note">${text('p', 'shot.note', 'class="fineprint"')}</div>

<section id="engines" class="section wrap" aria-labelledby="engines-title"><div class="section-head">${text('p', 'engines.kicker', 'class="eyebrow"')}${text('h2', 'engines.title', 'id="engines-title"')}${text('p', 'engines.body', 'class="intro-copy"')}</div>
  <div class="engine-tasks">${['identify', 'inspect', 'update'].map((id, index) => `<article><span class="task-number">0${index + 1}</span>${text('h3', `engines.${id}.title`)}${text('p', `engines.${id}.body`)}</article>`).join('')}</div><div class="section-links">${manual('engines', 'engines.link')}${manual('engine-updates', 'engines.patch')}</div>
</section>

<section id="projects" class="section section-tint" aria-labelledby="projects-title"><div class="feature-row wrap"><div class="feature-copy">${text('p', 'projects.kicker', 'class="eyebrow"')}${text('h2', 'projects.title', 'id="projects-title"')}${text('p', 'projects.body')}<ul class="feature-list">${[1, 2, 3].map(id => text('li', `projects.item${id}`)).join('')}</ul><div class="section-links">${manual('projects#required-choices', 'projects.link')}${manual('builds', 'projects.build')}</div></div>${shot('projects')}</div></section>

<section id="library" class="section wrap" aria-labelledby="library-title"><div class="section-head">${text('p', 'library.kicker', 'class="eyebrow"')}${text('h2', 'library.title', 'id="library-title"')}${text('p', 'library.body', 'class="intro-copy"')}</div>
<div class="library-explorer"><div class="library-tabs" role="tablist" data-i18n-label="library.tabs" aria-label="${value('library.tabs')}">${libraryTabs.map((item, index) => `<button type="button" role="tab" id="library-tab-${item.id}" aria-controls="library-panel-${item.id}" aria-selected="${index === 0}" tabindex="${index === 0 ? 0 : -1}" data-tab="${item.id}" data-i18n="library.tab.${item.id}">${value(`library.tab.${item.id}`)}</button>`).join('')}</div>
${libraryTabs.map((item, index) => `<div role="tabpanel" id="library-panel-${item.id}" aria-labelledby="library-tab-${item.id}" tabindex="0" ${index === 0 ? '' : 'hidden'}><div>${text('h3', `library.${item.id}.title`)}${text('p', `library.${item.id}.body`)}</div><div><ul class="feature-list">${[1, 2].map(id => text('li', `library.${item.id}.item${id}`)).join('')}</ul>${manual(item.doc, `library.${item.id}.link`)}</div></div>`).join('')}</div>
${text('p', 'library.warning', 'class="callout"')}
<div class="workflow"><p class="eyebrow" data-i18n="flow.label">${value('flow.label')}</p><ol>${['source', 'method', 'validate', 'install'].map(id => text('li', `flow.${id}`)).join('')}</ol></div>
</section>

<section id="conversion" class="section section-tint" aria-labelledby="conversion-title"><div class="wrap"><div class="feature-row reverse"><div class="feature-copy">${text('p', 'conversion.kicker', 'class="eyebrow"')}${text('h2', 'conversion.title', 'id="conversion-title"')}${text('p', 'conversion.body')}${manual('conversion', 'conversion.link')}</div>${shot('conversion')}</div>
<div class="two-up text-pair">${['source', 'native'].map(id => `<article>${text('h3', `conversion.${id}.title`)}${text('p', `conversion.${id}.body`)}</article>`).join('')}</div>${text('p', 'conversion.limit', 'class="callout"')}${manual('derivatives', 'conversion.derivatives')}</div></section>

<section id="plugins" class="section wrap" aria-labelledby="plugins-title"><div class="plugin-layout"><div class="feature-copy">${text('p', 'plugins.kicker', 'class="eyebrow"')}${text('h2', 'plugins.title', 'id="plugins-title"')}${text('p', 'plugins.body')}<ul class="feature-list">${[1, 2, 3].map(id => text('li', `plugins.item${id}`)).join('')}</ul>${manual('editor-plugin', 'plugins.link')}</div>
<div class="connection-diagram"><p class="eyebrow" data-i18n="plugins.diagram">${value('plugins.diagram')}</p><div class="connection-node"><img src="${logo}" width="40" height="40" alt="">${text('strong', 'plugins.hub')}</div><div class="connection-line"><span aria-hidden="true">↕</span>${text('span', 'plugins.pipe')}</div><div class="connection-node">${text('strong', 'plugins.editor')}</div></div></div>
<div class="feature-row sdk-row"><div class="feature-copy">${text('p', 'sdk.kicker', 'class="eyebrow"')}${text('h2', 'sdk.title')}${text('p', 'sdk.body')}<div class="section-links">${manual('extensions-sdk', 'sdk.link')}${manual('ai-mcp', 'sdk.agent')}</div></div>${shot('sdk')}</div>
</section>

<section id="documentation" class="section section-tint" aria-labelledby="docs-title"><div class="wrap"><div class="feature-row reverse"><div class="feature-copy">${text('p', 'docs.kicker', 'class="eyebrow"')}${text('h2', 'docs.title', 'id="docs-title"')}${text('p', 'docs.body')}${text('p', 'docs.local')}${manual('welcome', 'docs.open', 'button primary')}${text('p', 'docs.language', 'class="fineprint"')}</div>${shot('help')}</div><div class="doc-links">${manual('projects', 'docs.projects')}${manual('install', 'docs.install')}${manual('troubleshooting', 'docs.trouble')}</div></div></section>

<section id="availability" class="section wrap" aria-labelledby="availability-title"><div class="availability-box">${text('p', 'availability.kicker', 'class="eyebrow"')}${text('h2', 'availability.title', 'id="availability-title"')}${text('p', 'availability.body')}<div class="actions">${download('installer', 'availability.installer', 'button primary')}${download('archive', 'availability.zip', 'button secondary')}${download('release', 'availability.notes', 'text-link')}</div>${text('p', 'availability.note', 'class="fineprint"')}${text('p', 'availability.portable', 'class="fineprint"')}</div>
<div class="faq">${text('h2', 'faq.title')}${faq.map(id => `<details>${text('summary', `faq.${id}.q`)}${text('p', `faq.${id}.a`)}</details>`).join('')}</div></section>
</main>
<footer class="site-footer"><div class="wrap"><div class="footer-top"><div><a class="brand" href="#main"><img src="${logo}" width="30" height="30" alt=""><span>CyEngineHub</span></a>${text('p', 'footer.description')}</div><nav aria-label="CyEngineHub">${manual('welcome', 'footer.docs', '')}${source}</nav></div><div class="legal-links"><a href="./legal/LICENSE.txt" data-i18n="footer.license">${value('footer.license')}</a><a href="./legal/Unreal-Editor-MIT.txt" data-i18n="footer.plugins">${value('footer.plugins')}</a><a href="./legal/Native-Adapter-MIT.txt" data-i18n="footer.adapter">${value('footer.adapter')}</a></div>${text('p', 'footer.independent', 'class="fineprint"')}</div></footer>
<dialog id="image-viewer" aria-labelledby="viewer-caption" data-i18n-label="viewer.label" aria-label="${value('viewer.label')}"><div class="viewer-bar"><span id="viewer-count" aria-live="polite"></span><button class="button" type="button" data-viewer-close><span aria-hidden="true">×</span><span data-i18n="viewer.close">${value('viewer.close')}</span></button></div><img id="viewer-image" alt=""><div class="viewer-bottom"><button type="button" class="icon-button" data-viewer-prev data-i18n-label="viewer.previous" aria-label="${value('viewer.previous')}">←</button><p id="viewer-caption"></p><button type="button" class="icon-button" data-viewer-next data-i18n-label="viewer.next" aria-label="${value('viewer.next')}">→</button></div></dialog>
<script type="application/json" id="showcase-content">${localData}</script>
</body></html>`;
}
