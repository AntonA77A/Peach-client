'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { spawn, execFile } = require('child_process');
const { resolveRunner, languageLabel } = require('./runners');

const { EventHub } = require('./events');

const PERMISSIONS = ['notify', 'clipboard', 'windows', 'registry', 'wallpaper', 'shell', 'hotkeys'];
const MAX_LOG = 300;

class ModManager {
  constructor({ root, config, version }) {
    this.root = root;
    this.config = config;
    this.version = version;
    this.modsDir = path.resolve(root, config.modsDir || 'mods');
    this.dataDir = path.resolve(root, config.dataDir || 'data');
    this.sdkDir = path.join(root, 'sdk');
    this.apiBase = null;           // set by the server once it is listening
    this.mods = new Map();         // id -> manifest info
    this.procs = new Map();        // id -> { proc, token, status, ... }
    this.tokens = new Map();       // token -> id
    this.logs = new Map();         // id -> [{t, s, m}]
    this.hub = new EventHub();
    this.statePath = path.join(this.dataDir, 'state.json');
    this.state = this._loadState();
    this.scan();
  }

  _loadState() { try { return JSON.parse(fs.readFileSync(this.statePath, 'utf8')); } catch { return { enabled: {} }; } }
  _saveState() {
    fs.mkdirSync(this.dataDir, { recursive: true });
    fs.writeFileSync(this.statePath, JSON.stringify(this.state, null, 2));
  }

  scan() {
    this.mods.clear();
    if (!fs.existsSync(this.modsDir)) return [];
    for (const d of fs.readdirSync(this.modsDir, { withFileTypes: true })) {
      if (!d.isDirectory()) continue;
      const dir = path.join(this.modsDir, d.name);
      const file = path.join(dir, 'mod.json');
      if (!fs.existsSync(file)) continue;
      try {
        const m = JSON.parse(fs.readFileSync(file, 'utf8'));
        if (!m.entry) throw new Error('"entry" missing');
        const entryPath = path.resolve(dir, m.entry);
        if (!entryPath.startsWith(dir + path.sep)) throw new Error('"entry" must stay inside the mod folder');
        if (!fs.existsSync(entryPath)) throw new Error('entry file not found: ' + m.entry);
        const permissions = (m.permissions || []).filter(p => PERMISSIONS.includes(p));
        this.mods.set(d.name, {
          id: d.name, name: m.name || d.name, version: m.version || '0.0.0', description: m.description || '',
          author: m.author || '', entry: m.entry, entryPath, dir, permissions,
          unknownPermissions: (m.permissions || []).filter(p => !PERMISSIONS.includes(p)),
          autostart: m.autostart !== false, language: languageLabel(entryPath, this.config),
        });
      } catch (e) {
        this.log(d.name, 'err', 'Invalid mod.json: ' + e.message);
      }
    }
    return [...this.mods.values()];
  }

  isEnabled(id) {
    const mod = this.mods.get(id);
    return id in this.state.enabled ? this.state.enabled[id] : !!(mod && mod.autostart);
  }

  log(id, stream, message) {
    const arr = this.logs.get(id) || [];
    for (const line of String(message).split(/\r?\n/)) {
      if (line === '') continue;
      arr.push({ t: Date.now(), s: stream, m: line });
    }
    while (arr.length > MAX_LOG) arr.shift();
    this.logs.set(id, arr);
  }

  modForToken(token) {
    const id = this.tokens.get(token);
    return id ? this.mods.get(id) : null;
  }

  start(id) {
    const mod = this.mods.get(id);
    if (!mod) throw new Error('Unknown mod: ' + id);
    const cur = this.procs.get(id);
    if (cur && cur.status === 'running') return;
    if (!this.apiBase) throw new Error('API server not ready');

    const runner = resolveRunner(mod.entryPath, this.config);
    const token = crypto.randomBytes(24).toString('hex');
    const sep = path.delimiter;
    const env = {
      ...process.env,
      PEACH_API: this.apiBase, PEACH_TOKEN: token, PEACH_MOD: id, PEACH_SDK: this.sdkDir,
      NODE_PATH: this.sdkDir + (process.env.NODE_PATH ? sep + process.env.NODE_PATH : ''),
      PYTHONPATH: this.sdkDir + (process.env.PYTHONPATH ? sep + process.env.PYTHONPATH : ''),
      PYTHONIOENCODING: 'utf-8',
    };
    const proc = spawn(runner.cmd, runner.args, { cwd: mod.dir, env, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
    const rec = { proc, token, status: 'running', startedAt: Date.now(), exitCode: null, pid: proc.pid };
    this.procs.set(id, rec);
    this.tokens.set(token, id);
    this.state.enabled[id] = true; this._saveState();
    this.log(id, 'sys', `started via ${runner.label} (pid ${proc.pid})`);

    const pipe = (stream, tag) => {
      let buf = '';
      stream.setEncoding('utf8');
      stream.on('data', chunk => {
        buf += chunk;
        const lines = buf.split(/\r?\n/); buf = lines.pop();
        lines.forEach(l => this.log(id, tag, l));
      });
      stream.on('end', () => buf && this.log(id, tag, buf));
    };
    pipe(proc.stdout, 'out'); pipe(proc.stderr, 'err');

    proc.on('error', e => { rec.status = 'crashed'; this.tokens.delete(token); this.hub.cleanup(id); this.log(id, 'err', 'spawn failed: ' + e.message); });
    proc.on('exit', (code, signal) => {
      this.tokens.delete(token);
      this.hub.cleanup(id);
      if (rec.status === 'stopping') rec.status = 'stopped';
      else rec.status = code === 0 ? 'stopped' : 'crashed';
      rec.exitCode = code;
      this.log(id, 'sys', `exited (${signal || 'code ' + code})`);
    });
  }

  stop(id, { persist = true } = {}) {
    const rec = this.procs.get(id);
    if (persist) { this.state.enabled[id] = false; this._saveState(); }
    if (!rec || rec.status !== 'running') return Promise.resolve();
    rec.status = 'stopping';
    return new Promise(resolve => {
      rec.proc.once('exit', resolve);
      execFile('taskkill', ['/PID', String(rec.proc.pid), '/T', '/F'], { windowsHide: true }, err => {
        if (err) try { rec.proc.kill(); } catch { /* already gone */ }
      });
      setTimeout(resolve, 3000).unref();
    });
  }

  startEnabled() {
    for (const id of this.mods.keys()) {
      if (!this.isEnabled(id)) continue;
      try { this.start(id); } catch (e) { this.log(id, 'err', e.message); }
    }
  }

  stopAll() {
    return Promise.all([...this.procs.keys()].map(id => this.stop(id, { persist: false })))
      .then(() => this.hub.shutdown());
  }

  snapshot() {
    return [...this.mods.values()].map(m => {
      const rec = this.procs.get(m.id);
      return {
        id: m.id, name: m.name, version: m.version, description: m.description, author: m.author,
        language: m.language, entry: m.entry, permissions: m.permissions, unknownPermissions: m.unknownPermissions,
        enabled: this.isEnabled(m.id),
        status: rec ? rec.status : 'stopped', pid: rec && rec.status === 'running' ? rec.pid : null,
        startedAt: rec ? rec.startedAt : null,
        log: (this.logs.get(m.id) || []).slice(-80),
      };
    });
  }
}

module.exports = { ModManager, PERMISSIONS };
