// Publish an allowlisted subset of the actual CyDoc export, not the repository,
// app data, build cache, browser profile, Markdown source tree or native binaries.
import { readFile, lstat, mkdir, rm, cp, writeFile, readdir, realpath } from 'node:fs/promises';
import { dirname, resolve, relative, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
await import('./verify.mjs');
const repo = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const source = resolve(repo, 'artifacts/cydoc/help'), target = resolve(repo, 'artifacts/pages');
const manifest = JSON.parse(await readFile(join(source, 'manifest.json'), 'utf8'));
const files = new Set(['index.html', 'app-reader.js', 'app-reader.css', 'viewer.css', 'data.js',
  'manifest.json', 'translation-report.json', 'media-image.js', 'CYDOC-LICENSE.txt', 'CYENGINEHUB-LICENSE.txt']);
for (const page of manifest.pages) for (const asset of page.assets || []) {
  assert.match(asset, /^assets\/[a-zA-Z0-9_./%~-]+$/);
  const decoded = decodeURIComponent(asset);
  assert(!decoded.split('/').includes('..'), 'Asset escapes documentation export');
  files.add(decoded);
}
assert.equal(dirname(target), resolve(repo, 'artifacts'), 'Publication staging must remain an owned artifact directory');
await rm(target, { recursive: true, force: true });
await mkdir(target, { recursive: true });
for (const name of files) {
  const input = resolve(source, name);
  assert(!relative(source, input).startsWith('..'));
  assert((await lstat(input)).isFile() && !(await lstat(input)).isSymbolicLink(), 'Only regular exported files may be published');
  assert(!(relative(await realpath(source), await realpath(input))).startsWith('..'), 'Publication must not follow external links');
  const output = join(target, name); await mkdir(dirname(output), { recursive: true });
  await cp(input, output);
  assert((await readFile(output)).equals(await readFile(input)), `Export changed during staging: ${name}`);
}
await writeFile(join(target, '.nojekyll'), '');
assert(!(await readdir(target)).some(name => ['Data', 'Logs', 'Profile', 'node_modules', 'source-sdk'].includes(name)));
console.log(`Pages: ${files.size} byte-identical reader/resources + .nojekyll; no application-local data. Exported reader is unchanged.`);
