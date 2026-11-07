import { readFile, writeFile, mkdir, readdir, lstat, copyFile, rename, rm } from 'node:fs/promises';
import { resolve, join, dirname, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createHash, randomUUID } from 'node:crypto';
import assert from 'node:assert/strict';
import { copy, media, optionalDestinations } from './content.mjs';
import { renderPage } from './template.mjs';

export const source = dirname(fileURLToPath(import.meta.url));
export const root = resolve(source, '..');
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
async function walk(directory, prefix = '') {
  const files = [];
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const name = prefix + entry.name;
    const path = join(directory, entry.name);
    assert(!entry.isSymbolicLink(), `Refusing symlink: ${name}`);
    if (entry.isDirectory()) files.push(...await walk(path, name + '/'));
    else { assert(entry.isFile()); files.push(name); }
  }
  return files.sort();
}
export function dimensions(data) {
  if (data.subarray(1, 4).toString() === 'PNG') return { width: data.readUInt32BE(16), height: data.readUInt32BE(20), extension: 'png' };
  assert(data[0] === 255 && data[1] === 216, 'Expected PNG or JPEG');
  for (let cursor = 2; cursor < data.length;) {
    assert(data[cursor] === 255, 'Invalid JPEG marker');
    while (data[cursor] === 255) cursor++;
    const marker = data[cursor++];
    if (marker === 217 || marker === 218) break;
    const length = data.readUInt16BE(cursor);
    if ([192, 193, 194, 195, 197, 198, 199, 201, 202, 203, 205, 206, 207].includes(marker)) {
      return { height: data.readUInt16BE(cursor + 3), width: data.readUInt16BE(cursor + 5), extension: 'jpg' };
    }
    cursor += length;
  }
  throw new Error('No JPEG dimensions');
}
export function validateCopy() {
  assert.deepEqual(Object.keys(copy.en).sort(), Object.keys(copy.fr).sort(), 'EN/FR keys must match');
  for (const [locale, messages] of Object.entries(copy)) for (const [key, value] of Object.entries(messages)) {
    assert(typeof value === 'string' && value.trim(), `${locale}/${key} is empty`);
    assert(!/TODO|PLACEHOLDER|Lorem ipsum/i.test(value), `${locale}/${key} is unfinished`);
  }
}
export async function build({ out = resolve(root, 'dist/website'), destinations = optionalDestinations } = {}) {
  validateCopy();
  out = resolve(out);
  assert(out === resolve(root, 'dist/website') || out.startsWith(resolve(root, 'artifacts') + sep), 'Output must be dist/website or inside this project’s artifacts');
  const outputParent = dirname(out);
  for (let parent = outputParent; parent && parent !== dirname(parent); parent = dirname(parent)) {
    try { assert(!(await lstat(parent)).isSymbolicLink(), 'Output ancestor is a link'); } catch (error) { if (error.code !== 'ENOENT') throw error; }
  }
  let previous;
  try {
    assert(!(await lstat(out)).isSymbolicLink(), 'Output is a link');
    previous = JSON.parse(await readFile(join(out, 'site-manifest.json'), 'utf8'));
    assert(previous.product === 'CyEngineHub showcase' && previous.schema === 1, 'Existing output is not owned by this builder');
    const actual = await walk(out);
    assert.deepEqual(actual, [...Object.keys(previous.files), 'site-manifest.json'].sort(), 'Output has unowned files; keep them safe and choose a new output');
  } catch (error) { if (error.code !== 'ENOENT') throw error; }
  const props = await readFile(resolve(root, 'Directory.Build.props'), 'utf8');
  const version = props.match(/<Version>([^<]+)<\/Version>/)[1];
  for (const destination of Object.values(destinations)) {
    const url = new URL(destination); assert.equal(url.protocol, 'https:');
    if (destination.includes('/releases/')) assert(destination.includes('v' + version), 'Verify destinations before changing release version');
  }
  const manualRoot = resolve(root, 'Documentation');
  const manifest = JSON.parse(await readFile(join(manualRoot, 'DocData/manifest.json'), 'utf8'));
  const report = JSON.parse(await readFile(join(manualRoot, 'DocData/translation-report.json'), 'utf8'));
  assert.equal(manifest.version, version, 'Regenerate the application documentation first');
  assert.equal(manifest.defaultLocale, 'en');
  assert.deepEqual(manifest.locales.map(item => item.code), ['en', 'fr']);
  assert.equal(report.missing.length, 0); assert.equal(report.outdated.length, 0);
  for (const page of manifest.pages.filter(page => page.locale === 'en')) {
    const translation = manifest.pages.find(candidate => candidate.id === page.id && candidate.locale === 'fr');
    assert(translation && translation.text.trim() && translation.text !== page.text, `Missing genuine French page: ${page.id}`);
    assert.deepEqual(translation.headings.map(h => h.id), page.headings.map(h => h.id));
  }
  const manualFiles = await walk(manualRoot);
  assert(manualFiles.includes('Documentation.html') && manualFiles.includes('DocAI.md'));
  assert(manualFiles.every(file => !/\.cs$|\.ts$|\.py$|\.ps1$|node_modules|\.CyDocBuild/.test(file)), 'Tooling must not enter the output');
  await mkdir(outputParent, { recursive: true });
  const stage = join(outputParent, '.website-build-' + randomUUID());
  await mkdir(join(stage, 'assets'), { recursive: true });
  await mkdir(join(stage, 'legal'), { recursive: true });
  const emit = async (name, data) => { const target = join(stage, name); await mkdir(dirname(target), { recursive: true }); await writeFile(target, data); };
  const images = {};
  const image = async (name, input) => {
    assert((await lstat(input)).isFile() && !(await lstat(input)).isSymbolicLink());
    const bytes = await readFile(input); const size = dimensions(bytes);
    const nameWithRevision = `${name}.${hash(bytes).slice(0, 12)}.${size.extension}`;
    await emit('assets/' + nameWithRevision, bytes);
    return { url: './assets/' + nameWithRevision, ...size };
  };
  try {
    for (const item of media) images[item.id] = await image(item.id, resolve(root, item.source));
    const logo = await image('cyenginehub-logo', resolve(root, 'assets/branding/cyenginehub-logo.png'));
    const css = await readFile(join(source, 'styles.css'));
    const js = await readFile(join(source, 'main.js'));
    const stylesheet = `assets/site.${hash(css).slice(0, 12)}.css`;
    const script = `assets/site.${hash(js).slice(0, 12)}.js`;
    await emit(stylesheet, css); await emit(script, js);
    const html = renderPage({ version, images, logo: logo.url, stylesheet: './' + stylesheet, script: './' + script, destinations });
    for (const [, key] of html.matchAll(/data-i18n(?:-label|-alt)?="([^"]+)"/g)) assert(key in copy.en, `Unknown message ${key}`);
    for (const [, target] of html.matchAll(/data-doc="([^"]+)"/g)) {
      const [id, anchor] = target.split('#');
      for (const locale of ['en', 'fr']) {
        const page = manifest.pages.find(item => item.id === id && item.locale === locale);
        assert(page, `Missing manual page ${locale}/${id}`);
        if (anchor) assert(page.headings.some(heading => heading.id === anchor), `Missing manual anchor ${target}`);
      }
    }
    await emit('index.html', html);
    for (const name of manualFiles) {
      const target = join(stage, 'manual', name); await mkdir(dirname(target), { recursive: true });
      await copyFile(join(manualRoot, name), target);
      assert.equal(hash(await readFile(target)), hash(await readFile(join(manualRoot, name))), 'The manual must remain byte-identical');
    }
    const notices = {
      'legal/LICENSE.txt': 'LICENSE',
      'legal/Unreal-Editor-MIT.txt': 'extensions/unreal/CyEngineHub/LICENSE',
      'legal/Native-Adapter-MIT.txt': 'extensions/builtin/cyenginehub.unreal-version-bridge/native-source/CyHubNativeBridge/LICENSE',
      'legal/THIRD_PARTY_NOTICES.md': 'THIRD_PARTY_NOTICES.md',
    };
    for (const [name, origin] of Object.entries(notices)) await emit(name, await readFile(resolve(root, origin)));
    const outputFiles = await walk(stage);
    const hashes = {};
    for (const name of outputFiles) hashes[name] = hash(await readFile(join(stage, name)));
    await emit('site-manifest.json', JSON.stringify({ schema: 1, product: 'CyEngineHub showcase', version, files: hashes }, null, 2) + '\n');
    if (previous) await rm(out, { recursive: true }); // Validated exact generated directory and file ownership above.
    await rename(stage, out);
    console.log(`Showcase ready: ${join(out, 'index.html')} (${outputFiles.length + 1} local files; ${manualFiles.length} byte-identical manual resources)`);
    return { out, version, images, files: hashes };
  } catch (error) {
    // Preserve the isolated failed stage for diagnosis, never delete arbitrary application files.
    throw new Error(`Showcase build failed; generated stage retained at ${stage}`, { cause: error });
  }
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2);
  assert(args.length === 0 || (args.length === 2 && args[0] === '--out'), 'Usage: node website/build.mjs [--out artifacts/new-output]');
  await build(args.length ? { out: resolve(args[1]) } : undefined);
}
