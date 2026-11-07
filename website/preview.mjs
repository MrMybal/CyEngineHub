import { createServer } from 'node:http';
import { readFile, lstat, realpath } from 'node:fs/promises';
import { resolve, relative, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import assert from 'node:assert/strict';
import { root } from './build.mjs';

export async function startPreview({ directory = resolve(root, 'dist/website'), port = 4789, base = '/showcase/' } = {}) {
  directory = await realpath(directory);
  assert(base.startsWith('/') && base.endsWith('/') && !base.includes('..') && !base.includes('\\'), 'Use a safe absolute mount path ending in /');
  const types = { '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.json': 'application/json; charset=utf-8', '.png': 'image/png', '.jpg': 'image/jpeg', '.webp': 'image/webp', '.svg': 'image/svg+xml', '.txt': 'text/plain; charset=utf-8', '.md': 'text/plain; charset=utf-8' };
  const server = createServer(async (request, response) => {
    try {
      if (!['GET', 'HEAD'].includes(request.method)) { response.writeHead(405).end(); return; }
      const url = new URL(request.url, 'http://127.0.0.1');
      if (url.pathname === '/' && base !== '/') { response.writeHead(302, { Location: base }).end(); return; }
      if (!url.pathname.startsWith(base)) { response.writeHead(404).end(); return; }
      const pathname = decodeURIComponent(url.pathname.slice(base.length));
      const target = resolve(directory, pathname || 'index.html');
      const delta = relative(directory, target);
      if (delta.startsWith('..') || delta.includes(':') || pathname.includes('\\')) { response.writeHead(403).end(); return; }
      const info = await lstat(target);
      if (!info.isFile() || info.isSymbolicLink() || relative(directory, await realpath(target)).startsWith('..')) { response.writeHead(404).end(); return; }
      response.writeHead(200, { 'Content-Type': types[extname(target)] || 'application/octet-stream', 'Cache-Control': 'no-store', 'X-Content-Type-Options': 'nosniff' });
      response.end(request.method === 'HEAD' ? undefined : await readFile(target));
    } catch { response.writeHead(404).end(); }
  });
  await new Promise((resolvePromise, reject) => { server.once('error', reject); server.listen(port, '127.0.0.1', resolvePromise); });
  const url = `http://127.0.0.1:${server.address().port}${base}`;
  return { server, url };
}
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const args = process.argv.slice(2), options = {};
  for (let index = 0; index < args.length; index += 2) {
    assert(['--port', '--base'].includes(args[index]) && args[index + 1], 'Usage: node website/preview.mjs [--port 4789] [--base /showcase/]');
    if (args[index] === '--port') { options.port = Number(args[index + 1]); assert(Number.isInteger(options.port) && options.port >= 0 && options.port < 65536); }
    else options.base = args[index + 1];
  }
  const preview = await startPreview(options);
  console.log(`Local showcase: ${preview.url} (PID ${process.pid}; loopback only)`);
  for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => preview.server.close(() => process.exit(0)));
  process.stdin.on('data', bytes => { if (bytes.includes(3)) preview.server.close(() => process.exit(0)); });
}
