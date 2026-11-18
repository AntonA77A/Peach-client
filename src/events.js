'use strict';
const { Native } = require('./native');

const MAX_QUEUE = 200;
const MAX_HOTKEYS_PER_MOD = 20;
const MOD_BITS = { alt: 1, ctrl: 2, control: 2, shift: 4, win: 8, super: 8, meta: 8 };
const NAMED_KEYS = {
  space: 0x20, enter: 0x0D, return: 0x0D, esc: 0x1B, escape: 0x1B, tab: 0x09, backspace: 0x08, delete: 0x2E, del: 0x2E,
  insert: 0x2D, home: 0x24, end: 0x23, pageup: 0x21, pagedown: 0x22, left: 0x25, up: 0x26, right: 0x27, down: 0x28,
};
// Subscribable events and the permission each one needs.
const EVENT_PERMS = { focus: 'windows', clipboard: 'clipboard' };

function parseCombo(combo) {
  const parts = String(combo).toLowerCase().split('+').map(s => s.trim()).filter(Boolean);
  if (parts.length < 2) throw new Error('Hotkey needs at least one modifier, e.g. "ctrl+alt+p"');
  const key = parts.pop();
  let mods = 0;
  for (const m of parts) {
    if (!(m in MOD_BITS)) throw new Error(`Unknown modifier "${m}" (use ctrl, alt, shift, win)`);
    mods |= MOD_BITS[m];
  }
  let vk;
  if (/^[a-z]$/.test(key)) vk = key.toUpperCase().charCodeAt(0);
  else if (/^[0-9]$/.test(key)) vk = key.charCodeAt(0);
  else if (/^f([1-9]|1\d|2[0-4])$/.test(key)) vk = 0x6F + Number(key.slice(1));
  else if (key in NAMED_KEYS) vk = NAMED_KEYS[key];
  else throw new Error(`Unknown key "${key}"`);
  const names = [['ctrl', 2], ['alt', 1], ['shift', 4], ['win', 8]].filter(([, b]) => mods & b).map(([n]) => n);
  return { norm: [...names, key].join('+'), mods, vk };
}

class EventHub {
  constructor() {
    this.native = new Native();
    this.queues = new Map();   // modId -> { items, waiter, subs:Set }
    this.hotkeys = new Map();  // numeric id -> { modId, combo }
    this.nextId = 1;
    this.native.on('hotkey', id => { const h = this.hotkeys.get(id); if (h) this.push(h.modId, { type: 'hotkey', combo: h.combo }); });
    this.native.on('focus', ev => this.broadcast('focus', ev));
    this.native.on('clipboard', () => this.broadcast('clipboard', {}));
    this.native.on('exit', () => this.hotkeys.clear());
  }

  _q(modId) {
    let q = this.queues.get(modId);
    if (!q) { q = { items: [], waiter: null, subs: new Set() }; this.queues.set(modId, q); }
    return q;
  }

  push(modId, ev) {
    const q = this._q(modId);
    q.items.push({ ...ev, time: Date.now() });
    while (q.items.length > MAX_QUEUE) q.items.shift();
    if (q.waiter) q.waiter();
  }

  broadcast(type, data) {
    for (const [modId, q] of this.queues) if (q.subs.has(type)) this.push(modId, { type, ...data });
  }

  poll(modId, timeoutMs) {
    const q = this._q(modId);
    const wait = Math.min(Math.max(Number(timeoutMs) || 25000, 0), 30000);
    if (q.waiter) q.waiter();            // release a stale poller
    return new Promise(resolve => {
      const finish = () => {
        clearTimeout(timer);
        if (q.waiter === finish) q.waiter = null;
        resolve(q.items.splice(0));
      };
      const timer = setTimeout(finish, wait);
      q.waiter = finish;
      if (q.items.length) finish();
    });
  }

  async subscribe(mod, names) {
    if (!Array.isArray(names)) throw new Error('"events" must be an array');
    for (const n of names) {
      if (!(n in EVENT_PERMS)) throw new Error(`Unknown event "${n}" (available: ${Object.keys(EVENT_PERMS).join(', ')}; hotkey events arrive automatically)`);
      if (!mod.permissions.includes(EVENT_PERMS[n])) throw new Error(`Event "${n}" needs permission "${EVENT_PERMS[n]}"`);
    }
    await this.native.ensure();
    const q = this._q(mod.id);
    names.forEach(n => q.subs.add(n));
    return [...q.subs];
  }

  async register(mod, combo) {
    const { norm, mods, vk } = parseCombo(combo);
    let count = 0;
    for (const h of this.hotkeys.values()) {
      if (h.combo === norm) throw new Error(`Hotkey ${norm} is already registered by "${h.modId}"`);
      if (h.modId === mod.id) count++;
    }
    if (count >= MAX_HOTKEYS_PER_MOD) throw new Error('Too many hotkeys for this mod');
    const id = this.nextId++;
    this.hotkeys.set(id, { modId: mod.id, combo: norm });   // reserve before awaiting
    let res;
    try { res = await this.native.request('reg', id, mods, vk); }
    catch (e) { this.hotkeys.delete(id); throw e; }
    if (res !== 'ok') { this.hotkeys.delete(id); throw new Error(`Windows refused ${norm} (already used by another program?)`); }
    this._q(mod.id);
    return { combo: norm };
  }

  async unregister(mod, combo) {
    const { norm } = parseCombo(combo);
    for (const [id, h] of this.hotkeys) {
      if (h.modId === mod.id && h.combo === norm) {
        this.hotkeys.delete(id);
        await this.native.request('unreg', id);
        return true;
      }
    }
    return false;
  }

  cleanup(modId) {
    for (const [id, h] of this.hotkeys) {
      if (h.modId !== modId) continue;
      this.hotkeys.delete(id);
      if (this.native.running) this.native.request('unreg', id).catch(() => {});
    }
    const q = this.queues.get(modId);
    if (q) { q.items.length = 0; if (q.waiter) q.waiter(); this.queues.delete(modId); }
  }

  shutdown() { this.native.shutdown(); }
}

module.exports = { EventHub, parseCombo };
