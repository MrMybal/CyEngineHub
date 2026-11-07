import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, mkdtemp, cp, rm, readdir } from 'node:fs/promises';
import { resolve, join, dirname } from 'node:path';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { build, root, validateCopy } from '../build.mjs';
import { verify } from '../verify.mjs';
import { copy, media } from '../content.mjs';
import { startPreview } from '../preview.mjs';
const run = promisify(execFile);
const artifacts = resolve(root, 'artifacts');
await mkdir(artifacts, { recursive: true });

test('Both editions have complete matching human copy, without copied body paragraphs', () => {
  validateCopy();
  for (const [key, value] of Object.entries(copy.en)) if (key.endsWith('.body') || key.endsWith('.a')) assert.notEqual(copy.fr[key], value, key);
  assert.equal(media.length, 5); assert.equal(new Set(media.map(item => item.source)).size, 5);
});

test('Standalone artifact preserves reader bytes, resources, notices and relative routes', async () => {
  const temp = await mkdtemp(join(artifacts, 'website-check-'));
  try { await build({ out: join(temp, 'site') }); const report = await verify(join(temp, 'site')); assert.equal(report.manualFiles, 18); }
  finally { await rm(temp, { recursive: true, force: true }); }
});

test('Builder refuses to overwrite unowned output files and unsafe destinations', async () => {
  const temp = await mkdtemp(join(artifacts, 'website-ownership-'));
  try {
    const out = join(temp, 'site'); await build({ out });
    await writeFile(join(out, 'keep-me.txt'), 'unrelated data');
    await assert.rejects(build({ out }), /unowned files/);
    assert.equal(await readFile(join(out, 'keep-me.txt'), 'utf8'), 'unrelated data');
    await assert.rejects(build({ out: root }), /Output must/);
  } finally { await rm(temp, { recursive: true, force: true }); }
});

test('Preview serves a non-root static mount, rejects mutation and does not expose sources', async () => {
  const temp = await mkdtemp(join(artifacts, 'website-server-'));
  let preview;
  try {
    await build({ out: join(temp, 'site') });
    preview = await startPreview({ directory: join(temp, 'site'), port: 0, base: '/nested/product/' });
    const response = await fetch(preview.url); assert.equal(response.status, 200);
    assert((await response.text()).includes('CyEngineHub'));
    const head = await fetch(preview.url + 'index.html', { method: 'HEAD' }); assert.equal(head.status, 200); assert.equal(await head.text(), '');
    assert.equal((await fetch(preview.url, { method: 'POST', body: 'no mutation' })).status, 405);
    assert.notEqual((await fetch(preview.url + '../../website/content.mjs')).status, 200);
    assert.equal((await fetch(preview.url + 'assets/missing.png')).status, 404);
  } finally { if (preview) await new Promise(resolve => preview.server.close(resolve)); await rm(temp, { recursive: true, force: true }); }
});

test('Build works without Git or installed packages; source changes renew CSS and script URLs', async () => {
  const temp = await mkdtemp(join(artifacts, 'website-no-git-'));
  try {
    const fixture = join(temp, 'application');
    const inputs = ['website/content.mjs', 'website/template.mjs', 'website/build.mjs', 'website/main.js', 'website/styles.css', 'Directory.Build.props', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'assets/branding/cyenginehub-logo.png', 'Documentation', ...media.map(item => item.source), 'extensions/unreal/CyEngineHub/LICENSE', 'extensions/builtin/cyenginehub.unreal-version-bridge/native-source/CyHubNativeBridge/LICENSE'];
    for (const input of new Set(inputs)) { const target = join(fixture, input); await mkdir(dirname(target), { recursive: true }); await cp(join(root, input), target, { recursive: true }); }
    const command = () => run(process.execPath, [join(fixture, 'website/build.mjs')], { cwd: fixture, env: { ...process.env, PATH: '' }, windowsHide: true });
    await command(); const first = await readFile(join(fixture, 'dist/website/index.html'), 'utf8');
    await writeFile(join(fixture, 'website/styles.css'), (await readFile(join(fixture, 'website/styles.css'))) + '\n/* cache revision test */\n');
    await writeFile(join(fixture, 'website/main.js'), (await readFile(join(fixture, 'website/main.js'))) + '\n/* cache revision test */\n');
    await command(); const second = await readFile(join(fixture, 'dist/website/index.html'), 'utf8');
    const revisions = html => [...html.matchAll(/assets\/site\.([a-f0-9]{12})\.(?:css|js)/g)].map(match => match[1]);
    assert.equal(revisions(first).length, 2); assert.equal(revisions(second).length, 2);
    assert(revisions(first).every((value, index) => value !== revisions(second)[index]));
    assert(!(await readdir(fixture)).includes('.git'));
  } finally { await rm(temp, { recursive: true, force: true }); }
});
