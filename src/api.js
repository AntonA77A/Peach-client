'use strict';
// The API every mod sees, regardless of language. Each method names the permission
// a mod must list in its mod.json "permissions" array (null = always allowed).
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFile, spawn } = require('child_process');
const win = require('./windows');

function createApi({ manager }) {
  const storageFile = id => path.join(manager.dataDir, 'storage', id + '.json');
  const readStore = id => { try { return JSON.parse(fs.readFileSync(storageFile(id), 'utf8')); } catch { return {}; } };
  const need = (params, key) => {
    if (params[key] === undefined || params[key] === null) throw new Error(`Missing parameter "${key}"`);
    return params[key];
  };

  const methods = {
    'log': { perm: null, run: (p, mod) => { manager.log(mod.id, p.level === 'error' ? 'err' : 'out', String(p.message ?? '')); return true; } },
    'system.info': { perm: null, run: () => ({
      platform: os.platform(), release: os.release(), arch: os.arch(), hostname: os.hostname(),
      cpus: os.cpus().length, totalMem: os.totalmem(), freeMem: os.freemem(), uptime: os.uptime(),
      user: os.userInfo().username, peach: manager.version,
    }) },
    'storage.get': { perm: null, run: (p, mod) => { const s = readStore(mod.id); return p.key === undefined ? s : (s[p.key] ?? null); } },
    'storage.set': { perm: null, run: (p, mod) => {
      const s = readStore(mod.id);
      if (p.value === null || p.value === undefined) delete s[need(p, 'key')]; else s[need(p, 'key')] = p.value;
      fs.mkdirSync(path.dirname(storageFile(mod.id)), { recursive: true });
      fs.writeFileSync(storageFile(mod.id), JSON.stringify(s, null, 2));
      return true;
    } },
    'notify': { perm: 'notify', run: p => { win.notify(p.title, p.message); return true; } },
    'clipboard.get': { perm: 'clipboard', run: () => win.clipboardGet() },
    'clipboard.set': { perm: 'clipboard', run: p => win.clipboardSet(need(p, 'text')) },
    'hotkey.register': { perm: 'hotkeys', run: (p, mod) => manager.hub.register(mod, String(need(p, 'combo'))) },
    'hotkey.unregister': { perm: 'hotkeys', run: (p, mod) => manager.hub.unregister(mod, String(need(p, 'combo'))) },
    'events.subscribe': { perm: null, run: (p, mod) => manager.hub.subscribe(mod, p.events || []) },
    'events.poll': { perm: null, run: (p, mod) => manager.hub.poll(mod.id, p.timeoutMs) },
    'windows.list': { perm: 'windows', run: () => win.windowsList() },
    'registry.get': { perm: 'registry', run: p => win.registryGet(String(need(p, 'path')), need(p, 'name')) },
    'wallpaper.set': { perm: 'wallpaper', run: p => {
      const file = path.resolve(String(need(p, 'path')));
      if (!fs.existsSync(file)) throw new Error('File not found: ' + file);
      return win.wallpaperSet(file);
    } },
    'shell.run': { perm: 'shell', run: p => new Promise(resolve => {
      const args = Array.isArray(p.args) ? p.args.map(String) : [];
      execFile(String(need(p, 'command')), args, { windowsHide: true, timeout: p.timeoutMs || 30000, maxBuffer: 8 * 1024 * 1024, encoding: 'utf8' },
        (err, stdout, stderr) => resolve({ code: err ? (typeof err.code === 'number' ? err.code : -1) : 0, stdout, stderr: stderr || (err && typeof err.code !== 'number' ? err.message : '') }));
    }) },
    'shell.open': { perm: 'shell', run: p => {
      const target = String(need(p, 'target'));
      if (!/^https?:\/\//i.test(target) && !fs.existsSync(target)) throw new Error('Target must be an http(s) URL or an existing path');
      spawn('explorer.exe', [target], { detached: true, stdio: 'ignore' }).unref();
      return true;
    } },
  };

  async function call(name, params, mod) {
    const m = methods[name];
    if (!m) { const e = new Error('Unknown method: ' + name); e.status = 404; throw e; }
    if (m.perm && !mod.permissions.includes(m.perm)) {
      const e = new Error(`Mod "${mod.id}" lacks permission "${m.perm}" (add it to mod.json)`); e.status = 403; throw e;
    }
    return m.run(params || {}, mod);
  }

  return { call, methods: Object.keys(methods) };
}

module.exports = { createApi };
