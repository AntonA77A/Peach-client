'use strict';
const path = require('path');
const readline = require('readline');
const { spawn } = require('child_process');
const { EventEmitter } = require('events');

// Manages the long-lived PowerShell helper (src/native/helper.ps1).
// Emits: 'hotkey' (id), 'focus' ({pid, process, title}), 'clipboard', 'exit'.
class Native extends EventEmitter {
  constructor() {
    super();
    this.proc = null;
    this.readyPromise = null;
    this.pending = new Map();
    this.seq = 0;
  }

  ensure() {
    if (this.readyPromise) return this.readyPromise;
    this.readyPromise = new Promise((resolve, reject) => {
      const proc = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File',
        path.join(__dirname, 'native', 'helper.ps1')], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
      this.proc = proc;
      let ready = false, stderr = '';
      proc.stderr.on('data', d => { stderr += d; });
      readline.createInterface({ input: proc.stdout }).on('line', line => {
        const [kind, a, b, c] = line.split('\t');
        if (kind === 'ready') { ready = true; resolve(); }
        else if (kind === 'res') { const p = this.pending.get(a); if (p) { this.pending.delete(a); clearTimeout(p.timer); p.resolve(b); } }
        else if (kind === 'hotkey') this.emit('hotkey', Number(a));
        else if (kind === 'focus') this.emit('focus', { pid: Number(a), process: b, title: c || '' });
        else if (kind === 'clip') this.emit('clipboard');
      });
      proc.on('error', e => { this.readyPromise = null; this.proc = null; reject(e); });
      proc.on('exit', () => {
        this.readyPromise = null; this.proc = null;
        if (!ready) reject(new Error('native helper failed to start: ' + stderr.trim().slice(0, 300)));
        for (const p of this.pending.values()) { clearTimeout(p.timer); p.resolve('fail'); }
        this.pending.clear();
        this.emit('exit');
      });
    });
    return this.readyPromise;
  }

  async request(...parts) {
    await this.ensure();
    const req = String(++this.seq);
    return new Promise(resolve => {
      const timer = setTimeout(() => { this.pending.delete(req); resolve('fail'); }, 5000);
      this.pending.set(req, { resolve, timer });
      this.proc.stdin.write([parts[0], req, ...parts.slice(1)].join('\t') + '\n');
    });
  }

  get running() { return !!this.proc; }

  shutdown() {
    const p = this.proc;
    if (!p) return;
    try { p.stdin.end(); } catch { /* ignore */ }
    setTimeout(() => { try { p.kill(); } catch { /* ignore */ } }, 1000).unref();
  }
}

module.exports = { Native };
