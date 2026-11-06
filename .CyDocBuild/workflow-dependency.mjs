import { readFile, appendFile } from 'node:fs/promises';
import assert from 'node:assert/strict';
const dependency = JSON.parse(await readFile(new URL('./dependency.json', import.meta.url), 'utf8'));
assert.equal(dependency.repository, 'https://github.com/MrMybal/CyDoc.git');
assert.match(dependency.commit, /^[a-f0-9]{40}$/);
assert.deepEqual(dependency.modules, ['image','gallery']);
const output = `commit=${dependency.commit}\ndirectory=.CyDocBuild/.cache/cydoc-source-${dependency.commit.slice(0, 7)}\n`;
if (process.env.GITHUB_OUTPUT) await appendFile(process.env.GITHUB_OUTPUT, output);
else process.stdout.write(output);
