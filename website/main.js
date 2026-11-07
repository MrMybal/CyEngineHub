(() => {
  'use strict';
  const { copy, version } = JSON.parse(document.getElementById('showcase-content').textContent);
  const root = document.documentElement;
  const menu = document.querySelector('.menu-button');
  const navigation = document.getElementById('site-navigation');
  const dialog = document.getElementById('image-viewer');
  const picture = document.getElementById('viewer-image');
  const caption = document.getElementById('viewer-caption');
  const shots = [...document.querySelectorAll('[data-shot]')];
  const tabButtons = [...document.querySelectorAll('[data-tab]')];
  const chapterLinks = [...document.querySelectorAll('[data-chapter]')];
  let locale = 'en';
  let origin;
  let position = { x: 0, y: 0 };
  let shotIndex = 0;
  let scheduled = false;
  root.classList.add('js');

  function t(key, values = {}) {
    return copy[locale][key].replace(/\{([a-z]+)\}/g, (whole, name) => String(({ version, ...values })[name] ?? whole));
  }
  function documentationUrl(target) {
    const [page, anchor] = target.split('#');
    return `./manual/Documentation.html#/${locale}/${encodeURIComponent(page)}${anchor ? '/' + encodeURIComponent(anchor) : ''}`;
  }
  function readLocale() {
    const url = new URL(location.href);
    locale = url.searchParams.get('lang') === 'fr' ? 'fr' : 'en';
    if (url.searchParams.has('lang') && !['en', 'fr'].includes(url.searchParams.get('lang'))) {
      url.searchParams.set('lang', 'en');
      history.replaceState(null, '', url);
    }
  }
  function localize() {
    root.lang = locale;
    document.title = t('meta.title');
    document.querySelector('meta[name="description"]').content = t('meta.description');
    document.querySelector('meta[property="og:title"]').content = t('meta.title');
    document.querySelector('meta[property="og:description"]').content = t('meta.description');
    document.querySelectorAll('[data-i18n]').forEach(node => { node.textContent = t(node.dataset.i18n); });
    document.querySelectorAll('[data-i18n-label]').forEach(node => { node.setAttribute('aria-label', t(node.dataset.i18nLabel)); });
    document.querySelectorAll('[data-i18n-alt]').forEach(node => { node.alt = t(node.dataset.i18nAlt); });
    document.querySelectorAll('[data-doc]').forEach(node => { node.href = documentationUrl(node.dataset.doc); });
    document.querySelectorAll('[data-lang]').forEach(node => { node.setAttribute('aria-pressed', String(node.dataset.lang === locale)); });
    shots.forEach(node => { node.setAttribute('aria-label', `${t('viewer.open')}: ${t(`shot.${node.dataset.shot}.caption`)}`); });
    if (dialog.open) showShot(shotIndex);
    queueChapterUpdate();
  }
  function setLocale(value) {
    const url = new URL(location.href);
    locale = value === 'fr' ? 'fr' : 'en';
    url.searchParams.set('lang', locale);
    history.replaceState(null, '', url);
    localize();
  }
  function closeMenu(restoreFocus = false) {
    const wasOpen = menu.getAttribute('aria-expanded') === 'true';
    menu.setAttribute('aria-expanded', 'false');
    navigation.dataset.open = 'false';
    if (restoreFocus && wasOpen) menu.focus();
  }
  function chooseTab(id, focus = false) {
    tabButtons.forEach(button => {
      const active = button.dataset.tab === id;
      button.setAttribute('aria-selected', String(active));
      button.tabIndex = active ? 0 : -1;
      document.getElementById(button.getAttribute('aria-controls')).hidden = !active;
      if (active && focus) button.focus();
    });
  }
  function showShot(index) {
    shotIndex = (index + shots.length) % shots.length;
    const shot = shots[shotIndex];
    const image = shot.querySelector('img');
    picture.src = image.src;
    picture.alt = t(`shot.${shot.dataset.shot}.alt`);
    caption.textContent = t(`shot.${shot.dataset.shot}.caption`);
    document.getElementById('viewer-count').textContent = t('viewer.count', { current: shotIndex + 1, total: shots.length });
  }
  function openViewer(button) {
    origin = button;
    position = { x: scrollX, y: scrollY };
    showShot(shots.indexOf(button));
    root.classList.add('viewer-open');
    dialog.showModal();
    dialog.querySelector('[data-viewer-close]').focus();
  }
  function closeViewer() { dialog.close(); }
  dialog.addEventListener('close', () => {
    root.classList.remove('viewer-open');
    picture.removeAttribute('src');
    window.scrollTo({ left: position.x, top: position.y, behavior: 'instant' });
    origin?.focus({ preventScroll: true });
  });
  dialog.addEventListener('cancel', event => { event.preventDefault(); closeViewer(); });
  dialog.addEventListener('keydown', event => {
    if (event.key === 'Tab') {
      const controls = [...dialog.querySelectorAll('button')];
      const index = controls.indexOf(document.activeElement);
      event.preventDefault();
      controls[(index + (event.shiftKey ? -1 : 1) + controls.length) % controls.length].focus();
    }
    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      event.preventDefault(); showShot(shotIndex + (event.key === 'ArrowRight' ? 1 : -1));
    }
  });
  dialog.addEventListener('click', event => {
    if (event.target !== dialog) return;
    const bounds = dialog.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) closeViewer();
  });
  dialog.querySelector('[data-viewer-close]').addEventListener('click', closeViewer);
  dialog.querySelector('[data-viewer-prev]').addEventListener('click', () => showShot(shotIndex - 1));
  dialog.querySelector('[data-viewer-next]').addEventListener('click', () => showShot(shotIndex + 1));
  document.addEventListener('click', event => {
    const target = event.target instanceof Element ? event.target : null;
    const language = target?.closest('[data-lang]');
    if (language) setLocale(language.dataset.lang);
    const shot = target?.closest('[data-shot]');
    if (shot) openViewer(shot);
    const tab = target?.closest('[data-tab]');
    if (tab) chooseTab(tab.dataset.tab);
    if (target?.closest('#site-navigation a')) closeMenu();
    if (!target?.closest('.site-header')) closeMenu();
  });
  menu.addEventListener('click', () => {
    const expanded = menu.getAttribute('aria-expanded') !== 'true';
    menu.setAttribute('aria-expanded', String(expanded));
    navigation.dataset.open = String(expanded);
  });
  document.querySelector('[role="tablist"]').addEventListener('keydown', event => {
    if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
    event.preventDefault();
    let index = tabButtons.findIndex(button => button.getAttribute('aria-selected') === 'true');
    if (event.key === 'Home') index = 0;
    else if (event.key === 'End') index = tabButtons.length - 1;
    else index = (index + (event.key === 'ArrowRight' ? 1 : -1) + tabButtons.length) % tabButtons.length;
    chooseTab(tabButtons[index].dataset.tab, true);
  });
  document.addEventListener('keydown', event => { if (event.key === 'Escape' && !dialog.open) closeMenu(true); });
  function updateChapter() {
    scheduled = false;
    const limit = document.querySelector('.site-header').offsetHeight + document.querySelector('.chapters').offsetHeight + 40;
    let current;
    for (const link of chapterLinks) if (document.getElementById(link.dataset.chapter).getBoundingClientRect().top <= limit) current = link;
    chapterLinks.forEach(link => { if (link === current) link.setAttribute('aria-current', 'true'); else link.removeAttribute('aria-current'); });
  }
  function queueChapterUpdate() { if (!scheduled) { scheduled = true; requestAnimationFrame(updateChapter); } }
  window.addEventListener('scroll', queueChapterUpdate, { passive: true });
  window.addEventListener('resize', queueChapterUpdate);
  window.addEventListener('hashchange', queueChapterUpdate);
  window.addEventListener('popstate', () => { readLocale(); localize(); });
  readLocale(); localize();
})();
