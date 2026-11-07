import { readFile, readdir, lstat } from 'node:fs/promises';
import { resolve, join, dirname, relative, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';
import assert from 'node:assert/strict';
import { root, validateCopy } from './build.mjs';
import { media } from './content.mjs';

const digest = bytes => createHash('sha256').update(bytes).digest('hex');
async function walk(path, prefix = '') {
  const paths = [];
  for (const item of await readdir(path, { withFileTypes: true })) {
    assert(!item.isSymbolicLink(), 'No output links');
    if (item.isDirectory()) paths.push(...await walk(join(path, item.name), prefix + item.name + '/'));
    else { assert(item.isFile()); paths.push(prefix + item.name); }
  }
  return paths.sort();
}
export async function verify(directory = resolve(root, 'dist/website')) {
  validateCopy();
  directory = resolve(directory);
  const inventory = JSON.parse(await readFile(join(directory, 'site-manifest.json'), 'utf8'));
  const paths = await walk(directory);
  assert.deepEqual(paths, [...Object.keys(inventory.files), 'site-manifest.json'].sort());
  for (const [name, sha] of Object.entries(inventory.files)) {
    assert(!name.split('/').includes('..') && !name.startsWith('/') && !name.includes(':'), 'Safe inventory path');
    assert.equal(digest(await readFile(join(directory, name))), sha, name + ' integrity');
    assert(!/(?:^|\/)(?:\.git|\.CyDocBuild|node_modules|profiles|sessions|testresults)(?:\/|$)/i.test(name));
    assert(!/\.(db|log|pdb|pyc|pfx|tgz|sqlite|ps1|mjs|ts)$/i.test(name), 'No authoring services, runtimes or private output');
  }
  const html = await readFile(join(directory, 'index.html'), 'utf8');
  const runtimeName = html.match(/<script src="\.\/([^"]+)" defer>/)[1];
  const cssName = html.match(/<link rel="stylesheet" href="\.\/([^"]+)">/)[1];
  const runtime = await readFile(join(directory, runtimeName), 'utf8');
  assert(!/fetch\s*\(|XMLHttpRequest|requestFullscreen|enterFullscreen|import\s*\(/.test(runtime));
  assert(!/type="module"|<iframe|<form/.test(html));
  assert.equal((html.match(/<h1\b/g) || []).length, 1);
  assert.equal((html.match(/data-shot="/g) || []).length, media.length);
  assert(html.includes('id="image-viewer"') && html.includes('<dialog'));
  assert(html.includes('fetchpriority="high" loading="eager"'));
  assert(!/<(?:script|link|img)[^>]+(?:src|href)="(?:https?:|\/\/)/i.test(html), 'No runtime CDN');
  const anchors = new Set([...html.matchAll(/\bid="([^"]+)"/g)].map(match => match[1]));
  const manual = JSON.parse(await readFile(join(directory, 'manual/DocData/manifest.json'), 'utf8'));
  const translations = JSON.parse(await readFile(join(directory, 'manual/DocData/translation-report.json'), 'utf8'));
  assert.equal(manual.version, inventory.version); assert.equal(translations.missing.length, 0); assert.equal(translations.outdated.length, 0);
  for (const [, target] of html.matchAll(/(?:href|src)="([^"]+)"/g)) {
    if (/^https:\/\//.test(target)) continue;
    assert(!target.startsWith('/'), 'References must work under a subpath');
    const [name, fragment] = target.split('#', 2);
    if (!name) { assert(anchors.has(fragment), `Missing section ${fragment}`); continue; }
    const path = resolve(directory, decodeURIComponent(name));
    assert(!relative(directory, path).startsWith('..'));
    assert((await lstat(path)).isFile(), `Missing local reference: ${target}`);
    if (name.endsWith('/Documentation.html') && fragment) {
      const [, locale, id, anchor] = fragment.split('/');
      const page = manual.pages.find(item => item.locale === locale && item.id === id);
      assert(page, `Missing manual route ${target}`);
      if (anchor) assert(page.headings.some(heading => heading.id === anchor), target);
    }
  }
  for (const [, target] of html.matchAll(/data-doc="([^"]+)"/g)) {
    const [id, anchor] = target.split('#');
    for (const locale of ['en', 'fr']) {
      const page = manual.pages.find(item => item.id === id && item.locale === locale);
      assert(page, `${locale}/${id}`); if (anchor) assert(page.headings.some(heading => heading.id === anchor));
    }
  }
  for (const name of paths.filter(name => name.startsWith('assets/'))) {
    assert(['.css', '.js', '.png', '.jpg'].includes(extname(name)), 'Selected asset type');
    const revision = name.match(/\.([a-f0-9]{12})\.[^.]+$/)?.[1];
    assert.equal(revision, digest(await readFile(join(directory, name))).slice(0, 12), 'Asset URL content revision');
  }
  const applicationManual = resolve(root, 'Documentation');
  const manualFiles = paths.filter(name => name.startsWith('manual/'));
  for (const name of manualFiles) assert.equal(digest(await readFile(join(directory, name))), digest(await readFile(join(applicationManual, name.slice('manual/'.length)))), `Manual changed: ${name}`);
  const notices = ['legal/LICENSE.txt', 'legal/Unreal-Editor-MIT.txt', 'legal/Native-Adapter-MIT.txt', 'manual/DocData/CYDOC-LICENSE.txt', 'manual/DocData/CYENGINEHUB-LICENSE.txt'];
  for (const name of notices) assert((await readFile(join(directory, name), 'utf8')).trim().length > 500);
  const report = { version: inventory.version, files: paths.length, manualFiles: manualFiles.length, translationKeys: Object.keys(JSON.parse(html.match(/<script type="application\/json" id="showcase-content">([\s\S]*?)<\/script>/)[1]).copy.en).length, css: cssName, script: runtimeName };
  console.log('Showcase verification passed: ' + JSON.stringify(report));
  return report;
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await verify(process.argv[2]);
