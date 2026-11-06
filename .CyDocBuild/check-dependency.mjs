import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
const work=import.meta.dirname;
const lock=JSON.parse(await readFile(work+'/package-lock.json','utf8'));
const archive=await readFile(work+'/.cache/packages/cydoc-0.1.0.tgz');
assert.equal('sha512-'+createHash('sha512').update(archive).digest('base64'),lock.packages['node_modules/cydoc'].integrity,'Packed CyDoc differs from the application lockfile');
assert(!lock.packages['node_modules/three'],'Three.js must not be installed in this image/gallery documentation toolchain');
