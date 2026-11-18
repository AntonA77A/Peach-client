'use strict';
const http = require('http');
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { createApi } = require('./api');
const { supported } = require('./runners');

function startServer({ manager, config }) {
  const api = createApi({ manager });
  const uiToken = crypto.randomBytes(16).toString('hex');
  const uiHtml = fs.readFileSync(path.join(__dirname, 'ui.html'), 'utf8').replace('__UI_TOKEN__', uiToken);
  let port = config.port || 47800;

  const send = (res, status, body, type = 'application/json') => {
    res.writeHead(status, { 'content-type': type + '; charset=utf-8', 'cache-control': 'no-store' });
    res.end(typeof body === 'string' ? body : JSON.stringify(body));
  };
  const readBody = req => new Promise((resolve, reject) => {
    let data = '';
    req.on('data', c => { data += c; if (data.length > 1e6) { reject(new Error('Body too large')); req.destroy(); } });
    req.on('end', () => { try { resolve(data ? JSON.parse(data) : {}); } catch { reject(new Error('Invalid JSON')); } });
  });

  const server = http.createServer(async (req, res) => {
    // DNS-rebinding guard: only accept requests addressed to loopback.
    if (!/^(127\.0\.0\.1|localhost|\[::1\])(:\d+)?$/i.test(req.headers.host || '')) return send(res, 403, { ok: false, error: 'bad host' });
    const url = new URL(req.url, 'http://x');
    try {
      if (req.method === 'GET' && url.pathname === '/') return send(res, 200, uiHtml, 'text/html');

      if (url.pathname.startsWith('/api/') && req.method === 'POST') {
        const auth = (req.headers.authorization || '').replace(/^Bearer /, '');
        const mod = manager.modForToken(auth);
        if (!mod) return send(res, 401, { ok: false, error: 'invalid token' });
        const result = await api.call(url.pathname.slice(5), await readBody(req), mod);
        return send(res, 200, { ok: true, result: result === undefined ? null : result });
      }

      if (url.pathname.startsWith('/ui/')) {
        if (req.headers['x-peach-ui'] !== uiToken) return send(res, 401, { ok: false, error: 'bad ui token' });
        if (req.method === 'GET' && url.pathname === '/ui/state') {
          // "which interpreters are installed" scans PATH for ~20 programs: slow, and it hardly ever changes -> cache it
          if (!startServer.runnersCache || Date.now() - startServer.runnersAt > 60000) { startServer.runnersCache = supported(config); startServer.runnersAt = Date.now(); }
          return send(res, 200, { mods: manager.snapshot(), runners: startServer.runnersCache, version: manager.version });
        }
        const m = url.pathname.match(/^\/ui\/(start|stop|restart)\/([\w.-]+)$/);
        if (req.method === 'POST' && m) {
          const [, action, id] = m;
          if (action !== 'start') await manager.stop(id);
          if (action !== 'stop') manager.start(id);
          return send(res, 200, { ok: true });
        }
        if (req.method === 'POST' && url.pathname === '/ui/rescan') { manager.scan(); return send(res, 200, { ok: true }); }
      }
      send(res, 404, { ok: false, error: 'not found' });
    } catch (e) {
      send(res, e.status || 500, { ok: false, error: e.message });
    }
  });

  return new Promise((resolve, reject) => {
    const tryListen = attempt => {
      server.once('error', e => {
        if (e.code === 'EADDRINUSE' && attempt < 20) { port++; tryListen(attempt + 1); } else reject(e);
      });
      server.listen(port, '127.0.0.1', () => {
        manager.apiBase = `http://127.0.0.1:${port}/api`;
        resolve({ server, port, url: `http://127.0.0.1:${port}/` });
      });
    };
    tryListen(0);
  });
}

module.exports = { startServer };
